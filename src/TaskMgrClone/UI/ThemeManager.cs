using System.Windows;
using System.Windows.Media;

namespace TaskMgrClone.UI;

/// <summary>Applies the light/dark palette over the DynamicResource brushes used across the UI.</summary>
public static class ThemeManager
{
    private static readonly (string Key, Color Dark, Color Light)[] Palette =
    {
        ("WindowBg", Color.FromRgb(0x20, 0x20, 0x20), Color.FromRgb(0xF3, 0xF3, 0xF3)),
        ("TitleBarBg", Color.FromRgb(0x2B, 0x2B, 0x2B), Color.FromRgb(0xEE, 0xEE, 0xEE)),
        ("NavBg", Color.FromRgb(0x27, 0x27, 0x27), Color.FromRgb(0xEA, 0xEA, 0xEA)),
        ("ContentBg", Color.FromRgb(0x20, 0x20, 0x20), Color.FromRgb(0xF3, 0xF3, 0xF3)),
        ("CardBg", Color.FromRgb(0x2B, 0x2B, 0x2B), Color.FromRgb(0xFA, 0xFA, 0xFA)),
        ("ListBg", Color.FromRgb(0x23, 0x23, 0x23), Color.FromRgb(0xF9, 0xF9, 0xF9)),
        ("BorderBrush", Color.FromRgb(0x3A, 0x3A, 0x3A), Color.FromRgb(0xD6, 0xD6, 0xD6)),
        ("TextPrimary", Color.FromRgb(0xF2, 0xF2, 0xF2), Color.FromRgb(0x1B, 0x1B, 0x1B)),
        ("TextSecondary", Color.FromRgb(0xA0, 0xA0, 0xA0), Color.FromRgb(0x5C, 0x5C, 0x5C)),
        ("TextTertiary", Color.FromRgb(0x7A, 0x7A, 0x7A), Color.FromRgb(0x8A, 0x8A, 0x8A)),
        ("SelectionBg", Color.FromRgb(0x33, 0x33, 0x33), Color.FromRgb(0xDC, 0xE8, 0xF6)),
        ("HoverBg", Color.FromRgb(0x2F, 0x2F, 0x2F), Color.FromRgb(0xE8, 0xE8, 0xE8)),
        ("GridLine", Color.FromRgb(0x3A, 0x3A, 0x3A), Color.FromRgb(0xD9, 0xD9, 0xD9)),
        ("BarEmpty", Color.FromRgb(0x3F, 0x3F, 0x3F), Color.FromRgb(0xD0, 0xD0, 0xD0)),
        ("SelectionFg", Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x10, 0x10, 0x10)),
    };

    public static bool IsDark { get; private set; } = true;

    public static void Apply(string theme)
    {
        IsDark = !theme.Equals("Light", StringComparison.OrdinalIgnoreCase);
        var res = Application.Current?.Resources;
        if (res == null) return;
        foreach (var (key, dark, light) in Palette)
        {
            var color = IsDark ? dark : light;
            if (res[key] is SolidColorBrush { IsFrozen: false } brush) brush.Color = color;
            else res[key] = new SolidColorBrush(color);
        }
    }
}
