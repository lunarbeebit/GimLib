using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ImageMagick;

namespace GimLib.Core;

/// <summary>
///     Contains helper methods for images.
/// </summary>
internal class ImageHelper
{
    public static IMagickImage Resize(MagickImage image, int width, int height)
    {
        // If no resizing is necessary, return the image as-is.
        if (image.Width == width && image.Height == height) return image;

        var newImage = image.Clone();
        //newImage.Mutate(x => x.Resize(width, height));
        newImage.Resize((uint)width, (uint)height);

        return newImage;
    }

    /// <summary>
    ///     Builds an exact palette with up to the maximum amount of colors specified in <paramref name="maxColors" />.
    /// </summary>
    /// <typeparam name="TPixel"></typeparam>
    /// <param name="image"></param>
    /// <param name="maxColors">The maximum number of colors the palette may contain.</param>
    /// <param name="palette">
    ///     When this method returns, contains the palette that was created, if a palette was created;
    ///     otherwise, <see langword="null" /> if a palette was not created.
    ///     This parameter is passed uninitialized.
    /// </param>
    /// <returns><see langword="true" /> is an exact palette was created; otherwise <see langword="false" />.</returns>
    public static bool TryBuildExactPalette(MagickImage image, out MagickImage palette)
    {
        palette = (MagickImage)image.UniqueColors();
        return palette == null;
    }

}