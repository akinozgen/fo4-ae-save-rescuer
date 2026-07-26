using System.Windows.Media;
using System.Windows.Media.Imaging;
using SaveRescuer.Core;

namespace SaveRescuer.App;

/// <summary>Turns the screenshot a save carries into something WPF can draw.</summary>
public static class Screenshots
{
    /// <summary>
    /// The save stores the shot as RGBA rows. WPF wants blue first, and the stored alpha cannot be
    /// trusted, so the channels are reordered and the image is drawn fully opaque.
    /// </summary>
    public static BitmapSource? Load(SaveSummary summary)
    {
        var rgba = summary.ReadScreenshot();
        if (rgba is null || summary.ShotWidth == 0 || summary.ShotHeight == 0) return null;

        var width = (int)summary.ShotWidth;
        var height = (int)summary.ShotHeight;
        if (rgba.Length < width * height * 4) return null;

        var bgra = new byte[rgba.Length];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            bgra[i] = rgba[i + 2];
            bgra[i + 1] = rgba[i + 1];
            bgra[i + 2] = rgba[i];
            bgra[i + 3] = 255;
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
