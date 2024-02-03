using System.Runtime.InteropServices;
using Multiplayer.Common;

namespace TestsOnMono;

public static class ExternMethods
{
    [DllImport("ucrtbase.dll", EntryPoint = "fegetround", CallingConvention = CallingConvention.Cdecl)]
    public static extern RoundModeEnum GetRound();

    [DllImport("ucrtbase.dll", EntryPoint = "fesetround", CallingConvention = CallingConvention.Cdecl)]
    public static extern int SetRound(RoundModeEnum roundingMode);
}
