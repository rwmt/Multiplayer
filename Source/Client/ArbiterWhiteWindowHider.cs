using Multiplayer.Common;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Verse;

namespace Multiplayer.Client
{
    /// <summary>
    /// Class which hides the white window from arbiter in Rimworld 1.1. and puts focus back on the main game.
    /// (Might become irrelevant in later patches since it is probably a unity issue)
    /// </summary>
    internal class ArbiterWhiteWindowHider
    {
        private const int SW_HIDE = 0;
        private const int SW_MAXIMIZE = 3;
        private const string LpWindowName = "Rimworld by Ludeon Studios";
        private const string LpBatchModeClassName = "Unity.BatchModeWindow";
        private const string LpRimworldClassName = "UnityWndClass";

        [DllImport("User32")]
        private static extern int ShowWindow(int hwnd, int nCmdShow);
        [DllImport("User32")]
        private static extern IntPtr FindWindowA(string lpClassName, string lpWindowName);

        private static bool HideArbiter()
        {
            IntPtr arbiterhWndPtr = FindWindowA(LpBatchModeClassName, LpWindowName);
            return ShowWindow(arbiterhWndPtr.ToInt32(), SW_HIDE) != 0;
        }

        private static void FocusRimworld()
        {
            IntPtr rimworldhWnPtrd = FindWindowA(LpRimworldClassName, LpWindowName);
            ShowWindow(rimworldhWnPtrd.ToInt32(), SW_MAXIMIZE);
        }

        internal static void HideArbiterProcess()
        {
            if (Environment.OSVersion.Platform.Equals(PlatformID.Win32NT))
            {
                Task.Run(async () =>
                {
                    // re-focus main window now that Arbiter's been started
                    await Task.Delay(1);
                    ArbiterWhiteWindowHider.FocusRimworld();
                    // try a few more times, as the auto-save on load seems to vary in duration
                    await Task.Delay(100);
                    ArbiterWhiteWindowHider.FocusRimworld();
                    await Task.Delay(100);
                    ArbiterWhiteWindowHider.FocusRimworld();
                    await Task.Delay(100);
                    ArbiterWhiteWindowHider.FocusRimworld();

                    // Wait for Arbiter to open its own window, then hide it
                    do 
                    {
                        if (ArbiterWhiteWindowHider.HideArbiter()) {
                            ArbiterWhiteWindowHider.FocusRimworld();
                        }

                        await Task.Delay(1);
                    }
                    while (Multiplayer.session?.arbiter != null && !Multiplayer.session.ArbiterPlaying) ;
                });
            }
        }
    }

}

