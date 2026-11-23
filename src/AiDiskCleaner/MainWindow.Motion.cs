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

    private void StaggerRevealVisibleRows(DataGrid? grid, int maxAnimatedRows = 18)
    {
        if (!CanAnimate || grid == null) return;
        grid.UpdateLayout();

        var targets = new List<(DataGridRow Row, TranslateTransform Translate, int Epoch)>(maxAnimatedRows);
        for (int i = 0; i < grid.Items.Count && targets.Count < maxAnimatedRows; i++)
        {
            if (grid.ItemContainerGenerator.ContainerFromIndex(i) is not DataGridRow row || !row.IsVisible)
                continue;
            int epoch = NextMotionEpoch(row);
            var (translate, _) = EnsureCompositeTransform(row);
            row.BeginAnimation(OpacityProperty, null);
            translate.BeginAnimation(TranslateTransform.YProperty, null);
            row.Opacity = 0.0;
            translate.Y = 16.0;
            targets.Add((row, translate, epoch));
        }

        if (targets.Count == 0) return;

        // 先在首帧前把可见行锁定在待滑入位置，待本帧布局落地（DispatcherPriority.Render）后再统一启动 60fps 瀑布显现，
        // 避免首帧闪白或前几行延迟被同步布局耗时吞掉。
        grid.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(260);
            for (int idx = 0; idx < targets.Count; idx++)
            {
                var (row, translate, epoch) = targets[idx];
                if (!IsMotionEpoch(row, epoch)) continue;
                var begin = TimeSpan.FromMilliseconds(idx * 28);

                var fade = new DoubleAnimation(0.0, 1.0, dur) { BeginTime = begin, EasingFunction = ease };
                var slide = new DoubleAnimation(16.0, 0.0, dur) { BeginTime = begin, EasingFunction = ease };
                fade.Completed += (_, _) =>
                {
                    if (!IsMotionEpoch(row, epoch)) return;
                    row.BeginAnimation(OpacityProperty, null);
                    translate.BeginAnimation(TranslateTransform.YProperty, null);
                    row.Opacity = 1.0;
                    translate.Y = 0.0;
                };
                row.BeginAnimation(OpacityProperty, fade);
                translate.BeginAnimation(TranslateTransform.YProperty, slide);
            }
        }));
    }

    private void AnimateStreamedScanRowIn(DataGridRow row)
    {
        if (!CanAnimate || row == null) return;
        int epoch = NextMotionEpoch(row);
        var (translate, _) = EnsureCompositeTransform(row);
        row.BeginAnimation(OpacityProperty, null);
        translate.BeginAnimation(TranslateTransform.XProperty, null);
        translate.BeginAnimation(TranslateTransform.YProperty, null);
        row.Opacity = 0.0;
        translate.X = 28.0;
        translate.Y = 6.0;

        row.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
        {
            if (!IsMotionEpoch(row, epoch)) return;
            var lockEase = new BackEase { Amplitude = 0.14, EasingMode = EasingMode.EaseOut };
            var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
            var durX = TimeSpan.FromMilliseconds(520);
            var durFade = TimeSpan.FromMilliseconds(360);

            var fade = new DoubleAnimation(0.0, 1.0, durFade) { EasingFunction = easeOut };
            var slideX = new DoubleAnimation(28.0, 0.0, durX) { EasingFunction = lockEase };
            var slideY = new DoubleAnimation(6.0, 0.0, durFade) { EasingFunction = easeOut };
            slideX.Completed += (_, _) =>
            {
                if (!IsMotionEpoch(row, epoch)) return;
                row.BeginAnimation(OpacityProperty, null);
                translate.BeginAnimation(TranslateTransform.XProperty, null);
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                row.Opacity = 1.0;
                translate.X = 0.0;
                translate.Y = 0.0;
            };
            row.BeginAnimation(OpacityProperty, fade);
            translate.BeginAnimation(TranslateTransform.XProperty, slideX);
            translate.BeginAnimation(TranslateTransform.YProperty, slideY);
        }));
    }

    // ==================== 开机 6.8s「机甲组装」分阶段装配动画（除顶栏外全机落锁） ====================

    private void PrepareMechaAssemblyVisuals()
    {
        if (!CanAnimate) return;

        LockElementPreAssemble(NavRailPanel, offsetX: -58, offsetY: 0, scale: 1.0);
        LockElementPreAssemble(DriveCardsList, offsetX: 0, offsetY: -18, scale: 0.88);
        LockElementPreAssemble(RadialDriveMetaRow, offsetX: 0, offsetY: -14, scale: 0.90);
        LockElementPreAssemble(RadialRingHost, offsetX: 0, offsetY: 0, scale: 0.78);
        LockElementPreAssemble(FolderDeleteBarCard, offsetX: 0, offsetY: 22, scale: 0.92);
        LockElementPreAssemble(OrganizeFilterBar, offsetX: 0, offsetY: 20, scale: 0.92);

        if (OrganizeCenterSpineScale != null)
        {
            OrganizeCenterSpineScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            OrganizeCenterSpineScale.ScaleY = 0.0;
        }
        if (OrganizeSpineSpark != null && OrganizeSpineSparkY != null)
        {
            OrganizeSpineSpark.BeginAnimation(OpacityProperty, null);
            OrganizeSpineSparkY.BeginAnimation(TranslateTransform.YProperty, null);
            OrganizeSpineSpark.Opacity = 0.0;
            OrganizeSpineSparkY.Y = 0.0;
        }
    }

    private async Task PlayMechaAssemblyChassisPhaseAsync(CancellationToken ct)
    {
        if (!CanAnimate) return;

        await Task.Delay(60, ct);
        UnlockElementMecha(NavRailPanel, fromX: -58, fromY: 0, fromScale: 1.0, durationMs: 620, backAmp: 0.24);

        await Task.Delay(170, ct);
        UnlockElementMecha(DriveCardsList, fromX: 0, fromY: -18, fromScale: 0.88, durationMs: 500, backAmp: 0.26);

        // 1px 中轴分界线从上往下焊接拉出 + 亮橙光点同步滑落
        FireMechaCenterSpineWeld(durationMs: 840);

        await Task.Delay(150, ct);
        UnlockElementMecha(RadialDriveMetaRow, fromX: 0, fromY: -14, fromScale: 0.90, durationMs: 480, backAmp: 0.20);
        UnlockElementMecha(RadialRingHost, fromX: 0, fromY: 0, fromScale: 0.78, durationMs: 680, backAmp: 0.22);
    }

    private void PlayMechaAssemblyOutroButtons()
    {
        if (!CanAnimate)
        {
            RestoreMechaAssemblyVisualsImmediate();
            return;
        }

        UnlockElementMecha(FolderDeleteBarCard, fromX: 0, fromY: 22, fromScale: 0.92, durationMs: 520, backAmp: 0.24, delayMs: 110);
        UnlockElementMecha(OrganizeFilterBar, fromX: 0, fromY: 20, fromScale: 0.92, durationMs: 480, backAmp: 0.22, delayMs: 250);
    }

    private void RestoreMechaAssemblyVisualsImmediate()
    {
        FrameworkElement?[] parts =
        {
            NavRailPanel, DriveCardsList, RadialDriveMetaRow,
            RadialRingHost, FolderDeleteBarCard, OrganizeFilterBar,
        };
        foreach (var el in parts)
        {
            if (el == null) continue;
            NextMotionEpoch(el);
            el.BeginAnimation(OpacityProperty, null);
            el.Opacity = 1.0;
            if (el.RenderTransform is TransformGroup)
            {
                var (tr, sc) = EnsureCompositeTransform(el);
                tr.BeginAnimation(TranslateTransform.XProperty, null);
                tr.BeginAnimation(TranslateTransform.YProperty, null);
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                tr.X = 0;
                tr.Y = 0;
                sc.ScaleX = 1.0;
                sc.ScaleY = 1.0;
            }
        }

        if (OrganizeCenterSpineScale != null)
        {
            OrganizeCenterSpineScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            OrganizeCenterSpineScale.ScaleY = 1.0;
        }
        if (OrganizeSpineSpark != null && OrganizeSpineSparkY != null)
        {
            OrganizeSpineSpark.BeginAnimation(OpacityProperty, null);
            OrganizeSpineSparkY.BeginAnimation(TranslateTransform.YProperty, null);
            OrganizeSpineSpark.Opacity = 0.0;
            OrganizeSpineSparkY.Y = 0.0;
        }
    }

    private static void LockElementPreAssemble(FrameworkElement? el, double offsetX, double offsetY, double scale)
    {
        if (el == null) return;
        NextMotionEpoch(el);
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        var (tr, sc) = EnsureCompositeTransform(el);
        el.BeginAnimation(OpacityProperty, null);
        tr.BeginAnimation(TranslateTransform.XProperty, null);
        tr.BeginAnimation(TranslateTransform.YProperty, null);
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        el.Opacity = 0.0;
        tr.X = offsetX;
        tr.Y = offsetY;
        sc.ScaleX = scale;
        sc.ScaleY = scale;
    }

    private static void UnlockElementMecha(
        FrameworkElement? el, double fromX, double fromY, double fromScale,
        int durationMs, double backAmp, int delayMs = 0)
    {
        if (el == null) return;
        int epoch = NextMotionEpoch(el);
        var (tr, sc) = EnsureCompositeTransform(el);
        var begin = delayMs > 0 ? TimeSpan.FromMilliseconds(delayMs) : TimeSpan.Zero;
        var dur = TimeSpan.FromMilliseconds(durationMs);
        var backEase = new BackEase { Amplitude = backAmp, EasingMode = EasingMode.EaseOut };
        var fadeEase = new CubicEase { EasingMode = EasingMode.EaseOut };

        var fade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(Math.Min(durationMs, 380)))
        { BeginTime = begin, EasingFunction = fadeEase };
        fade.Completed += (_, _) =>
        {
            if (!IsMotionEpoch(el, epoch)) return;
            el.BeginAnimation(OpacityProperty, null);
            el.Opacity = 1.0;
        };
        el.BeginAnimation(OpacityProperty, fade);

        if (Math.Abs(fromX) > 0.01)
        {
            var animX = new DoubleAnimation(fromX, 0.0, dur) { BeginTime = begin, EasingFunction = backEase };
            animX.Completed += (_, _) => { if (IsMotionEpoch(el, epoch)) { tr.BeginAnimation(TranslateTransform.XProperty, null); tr.X = 0; } };
            tr.BeginAnimation(TranslateTransform.XProperty, animX);
        }
        if (Math.Abs(fromY) > 0.01)
        {
            var animY = new DoubleAnimation(fromY, 0.0, dur) { BeginTime = begin, EasingFunction = backEase };
            animY.Completed += (_, _) => { if (IsMotionEpoch(el, epoch)) { tr.BeginAnimation(TranslateTransform.YProperty, null); tr.Y = 0; } };
            tr.BeginAnimation(TranslateTransform.YProperty, animY);
        }
        if (Math.Abs(fromScale - 1.0) > 0.005)
        {
            var animS = new DoubleAnimation(fromScale, 1.0, dur) { BeginTime = begin, EasingFunction = backEase };
            animS.Completed += (_, _) =>
            {
                if (!IsMotionEpoch(el, epoch)) return;
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                sc.ScaleX = 1.0;
                sc.ScaleY = 1.0;
            };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, animS);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, animS.Clone());
        }
    }

    private void FireMechaCenterSpineWeld(int durationMs)
    {
        if (OrganizeCenterSpineScale == null) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(durationMs);

        var growY = new DoubleAnimation(0.0, 1.0, dur) { EasingFunction = ease };
        growY.Completed += (_, _) =>
        {
            OrganizeCenterSpineScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            OrganizeCenterSpineScale.ScaleY = 1.0;
        };
        OrganizeCenterSpineScale.BeginAnimation(ScaleTransform.ScaleYProperty, growY);

        if (OrganizeSpineSpark == null || OrganizeSpineSparkY == null || OrganizeCenterSpine == null) return;
        double spineH = OrganizeCenterSpine.ActualHeight > 60 ? OrganizeCenterSpine.ActualHeight - 18 : 480;
        int epoch = NextMotionEpoch(OrganizeSpineSpark);

        var sparkTravel = new DoubleAnimation(0.0, spineH, dur) { EasingFunction = ease };
        var sparkOp = new DoubleAnimationUsingKeyFrames();
        sparkOp.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        sparkOp.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(durationMs * 0.12))));
        sparkOp.KeyFrames.Add(new LinearDoubleKeyFrame(0.95, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(durationMs * 0.85))));
        sparkOp.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(dur)));
        sparkOp.Completed += (_, _) =>
        {
            if (!IsMotionEpoch(OrganizeSpineSpark, epoch)) return;
            OrganizeSpineSpark.BeginAnimation(OpacityProperty, null);
            OrganizeSpineSparkY.BeginAnimation(TranslateTransform.YProperty, null);
            OrganizeSpineSpark.Opacity = 0.0;
            OrganizeSpineSparkY.Y = 0.0;
        };
        OrganizeSpineSparkY.BeginAnimation(TranslateTransform.YProperty, sparkTravel);
        OrganizeSpineSpark.BeginAnimation(OpacityProperty, sparkOp);
    }

    private void AnimateJumpSpotlight(DataGridRow? row)
    {
        if (!CanAnimate || row == null || !row.IsVisible) return;
        int epoch = NextMotionEpoch(row);
        row.RenderTransformOrigin = new Point(0.5, 0.5);
        var (_, scale) = EnsureCompositeTransform(row);
        Panel.SetZIndex(row, 40);

        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var easeInOut = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var pulse = new DoubleAnimationUsingKeyFrames();
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1.018, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140)), easeOut));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(340)), easeInOut));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1.011, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(480)), easeOut));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(680)), easeInOut));
        pulse.Completed += (_, _) =>
        {
            if (!IsMotionEpoch(row, epoch)) return;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 1.0;
            scale.ScaleY = 1.0;
            Panel.SetZIndex(row, 0);
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse.Clone());
    }

    private static void ResetRowMotionTransform(DataGridRow? row)
    {
        if (row == null) return;
        NextMotionEpoch(row);
        row.BeginAnimation(OpacityProperty, null);
        row.Opacity = 1.0;
        Panel.SetZIndex(row, 0);
        if (row.RenderTransform is TransformGroup)
        {
            var (translate, scale) = EnsureCompositeTransform(row);
            translate.BeginAnimation(TranslateTransform.XProperty, null);
            translate.BeginAnimation(TranslateTransform.YProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            translate.X = 0;
            translate.Y = 0;
            scale.ScaleX = 1;
            scale.ScaleY = 1;
        }
        else if (row.RenderTransform != Transform.Identity)
        {
            row.RenderTransform = Transform.Identity;
        }
    }
}
