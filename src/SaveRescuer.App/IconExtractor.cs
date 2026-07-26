using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace SaveRescuer.App;

/// <summary>
/// Reads the icon out of an executable at runtime so the launch button can show the game's own
/// artwork. Nothing is copied or stored - the image comes from the user's installation.
/// </summary>
internal static class IconExtractor
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint count);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public static BitmapSource? FromExecutable(string? path)
    {
        if (path is null || !File.Exists(path)) return null;

        var large = new IntPtr[1];
        if (ExtractIconEx(path, 0, large, null, 1) == 0 || large[0] == IntPtr.Zero) return null;

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                large[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch { return null; }
        finally { DestroyIcon(large[0]); }
    }
}
