namespace GimLib.Textures.Gtx.PixelCodecs;

/// <inheritdoc />
internal class Index4PixelCodec : PixelCodec
{
    public override bool CanEncode => true;

    public override int BitsPerPixel => 4;

    public override int PaletteEntries => 16;

    public override byte[] Decode(byte[] source, int width, int height, int pixelsPerRow, int pixelsPerColumn)
    {
        if (Palette is null) throw new InvalidOperationException("Palette must be set.");

        var destination = new byte[width * height * 4];

        int sourceIndex;
        int destinationIndex;

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            sourceIndex = (y * pixelsPerRow + x) / 2;
            destinationIndex = (y * width + x) * 4;

            var paletteIndex = (byte)((source[sourceIndex] >> ((x & 0x1) * 4)) & 0xF);

            for (var i = 0; i < 4; i++) destination[destinationIndex + i] = Palette[paletteIndex * 4 + i];
        }

        return destination;
    }

    public override byte[] Encode(byte[] source, int width, int height, int pixelsPerRow, int pixelsPerColumn)
    {
        var destination = new byte[pixelsPerRow * pixelsPerColumn / 2];

        int sourceIndex;
        int destinationIndex;

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            sourceIndex = y * width + x;
            destinationIndex = (y * pixelsPerRow + x) / 2;

            destination[destinationIndex] |= (byte)((source[sourceIndex] & 0xF) << ((x & 0x1) * 4));
        }

        return destination;
    }
}