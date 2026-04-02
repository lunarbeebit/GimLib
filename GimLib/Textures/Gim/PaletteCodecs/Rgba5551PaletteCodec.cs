namespace GimLib.Textures.Gim.PaletteCodecs;

/// <inheritdoc />
internal class Rgba5551PaletteCodec : PaletteCodec
{
    public override int BitsPerPixel => 16;

    public override byte[] Decode(byte[] source)
    {
        var count = source.Length / 2;
        var destination = new byte[count * 4];

        var sourceIndex = 0;
        var destinationIndex = 0;

        for (var i = 0; i < count; i++)
        {
            var pixel = BitConverter.ToUInt16(source, sourceIndex);

            destination[destinationIndex + 3] = (byte)(((pixel >> 15) & 0x01) * 0xFF);
            destination[destinationIndex + 2] = (byte)(((pixel >> 0) & 0x1F) * 0xFF / 0x1F);
            destination[destinationIndex + 1] = (byte)(((pixel >> 5) & 0x1F) * 0xFF / 0x1F);
            destination[destinationIndex + 0] = (byte)(((pixel >> 10) & 0x1F) * 0xFF / 0x1F);

            sourceIndex += 2;
            destinationIndex += 4;
        }

        return destination;
    }

    public override byte[] Encode(byte[] source)
    {
        var count = source.Length / 4;
        var destination = new byte[count * 2];

        var sourceIndex = 0;
        var destinationIndex = 0;

        for (var i = 0; i < count; i++)
        {
            ushort pixel = 0x0000;
            pixel |= (ushort)((source[sourceIndex + 3] >> 7) << 15);
            pixel |= (ushort)((source[sourceIndex + 2] >> 3) << 0);
            pixel |= (ushort)((source[sourceIndex + 1] >> 3) << 5);
            pixel |= (ushort)((source[sourceIndex + 0] >> 3) << 10);

            destination[destinationIndex + 1] = (byte)((pixel >> 8) & 0xFF);
            destination[destinationIndex + 0] = (byte)(pixel & 0xFF);

            sourceIndex += 4;
            destinationIndex += 2;
        }

        return destination;
    }
}