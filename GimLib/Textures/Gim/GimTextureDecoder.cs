using System.Text;
using GimLib.Core;
using GimLib.Textures.Gim.PaletteCodecs;
using GimLib.Textures.Gim.PixelCodecs;
using ImageMagick;
using ImageMagick.Formats;

namespace GimLib.Textures.Gim;

public class GimTextureDecoder
{
    private static readonly byte[] magicCodeLittleEndian =
    {
        (byte)'M', (byte)'I', (byte)'G', (byte)'.',
        (byte)'0', (byte)'0', (byte)'.', (byte)'1'
        // The remaining 4 bytes present in the encoder are intentionally omitted.
    };

    private static readonly byte[] magicCodeBigEndian =
    {
        (byte)'.', (byte)'G', (byte)'I', (byte)'M',
        (byte)'1', (byte)'.', (byte)'0', (byte)'0'
        // The remaining 4 bytes present in the encoder are intentionally omitted.
    };

    private byte[]? decodedData;

    private Endianness endianness;

    private bool isSwizzled; // Is the texture data swizzled?
    private PaletteCodec? paletteCodec; // Palette codec

    private byte[]? paletteData;

    private ushort paletteEntries; // Number of entries in the palette
    private PixelCodec? pixelCodec; // Pixel codec
    private int pixelsPerColumn;
    private int pixelsPerRow;

    private int stride;
    private byte[]? textureData;

    /// <summary>
    ///     Open a GIM texture from a file.
    /// </summary>
    /// <param name="path">Filename of the file that contains the texture data.</param>
    public GimTextureDecoder(string path)
    {
        using (var stream = File.OpenRead(path))
        {
            Initialize(stream);
        }
    }

    /// <summary>
    ///     Open a GIM texture from a stream.
    /// </summary>
    /// <param name="source">Stream that contains the texture data.</param>
    public GimTextureDecoder(Stream source)
    {
        Initialize(source);
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
    ///     Gets the metadata, or <see langword="null" /> if there is no metadata.
    /// </summary>
    public GimMetadata? Metadata { get; private set; }

    private void Initialize(Stream source)
    {
        // Check to see if what we are dealing with is a GIM texture
        if (!Is(source)) throw new InvalidFormatException("Not a valid GIM texture.");

        var startPosition = source.Position;
        var reader = new BinaryReader(source);

        // Check to see what endianness we're dealing with.
        if (reader.At(startPosition, x => x.ReadBytes(magicCodeLittleEndian.Length))
            .SequenceEqual(magicCodeLittleEndian))
            endianness = Endianness.Little;
        else
            endianness = Endianness.Big;

        paletteEntries = 0;

        // A GIM is constructed of different chunks. They do not necessarily have to be in order.
        var eofOffset = -1;

        source.Position += 0x10;
        while (source.Position < source.Length)
        {
            var chunkPosition = source.Position;
            int chunkLength;

            var chunkType = reader.ReadUInt16(endianness);
            source.Position += 2; // 0x04

            switch (chunkType)
            {
                case 0x02: // EOF offset chunk

                    eofOffset = reader.ReadInt32(endianness) + 16;

                    // Get the length of this chunk
                    chunkLength = reader.ReadInt32(endianness);

                    break;

                case 0x03: // Metadata offset chunk

                    // Skip this chunk. It's not necessary for decoding this texture.
                    source.Position += 4; // 0x08
                    chunkLength = reader.ReadInt32(endianness);

                    break;

                case 0x04: // Texture data chunk

                    // Get the length of this chunk
                    source.Position += 4; // 0x08
                    chunkLength = reader.ReadInt32(endianness);

                    // Get the pixel format & codec
                    source.Position += 8; // 0x14
                    PixelFormat = (GimPixelFormat)reader.ReadUInt16(endianness);
                    pixelCodec = PixelCodecFactory.Create(PixelFormat);

                    // Get whether this texture is swizzled
                    isSwizzled = reader.ReadUInt16(endianness) == 1;

                    // Get the texture dimensions
                    Width = pixelsPerRow = reader.ReadUInt16(endianness);
                    Height = pixelsPerColumn = reader.ReadUInt16(endianness);

                    // If we don't have a known pixel codec for this format, that's ok.
                    // This will allow the properties to be read if the user doesn't want to decode this texture.
                    // The exception will be thrown when the texture is being decoded.
                    if (pixelCodec is null) break;

                    // Get the pixels per row and pixels per column.
                    source.Position += 2; // 0x1E
                    var strideAlignment = reader.ReadUInt16(endianness);
                    var heightAlignment = reader.ReadUInt16(endianness);

                    stride = (int)Math.Ceiling((double)Width * pixelCodec.BitsPerPixel / 8);
                    if (stride % strideAlignment != 0)
                    {
                        stride = MathHelper.RoundUp(stride, strideAlignment);
                        pixelsPerRow = stride * 8 / pixelCodec.BitsPerPixel;
                    }

                    if (pixelsPerColumn % heightAlignment != 0)
                        pixelsPerColumn = MathHelper.RoundUp(pixelsPerColumn, heightAlignment);

                    // Read the texture data
                    textureData = reader.At(chunkPosition + 0x50, x => x.ReadBytes(stride * pixelsPerColumn));

                    break;

                case 0x05: // Palette data chunk

                    // Get the length of this chunk
                    source.Position += 4; // 0x08
                    chunkLength = reader.ReadInt32(endianness);

                    // Get the palette format & codec
                    source.Position += 8; // 0x14
                    PaletteFormat = (GimPaletteFormat)reader.ReadUInt16(endianness);
                    paletteCodec = PaletteCodecFactory.Create(PaletteFormat.Value);

                    // Get the number of entries in the palette
                    source.Position += 2; // 0x18
                    paletteEntries = reader.ReadUInt16(endianness);

                    // If we don't have a known palette codec for this format, that's ok.
                    // This will allow the properties to be read if the user doesn't want to decode this texture.
                    // The exception will be thrown when the texture is being decoded.
                    if (paletteCodec is null) break;

                    // Read the palette data
                    paletteData = reader.At(chunkPosition + 0x50,
                        x => x.ReadBytes(paletteEntries * paletteCodec.BitsPerPixel / 8));

                    break;

                case 0xFF: // Metadata chunk

                    // Get the length of this chunk
                    source.Position += 4; // 0x08
                    chunkLength = reader.ReadInt32(endianness);

                    // Read the metadata
                    source.Position += 4; // 0x10

                    Metadata = new GimMetadata(
                        reader.ReadNullTerminatedString(), 
                        reader.ReadNullTerminatedString(), 
                        reader.ReadNullTerminatedString(), 
                        reader.ReadNullTerminatedString());
                    break;

                default: // Unknown chunk

                    throw new InvalidFormatException($"Unknown chunk type {chunkType:X}");
            }

            // Verify that the chunk length will allow the stream to progress
            if (chunkLength <= 0) throw new InvalidFormatException("Chunk length cannot be zero or negative.");

            // Go to the next chunk
            source.Position = chunkPosition + chunkLength;

            // Stop reading if all of the chunks have been read
            if (source.Position - startPosition == eofOffset) break;
        }

        // Verify that the stream's position is as the expected position
        if (source.Position - startPosition != eofOffset)
            throw new InvalidFormatException("Stream position does not match expected end-of-file position.");
    }

    /// <summary>
    ///     Saves the decoded texture to the specified file as a PNG.
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
    ///     Saves the decoded texture to the specified stream as a PNG.
    /// </summary>
    /// <param name="destination">The stream to save the texture to.</param>
    public void Save(Stream destination)
    {
        if (pixelCodec == null)
            throw new NullReferenceException();
        var image = new MagickImage(GetPixelData(), new MagickReadSettings()
            {
                Width = (uint) this.Width,
                Height = (uint) this.Height,
                Depth = (uint) pixelCodec.BitsPerPixel,
                Format = MagickFormat.Bgra,                
            });
        image.Write(destination, MagickFormat.Png);
    }

    // Decodes a texture
    private byte[] DecodeTexture()
    {
        // Verify that a palette codec (if required) and pixel codec have been set.
        if (pixelCodec is null)
            throw new NotSupportedException($"Pixel format {PixelFormat:X} is not supported for decoding.");
        if (paletteCodec is null && pixelCodec.PaletteEntries != 0)
            throw new NotSupportedException($"Palette format {PaletteFormat:X} is not supported for decoding.");

        if (paletteData is not null) // The texture contains an embedded palette
            pixelCodec.Palette = paletteCodec?.Decode(paletteData);

        if (isSwizzled)
            if (textureData != null)
                return pixelCodec.Decode(Unswizzle(textureData, stride, pixelsPerColumn), Width, Height, pixelsPerRow,
                    pixelsPerColumn);

        return textureData != null ? pixelCodec.Decode(textureData, Width, Height, pixelsPerRow, pixelsPerColumn) : throw new ArgumentNullException(nameof(textureData), "Texture data can't be null.");
    }

    /// <summary>
    ///     Decodes the texture and returns the pixel data.
    /// </summary>
    /// <returns>The pixel data as a byte array.</returns>
    public byte[] GetPixelData()
    {
        if (decodedData == null) decodedData = DecodeTexture();

        return decodedData;
    }

    private static byte[] Unswizzle(byte[] source, int stride, int pixelsPerColumn)
    {
        var destinationIndex = 0;

        var destination = new byte[stride * pixelsPerColumn];

        var rowblocks = stride / 16;

        for (var y = 0; y < pixelsPerColumn; y++)
        for (var x = 0; x < stride; x++)
        {
            var blockX = x / 16;
            var blockY = y / 8;

            var blockIndex = blockX + blockY * rowblocks;
            var blockAddress = blockIndex * 16 * 8;

            destination[destinationIndex] = source[blockAddress + (x - blockX * 16) + (y - blockY * 8) * 16];
            destinationIndex++;
        }

        return destination;
    }

    /// <summary>
    ///     Determines if this is a GIM texture.
    /// </summary>
    /// <param name="source">The stream to read.</param>
    /// <returns>True if this is a GIM texture, false otherwise.</returns>
    public static bool Is(Stream source)
    {
        var startPosition = source.Position;
        var remainingLength = source.Length - startPosition;

        using (var reader = new BinaryReader(source, Encoding.UTF8, true))
        {
            if (!(remainingLength > 24)) return false;

            var magicCode = reader.At(startPosition, x => x.ReadBytes(magicCodeLittleEndian.Length));
            uint expectedLength;

            if (magicCode.SequenceEqual(magicCodeLittleEndian)
                && reader.At(startPosition + 0x10, x => x.ReadUInt16()) == 0x2)
                expectedLength = reader.At(startPosition + 0x14, x => x.ReadUInt32());
            else if (magicCode.SequenceEqual(magicCodeBigEndian)
                     && reader.At(startPosition + 0x10, x => x.ReadUInt16BigEndian()) == 0x2)
                expectedLength = reader.At(startPosition + 0x14, x => x.ReadUInt32BigEndian());
            else
                return false;

            return expectedLength == remainingLength - 16
                   || MathHelper.RoundUp(expectedLength, 16) == remainingLength - 16;
        }
    }

    /// <summary>
    ///     Determines if this is a GIM texture.
    /// </summary>
    /// <param name="file">Filename of the file that contains the data.</param>
    /// <returns>True if this is a GIM texture, false otherwise.</returns>
    public static bool Is(string file)
    {
        using (var stream = File.OpenRead(file))
        {
            return Is(stream);
        }
    }
}