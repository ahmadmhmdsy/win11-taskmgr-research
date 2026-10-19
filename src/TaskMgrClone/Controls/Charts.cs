using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TaskMgrClone.Core;

namespace TaskMgrClone.Controls;

public sealed class SeriesDef
{
    public required RingBuffer Data { get; init; }
    public required Brush Stroke { get; init; }
    public Brush? Fill { get; init; }
    public double Scale { get; init; } = 100;
}

/// <summary>Small filled spark line used by the performance component list and process rows.</summary>
public sealed class MiniGraph : FrameworkElement
{
    public RingBuffer? Data { get; set; }
    public Brush Line { get; set; } = Brushes.DodgerBlue;
    public double Max { get; set; } = 100;
    public bool AutoScale { get; set; } = true;
    public bool ShowGrid { get; set; } = true;

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 1 || h <= 1) return;

        if (ShowGrid)
        {
            var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1);
            for (int i = 1; i < 4; i++)
            {
                var x = w * i / 4.0;
                dc.DrawLine(gridPen, new Point(x, 0), new Point(x, h));
            }
            for (int i = 1; i < 2; i++)
            {
                var y = h * i / 2.0;
                dc.DrawLine(gridPen, new Point(0, y), new Point(w, y));
            }
        }

        if (Data == null || Data.Count < 2) return;
        var max = AutoScale ? Math.Max(1, Data.Max) : Math.Max(0.0001, Max);
        var count = Data.Count;
        var step = w / Math.Max(1, count - 1);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var first = Data[0];
            var startY = h - Math.Clamp(first / max, 0, 1) * h;
            ctx.BeginFigure(new Point(0, h), true, true);
            ctx.LineTo(new Point(0, startY), true, false);
            for (int i = 1; i < count; i++)
            {
                var x = i * step;
                var y = h - Math.Clamp(Data[i] / max, 0, 1) * h;
                ctx.LineTo(new Point(x, y), true, false);
            }
            ctx.LineTo(new Point(w, h), false, false);
        }
        geometry.Freeze();

        var fill = new LinearGradientBrush(
            Color.FromArgb(150, ((SolidColorBrush)Line).Color.R, ((SolidColorBrush)Line).Color.G, ((SolidColorBrush)Line).Color.B),
            Color.FromArgb(20, ((SolidColorBrush)Line).Color.R, ((SolidColorBrush)Line).Color.G, ((SolidColorBrush)Line).Color.B),
            new Point(0, 0), new Point(0, 1));
        dc.DrawGeometry(fill, new Pen(Line, 1.2), geometry);
    }
}

/// <summary>Large performance graph: grid, optional multiple series, fixed or auto scale.</summary>
public sealed class AreaGraph : FrameworkElement
{
    public List<SeriesDef> Series { get; } = new();
    public double Max { get; set; } = 100;
    public string LeftLabel { get; set; } = "";
    public bool ShowAxis { get; set; }
    public Brush AxisBrush { get; set; } = Brushes.Gray;
    public int HorizontalLines { get; set; } = 4;
    public int VerticalLines { get; set; } = 10;
    public bool AutoScale { get; set; }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 2 || h <= 2) return;

        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)), 1);
        for (int i = 1; i < HorizontalLines; i++)
        {
            var y = h * i / (double)HorizontalLines;
            dc.DrawLine(gridPen, new Point(0, y), new Point(w, y));
        }
        for (int i = 1; i < VerticalLines; i++)
        {
            var x = w * i / (double)VerticalLines;
            dc.DrawLine(gridPen, new Point(x, 0), new Point(x, h));
        }
        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 1), new Rect(0.5, 0.5, Math.Max(0, w - 1), Math.Max(0, h - 1)));

        double max = Max;
        if (AutoScale)
        {
            max = Math.Max(1, Series.SelectMany(s => Enumerable.Range(0, s.Data.Count).Select(i => s.Data[i])).DefaultIfEmpty(0).Max());
        }
        max = Math.Max(0.0001, max);

        foreach (var def in Series)
        {
            var data = def.Data;
            if (data.Count < 2) continue;
            var scale = def.Scale <= 0 ? max : def.Scale;
            var count = data.Count;
            var step = w / Math.Max(1, count - 1);
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(0, h), true, true);
                for (int i = 0; i < count; i++)
                {
                    var x = i * step;
                    var y = h - Math.Clamp(data[i] / scale, 0, 1) * h;
                    ctx.LineTo(new Point(x, y), true, false);
                }
                ctx.LineTo(new Point(w, h), false, false);
            }
            geometry.Freeze();

            Brush? fill = def.Fill;
            if (fill == null && def.Stroke is SolidColorBrush scb)
            {
                fill = new LinearGradientBrush(
                    Color.FromArgb(120, scb.Color.R, scb.Color.G, scb.Color.B),
                    Color.FromArgb(10, scb.Color.R, scb.Color.G, scb.Color.B),
                    new Point(0, 0), new Point(0, 1));
            }
            dc.DrawGeometry(fill, new Pen(def.Stroke, 1.4), geometry);
        }
    }
}

/// <summary>Per logical processor heat tiles ("Logical processors" CPU view).</summary>
public sealed class CpuHeatmap : FrameworkElement
{
    public double[] Values { get; set; } = Array.Empty<double>();
    public int Columns { get; set; } = 4;

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 2 || h <= 2 || Values.Length == 0) return;
        Columns = Math.Max(1, Columns);
        var rows = (int)Math.Ceiling(Values.Length / (double)Columns);
        var cellW = (w - (Columns - 1) * 3) / Columns;
        var cellH = (h - (rows - 1) * 3) / rows;
        if (cellW <= 1 || cellH <= 1) return;
        for (int i = 0; i < Values.Length; i++)
        {
            var r = i / Columns;
            var c = i % Columns;
            var rect = new Rect(c * (cellW + 3), r * (cellH + 3), cellW, cellH);
            dc.DrawRoundedRectangle(new SolidColorBrush(HeatColor(Values[i])), null, rect, 2, 2);
        }
    }

    public static Color HeatColor(double percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        // Task-Manager-like heat scale: dark blue -> steel blue -> amber -> red
        if (percent < 50)
        {
            var t = percent / 50.0;
            return Lerp(Color.FromRgb(0x25, 0x3B, 0x55), Color.FromRgb(0x3C, 0x86, 0xC8), t);
        }
        if (percent < 80)
        {
            var t = (percent - 50) / 30.0;
            return Lerp(Color.FromRgb(0x3C, 0x86, 0xC8), Color.FromRgb(0xE0, 0xB0, 0x2B), t);
        }
        var t2 = (percent - 80) / 20.0;
        return Lerp(Color.FromRgb(0xE0, 0xB0, 0x2B), Color.FromRgb(0xE0, 0x3C, 0x2B), t2);
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));
}

public sealed class CompositionSegment
{
    public string Label { get; init; } = "";
    public double Value { get; init; }
    public Brush Brush { get; init; } = Brushes.Gray;
}

/// <summary>Memory composition bar (in use / compressed / modified / standby / free).</summary>
public sealed class CompositionBar : FrameworkElement
{
    public List<CompositionSegment> Segments { get; } = new();
    public double Total { get; set; } = 1;

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 2 || h <= 2) return;
        var total = Math.Max(1, Total);
        double x = 0;
        var gap = Segments.Count > 1 ? 2.0 : 0.0;
        var totalGap = gap * (Segments.Count - 1);
        var usable = Math.Max(1, w - totalGap);
        foreach (var segment in Segments)
        {
            var width = Math.Clamp(segment.Value / total * usable, 0, usable);
            if (width <= 0) continue;
            dc.DrawRoundedRectangle(segment.Brush, null, new Rect(x, 0, width, h), 2, 2);
            x += width + gap;
        }
    }
}

/// <summary>Horizontal 0-100 bar with a caption, used by disk/network/gpu adapter rows.</summary>
public sealed class PercentBar : FrameworkElement
{
    public double Value { get; set; }
    public Brush Fill { get; set; } = Brushes.DodgerBlue;

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 1 || h <= 1) return;
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x3F)), null, new Rect(0, 0, w, h), h / 2, h / 2);
        var width = Math.Clamp(Value / 100.0, 0, 1) * w;
        if (width > 1)
            dc.DrawRoundedRectangle(Fill, null, new Rect(0, 0, width, h), h / 2, h / 2);
    }
}

public static class Format
{
    public static string Bytes(ulong value)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
        double size = value;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return unit switch
        {
            0 => $"{size:0} {units[unit]}",
            1 => $"{size:0} {units[unit]}",
            _ => $"{size:0.0} {units[unit]}",
        };
    }

    public static string Rate(double bytesPerSec)
    {
        if (bytesPerSec < 1024) return $"{bytesPerSec:0} B/s";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024:0.0} KB/s";
        if (bytesPerSec < 1024.0 * 1024 * 1024) return $"{bytesPerSec / 1024 / 1024:0.0} MB/s";
        return $"{bytesPerSec / 1024 / 1024 / 1024:0.00} GB/s";
    }

    public static string Mbps(double bytesPerSec) => $"{bytesPerSec * 8 / 1_000_000:0.0} Mbps";

    public static string Uptime(TimeSpan span) => $"{(int)span.TotalDays}:{span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}";

    public static string Duration(TimeSpan span)
    {
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}";
        if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes}:{span.Seconds:00}";
        return $"{span.TotalSeconds:0} sec";
    }

    public static string Percent(double value) => value.ToString("0", CultureInfo.CurrentCulture) + "%";
}
