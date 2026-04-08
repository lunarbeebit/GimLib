using System.Text;
using GimLib.Core;
using GimLib.Textures.Gtx.PaletteCodecs;
using GimLib.Textures.Gtx.PixelCodecs;
using ImageMagick;

namespace GimLib.Textures.Gtx;

public class GtxTextureDecoder
{

    internal delegate void PixelOrderingDelegate(int origX, int origY, int width, int height, PixelDataFormat pixelFormat, out int transformedX, out int transformedY);



    /// <summary>
    ///     Open a GTX texture from a file.
    /// </summary>
    /// <param name="path">Filename of the file that contains the texture data.</param>
    public GtxTextureDecoder(string path) : this(File.OpenRead(path))
    {
    }

    /// <summary>
    ///     Open a GTX texture from a stream.
    /// </summary>
    /// <param name="source">Stream that contains the texture data.</param>
    public GtxTextureDecoder(Stream source)
    {
        BinaryReader reader = new BinaryReader(source, Encoding.ASCII, true);
        Header = new SceGxtHeader(reader);

        if (Header.MagicNumber != "GXT")
            throw new InvalidDataException("Stream does not contain valid GXT data.");

        Func<BinaryReader, SceGxtTextureInfo> textureInfoGeneratorFunc;
        switch (Header.Version)
        {
            case 0x10000003: textureInfoGeneratorFunc = new Func<BinaryReader, SceGxtTextureInfo>((r) => { return new SceGxtTextureInfoV301(r); }); break;
            case 0x10000002: textureInfoGeneratorFunc = new Func<BinaryReader, SceGxtTextureInfo>((r) => { return new SceGxtTextureInfoV201(r); }); break;
            case 0x10000001: textureInfoGeneratorFunc = new Func<BinaryReader, SceGxtTextureInfo>((r) => { return new SceGxtTextureInfoV101(r); }); break;
            default: throw new Exception("GXT version not implemented");
        }

        TextureInfos = new SceGxtTextureInfo[Header.NumTextures];
        for (int i = 0; i < TextureInfos.Length; i++)
            TextureInfos[i] = textureInfoGeneratorFunc(reader);

        // TODO: any other way to detect these?
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) == BUVChunk.ExpectedMagicNumber)
        {
            reader.BaseStream.Seek(-4, SeekOrigin.Current);
            BUVChunk = new BUVChunk(reader);
        }

        long paletteOffset = Header.TextureDataOffset + Header.TextureDataSize - ((Header.NumP8Palettes * 256 * 4) + (Header.NumP4Palettes * 16 * 4));
        reader.BaseStream.Seek(paletteOffset, SeekOrigin.Begin);

        P4Palettes = new byte[Header.NumP4Palettes][];
        for (int i = 0; i < P4Palettes.Length; i++) P4Palettes[i] = reader.ReadBytes(16 * 4);

        P8Palettes = new byte[Header.NumP8Palettes][];
        for (int i = 0; i < P8Palettes.Length; i++) P8Palettes[i] = reader.ReadBytes(256 * 4);

        PixelData = new byte[Header.NumTextures][];
        for (int i = 0; i < TextureInfos.Length; i++)
        {
            SceGxtTextureInfo info = TextureInfos[i];

            reader.BaseStream.Seek(info.DataOffset, SeekOrigin.Begin);
            PixelData[i] = reader.ReadBytes((int)info.DataSize);
        }
    }


    public SceGxtHeader Header { get; private set; }
    public SceGxtTextureInfo[] TextureInfos { get; private set; }

    public BUVChunk? BUVChunk { get; private set; }

    public byte[][] P4Palettes { get; private set; }
    public byte[][] P8Palettes { get; private set; }

    public byte[][] PixelData { get; private set; }

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
        for (int imageNumber = 0; imageNumber < TextureInfos.Length; imageNumber++)
        {
            SceGxtTextureInfo info = TextureInfos[imageNumber];
            var Width = info.GetWidth();
            var Height = info.GetHeight();
            var InputPixelFormat = PSVita.GetPixelDataFormat(info.GetTextureFormat());
            int PhysicalWidth;
            int PhysicalHeight;
            PixelDataFormat InputPaletteFormat = PixelDataFormat.Undefined;
            List<byte[]> inputPixelData = [];
            List<byte[]> inputPaletteData = [];

            // TODO: verify all this crap, GXT conversion wrt image [dimension/format/type] is fragile as all hell

            SceGxmTextureBaseFormat textureBaseFormat = info.GetTextureBaseFormat();
            SceGxmTextureType textureType = info.GetTextureType();

            if (textureType == SceGxmTextureType.Linear &&
                textureBaseFormat != SceGxmTextureBaseFormat.UBC1 && textureBaseFormat != SceGxmTextureBaseFormat.UBC2 && textureBaseFormat != SceGxmTextureBaseFormat.UBC3 &&
                textureBaseFormat != SceGxmTextureBaseFormat.UBC4 && textureBaseFormat != SceGxmTextureBaseFormat.SBC4 && textureBaseFormat != SceGxmTextureBaseFormat.UBC5 &&
                textureBaseFormat != SceGxmTextureBaseFormat.SBC5 && textureBaseFormat != SceGxmTextureBaseFormat.PVRT2BPP && textureBaseFormat != SceGxmTextureBaseFormat.PVRT4BPP &&
                textureBaseFormat != SceGxmTextureBaseFormat.PVRTII2BPP && textureBaseFormat != SceGxmTextureBaseFormat.PVRTII4BPP)
            {
                PhysicalWidth = (int)(info.DataSize / Height * 8 / PSVita.GetBitsPerPixel(textureBaseFormat));
                PhysicalHeight = info.GetHeight();
            }
            else
            {
                PhysicalWidth = info.GetWidthRounded();
                PhysicalHeight = info.GetHeightRounded();
            }

            if (textureBaseFormat != SceGxmTextureBaseFormat.PVRT2BPP && textureBaseFormat != SceGxmTextureBaseFormat.PVRT4BPP)
            {
                switch (textureType)
                {
                    case SceGxmTextureType.Linear:
                        // Nothing to be done!
                        break;

                    case SceGxmTextureType.Tiled:
                        // TODO: verify me!
                        InputPaletteFormat |= PixelDataFormat.PixelOrderingTiled3DS;
                        break;

                    case SceGxmTextureType.Swizzled:
                    case SceGxmTextureType.Cube:
                    case SceGxmTextureType.SwizzledArbitrary:
                    case SceGxmTextureType.CubeArbitrary:

                        InputPixelFormat |= PixelDataFormat.PixelOrderingSwizzledVita;
                        break;

                }
            }

            if (textureBaseFormat == SceGxmTextureBaseFormat.P4 || textureBaseFormat == SceGxmTextureBaseFormat.P8)
            {
                InputPaletteFormat = PSVita.GetPaletteFormat(info.GetTextureFormat());

                if (textureBaseFormat == SceGxmTextureBaseFormat.P4)
                    foreach (byte[] paletteData in P4Palettes)
                        inputPaletteData.Add(paletteData);
                else if (textureBaseFormat == SceGxmTextureBaseFormat.P8)
                    foreach (byte[] paletteData in P8Palettes)
                        inputPaletteData.Add(paletteData);
            }
            var image = new MagickImage(DecodeTexture(PixelData[imageNumber],
                                                      inputPaletteData[info.PaletteIndex],
                                                      InputPixelFormat,
                                                      InputPaletteFormat,
                                                      Width,
                                                      Height,
                                                      PhysicalWidth,
                                                      PhysicalHeight), new MagickReadSettings()
            {
                Width = Width,
                Height = Height,
                Depth = (uint) PixelCodecFactory.Create(InputPixelFormat)!.BitsPerPixel,
                Format = MagickFormat.Rgba
            });
            image.Write(destination, MagickFormat.Png);
        }
    }

    // Decodes a texture
    private byte[] DecodeTexture(byte[]? inputPixels, byte[]? paletteData, PixelDataFormat inputPixelFormat, PixelDataFormat inputPaletteFormat, int Width, int Height, int PhysicalWidth, int PhysicalHeight)
    {
        var pixelCodec = PixelCodecFactory.Create(inputPixelFormat);
        var paletteCodec = PaletteCodecFactory.Create(inputPaletteFormat);
        // Verify that a palette codec (if required) and pixel codec have been set.
        if (pixelCodec is null)
            throw new NotSupportedException($"Pixel format {inputPixelFormat:X} is not supported for decoding.");
        if (paletteCodec is null && pixelCodec.PaletteEntries != 0)
            throw new NotSupportedException($"Palette format {inputPaletteFormat:X} is not supported for decoding.");

        if (paletteData is not null) // The texture contains an embedded palette
            pixelCodec.Palette = paletteCodec?.Decode(paletteData);

        PixelOrderingDelegate pixelOrderingFunc = GetPixelOrderingFunction(inputPixelFormat);

        return inputPixels != null ? pixelCodec.Decode(Unswizzle(inputPixels, PhysicalWidth, PhysicalHeight, pixelOrderingFunc, inputPixelFormat, pixelCodec), Width, Height, PhysicalWidth, PhysicalHeight) : throw new ArgumentNullException(nameof(inputPixels), "Texture data can't be null.");
    }

    private static byte[] Unswizzle(byte[] source, int stride, int pixelsPerColumn, PixelOrderingDelegate pixelOrderingFunc, PixelDataFormat pixelDataFormat, PixelCodec pixelCodec)
    {
        var destinationIndex = 0;

        var destination = new byte[stride * pixelsPerColumn];

        for (var y = 0; y < pixelsPerColumn; y++)
            for (var x = 0; x < stride; x++)
            {
                pixelOrderingFunc(x, y, stride, pixelsPerColumn, pixelDataFormat, out int tx, out int ty);
                int pixelOffset = ((ty * stride) + tx) * (pixelCodec.BitsPerPixel / 8);

                destination[pixelOffset] = source[destinationIndex];
                destinationIndex++;
            }

        return destination;
    }



    internal static PixelOrderingDelegate GetPixelOrderingFunction(PixelDataFormat inputPixelFormat)
    {
        PixelDataFormat pixelOrdering = (inputPixelFormat & PixelDataFormat.MaskPixelOrdering);

        PixelOrderingDelegate pixelOrderingFunc;
        switch (pixelOrdering)
        {
            case PixelDataFormat.PixelOrderingLinear: pixelOrderingFunc = (int x, int y, int w, int h, PixelDataFormat pf, out int tx, out int ty) => { tx = x; ty = y; }; break;
            case PixelDataFormat.PixelOrderingTiled: pixelOrderingFunc = new PixelOrderingDelegate(GetPixelCoordinatesTiled); break;
            case PixelDataFormat.PixelOrderingTiled3DS: pixelOrderingFunc = new PixelOrderingDelegate(GetPixelCoordinates3DS); break;
            case PixelDataFormat.PixelOrderingSwizzledPS4: pixelOrderingFunc = new PixelOrderingDelegate(GetPixelCoordinatesSwizzledPS4); break;
            case PixelDataFormat.PixelOrderingSwizzledVita: pixelOrderingFunc = new PixelOrderingDelegate(GetPixelCoordinatesSwizzledVita); break;
            case PixelDataFormat.PixelOrderingSwizzledPSP: pixelOrderingFunc = new PixelOrderingDelegate(GetPixelCoordinatesPSP); break;
            case PixelDataFormat.PixelOrderingSwizzledSwitch: throw new Exception("Switch swizzle is unimplemented; check Tegra X1 TRM for Block Linear?");

            default: throw new Exception("Unimplemented pixel ordering mode");
        }

        return pixelOrderingFunc;
    }
    static readonly int[] pixelOrderingTiledDefault =
    {
             0,  1,  2,  3,  4,  5,  6,  7,
             8,  9, 10, 11, 12, 13, 14, 15,
            16, 17, 18, 19, 20, 21, 22, 23,
            24, 25, 26, 27, 28, 29, 30, 31,
            32, 33, 34, 35, 36, 37, 38, 39,
            40, 41, 42, 43, 44, 45, 46, 47,
            48, 49, 50, 51, 52, 53, 54, 55,
            56, 57, 58, 59, 60, 61, 62, 63
        };

    static readonly int[] pixelOrderingTiled3DS =
    {
             0,  1,  8,  9,  2,  3, 10, 11,
            16, 17, 24, 25, 18, 19, 26, 27,
             4,  5, 12, 13,  6,  7, 14, 15,
            20, 21, 28, 29, 22, 23, 30, 31,
            32, 33, 40, 41, 34, 35, 42, 43,
            48, 49, 56, 57, 50, 51, 58, 59,
            36, 37, 44, 45, 38, 39, 46, 47,
            52, 53, 60, 61, 54, 55, 62, 63
        };
    private static void GetPixelCoordinatesTiled(int origX, int origY, int width, int height, PixelDataFormat inputPixelFormat, out int transformedX, out int transformedY)
    {
        GetPixelCoordinatesTiledEx(origX, origY, width, height, inputPixelFormat, out transformedX, out transformedY, 8, 8, pixelOrderingTiledDefault);
    }

    private static void GetPixelCoordinates3DS(int origX, int origY, int width, int height, PixelDataFormat inputPixelFormat, out int transformedX, out int transformedY)
    {
        GetPixelCoordinatesTiledEx(origX, origY, width, height, inputPixelFormat, out transformedX, out transformedY, 8, 8, pixelOrderingTiled3DS);
    }

    private static void GetPixelCoordinatesPSP(int origX, int origY, int width, int height, PixelDataFormat inputPixelFormat, out int transformedX, out int transformedY)
    {
        // TODO: verify me...?

        PixelDataFormat inBpp = (inputPixelFormat & PixelDataFormat.MaskBpp);
        int bitsPerPixel = Constants.RealBitsPerPixel[inBpp];

        int tileWidth = (bitsPerPixel < 8 ? 32 : (16 / (bitsPerPixel / 8)));
        GetPixelCoordinatesTiledEx(origX, origY, width, height, inputPixelFormat, out transformedX, out transformedY, tileWidth, 8, null);
    }

    private static void GetPixelCoordinatesTiledEx(int origX, int origY, int width, int height, PixelDataFormat inputPixelFormat, out int transformedX, out int transformedY, int tileWidth, int tileHeight, int[]? pixelOrdering)
    {
        // TODO: sometimes eats the last few blocks(?) in the image (ex. BC7 GNFs)

        // Sanity checks
        if (width == 0) width = tileWidth;
        if (height == 0) height = tileHeight;

        // Calculate coords in image
        int tileSize = (tileWidth * tileHeight);
        int globalPixel = ((origY * width) + origX);
        int globalX = ((globalPixel / tileSize) * tileWidth);
        int globalY = ((globalX / width) * tileHeight);
        globalX %= width;

        // Calculate coords in tile
        int inTileX = (globalPixel % tileWidth);
        int inTileY = ((globalPixel / tileWidth) % tileHeight);
        int inTilePixel = ((inTileY * tileHeight) + inTileX);

        // If applicable, transform by ordering table
        if (pixelOrdering != null && tileSize <= pixelOrdering.Length)
        {
            inTileX = (pixelOrdering[inTilePixel] % 8);
            inTileY = (pixelOrdering[inTilePixel] / 8);
        }

        // Set final image coords
        transformedX = (globalX + inTileX);
        transformedY = (globalY + inTileY);
    }

    // Unswizzle logic by @FireyFly
    // http://xen.firefly.nu/up/rearrange.c.html

    private static int Compact1By1Vita(int x)
    {
        x &= 0x55555555;                    // x = -f-e -d-c -b-a -9-8 -7-6 -5-4 -3-2 -1-0
        x = (x ^ (x >> 1)) & 0x33333333;    // x = --fe --dc --ba --98 --76 --54 --32 --10
        x = (x ^ (x >> 2)) & 0x0f0f0f0f;    // x = ---- fedc ---- ba98 ---- 7654 ---- 3210
        x = (x ^ (x >> 4)) & 0x00ff00ff;    // x = ---- ---- fedc ba98 ---- ---- 7654 3210
        x = (x ^ (x >> 8)) & 0x0000ffff;    // x = ---- ---- ---- ---- fedc ba98 7654 3210
        return x;
    }

    private static int DecodeMorton2XVita(int code)
    {
        return Compact1By1Vita(code >> 0);
    }

    private static int DecodeMorton2YVita(int code)
    {
        return Compact1By1Vita(code >> 1);
    }

    private static void GetPixelCoordinatesSwizzledVita(int origX, int origY, int width, int height, PixelDataFormat inputPixelFormat, out int transformedX, out int transformedY)
    {
        // TODO: verify this is even sensible
        if (width == 0) width = 16;
        if (height == 0) height = 16;

        int i = (origY * width) + origX;

        int min = width < height ? width : height;
        int k = (int)Math.Log(min, 2);

        if (height < width)
        {
            // XXXyxyxyx → XXXxxxyyy
            int j = i >> (2 * k) << (2 * k)
                | (DecodeMorton2YVita(i) & (min - 1)) << k
                | (DecodeMorton2XVita(i) & (min - 1)) << 0;
            transformedX = j / height;
            transformedY = j % height;
        }
        else
        {
            // YYYyxyxyx → YYYyyyxxx
            int j = i >> (2 * k) << (2 * k)
                | (DecodeMorton2XVita(i) & (min - 1)) << k
                | (DecodeMorton2YVita(i) & (min - 1)) << 0;
            transformedX = j % width;
            transformedY = j / width;
        }
    }

    private static int Compact1By1PS4(int x)
    {
        x &= 0x55555555;                    // x = -f-e -d-c -b-a -9-8 -7-6 -5-4 -3-2 -1-0
        x = (x ^ (x >> 1)) & 0x33333333;    // x = --fe --dc --ba --98 --76 --54 --32 --10
        x = (x ^ (x >> 2)) & 0x0f0f0f0f;    // x = ---- fedc ---- ba98 ---- 7654 ---- 3210
        x = (x ^ (x >> 4)) & 0x00ff00ff;    // x = ---- ---- fedc ba98 ---- ---- 7654 3210
        x = (x ^ (x >> 8)) & 0x0000ffff;    // x = ---- ---- ---- ---- fedc ba98 7654 3210
        return x;
    }

    private static int DecodeMorton2XPS4(int code)
    {
        return Compact1By1PS4(code >> 0);
    }

    private static int DecodeMorton2YPS4(int code)
    {
        return Compact1By1PS4(code >> 1);
    }

    private static void GetPixelCoordinatesSwizzledPS4(int origX, int origY, int width, int height, PixelDataFormat inputPixelFormat, out int transformedX, out int transformedY)
    {
        // TODO: verify this is even sensible
        if (width == 0) width = 16;
        if (height == 0) height = 16;

        int i = (origY * width) + origX;

        int min = width < height ? width : height;
        int k = (int)Math.Log(min, 2);

        if (height < width)
        {
            // XXXyxyxyx → XXXxxxyyy
            int j = i >> (8 * k) << (8 * k)
                | (DecodeMorton2YPS4(i) & (min - 1)) << k
                | (DecodeMorton2XPS4(i) & (min - 1)) << 0;
            transformedX = j / height;
            transformedY = j % height;
        }
        else
        {
            // YYYyxyxyx → YYYyyyxxx
            int j = i >> (8 * k) << (8 * k)
                | (DecodeMorton2XPS4(i) & (min - 1)) << k
                | (DecodeMorton2YPS4(i) & (min - 1)) << 0;
            transformedX = j % width;
            transformedY = j / width;
        }
    }
}