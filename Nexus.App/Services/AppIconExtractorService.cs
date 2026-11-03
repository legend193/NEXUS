using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nexus.App.Services;

public static class AppIconExtractorService
{
    private static readonly ConcurrentDictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? GetProcessIcon(int processId, string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return null;

        if (IconCache.TryGetValue(processName, out var cached))
        {
            return cached;
        }

        try
        {
            string? exePath = null;
            if (processId > 0)
            {
                try
                {
                    using var proc = Process.GetProcessById(processId);
                    exePath = proc.MainModule?.FileName;
                }
                catch { }
            }

            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                IconCache[processName] = null;
                return null;
            }

            using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (sysIcon != null)
            {
                var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                    sysIcon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                bitmapSource.Freeze();
                IconCache[processName] = bitmapSource;
                return bitmapSource;
            }
        }
        catch
        {
            // Silently fallback if access denied or no associated icon
        }

        IconCache[processName] = null;
        return null;
    }
}
