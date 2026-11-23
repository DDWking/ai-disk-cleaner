using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services;

namespace AiDiskCleaner;

/// <summary>
/// 方案 1（Swiss Radial Ring）：亮橙分段圆环容量图 + 6.8s 机甲扫描旋转生长 + 四色分段环 AI 变速呼吸脉冲。
/// 全程仅驱动静态冻结几何的 StrokeDashOffset / RotateTransform / ScaleTransform / Opacity，零逐帧几何分配、稳定 60fps。
/// </summary>
public partial class MainWindow
{
    private const double RingScanCircumferenceDash = 22.515;

    private long _radialGreenBytes;
    private long _radialYellowBytes;
    private long _radialRedBytes;
    private long _radialFreeBytes;
    private bool _topBeamSweeping;

    private bool _scanRingActive;
    private bool _scanRingSpinning;
    private double _scanRingFraction;
    private bool _aiRingRotating;
    private int _aiPulseToken;

    private void UpdateRadialRing(long greenBytes, long yellowBytes, long redBytes, long freeBytes)
    {
        _radialGreenBytes = Math.Max(0, greenBytes);
        _radialYellowBytes = Math.Max(0, yellowBytes);
        _radialRedBytes = Math.Max(0, redBytes);
        _radialFreeBytes = Math.Max(0, freeBytes);

        if (_scanRingActive) return;
        UpdateRadialStaticArcGeometries();
        SetStaticRingArcsOpacityImmediate(1.0);
        UpdateRadialCenterText();
    }

    private void UpdateRadialStaticArcGeometries()
    {
        if (RingArcOrange == null || RingArcYellow == null || RingArcRed == null || RingArcFree == null)
            return;

        long total = _radialGreenBytes + _radialYellowBytes + _radialRedBytes + _radialFreeBytes;
        if (total <= 0)
        {
            RingArcOrange.Data = Geometry.Empty;
            RingArcYellow.Data = Geometry.Empty;
            RingArcRed.Data = Geometry.Empty;
            RingArcFree.Data = CreateArcGeometry(108, 108, 86, -90, 269.5);
            return;
        }

        double[] raw =
        {
            _radialGreenBytes > 0 ? Math.Max(_radialGreenBytes / (double)total, 0.04) : 0,
            _radialYellowBytes > 0 ? Math.Max(_radialYellowBytes / (double)total, 0.05) : 0,
            _radialRedBytes > 0 ? Math.Max(_radialRedBytes / (double)total, 0.05) : 0,
            _radialFreeBytes > 0 ? Math.Max(_radialFreeBytes / (double)total, 0.05) : 0,
        };
        int activeCount = 0;
        double sum = 0;
        for (int i = 0; i < 4; i++)
        {
            if (raw[i] > 0) activeCount++;
            sum += raw[i];
        }

        const double gapDeg = 3.4;
        double usableDeg = activeCount > 1 ? 360.0 - activeCount * gapDeg : 359.6;

        System.Windows.Shapes.Path[] arcs = { RingArcOrange, RingArcYellow, RingArcRed, RingArcFree };
        double cursorDeg = -90.0;

        for (int i = 0; i < 4; i++)
        {
            if (raw[i] <= 0 || sum <= 0)
            {
                arcs[i].Data = Geometry.Empty;
                continue;
            }
            double sweep = (raw[i] / sum) * usableDeg;
            double endDeg = cursorDeg + sweep;
            arcs[i].Data = CreateArcGeometry(108, 108, 86, cursorDeg, endDeg);
            cursorDeg = endDeg + (activeCount > 1 ? gapDeg : 0);
        }
    }

    private void SetStaticRingArcsOpacityImmediate(double opacity)
    {
        System.Windows.Shapes.Path?[] arcs = { RingArcOrange, RingArcYellow, RingArcRed, RingArcFree };
        foreach (var arc in arcs)
        {
            if (arc == null) continue;
            arc.BeginAnimation(OpacityProperty, null);
            arc.Opacity = opacity;
        }
    }

    private void FadeStaticRingArcsTo(double targetOpacity, int durationMs, int beginMs = 0)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        System.Windows.Shapes.Path?[] arcs = { RingArcOrange, RingArcYellow, RingArcRed, RingArcFree };
        foreach (var arc in arcs)
        {
            if (arc == null) continue;
            int epoch = NextMotionEpoch(arc);
            var anim = new DoubleAnimation(targetOpacity, TimeSpan.FromMilliseconds(durationMs))
            {
                BeginTime = beginMs > 0 ? TimeSpan.FromMilliseconds(beginMs) : TimeSpan.Zero,
                EasingFunction = ease,
            };
            anim.Completed += (_, _) =>
            {
                if (!IsMotionEpoch(arc, epoch)) return;
                arc.BeginAnimation(OpacityProperty, null);
                arc.Opacity = targetOpacity;
            };
            arc.BeginAnimation(OpacityProperty, anim);
        }
    }

    private void UpdateRadialCenterText()
    {
        if (RadialHeroValue == null || RadialHeroCaption == null) return;
        if (_scanRingActive) return;

        if (RadialDriveMeta != null)
        {
            string driveName = DriveBox?.SelectedItem?.ToString()?.TrimEnd('\\') ?? "C:";
            try
            {
                if (_volumeTotal > 0)
                {
                    long free = Math.Max(0, _volumeTotal - _volumeUsed);
                    RadialDriveMeta.Text = Loc.IsEn
                        ? $"{driveName}  ·  {FileEntry.FormatSize(free)} free"
                        : $"{driveName}  ·  余 {FileEntry.FormatSize(free)}";
                }
                else
                {
                    RadialDriveMeta.Text = driveName;
                }
            }
            catch
            {
                RadialDriveMeta.Text = driveName;
            }
        }

        if (_aiRingRotating)
        {
            RadialHeroValue.Text = "AI";
            RadialHeroValue.Foreground = ThemeService.Brush("BrandAccent");
            RadialHeroCaption.Text = Loc.IsEn ? "Sorting folders..." : "正在透视整理";
            return;
        }

        if (_folderCheckedBytes > 0 && FolderDelete.HasSelection)
        {
            RadialHeroValue.Text = FileEntry.FormatSize(_folderCheckedBytes);
            RadialHeroValue.Foreground = ThemeService.Brush("BrandAccent");
            RadialHeroCaption.Text = Loc.IsEn
                ? $"{_folderCheckedCount} selected"
                : $"已选 {_folderCheckedCount} 项";
            return;
        }

        if (_radialGreenBytes > 0)
        {
            RadialHeroValue.Text = FileEntry.FormatSize(_radialGreenBytes);
            RadialHeroValue.Foreground = ThemeService.Brush("Text");
            RadialHeroCaption.Text = Loc.IsEn ? "Cleanable" : "可清理";
            return;
        }

        if (_volumeUsed > 0)
        {
            RadialHeroValue.Text = FileEntry.FormatSize(_volumeUsed);
            RadialHeroValue.Foreground = ThemeService.Brush("Text");
            RadialHeroCaption.Text = Loc.IsEn ? "Used space" : "已用空间";
            return;
        }

        RadialHeroValue.Text = "—";
        RadialHeroValue.Foreground = ThemeService.Brush("TextDim");
        RadialHeroCaption.Text = Loc.IsEn ? "Ready" : "准备就绪";
    }

    // ==================== 扫盘旋转生长双层流光弧（6.8s 机甲开机 / 实时扫盘） ====================

    private void BeginRadialScanGrowth()
        => BeginRadialScanGrowthCore(choreographedSweep: false, growMs: 5200);

    private void BeginRadialScanGrowthChoreographed(int growMs = 5200)
        => BeginRadialScanGrowthCore(choreographedSweep: true, growMs: growMs);

    private void BeginRadialScanGrowthCore(bool choreographedSweep, int growMs)
    {
        if (RingArcOrange == null || RingArcYellow == null || RingArcRed == null
            || RingArcFree == null || RingScanArc == null)
            return;

        _aiRingRotating = false;
        _scanRingActive = true;
        _scanRingSpinning = false;
        _scanRingFraction = 0.04;

        NextMotionEpoch(RingScanArc);
        if (RingScanTrailArc != null) NextMotionEpoch(RingScanTrailArc);
        if (RingTrackBase != null) NextMotionEpoch(RingTrackBase);

        SetStaticRingArcsOpacityImmediate(0.0);
        if (RadialRingRotate != null)
        {
            RadialRingRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            RadialRingRotate.Angle = 0;
        }
        ResetRadialRingScaleImmediate();

        if (RingTrackBase != null)
        {
            RingTrackBase.BeginAnimation(OpacityProperty, null);
            RingTrackBase.Visibility = Visibility.Visible;
            RingTrackBase.Opacity = 0.28;
        }

        double initDash = RingScanCircumferenceDash * (1.0 - 0.04);
        double initTrailDash = RingScanCircumferenceDash * (1.0 - 0.08);

        if (RingScanTrailArc != null)
        {
            RingScanTrailArc.BeginAnimation(OpacityProperty, null);
            RingScanTrailArc.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            RingScanTrailArc.Opacity = 0.28;
            RingScanTrailArc.StrokeDashOffset = initTrailDash;
            RingScanTrailArc.Visibility = Visibility.Visible;
        }
        if (RingScanTrailRotate != null)
        {
            RingScanTrailRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            RingScanTrailRotate.Angle = -30;
        }

        RingScanArc.BeginAnimation(OpacityProperty, null);
        RingScanArc.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
        RingScanArc.Opacity = 1.0;
        RingScanArc.StrokeDashOffset = initDash;
        RingScanArc.Visibility = Visibility.Visible;
        if (RingScanRotate != null)
        {
            RingScanRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            RingScanRotate.Angle = 0;
        }

        if (RadialHeroValue != null)
        {
            RadialHeroValue.Text = "0%";
            RadialHeroValue.Foreground = ThemeService.Brush("BrandAccent");
        }
        if (RadialHeroCaption != null)
            RadialHeroCaption.Text = Loc.IsEn ? "Scanning..." : "正在扫描";

        if (!CanAnimate) return;

        if (choreographedSweep)
            StartChoreographedScanRingSweep(growMs);
        else
            StartLiveScanRingSpin();
    }

    /// <summary>
    /// 开机机甲装配专用 ~5.2s 旋转充能曲线：短亮弧由 4% 一路拉长至 100% 满圆，
    /// 同步顺时针优雅旋转 2.5 圈（900°）并轻柔减速对正 12 点钟方向，配合脉冲光环熔铸为四色容量环。
    /// </summary>
    private void StartChoreographedScanRingSweep(int growMs)
    {
        if (!CanAnimate || RingScanArc == null) return;
        _scanRingSpinning = true;

        var growEase = new PowerEase { Power = 1.55, EasingMode = EasingMode.EaseInOut };
        var spinEase = new PowerEase { Power = 2.6, EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(growMs);

        RingScanArc.BeginAnimation(Shape.StrokeDashOffsetProperty,
            new DoubleAnimation(RingScanCircumferenceDash * 0.96, 0.0, dur) { EasingFunction = growEase });
        if (RingScanRotate != null)
        {
            RingScanRotate.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0.0, 900.0, dur) { EasingFunction = spinEase });
        }

        if (RingScanTrailArc != null)
        {
            RingScanTrailArc.BeginAnimation(Shape.StrokeDashOffsetProperty,
                new DoubleAnimation(RingScanCircumferenceDash * 0.92, 0.0, dur)
                { EasingFunction = new PowerEase { Power = 1.38, EasingMode = EasingMode.EaseInOut } });
        }
        if (RingScanTrailRotate != null)
        {
            RingScanTrailRotate.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(-32.0, 900.0, dur) { EasingFunction = spinEase });
        }
    }

    private void StartLiveScanRingSpin()
    {
        if (!CanAnimate || _scanRingSpinning) return;
        _scanRingSpinning = true;

        AnimateScanDashTo(Math.Max(_scanRingFraction, 0.14), durationMs: 360);

        if (RingScanRotate != null)
        {
            double cur = RingScanRotate.Angle % 360.0;
            var kf = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            kf.KeyFrames.Add(new DiscreteDoubleKeyFrame(cur, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            kf.KeyFrames.Add(new SplineDoubleKeyFrame(cur + 170.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(720)), new KeySpline(0.42, 0.0, 0.25, 1.0)));
            kf.KeyFrames.Add(new SplineDoubleKeyFrame(cur + 360.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1600)), new KeySpline(0.35, 0.0, 0.20, 1.0)));
            RingScanRotate.BeginAnimation(RotateTransform.AngleProperty, kf);
        }
        if (RingScanTrailRotate != null)
        {
            double curT = RingScanTrailRotate.Angle % 360.0;
            var kfT = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            kfT.KeyFrames.Add(new DiscreteDoubleKeyFrame(curT, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            kfT.KeyFrames.Add(new SplineDoubleKeyFrame(curT + 150.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(820)), new KeySpline(0.42, 0.0, 0.28, 1.0)));
            kfT.KeyFrames.Add(new SplineDoubleKeyFrame(curT + 360.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1780)), new KeySpline(0.35, 0.0, 0.22, 1.0)));
            RingScanTrailRotate.BeginAnimation(RotateTransform.AngleProperty, kfT);
        }
    }

    private void AnimateScanDashTo(double fraction, int durationMs = 240)
    {
        if (RingScanArc == null) return;
        double clamped = Math.Clamp(fraction, 0.04, 1.0);
        _scanRingFraction = clamped;
        double targetOffset = RingScanCircumferenceDash * (1.0 - clamped);
        double trailOffset = RingScanCircumferenceDash * (1.0 - Math.Min(1.0, clamped + 0.05));

        if (!CanAnimate)
        {
            RingScanArc.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            RingScanArc.StrokeDashOffset = targetOffset;
            if (RingScanTrailArc != null)
            {
                RingScanTrailArc.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
                RingScanTrailArc.StrokeDashOffset = trailOffset;
            }
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        RingScanArc.BeginAnimation(Shape.StrokeDashOffsetProperty,
            new DoubleAnimation(targetOffset, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = ease });
        if (RingScanTrailArc != null)
        {
            RingScanTrailArc.BeginAnimation(Shape.StrokeDashOffsetProperty,
                new DoubleAnimation(trailOffset, TimeSpan.FromMilliseconds(durationMs + 50)) { EasingFunction = ease });
        }
    }

    private void UpdateRadialScanGrowth(int percent, long fileCount)
    {
        if (!_scanRingActive)
            BeginRadialScanGrowth();

        if (percent >= 0)
        {
            double frac = 0.05 + Math.Clamp(percent, 0, 100) / 100.0 * 0.93;
            AnimateScanDashTo(Math.Max(_scanRingFraction, frac), durationMs: 240);
            if (RadialHeroValue != null)
                RadialHeroValue.Text = $"{Math.Clamp(percent, 1, 99)}%";
        }
        else
        {
            StartLiveScanRingSpin();
            double nextFrac = Math.Min(0.84, _scanRingFraction + 0.035);
            AnimateScanDashTo(nextFrac, durationMs: 260);
            if (RadialHeroValue != null)
                RadialHeroValue.Text = fileCount > 0 ? $"{fileCount:N0}" : "…";
        }

        if (RadialHeroCaption != null)
            RadialHeroCaption.Text = fileCount > 0
                ? $"正在扫描 · {fileCount:N0} 项"
                : (Loc.IsEn ? "Scanning..." : "正在扫描");
    }

    private void SetChoreographedScanCounterText(int percent, long fileCount)
    {
        if (!_scanRingActive) return;
        if (RadialHeroValue != null)
            RadialHeroValue.Text = $"{Math.Clamp(percent, 0, 100)}%";
        if (RadialHeroCaption != null)
            RadialHeroCaption.Text = fileCount > 0
                ? $"正在扫描 · {fileCount:N0} 项"
                : (Loc.IsEn ? "Scanning..." : "正在扫描");
    }

    private void CompleteRadialScanGrowth()
        => CompleteRadialScanGrowthCore(fastSnap: false);

    private void CompleteRadialScanGrowthImmediate()
        => CompleteRadialScanGrowthCore(fastSnap: true);

    private void CompleteRadialScanGrowthCore(bool fastSnap)
    {
        _scanRingSpinning = false;
        if (!_scanRingActive) return;
        _scanRingActive = false;

        UpdateRadialStaticArcGeometries();

        if (RingScanArc == null || !CanAnimate || fastSnap)
        {
            HideScanArcsImmediate();
            SetStaticRingArcsOpacityImmediate(1.0);
            UpdateRadialCenterText();
            return;
        }

        int epoch = NextMotionEpoch(RingScanArc);
        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };

        // 满圆合拢瞬间触发外扩脉冲光晕，交叉熔铸为四色容量分布环
        FireRingPulseHalo(null);

        RingScanArc.BeginAnimation(Shape.StrokeDashOffsetProperty,
            new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(220)) { EasingFunction = easeOut });
        if (RingScanTrailArc != null)
        {
            RingScanTrailArc.BeginAnimation(Shape.StrokeDashOffsetProperty,
                new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(220)) { EasingFunction = easeOut });
        }

        if (RingScanRotate != null)
        {
            double cur = RingScanRotate.Angle;
            double rem = cur % 360.0;
            if (rem < 0) rem += 360.0;
            double target = rem > 2.0 ? cur + (360.0 - rem) : cur;
            RingScanRotate.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(cur, target, TimeSpan.FromMilliseconds(260)) { EasingFunction = easeOut });
        }
        if (RingScanTrailRotate != null)
        {
            double curT = RingScanTrailRotate.Angle;
            double remT = curT % 360.0;
            if (remT < 0) remT += 360.0;
            double targetT = remT > 2.0 ? curT + (360.0 - remT) : curT;
            RingScanTrailRotate.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(curT, targetT, TimeSpan.FromMilliseconds(260)) { EasingFunction = easeOut });
        }

        SetStaticRingArcsOpacityImmediate(0.0);
        FadeStaticRingArcsTo(1.0, durationMs: 420, beginMs: 70);
        AnimateRadialRingSettlePop();
        UpdateRadialCenterText();

        var fadeOut = new DoubleAnimation(RingScanArc.Opacity, 0.0, TimeSpan.FromMilliseconds(380))
        {
            BeginTime = TimeSpan.FromMilliseconds(70),
            EasingFunction = easeOut,
        };
        fadeOut.Completed += (_, _) =>
        {
            if (_scanRingActive || !IsMotionEpoch(RingScanArc, epoch)) return;
            HideScanArcsImmediate();
        };
        RingScanArc.BeginAnimation(OpacityProperty, fadeOut);

        if (RingScanTrailArc != null)
        {
            RingScanTrailArc.BeginAnimation(OpacityProperty,
                new DoubleAnimation(RingScanTrailArc.Opacity, 0.0, TimeSpan.FromMilliseconds(320))
                {
                    BeginTime = TimeSpan.FromMilliseconds(50),
                    EasingFunction = easeOut,
                });
        }
        if (RingTrackBase != null)
        {
            int trackEpoch = NextMotionEpoch(RingTrackBase);
            var trackFade = new DoubleAnimation(RingTrackBase.Opacity, 0.0, TimeSpan.FromMilliseconds(320))
            {
                BeginTime = TimeSpan.FromMilliseconds(70),
                EasingFunction = easeOut,
            };
            trackFade.Completed += (_, _) =>
            {
                if (_scanRingActive || !IsMotionEpoch(RingTrackBase, trackEpoch)) return;
                RingTrackBase.BeginAnimation(OpacityProperty, null);
                RingTrackBase.Visibility = Visibility.Collapsed;
                RingTrackBase.Opacity = 0;
            };
            RingTrackBase.BeginAnimation(OpacityProperty, trackFade);
        }
    }

    private void HideScanArcsImmediate()
    {
        if (RingScanArc != null)
        {
            RingScanArc.BeginAnimation(OpacityProperty, null);
            RingScanArc.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            RingScanArc.Visibility = Visibility.Collapsed;
            RingScanArc.Opacity = 1.0;
        }
        if (RingScanTrailArc != null)
        {
            RingScanTrailArc.BeginAnimation(OpacityProperty, null);
            RingScanTrailArc.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            RingScanTrailArc.Visibility = Visibility.Collapsed;
            RingScanTrailArc.Opacity = 0.26;
        }
        if (RingScanRotate != null)
        {
            RingScanRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            RingScanRotate.Angle = 0;
        }
        if (RingScanTrailRotate != null)
        {
            RingScanTrailRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            RingScanTrailRotate.Angle = -30;
        }
        if (RingTrackBase != null)
        {
            RingTrackBase.BeginAnimation(OpacityProperty, null);
            RingTrackBase.Visibility = Visibility.Collapsed;
            RingTrackBase.Opacity = 0;
        }
    }

    // ==================== AI 分析期：保留四色不等长分段环 + 变速呼吸旋转 + 归类同色共振脉冲 ====================

    private void SetRadialAiRotating(bool active)
    {
        if (active && _scanRingActive)
            CompleteRadialScanGrowthImmediate();

        _aiRingRotating = active;
        if (RadialRingCanvas == null || RadialRingRotate == null)
        {
            UpdateRadialCenterText();
            return;
        }

        if (active)
        {
            UpdateRadialStaticArcGeometries();
            SetStaticRingArcsOpacityImmediate(1.0);
            UpdateRadialCenterText();
            if (!CanAnimate) return;

            // 保留原本“长深灰 + 中灰 + 浅灰 + 短亮橙”四段不等长弧，用三段变速 Spline 曲线呈现灵动呼吸旋转
            double start = RadialRingRotate.Angle % 360.0;
            var livelySpin = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            livelySpin.KeyFrames.Add(new DiscreteDoubleKeyFrame(start, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            livelySpin.KeyFrames.Add(new SplineDoubleKeyFrame(start + 148.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(640)), new KeySpline(0.45, 0.0, 0.22, 1.0)));
            livelySpin.KeyFrames.Add(new SplineDoubleKeyFrame(start + 236.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1460)), new KeySpline(0.32, 0.0, 0.36, 1.0)));
            livelySpin.KeyFrames.Add(new SplineDoubleKeyFrame(start + 360.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2200)), new KeySpline(0.42, 0.0, 0.18, 1.0)));
            RadialRingRotate.BeginAnimation(RotateTransform.AngleProperty, livelySpin);

            StartRadialAiBreathingScale();
            return;
        }

        // 分析完成：平滑归零角度与缩放
        double curAngle = RadialRingRotate.Angle % 360.0;
        RadialRingRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        ResetRadialRingScaleImmediate();

        if (CanAnimate && Math.Abs(curAngle) > 0.5)
        {
            double settleTarget = curAngle > 180.0 ? 360.0 : 0.0;
            var settle = new DoubleAnimation(curAngle, settleTarget, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            settle.Completed += (_, _) =>
            {
                if (_aiRingRotating) return;
                RadialRingRotate.BeginAnimation(RotateTransform.AngleProperty, null);
                RadialRingRotate.Angle = 0;
            };
            RadialRingRotate.BeginAnimation(RotateTransform.AngleProperty, settle);
        }
        else
        {
            RadialRingRotate.Angle = 0;
        }

        UpdateRadialCenterText();
    }

    private void StartRadialAiBreathingScale()
    {
        if (!CanAnimate || !_aiRingRotating || RadialRingScale == null) return;
        var breath = new DoubleAnimation(0.985, 1.015, TimeSpan.FromMilliseconds(1350))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        RadialRingScale.BeginAnimation(ScaleTransform.ScaleXProperty, breath);
        RadialRingScale.BeginAnimation(ScaleTransform.ScaleYProperty, breath.Clone());
    }

    /// <summary>
    /// 右侧每有一个文件夹起飞归入色组时触发：四色分段环轻盈弹性脉冲，外围同步扩散一道分类同色光晕环。
    /// </summary>
    private void PulseRadialAiRing(string? accentHex = null)
    {
        if (!CanAnimate || !_aiRingRotating) return;

        int token = ++_aiPulseToken;
        FireRingPulseHalo(accentHex);

        if (RadialRingScale != null)
        {
            var scalePulse = new DoubleAnimationUsingKeyFrames();
            scalePulse.KeyFrames.Add(new EasingDoubleKeyFrame(1.052, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(115)),
                new CubicEase { EasingMode = EasingMode.EaseOut }));
            scalePulse.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(380)),
                new CubicEase { EasingMode = EasingMode.EaseInOut }));
            scalePulse.Completed += (_, _) =>
            {
                if (_aiPulseToken == token && _aiRingRotating)
                    StartRadialAiBreathingScale();
            };
            RadialRingScale.BeginAnimation(ScaleTransform.ScaleXProperty, scalePulse);
            RadialRingScale.BeginAnimation(ScaleTransform.ScaleYProperty, scalePulse.Clone());
        }
    }

    private void FireRingPulseHalo(string? accentHex)
    {
        if (!CanAnimate || RingPulseHalo == null || RingPulseHaloScale == null) return;
        int epoch = NextMotionEpoch(RingPulseHalo);

        RingPulseHalo.Stroke = !string.IsNullOrWhiteSpace(accentHex)
            ? CachedHexBrushConverter.Resolve(accentHex)
            : ThemeService.Brush("RingOrange");

        RingPulseHalo.BeginAnimation(OpacityProperty, null);
        RingPulseHaloScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        RingPulseHaloScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        var ease = new PowerEase { Power = 2.4, EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(560);
        var scaleAnim = new DoubleAnimation(0.96, 1.13, dur) { EasingFunction = ease };
        var opAnim = new DoubleAnimation(0.72, 0.0, dur) { EasingFunction = ease };
        opAnim.Completed += (_, _) =>
        {
            if (!IsMotionEpoch(RingPulseHalo, epoch)) return;
            RingPulseHalo.BeginAnimation(OpacityProperty, null);
            RingPulseHalo.Opacity = 0;
        };
        RingPulseHaloScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        RingPulseHaloScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim.Clone());
        RingPulseHalo.BeginAnimation(OpacityProperty, opAnim);
    }

    private void AnimateRadialRingSettlePop()
    {
        if (!CanAnimate || RadialRingScale == null) return;
        var ease = new BackEase { Amplitude = 0.24, EasingMode = EasingMode.EaseOut };
        var pop = new DoubleAnimation(0.955, 1.0, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease };
        RadialRingScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        RadialRingScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop.Clone());
    }

    private void ResetRadialRingScaleImmediate()
    {
        if (RadialRingScale == null) return;
        RadialRingScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        RadialRingScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        RadialRingScale.ScaleX = 1.0;
        RadialRingScale.ScaleY = 1.0;
    }

    private static Geometry CreateArcGeometry(double cx, double cy, double r, double startDeg, double endDeg)
    {
        double sweep = endDeg - startDeg;
        if (sweep <= 0.2) return Geometry.Empty;
        if (sweep >= 359.9) sweep = 359.5;

        double startRad = startDeg * Math.PI / 180.0;
        double endRad = (startDeg + sweep) * Math.PI / 180.0;

        var startPt = new Point(cx + r * Math.Cos(startRad), cy + r * Math.Sin(startRad));
        var endPt = new Point(cx + r * Math.Cos(endRad), cy + r * Math.Sin(endRad));

        var seg = new ArcSegment(endPt, new Size(r, r), 0, sweep > 180.0, SweepDirection.Clockwise, true);
        var fig = new PathFigure(startPt, new[] { seg }, false);
        var geo = new PathGeometry(new[] { fig });
        geo.Freeze();
        return geo;
    }

    private void SetTopScanBeam(bool active, double percent = -1)
    {
        if (TopScanBeamHost == null || TopScanBeamBar == null || TopScanBeamSweep == null || TopScanSweepTranslate == null)
            return;

        if (!active)
        {
            _topBeamSweeping = false;
            TopScanSweepTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            TopScanBeamSweep.Opacity = 0;

            if (!CanAnimate)
            {
                TopScanBeamBar.BeginAnimation(WidthProperty, null);
                TopScanBeamBar.Width = 0;
                TopScanBeamBar.Opacity = 0;
                return;
            }

            double hostW = TopScanBeamHost.ActualWidth > 0 ? TopScanBeamHost.ActualWidth : Math.Max(400, ActualWidth);
            TopScanBeamBar.BeginAnimation(WidthProperty, new DoubleAnimation(hostW, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
            var fade = new DoubleAnimation(TopScanBeamBar.Opacity, 0, TimeSpan.FromMilliseconds(260))
            {
                BeginTime = TimeSpan.FromMilliseconds(120),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            fade.Completed += (_, _) =>
            {
                if (_topBeamSweeping) return;
                TopScanBeamBar.BeginAnimation(OpacityProperty, null);
                TopScanBeamBar.BeginAnimation(WidthProperty, null);
                TopScanBeamBar.Opacity = 0;
                TopScanBeamBar.Width = 0;
            };
            TopScanBeamBar.BeginAnimation(OpacityProperty, fade);
            return;
        }

        double totalW = TopScanBeamHost.ActualWidth > 0 ? TopScanBeamHost.ActualWidth : Math.Max(400, ActualWidth);

        if (percent >= 0)
        {
            if (_topBeamSweeping)
            {
                _topBeamSweeping = false;
                TopScanSweepTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                TopScanBeamSweep.Opacity = 0;
            }
            TopScanBeamBar.BeginAnimation(OpacityProperty, null);
            TopScanBeamBar.Opacity = 1;
            double targetW = Math.Clamp(percent, 2, 100) / 100.0 * totalW;
            if (!CanAnimate)
            {
                TopScanBeamBar.Width = targetW;
                return;
            }
            TopScanBeamBar.BeginAnimation(WidthProperty, new DoubleAnimation(targetW, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
            return;
        }

        TopScanBeamBar.BeginAnimation(OpacityProperty, null);
        TopScanBeamBar.Opacity = 0;
        TopScanBeamSweep.Opacity = 0.92;
        if (_topBeamSweeping || !CanAnimate) return;
        _topBeamSweeping = true;

        var sweep = new DoubleAnimation(-260, totalW + 40, TimeSpan.FromMilliseconds(1350))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        TopScanSweepTranslate.BeginAnimation(TranslateTransform.XProperty, sweep);
    }
}
