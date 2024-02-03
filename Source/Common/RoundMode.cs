// ReSharper disable CompareOfFloatsByEqualityOperator
namespace Multiplayer.Common;

public static class RoundMode
{
    // ReSharper disable once ConvertToConstant.Local
    static volatile float epsilon = 1e-38f;

    // Based on https://lemire.me/blog/2022/11/16/a-fast-function-to-check-your-floating-point-rounding-mode/
    public static RoundModeEnum GetCurrentRoundMode()
    {
        return (-1f + epsilon == -1f, 1f - epsilon == 1f) switch
        {
            (true, true) => RoundModeEnum.ToNearest,
            (false, true) => RoundModeEnum.Upward,
            (true, false) => RoundModeEnum.Downward,
            (false, false) => RoundModeEnum.TowardZero,
        };
    }
}

/// <remarks>
/// https://en.cppreference.com/w/cpp/numeric/fenv/FE_round
/// </remarks>
public enum RoundModeEnum : short
{
    /// <summary>
    /// Rounding towards nearest representable value.
    /// </summary>
    ToNearest = 0x00000000,

    /// <summary>
    /// Rounding towards negative infinity.
    /// </summary>
    Downward = 0x00000100,

    /// <summary>
    /// Rounding towards positive infinity.
    /// </summary>
    Upward = 0x00000200,

    /// <summary>
    /// Rounding towards zero.
    /// </summary>
    TowardZero = 0x00000300,
}
