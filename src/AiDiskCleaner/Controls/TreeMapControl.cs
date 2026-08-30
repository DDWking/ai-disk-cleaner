using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Controls;

/// <summary>自绘的 squarified TreeMap 控件，用于展示磁盘空间分布。</summary>
public class TreeMapControl : FrameworkElement
{
    private List<FileEntry> _items = new();
    private readonly List<RectInfo> _rects = new();

    /// <summary>点击某个目录色块时触发（用于钻入子目录）。</summary>
    public event Action<FileEntry>? DirectoryClicked;

    public void SetItems(IEnumerable<FileEntry> items)
    {
        _items = items.Where(i => i.Size > 0).ToList();
        InvalidateVisual();
    }

    private sealed class RectInfo
    {
        public Rect Bounds;
        public FileEntry Entry = null!;
    }

    private sealed class LayoutRect
    {
        public double X, Y, Width, Height;
        public FileEntry Entry = null!;
    }

    protected override void OnRender(DrawingContext dc)
    {
        _rects.Clear();
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0b, 0x12, 0x20)), null,
            new Rect(0, 0, ActualWidth, ActualHeight));
        if (_items.Count == 0 || ActualWidth < 4 || ActualHeight < 4) return;

        var layouts = Squarify(_items, 2, 2, ActualWidth - 4, ActualHeight - 4);
        Color[] palette =
        {
            Color.FromRgb(0x3b, 0x82, 0xf6), Color.FromRgb(0x63, 0x66, 0xf1),
            Color.FromRgb(0x8b, 0x5c, 0xf6), Color.FromRgb(0x0e, 0xa5, 0xe9),
            Color.FromRgb(0x22, 0xc5, 0x5e), Color.FromRgb(0xea, 0xb3, 0x08),
            Color.FromRgb(0xf9, 0x73, 0x16), Color.FromRgb(0xec, 0x48, 0x99),
        };

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        int idx = 0;
        foreach (var l in layouts)
        {
            const double pad = 1.5;
            var bounds = new Rect(l.X + pad, l.Y + pad,
                Math.Max(0, l.Width - pad * 2), Math.Max(0, l.Height - pad * 2));
            if (bounds.Width <= 0 || bounds.Height <= 0) { idx++; continue; }
            var color = l.Entry.IsDirectory ? palette[idx % palette.Length] : Color.FromRgb(0x64, 0x74, 0x8b);
            dc.DrawRectangle(new SolidColorBrush(color), null, bounds);
            if (bounds.Width > 34 && bounds.Height > 18)
            {
                var ft = new FormattedText(l.Entry.Name, CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.White, dpi);
                ft.MaxTextWidth = bounds.Width - 8;
                ft.MaxTextHeight = bounds.Height - 8;
                ft.Trimming = TextTrimming.CharacterEllipsis;
                dc.DrawText(ft, new Point(bounds.X + 4, bounds.Y + 4));
            }
            _rects.Add(new RectInfo { Bounds = bounds, Entry = l.Entry });
            idx++;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        var hit = _rects.FirstOrDefault(r => r.Bounds.Contains(p));
        if (hit != null)
        {
            Cursor = hit.Entry.IsDirectory ? Cursors.Hand : Cursors.Arrow;
            ToolTip = hit.Entry.IsDirectory
                ? $"{hit.Entry.Name} — {FileEntry.FormatSize(hit.Entry.Size)}（点击进入）"
                : $"{hit.Entry.Name} — {FileEntry.FormatSize(hit.Entry.Size)}";
        }
        else { Cursor = Cursors.Arrow; ToolTip = null; }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        var hit = _rects.FirstOrDefault(r => r.Bounds.Contains(p));
        if (hit != null && hit.Entry.IsDirectory) DirectoryClicked?.Invoke(hit.Entry);
        e.Handled = true;
        base.OnMouseLeftButtonDown(e);
    }

    private static List<LayoutRect> Squarify(List<FileEntry> items, double x, double y, double w, double h)
    {
        var sorted = items.OrderByDescending(i => i.Size).ToList();
        var result = new List<LayoutRect>();

        static double Worst(List<FileEntry> row, double length)
        {
            double s = row.Sum(n => (double)n.Size);
            if (s <= 0) return double.PositiveInfinity;
            double mx = row.Max(n => (double)n.Size);
            double mn = row.Min(n => (double)n.Size);
            if (mn <= 0) return double.PositiveInfinity;
            return Math.Max((length * length * mx) / (s * s), (s * s) / (length * length * mn));
        }

        void Place(List<FileEntry> nodes, double rx, double ry, double rw, double rh)
        {
            if (nodes.Count == 0) return;
            if (nodes.Count == 1)
            {
                result.Add(new LayoutRect { X = rx, Y = ry, Width = rw, Height = rh, Entry = nodes[0] });
                return;
            }
            double length = Math.Min(rw, rh);
            bool horizontal = rw >= rh;
            var row = new List<FileEntry> { nodes[0] };
            int i = 1;
            double cur = Worst(row, length);
            while (i < nodes.Count)
            {
                var cand = new List<FileEntry>(row) { nodes[i] };
                double w2 = Worst(cand, length);
                if (w2 > cur) break;
                row = cand; cur = w2; i++;
            }
            var rest = nodes.Skip(i).ToList();
            double rowSum = row.Sum(n => (double)n.Size);
            double total = rowSum + rest.Sum(n => (double)n.Size);
            double frac = rowSum / total;

            if (horizontal)
            {
                double rowW = rw * frac;
                double yy = ry;
                foreach (var n in row)
                {
                    double hh = rh * ((double)n.Size / rowSum);
                    result.Add(new LayoutRect { X = rx, Y = yy, Width = rowW, Height = hh, Entry = n });
                    yy += hh;
                }
                Place(rest, rx + rowW, ry, rw - rowW, rh);
            }
            else
            {
                double rowH = rh * frac;
                double xx = rx;
                foreach (var n in row)
                {
                    double ww = rw * ((double)n.Size / rowSum);
                    result.Add(new LayoutRect { X = xx, Y = ry, Width = ww, Height = rowH, Entry = n });
                    xx += ww;
                }
                Place(rest, rx, ry + rowH, rw, rh - rowH);
            }
        }

        Place(sorted, x, y, w, h);
        return result;
    }
}
