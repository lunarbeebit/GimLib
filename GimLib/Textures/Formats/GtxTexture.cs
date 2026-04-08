using GimLib.Textures.Gtx;

namespace GimLib.Textures.Formats;

public class GtxTexture : TextureBase
{
    public GtxTexture()
    {
    }

    /// <summary>
    ///     Decodes a texture from a stream.
    /// </summary>
    /// <param name="source">The stream to read from.</param>
    /// <param name="destination">The stream to write to.</param>
    /// <param name="length">Number of bytes to read.</param>
    public override void Read(Stream source, Stream destination)
    {
        // Reading GIM textures is done through the GIM texture decoder, so just pass it to that
        var texture = new GtxTextureDecoder(source);

        texture.Save(destination);
    }

    public override void Write(Stream source, Stream destination)
    {
        throw new NotImplementedException("Writing Gtx textures is not yet implemented.");
        /*
        // Writing GIM textures is done through GIM texture encoder, so just pass it to that
        var texture = new GtxTextureEncoder(source, PaletteFormat, DataFormat);

        texture.HasMetadata = HasMetadata;
        texture.IsSwizzled = Swizzle;
        texture.Dither = Dither;
        if (texture.HasMetadata)
        {
            texture.Metadata.OriginalFilename = source is FileStream fs
                ? Path.GetFileName(fs.Name)
                : string.Empty;
            texture.Metadata.User = Environment.UserName;
            texture.Metadata.Program = "Puyo Tools";
        /}

        texture.Save(destination); 
        */
    }

    #region Writer Settings

    /// <summary>
    ///     The texture's palette format. The default value is GimPaletteFormat.Unknown.
    /// </summary>
    public PixelDataFormat? PaletteFormat { get; set; }

    /// <summary>
    ///     The texture's data format. The default value is GimDataFormat.Rgb565.
    /// </summary>
    public PixelDataFormat DataFormat { get; set; }

    /// <summary>
    ///     Gets or sets if the texture should include metadata. The default value is true.
    /// </summary>
    public bool HasMetadata { get; set; }

    /// <summary>
    ///     Gets or sets if the texture should be swizzled.
    /// </summary>
    public bool Swizzle { get; set; }

    /// <summary>
    ///     Gets or sets if dithering should be used when creating palette-based textures.
    /// </summary>
    public bool Dither { get; set; }

    #endregion
}