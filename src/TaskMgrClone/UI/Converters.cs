using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using TaskMgrClone.Controls;

namespace TaskMgrClone.UI;

public sealed class BytesConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ulong u => Format.Bytes(u),
        long l => Format.Bytes((ulong)Math.Max(0, l)),
        double d => Format.Bytes((ulong)Math.Max(0, d)),
        int i => Format.Bytes((ulong)Math.Max(0, i)),
        _ => "",
    };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class RateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? Format.Rate(d) : "";
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class MbpsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? Format.Mbps(d) : "";
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class PercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        double d => d < 0.5 ? "0%" : $"{d:0}%",
        float f => f < 0.5 ? "0%" : $"{f:0}%",
        int i => i + "%",
        ulong u => u + "%",
        _ => "",
    };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class Percent1Converter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? $"{d:0.0}%" : "";
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class PowerUsageConverter : IValueConverter
{
    public static string Bucket(double value) => value switch
    {
        <= 1 => "Very low",
        <= 2 => "Low",
        <= 3 => "Moderate",
        <= 4 => "High",
        _ => "Very high",
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d and > 0 ? Bucket(d) : "";
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class ProcessIconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        ProcessIcons.Get(value as string);
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null || (value is string s && s.Length == 0) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Value in 0..100 to a fixed-width bar in a Grid cell (parameter = max width).</summary>
public sealed class PercentToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double pct = value is double d ? d : 0;
        double max = 100;
        if (parameter is string s && double.TryParse(s, out var parsed)) max = parsed;
        return Math.Clamp(pct, 0, 100) / 100.0 * max;
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class GroupTitleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        TaskMgrClone.Core.ProcessGroup.Apps => "Apps",
        TaskMgrClone.Core.ProcessGroup.Background => "Background processes",
        TaskMgrClone.Core.ProcessGroup.Windows => "Windows processes",
        _ => "",
    };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
