using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TaskMgrClone.UI;

/// <summary>Extracts shell icons for process image paths, with a small cache.</summary>
public static class ProcessIcons
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Get(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        lock (Cache) { if (Cache.TryGetValue(path, out var cached)) return cached; }

        ImageSource? source = null;
        try
        {
            if (File.Exists(path))
            {
                using var icon = Icon.ExtractAssociatedIcon(path);
                if (icon != null)
                {
                    source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                }
            }
        }
        catch { /* icons are cosmetic */ }

        lock (Cache) Cache[path] = source;
        return source;
    }
}
