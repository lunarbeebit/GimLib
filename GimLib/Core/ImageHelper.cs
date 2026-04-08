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
    public static IMagickImage<TQuantumType> Resize<TQuantumType>(IMagickImage<TQuantumType> image, int width, int height)
    where TQuantumType : struct, IConvertible
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
    public static bool TryBuildExactPalette<TQuantumType>(IMagickImage<TQuantumType> image, int maxColors, out List<IMagickColor<TQuantumType>> palette)
    where TQuantumType : struct, IConvertible
    {
        palette = new List<IMagickColor<TQuantumType>>(maxColors);
        bool exactPaletteCreated = false;
        foreach (var pixel in image.GetPixels())
        {
            var color = pixel.ToColor();
            if(color is null) continue;
            if(palette.Contains(color)) continue;
        
            if (palette.Count == maxColors)
            {   
                exactPaletteCreated = false;
                break;
            }

            palette.Add(color);
            exactPaletteCreated = true;
        }

        return exactPaletteCreated;
    }

    /// <summary>
    ///     Gets the pixel data for the specified image frame as a byte array.
    /// </summary>
    /// <typeparam name="TPixel"></typeparam>
    /// <param name="imageFrame"></param>
    /// <returns>Pixel data as a byte array.</returns>
    public static TQuantumType[] GetPixelDataAsBytes<TQuantumType>(IMagickImage<TQuantumType> imageFrame)
    where TQuantumType : struct, IConvertible
    {
        var data = new TQuantumType[imageFrame.Width * imageFrame.Height];

        int pixelNumber = 0;
        foreach(var pixel in imageFrame.GetPixels()){
            var pixelData = pixel.ToArray();
            for(int i = 0; i < pixelData.Length; i++)
            {
                data[pixelNumber] = pixelData[i];
                pixelNumber++;
            }
        }
        return data;
    }

    /// <summary>
    ///     Gets the pixel data for the specified indexed image frame as a byte array.
    /// </summary>
    /// <typeparam name="TPixel"></typeparam>
    /// <param name="imageFrame"></param>
    /// <returns>Pixel data as a byte array.</returns>
    public static TQuantumType[] GetPixelDataAsBytes<TQuantumType>(IMagickImage<TQuantumType> imageFrame, int maxColors)
        where TQuantumType : struct, IConvertible
    {
        var data = new TQuantumType[imageFrame.Width * imageFrame.Height];
        var palette = new List<IMagickColor<TQuantumType>>(maxColors);
        var i = 0;
        foreach (var pixel in imageFrame.GetPixels())
        {
            var color = pixel.ToColor();
            if (color is null)
            {
                return GetPixelDataAsBytes(imageFrame);
            }
            if (palette.Contains(color))
            {
                data[i] = (TQuantumType) Convert.ChangeType(palette.FindIndex(0, color.Equals) , typeof(TQuantumType));
                i++;
                continue;
            }
        
            if (palette.Count == maxColors)
            {   
                return GetPixelDataAsBytes(imageFrame); 
            }

            palette.Add(color);
            data[i] = (TQuantumType) Convert.ChangeType(palette.FindIndex(0, color.Equals) , typeof(TQuantumType));
            i++;
        }

        return data;
    }
}