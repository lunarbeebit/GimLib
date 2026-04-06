namespace GimLib.Textures.Gtx.PaletteCodecs;
/// <inheritdoc />
internal class Argb8888PaletteCodec : PaletteCodec
{
    public override int BitsPerPixel => 32;

    public override byte[] Decode(byte[] source)
    {
        var count = source.Length / 4;
        var destination = new byte[count * 4];

        var sourceIndex = 0;
        var destinationIndex = 0;

        for (var i = 0; i < count; i++)
        {
            destination[destinationIndex + 2] = source[sourceIndex + 0];
            destination[destinationIndex + 1] = source[sourceIndex + 1];
            destination[destinationIndex + 0] = source[sourceIndex + 2];
            destination[destinationIndex + 3] = source[sourceIndex + 3];

            sourceIndex += 4;
            destinationIndex += 4;
        }

        return destination;
    }

    public override byte[] Encode(byte[] source)
    {
        var count = source.Length / 4;
        var destination = new byte[count * 4];

        var sourceIndex = 0;
        var destinationIndex = 0;

        for (var i = 0; i < count; i++)
        {
            destination[destinationIndex + 2] = source[sourceIndex + 0];
            destination[destinationIndex + 1] = source[sourceIndex + 1];
            destination[destinationIndex + 0] = source[sourceIndex + 2];
            destination[destinationIndex + 3] = source[sourceIndex + 3];

            sourceIndex += 4;
            destinationIndex += 4;
        }

        return destination;
    }
}