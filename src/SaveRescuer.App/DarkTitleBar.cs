using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SaveRescuer.App;

/// <summary>
/// Asks Windows for a dark title bar so the frame matches the window it belongs to. Silently does
/// nothing on builds that do not know the attribute.
/// </summary>
internal static class DarkTitleBar
{
    private const int UseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    public static void Apply(Window window)
    {
        void Set()
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            var on = 1;
            try { DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref on, sizeof(int)); }
            catch (DllNotFoundException) { /* older Windows: keep the light frame */ }
        }

        if (window.IsLoaded) Set();
        else window.SourceInitialized += (_, _) => Set();
    }
}
