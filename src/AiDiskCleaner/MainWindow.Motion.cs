using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AiDiskCleaner;

/// <summary>
/// GPU 合成轻量动效工具集（RenderTransform + Opacity）：
/// 1) 所有入口统一由 CanAnimate 门控，保证离屏回归/布局测试零延迟、零位移；
/// 2) 使用逐元素动画代次在 Completed 时回收时钟，避免快速切换/连点卡在半透明或错位态；
/// 3) 绝不对 DataGridRow 高度或 LayoutTransform 做动画，保持虚拟化 60fps。
/// </summary>
public partial class MainWindow
{
    private sealed class MotionEpoch
    {
        public int Value;
    }

    private static readonly ConditionalWeakTable<DependencyObject, MotionEpoch> _motionEpochs = new();

    private bool CanAnimate => IsUiReady && IsLoaded && IsVisible;

    private static int NextMotionEpoch(DependencyObject target)
    {
        var box = _motionEpochs.GetOrCreateValue(target);
        box.Value++;
        return box.Value;
    }

    private static bool IsMotionEpoch(DependencyObject target, int epoch)
        => _motionEpochs.TryGetValue(target, out var box) && box.Value == epoch;

    private static (TranslateTransform Translate, ScaleTransform Scale) EnsureCompositeTransform(UIElement el)
    {
        if (el.RenderTransform is TransformGroup group)
        {
            TranslateTransform? t = null;
            ScaleTransform? s = null;
            foreach (var child in group.Children)
            {
                if (child is TranslateTransform tt && t == null) t = tt;
                else if (child is ScaleTransform st && s == null) s = st;
            }
            if (t != null && s != null) return (t, s);

            s ??= new ScaleTransform(1, 1);
            t ??= new TranslateTransform(0, 0);
            if (!group.Children.Contains(s)) group.Children.Add(s);
            if (!group.Children.Contains(t)) group.Children.Add(t);
            return (t, s);
        }

        if (el.RenderTransform is TranslateTransform existingT)
        {
            var s = new ScaleTransform(1, 1);
            var tg = new TransformGroup();
            tg.Children.Add(s);
            tg.Children.Add(existingT);
            el.RenderTransform = tg;
            return (existingT, s);
        }

        if (el.RenderTransform is ScaleTransform existingS)
        {
            var t = new TranslateTransform(0, 0);
            var tg = new TransformGroup();
            tg.Children.Add(existingS);
            tg.Children.Add(t);
            el.RenderTransform = tg;
            return (t, existingS);
        }

        var scale = new ScaleTransform(1, 1);
        var translate = new TranslateTransform(0, 0);
        var composite = new TransformGroup();
        composite.Children.Add(scale);
        composite.Children.Add(translate);
        el.RenderTransform = composite;
        return (translate, scale);
    }

    private void AnimateSlideFadeIn(FrameworkElement? el, double offsetX = 0, double offsetY = 10,
        int durationMs = 220, int delayMs = 0, double fromOpacity = 0.0)
    {
        if (!CanAnimate || el == null) return;

        int epoch = NextMotionEpoch(el);
        var (translate, _) = EnsureCompositeTransform(el);

        el.BeginAnimation(OpacityProperty, null);
        translate.BeginAnimation(TranslateTransform.XProperty, null);
        translate.BeginAnimation(TranslateTransform.YProperty, null);

        el.Opacity = fromOpacity;
        translate.X = offsetX;
        translate.Y = offsetY;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(durationMs);
        var begin = delayMs > 0 ? TimeSpan.FromMilliseconds(delayMs) : TimeSpan.Zero;

        var fade = new DoubleAnimation(fromOpacity, 1.0, dur)
        {
            BeginTime = begin,
            EasingFunction = ease,
        };
        fade.Completed += (_, _) =>
        {
            if (!IsMotionEpoch(el, epoch)) return;
            el.BeginAnimation(OpacityProperty, null);
            el.Opacity = 1.0;
        };
        el.BeginAnimation(OpacityProperty, fade);

        if (Math.Abs(offsetX) > 0.01)
        {
            var animX = new DoubleAnimation(offsetX, 0.0, dur)
            {
                BeginTime = begin,
                EasingFunction = ease,
            };
            animX.Completed += (_, _) =>
            {
                if (!IsMotionEpoch(el, epoch)) return;
                translate.BeginAnimation(TranslateTransform.XProperty, null);
                translate.X = 0.0;
            };
            translate.BeginAnimation(TranslateTransform.XProperty, animX);
        }

        if (Math.Abs(offsetY) > 0.01)
        {
            var animY = new DoubleAnimation(offsetY, 0.0, dur)
            {
                BeginTime = begin,
                EasingFunction = ease,
            };
            animY.Completed += (_, _) =>
            {
                if (!IsMotionEpoch(el, epoch)) return;
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                translate.Y = 0.0;
            };
            translate.BeginAnimation(TranslateTransform.YProperty, animY);
        }
    }

    private void AnimateSpringPop(FrameworkElement? el, double fromScale = 0.94, double offsetY = 4, int durationMs = 230)
    {
        if (!CanAnimate || el == null) return;

        int epoch = NextMotionEpoch(el);
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        var (translate, scale) = EnsureCompositeTransform(el);

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        translate.BeginAnimation(TranslateTransform.YProperty, null);

        scale.ScaleX = fromScale;
        scale.ScaleY = fromScale;
        translate.Y = offsetY;

        var dur = TimeSpan.FromMilliseconds(durationMs);
        var springEase = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };
        var slideEase = new CubicEase { EasingMode = EasingMode.EaseOut };

        var animX = new DoubleAnimation(fromScale, 1.0, dur) { EasingFunction = springEase };
        var animY = new DoubleAnimation(fromScale, 1.0, dur) { EasingFunction = springEase };
        animX.Completed += (_, _) =>
        {
            if (!IsMotionEpoch(el, epoch)) return;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 1.0;
            scale.ScaleY = 1.0;
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animX);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animY);

        if (Math.Abs(offsetY) > 0.01)
        {
            var animTY = new DoubleAnimation(offsetY, 0.0, dur) { EasingFunction = slideEase };
            animTY.Completed += (_, _) =>
            {
                if (!IsMotionEpoch(el, epoch)) return;
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                translate.Y = 0.0;
            };
            translate.BeginAnimation(TranslateTransform.YProperty, animTY);
        }
    }

    private void AnimateScaleXReveal(FrameworkElement? el, double fromScaleX = 0.14, int durationMs = 340)
    {
        if (!CanAnimate || el == null) return;

        int epoch = NextMotionEpoch(el);
        el.RenderTransformOrigin = new Point(0, 0.5);

        ScaleTransform scale;
        if (el.RenderTransform is ScaleTransform existing)
        {
            scale = existing;
        }
        else
        {
            scale = EnsureCompositeTransform(el).Scale;
        }

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.ScaleX = fromScaleX;

        var anim = new DoubleAnimation(fromScaleX, 1.0, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut },
        };
        anim.Completed += (_, _) =>
        {
            if (!IsMotionEpoch(el, epoch)) return;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.ScaleX = 1.0;
            el.RenderTransformOrigin = new Point(0.5, 0.5);
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
    }

    private void AnimateSpinIconOnce(UIElement? icon, int durationMs = 480)
    {
        if (!CanAnimate || icon == null) return;

        int epoch = NextMotionEpoch(icon);
        icon.RenderTransformOrigin = new Point(0.5, 0.5);
        if (icon.RenderTransform is not RotateTransform rot)
        {
            rot = new RotateTransform(0);
            icon.RenderTransform = rot;
        }

        rot.BeginAnimation(RotateTransform.AngleProperty, null);
        rot.Angle = 0;

        var anim = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        anim.Completed += (_, _) =>
        {
            if (!IsMotionEpoch(icon, epoch)) return;
            rot.BeginAnimation(RotateTransform.AngleProperty, null);
            rot.Angle = 0;
        };
        rot.BeginAnimation(RotateTransform.AngleProperty, anim);
    }

    private void AnimateThemeSwitchFeedback()
    {
        if (!CanAnimate) return;

        if (ThemeToggleIcon != null && ThemeToggleRotate != null && ThemeToggleScale != null)
        {
            int iconEpoch = NextMotionEpoch(ThemeToggleIcon);
            ThemeToggleRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            ThemeToggleScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            ThemeToggleScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

            ThemeToggleRotate.Angle = -160;
            ThemeToggleScale.ScaleX = 0.65;
            ThemeToggleScale.ScaleY = 0.65;

            var spin = new DoubleAnimation(-160, 0, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new BackEase { Amplitude = 0.30, EasingMode = EasingMode.EaseOut },
            };
            var popX = new DoubleAnimation(0.65, 1.0, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new BackEase { Amplitude = 0.38, EasingMode = EasingMode.EaseOut },
            };
            var popY = popX.Clone();

            spin.Completed += (_, _) =>
            {
                if (!IsMotionEpoch(ThemeToggleIcon, iconEpoch)) return;
                ThemeToggleRotate.BeginAnimation(RotateTransform.AngleProperty, null);
                ThemeToggleScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                ThemeToggleScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                ThemeToggleRotate.Angle = 0;
                ThemeToggleScale.ScaleX = 1.0;
                ThemeToggleScale.ScaleY = 1.0;
            };

            ThemeToggleRotate.BeginAnimation(RotateTransform.AngleProperty, spin);
            ThemeToggleScale.BeginAnimation(ScaleTransform.ScaleXProperty, popX);
            ThemeToggleScale.BeginAnimation(ScaleTransform.ScaleYProperty, popY);
        }

        AnimateOpacityBloom(RightPanel, 0.85, 210);
        AnimateOpacityBloom(NavRailPanel, 0.88, 180);
    }

    private void AnimateOpacityBloom(FrameworkElement? el, double fromOpacity, int durationMs)
    {
        if (!CanAnimate || el == null) return;

        int epoch = NextMotionEpoch(el);
        el.BeginAnimation(OpacityProperty, null);
        el.Opacity = fromOpacity;

        var fade = new DoubleAnimation(fromOpacity, 1.0, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        fade.Completed += (_, _) =>
        {
            if (!IsMotionEpoch(el, epoch)) return;
            el.BeginAnimation(OpacityProperty, null);
            el.Opacity = 1.0;
        };
        el.BeginAnimation(OpacityProperty, fade);
    }

    private void AnimateHoverLift(UIElement? card, bool lifted)
    {
        if (!CanAnimate || card == null) return;

        TranslateTransform translate;
        if (card.RenderTransform is TranslateTransform direct)
            translate = direct;
        else
            translate = EnsureCompositeTransform(card).Translate;

        double targetY = lifted ? -2.0 : 0.0;
        var anim = new DoubleAnimation(targetY, TimeSpan.FromMilliseconds(lifted ? 130 : 160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        translate.BeginAnimation(TranslateTransform.YProperty, anim);
    }
}
