using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Windows
{
    public static class MpWindowExtensions
    {
        public static Rect GetInitialRect(this Window w)
        {
            Rect initialRect = new Rect(0f, 0f, w.InitialSize.x, w.InitialSize.y);
            initialRect.center = new Vector2(UI.screenWidth / 2f, UI.screenHeight / 2f);
            return initialRect;
        }

        public static void KeepWindowOnScreen(this Window w)
        {
            w.windowRect.center = new Vector2(
                x: Math.Min((float) UI.screenWidth, w.windowRect.center.x),
                y: Math.Min((float) UI.screenHeight, w.windowRect.center.y)
            );
        }

        public static void SaveWindowRect(this Window w)
        {
            MultiplayerMod.settings.windowRectLookup.SetOrAdd(w.GetType(), w.windowRect);
            MultiplayerMod.settings.Write();
        }

        public static void SetWindowRect(this Window w, Rect rect)
        {
            w.windowRect = rect;
            w.KeepWindowOnScreen();
        }

        public static void RestoreWindowSize(this Window w)
        {
            w.SetWindowRect(MultiplayerMod.settings.windowRectLookup.TryGetValue(w.GetType(), fallback: w.GetInitialRect()));
        }
    }
}
