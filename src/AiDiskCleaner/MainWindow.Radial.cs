using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services;

namespace AiDiskCleaner;

/// <summary>
/// 方案 1（Swiss Radial Ring）：亮橙分段圆环容量图 + 顶部 2.5px 无感扫盘激光束。
/// </summary>
public partial class MainWindow
{
    private long _radialGreenBytes;
    private long _radialYellowBytes;
    private long _radialRedBytes;
    private long _radialFreeBytes;
    private bool _topBeamSweeping;

    private void UpdateRadialRing(long greenBytes, long yellowBytes, long redBytes, long freeBytes)
    {
        _radialGreenBytes = Math.Max(0, greenBytes);
        _radialYellowBytes = Math.Max(0, yellowBytes);
        _radialRedBytes = Math.Max(0, redBytes);
        _radialFreeBytes = Math.Max(0, freeBytes);

        if (RingArcOrange == null || RingArcYellow == null || RingArcRed == null || RingArcFree == null)
            return;

        long total = _radialGreenBytes + _radialYellowBytes + _radialRedBytes + _radialFreeBytes;
        if (total <= 0)
        {
            RingArcOrange.Data = Geometry.Empty;
            RingArcYellow.Data = Geometry.Empty;
            RingArcRed.Data = Geometry.Empty;
            RingArcFree.Data = CreateArcGeometry(108, 108, 86, -90, 269.5);
            UpdateRadialCenterText();
            return;
        }

        // 计算非零分段的角度，保证微小高亮分片至少占 14° 可见，相邻分片留 3.4° 瑞士白色呼吸缝隙
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

        UpdateRadialCenterText();
    }

    private void UpdateRadialCenterText()
    {
        if (RadialHeroValue == null || RadialHeroCaption == null) return;

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

        // 不定长激光脉冲：2.5px 顶部光束连续掠过
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
