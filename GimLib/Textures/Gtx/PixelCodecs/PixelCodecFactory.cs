namespace GimLib.Textures.Gtx.PixelCodecs;

internal static class PixelCodecFactory
{
    /// <summary>
    ///     Returns a pixel codec for the specified format.
    /// </summary>
    /// <param name="format"></param>
    /// <returns>The pixel codec, or <see langword="null" /> if one does not exist.</returns>
    public static PixelCodec? Create(PixelDataFormat format)
    {

        if (format.HasFlag(PixelDataFormat.FormatRgb565)) return new Rgb565PixelCodec();
        
        if (format.HasFlag(PixelDataFormat.FormatRgba5551)) return new Rgba5551PixelCodec();
        
        if (format.HasFlag(PixelDataFormat.FormatRgba4444)) return new Rgba4444PixelCodec();
        
        if (format.HasFlag(PixelDataFormat.FormatRgba8888)) return new Rgba8888PixelCodec();

        if (format.HasFlag(PixelDataFormat.FormatIndexed4)) return new Index4PixelCodec();

        if (format.HasFlag(PixelDataFormat.FormatIndexed8)) return new Index8PixelCodec();

        if (format.HasFlag(PixelDataFormat.FormatDXT1Rgba)) return new Dxt1PixelCodec();

        if (format.HasFlag(PixelDataFormat.SpecialFormatDXT1)) return new Dxt1PixelCodec();

        if (format.HasFlag(PixelDataFormat.FormatDXT3)) return new Dxt3PixelCodec();
        
        if (format.HasFlag(PixelDataFormat.SpecialFormatDXT3)) return new Dxt3PixelCodec();

        if (format.HasFlag(PixelDataFormat.FormatDXT5)) return new Dxt5PixelCodec();
        
        if (format.HasFlag(PixelDataFormat.SpecialFormatDXT5)) return new Dxt5PixelCodec();

        return null;
    }
}