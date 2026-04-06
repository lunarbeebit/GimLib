namespace GimLib.Textures.Gtx.PaletteCodecs;

internal static class PaletteCodecFactory
{
    /// <summary>
    ///     Returns a palette codec for the specified format.
    /// </summary>
    /// <param name="format"></param>
    /// <returns>The palette codec, or <see langword="null" /> if one does not exist.</returns>
    public static PaletteCodec? Create(PixelDataFormat format)
    {
        if (format.HasFlag(PixelDataFormat.FormatRgb565)) return new Rgb565PaletteCodec();
        if (format.HasFlag(PixelDataFormat.FormatRgba5551)) return new Rgba5551PaletteCodec();
        if (format.HasFlag(PixelDataFormat.FormatRgba4444)) return new Rgba4444PaletteCodec();
        if (format.HasFlag(PixelDataFormat.FormatRgba8888)) return new Rgba8888PaletteCodec();
        if (format.HasFlag(PixelDataFormat.FormatXrgb8888)) return new Argb8888PaletteCodec();
        if (format.HasFlag(PixelDataFormat.FormatArgb8888)) return new Argb8888PaletteCodec();
        
        return null;
    }
}