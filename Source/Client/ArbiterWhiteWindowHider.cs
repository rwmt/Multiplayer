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

        private static void HideArbiter()
        {
            IntPtr arbiterhWndPtr = FindWindowA(LpBatchModeClassName, LpWindowName);
            ShowWindow(arbiterhWndPtr.ToInt32(), SW_HIDE);
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
                Task.Run(() =>
                {
                    var completed = false;
                    while (!completed)
                    {

                        ArbiterWhiteWindowHider.FocusRimworld();
                        ArbiterWhiteWindowHider.HideArbiter();
                        completed = Multiplayer.session?.arbiter != null && !Multiplayer.session.ArbiterPlaying;
                        Task.Delay(25);
                    }
                });
            }
        }
    }

}

