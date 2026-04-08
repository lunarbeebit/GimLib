using System.Runtime.CompilerServices;

namespace GimLib.Core;

public static class MathHelper
{
    public static int RoundUp(int value, int multiple)
    {
        // Return the same number if it is already a multiple
        if (value % multiple == 0)
            return value;

        return value + (multiple - (value % multiple));
    }

    public static uint RoundUp(uint value, int multiple)
    {
        // Return the same number if it is already a multiple
        if (value % multiple == 0)
            return value;

        return value + ((uint)multiple - (value % (uint)multiple));
    }

    /// <summary>
    ///     Evaluate whether a given integral value is a power of 2.
    /// </summary>
    /// <param name="value">The value.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPow2(int value)
    {
        return (value & (value - 1)) == 0 && value > 0;
    }
}