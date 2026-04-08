using System.Data;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using GimLib.Core;
using GimLib.Textures.Gim.PaletteCodecs;
using GimLib.Textures.Gim.PixelCodecs;
using ImageMagick;
using ImageMagick.Factories;

namespace GimLib.Textures.Gim;

public class GimTextureEncoder
{
    private static readonly byte[] magicCodeLittleEndian =
    {
        (byte)'M', (byte)'I', (byte)'G', (byte)'.',
        (byte)'0', (byte)'0', (byte)'.', (byte)'1',
        (byte)'P', (byte)'S', (byte)'P', 0
    };

    private static readonly byte[] magicCodeBigEndian =
    {
        (byte)'.', (byte)'G', (byte)'I', (byte)'M',
        (byte)'1', (byte)'.', (byte)'0', (byte)'0',
        0, (byte)'P', (byte)'S', (byte)'P'
    };

    private readonly int
        strideAlignment = 16; // Stride alignment will always be 16, except for DXTn-based pixel formats.

    private byte[]? encodedPaletteData;
    private byte[]? encodedTextureData;
    private Endianness endianness = Endianness.Little;

    private int
        heightAlignment; // Height alignment will be 8 (if swizzled) or 1 (if not swizzled), or 4 if using a DXTn pixel format.

    private GimMetadata? metadata;
    private PaletteCodec? paletteCodec; // Palette codec

    private int paletteEntries; // Number of palette entries in the palette data
    private PixelCodec? pixelCodec; // Pixel codec
    private int pixelsPerColumn;
    private int pixelsPerRow;

    private MagickImage? sourceImage;

    private int stride;

    /// <summary>
    ///     Opens a texture to encode from a file.
    /// </summary>
    /// <param name="path">Filename of the file that contains the texture data.</param>
    /// <param name="pixelFormat">Pixel format to encode the texture to.</param>
    /// <param name="dataFormat">Data format to encode the texture to.</param>
    public GimTextureEncoder(string path, GimPixelFormat pixelFormat)
        : this(path, null, pixelFormat)
    {
    }

    /// <summary>
    ///     Opens a texture to encode from a file.
    /// </summary>
    /// <param name="path">Filename of the file that contains the texture data.</param>
    /// <param name="pixelFormat">Pixel format to encode the texture to.</param>
    /// <param name="dataFormat">Data format to encode the texture to.</param>
    public GimTextureEncoder(string path, GimPaletteFormat? paletteFormat, GimPixelFormat pixelFormat)
    {
        Initialize(path, paletteFormat, pixelFormat);
    }

    /// <summary>
    ///     Opens a texture to encode from a stream.
    /// </summary>
    /// <param name="source">Stream that contains the texture data.</param>
    /// <param name="pixelFormat">Pixel format to encode the texture to.</param>
    /// <param name="dataFormat">Data format to encode the texture to.</param>
    public GimTextureEncoder(Stream source, GimPixelFormat pixelFormat)
        : this(source, null, pixelFormat)
    {
    }

    /// <summary>
    ///     Opens a texture to encode from a stream.
    /// </summary>
    /// <param name="source">Stream that contains the texture data.</param>
    /// <param name="pixelFormat">Pixel format to encode the texture to.</param>
    /// <param name="dataFormat">Data format to encode the texture to.</param>
    public GimTextureEncoder(Stream source, GimPaletteFormat? paletteFormat, GimPixelFormat pixelFormat)
    {
        Initialize(source, paletteFormat, pixelFormat);
    }

    /// <summary>
    ///     Gets the width.
    /// </summary>
    public int Width { get; private set; }

    /// <summary>
    ///     Gets the height.
    /// </summary>
    public int Height { get; private set; }

    /// <summary>
    ///     Gets the palette format, or null if a palette is not used.
    /// </summary>
    public GimPaletteFormat? PaletteFormat { get; private set; }

    /// <summary>
    ///     Gets the pixel format.
    /// </summary>
    public GimPixelFormat PixelFormat { get; private set; }

    /// <summary>
    ///     Gets or sets if the texture should include metadata.
    /// </summary>
    public bool HasMetadata { get; set; } = true;

    /// <summary>
    ///     Gets or sets the endianness. Defaults to <see cref="Endianness.Little" />.
    /// </summary>
    public Endianness Endianness
    {
        get => endianness;
        set
        {
            ArgumentHelper.ThrowIfInvalidEnumValue(value);

            endianness = value;
        }
    }

    /// <summary>
    ///     Gets or sets if this texture should be swizzled.
    /// </summary>
    /// <remarks>Swizzling isn't supported when using DXTn-based pixel formats.</remarks>
    public bool IsSwizzled { get; set; }

    /// <summary>
    ///     Gets or sets whether dithering should be used when quantizing.
    /// </summary>
    public bool Dither { get; set; }

    private void Initialize(string source, GimPaletteFormat? paletteFormat, GimPixelFormat pixelFormat)
    {
        // Set the palette and pixel formats, and verify that we can encode to them.
        // We'll also need to verify that the palette format is set if it's a palettized pixel format.
        // Unlike with the decoder, an exception will be thrown here if a codec cannot be used to encode them.
        PixelFormat = pixelFormat;
        pixelCodec = PixelCodecFactory.Create(pixelFormat);
        if (pixelCodec is null)
            throw new NotSupportedException($"Pixel format {PixelFormat:X} is not supported for encoding.");

        // Get the number of palette entries.
        paletteEntries = pixelCodec.PaletteEntries;

        if (paletteEntries != 0)
        {
            if (paletteFormat is null)
                throw new ArgumentNullException(nameof(paletteFormat),
                    $"Palette format must be set for pixel format {PixelFormat}");

            PaletteFormat = paletteFormat.Value;
            paletteCodec = PaletteCodecFactory.Create(paletteFormat.Value);
            if (paletteCodec is null)
                throw new NotSupportedException($"Palette format {PaletteFormat:X} is not supported for encoding.");
        }

        // Read the image.
        //sourceImage = Image.Load<Bgra32>(source);
        sourceImage = new MagickImage(source, MagickFormat.Png8);

        Width = (int) sourceImage.Width;
        Height = (int) sourceImage.Height;

        // Create the metadata and set the default values.
        metadata = new GimMetadata(
            Path.GetFileName(source),
            Environment.UserName,
            DateTime.Now.ToString("ddd MMM dd HH:mm:ss yyyy"),
            Assembly.GetEntryAssembly()!.GetCustomAttribute<AssemblyProductAttribute>()?.Product
            + " " +
            Assembly.GetEntryAssembly()!.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
        );
    }

    private void Initialize(Stream source, GimPaletteFormat? paletteFormat, GimPixelFormat pixelFormat)
    {
        // Set the palette and pixel formats, and verify that we can encode to them.
        // We'll also need to verify that the palette format is set if it's a palettized pixel format.
        // Unlike with the decoder, an exception will be thrown here if a codec cannot be used to encode them.
        PixelFormat = pixelFormat;
        pixelCodec = PixelCodecFactory.Create(pixelFormat);
        if (pixelCodec is null)
            throw new NotSupportedException($"Pixel format {PixelFormat:X} is not supported for encoding.");

        // Get the number of palette entries.
        paletteEntries = pixelCodec.PaletteEntries;

        if (paletteEntries != 0)
        {
            if (paletteFormat is null)
                throw new ArgumentNullException(nameof(paletteFormat),
                    $"Palette format must be set for pixel format {PixelFormat}");

            PaletteFormat = paletteFormat.Value;
            paletteCodec = PaletteCodecFactory.Create(paletteFormat.Value);
            if (paletteCodec is null)
                throw new NotSupportedException($"Palette format {PaletteFormat:X} is not supported for encoding.");
        }

        // Read the image.
        //sourceImage = Image.Load<Bgra32>(source);
        sourceImage = new MagickImage(source, new MagickReadSettings()
        {
            Depth = (uint) pixelCodec.BitsPerPixel,        
        });

        Width = (int) sourceImage.Width;
        Height = (int) sourceImage.Height;

        // Create the metadata and set the default values.
        metadata = new GimMetadata(
            source is FileStream fileStream ? Path.GetFileName(fileStream.Name) : "unnamed",
            Environment.UserName,
            DateTime.Now.ToString("ddd MMM dd HH:mm:ss yyyy"),
            Assembly.GetEntryAssembly()!.GetCustomAttribute<AssemblyProductAttribute>()?.Product
            + " " +
            Assembly.GetEntryAssembly()!.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
        );
    }

    /// <summary>
    ///     Saves the encoded texture to the specified path.
    /// </summary>
    /// <param name="path">Name of the file to save the data to.</param>
    public void Save(string path)
    {
        using (var stream = File.OpenWrite(path))
        {
            Save(stream);
        }
    }

    /// <summary>
    ///     Saves the encoded texture to the specified stream.
    /// </summary>
    /// <param name="destination">The stream to save the texture to.</param>
    public void Save(Stream destination)
    {
        var writer = new BinaryWriter(destination);

        if (encodedTextureData is null) encodedTextureData = EncodeTexture();

        // Get the lengths of the various chunks
        int eofOffsetChunkLength = 16,
            metadataOffsetChunkLength = 16,
            paletteDataChunkLength = 0,
            textureDataChunkLength = 0,
            metadataChunkLength = 0;

        if (encodedPaletteData is not null) paletteDataChunkLength = 80 + encodedPaletteData.Length;

        textureDataChunkLength = 80 + encodedTextureData.Length;

        if (HasMetadata)
        {
            metadataChunkLength = 16;

            if (metadata?.OriginalFilename is not null) metadataChunkLength += metadata.OriginalFilename.Length;
            metadataChunkLength++;

            if (metadata?.User is not null) metadataChunkLength += metadata.User.Length;
            metadataChunkLength++;

            if (metadata?.Timestamp is not null) metadataChunkLength += metadata.Timestamp.Length;
            metadataChunkLength++;

            if (metadata?.Program is not null) metadataChunkLength += metadata.Program.Length;
            metadataChunkLength++;
        }

        // Calculate what the length of the texture will be
        var textureLength = 16 +
                            eofOffsetChunkLength +
                            metadataOffsetChunkLength +
                            textureDataChunkLength +
                            paletteDataChunkLength +
                            metadataChunkLength;

        // Write the GIM header
        if (endianness == Endianness.Big)
            writer.Write(magicCodeBigEndian);
        else
            writer.Write(magicCodeLittleEndian);
        writer.WriteInt32(0);

        // Write the EOF offset chunk
        writer.WriteUInt16(0x02, endianness);
        writer.WriteUInt16(0);
        writer.WriteInt32(textureLength - 16, endianness);
        writer.WriteInt32(eofOffsetChunkLength, endianness);
        writer.WriteUInt32(16, endianness);

        // Write the metadata offset chunk
        writer.WriteUInt16(0x03, endianness);
        writer.WriteUInt16(0);

        if (HasMetadata)
            writer.WriteInt32(textureLength - metadataChunkLength - 32, endianness);
        else
            writer.WriteInt32(textureLength - 32, endianness);

        writer.WriteInt32(metadataOffsetChunkLength, endianness);
        writer.WriteUInt32(16, endianness);

        // Write the texture data
        writer.WriteUInt16(0x04, endianness);
        writer.WriteUInt16(0);
        writer.WriteInt32(textureDataChunkLength, endianness);
        writer.WriteInt32(textureDataChunkLength, endianness);
        writer.WriteUInt32(16, endianness);

        writer.WriteUInt16(48, endianness);
        writer.WriteUInt16(0);
        writer.WriteUInt16((ushort)PixelFormat, endianness);
        writer.WriteUInt16((ushort)(IsSwizzled ? 1 : 0), endianness);
        writer.WriteUInt16((ushort)Width, endianness);
        writer.WriteUInt16((ushort)Height, endianness);

        if (paletteEntries != 0)
            // For palettized textures, this is the bpp for this data format
        {
            if (pixelCodec != null) writer.WriteUInt16((ushort)pixelCodec.BitsPerPixel, endianness);
        }
        else
            // For non-palettized textures, this is always specified as 32bpp
            writer.WriteUInt16(32, endianness);

        writer.WriteUInt16((ushort)strideAlignment, endianness); // Stride alignment (always 16)
        writer.WriteUInt16((ushort)heightAlignment, endianness); // Height alignment (always 8 for swizzled)

        writer.WriteUInt16(0x02, endianness);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0x30, endianness);
        writer.WriteUInt32(0x40, endianness);

        writer.WriteInt32(textureDataChunkLength - 16, endianness);
        writer.WriteUInt32(0);
        writer.WriteUInt16(0x01, endianness);
        writer.WriteUInt16(0x01, endianness);
        writer.WriteUInt16(0x03, endianness);
        writer.WriteUInt16(0x01, endianness);
        writer.WriteUInt32(0x40, endianness);
        writer.Write(new byte[12]);

        if (IsSwizzled)
            writer.Write(Swizzle(encodedTextureData, stride, pixelsPerColumn));
        else
            writer.Write(encodedTextureData);

        // Write the palette data, if we have a palette
        if (encodedPaletteData is not null)
        {
            writer.WriteUInt16(0x05, endianness);
            writer.WriteUInt16(0);
            writer.WriteInt32(paletteDataChunkLength, endianness);
            writer.WriteInt32(paletteDataChunkLength, endianness);
            writer.WriteUInt32(16, endianness);

            writer.WriteUInt16(48, endianness);
            writer.WriteUInt16(0);
            if (PaletteFormat != null) writer.WriteUInt16((byte)PaletteFormat.Value, endianness);
            writer.WriteUInt16(0);
            writer.WriteUInt16((ushort)paletteEntries, endianness);

            writer.WriteUInt16(0x01, endianness);
            writer.WriteUInt16(0x20, endianness);
            writer.WriteUInt16(0x10, endianness);
            writer.WriteUInt16(0x01, endianness);
            writer.WriteUInt16(0x02, endianness);
            writer.WriteUInt32(0);
            writer.WriteUInt32(0x30, endianness);
            writer.WriteUInt32(0x40, endianness);

            writer.WriteInt32(paletteDataChunkLength - 16, endianness);
            writer.WriteUInt32(0);
            writer.WriteUInt16(0x02, endianness);
            writer.WriteUInt16(0x01, endianness);
            writer.WriteUInt16(0x03, endianness);
            writer.WriteUInt16(0x01, endianness);
            writer.WriteUInt32(0x40, endianness);
            writer.Write(new byte[12]);

            writer.Write(encodedPaletteData);
        }

        // Write the metadata, only if we are including it
        if (HasMetadata)
        {
            writer.WriteUInt16(0xFF, endianness);
            writer.WriteUInt16(0);
            writer.WriteInt32(metadataChunkLength, endianness);
            writer.WriteInt32(metadataChunkLength, endianness);
            writer.WriteUInt32(16, endianness);

            if (metadata?.OriginalFilename != null) writer.WriteNullTerminatedString(metadata.OriginalFilename);
            if (metadata?.User != null) writer.WriteNullTerminatedString(metadata.User);
            if (metadata?.Timestamp != null) writer.WriteNullTerminatedString(metadata.Timestamp);
            if (metadata?.Program != null) writer.WriteNullTerminatedString(metadata.Program);
        }
    }

    /// <summary>
    ///     Encodes the texture. Also encodes the palette if needed.
    /// </summary>
    /// <returns>The byte array containing the encoded texture data.</returns>
    private byte[] EncodeTexture()
    {
        // Calculate the alignment, stride, and pixels per row/column.
        heightAlignment = IsSwizzled ? 8 : 1;

        if (pixelCodec != null)
        {
            stride = MathHelper.RoundUp((int)Math.Ceiling((double)Width * pixelCodec.BitsPerPixel / 8),
                strideAlignment);
            pixelsPerRow = stride * 8 / pixelCodec.BitsPerPixel;
            pixelsPerColumn = MathHelper.RoundUp(Height, heightAlignment);

            if (sourceImage == null)
                {
                    throw new ArgumentNullException(nameof(sourceImage), "source image can't be null");
                }

            // Encode as a palettized image.
            if (paletteEntries != 0)
            {
                /*
                // Create the quantizer and quantize the texture.
                image.
                IQuantizer<Bgra32> quantizer;
                IndexedImageFrame<Bgra32> imageFrame;
                var quantizerOptions = new QuantizerOptions
                {
                    MaxColors = paletteEntries,
                    Dither = Dither ? QuantizerConstants.DefaultDither : null
                };
                */

                var settings = new QuantizeSettings
                {
                    Colors = (uint)paletteEntries,
                    DitherMethod = Dither ? DitherMethod.FloydSteinberg : DitherMethod.No,
                    ColorSpace = ColorSpace.sRGB
                };

            
                settings.ColorSpace = ColorSpace.RGB;
                //sourceImage.Quantize(settings);
                
                // Save the palette
                encodedPaletteData = EncodePalette();
            }
            File.WriteAllBytes("/Users/josesa/Documents/YADEWorkplace/EXTRACTED/.GIM/binary_encoder.bin",sourceImage.ToByteArray(MagickFormat.Bgra));

            return pixelCodec.Encode(sourceImage.ToByteArray(MagickFormat.Bgra), Width, Height, pixelsPerRow, pixelsPerColumn);
        }
        
        throw new ArgumentNullException(nameof(pixelCodec), "Pixel codec can't be null.");
    }

    /// <summary>
    ///     Encodes the palette.
    /// </summary>
    /// <returns></returns>
    private byte[] EncodePalette()
    {
        if(sourceImage == null)
        {
            throw new NullReferenceException();
        }
        //var palette = (MagickImage)sourceImage.UniqueColors();
        List<byte> paletteData = [];
        var paletteSize = sourceImage.ColormapSize;
        for (int i = 0; i < paletteSize; i++)
        {
            MagickColor? color = (MagickColor?)sourceImage.GetColormapColor(i) ?? throw new NullReferenceException();
            paletteData.Add(color.B);
            paletteData.Add(color.G);
            paletteData.Add(color.R);
            paletteData.Add(color.A);
        }
        return paletteCodec != null ? paletteCodec.Encode([.. paletteData]) : throw new ArgumentException("Palette can't be null.", nameof(paletteData));
    }

    private static byte[] Swizzle(byte[] source, int stride, int pixelsPerColumn)
    {
        var sourceIndex = 0;

        var destination = new byte[stride * pixelsPerColumn];

        var rowblocks = stride / 16;

        for (var y = 0; y < pixelsPerColumn; y++)
        for (var x = 0; x < stride; x++)
        {
            var blockX = x / 16;
            var blockY = y / 8;

            var blockIndex = blockX + blockY * rowblocks;
            var blockAddress = blockIndex * 16 * 8;

            destination[blockAddress + (x - blockX * 16) + (y - blockY * 8) * 16] = source[sourceIndex];
            sourceIndex++;
        }

        return destination;
    }
}