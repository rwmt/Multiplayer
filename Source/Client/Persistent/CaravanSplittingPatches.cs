using Verse;
using RimWorld.Planet;
using HarmonyLib;
using Multiplayer.API;

namespace Multiplayer.Client.Persistent
{
    /// <summary>
    /// When a Dialog_SplitCaravan would be added to the window stack in multiplayer mode, cancel it.
    /// </summary>
    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    class CancelDialogSplitCaravan
    {
        static bool Prefix(Window window)
        {
            //When not playing multiplayer, don't modify behavior.
            if (Multiplayer.Client == null) return true;

            //If the dialog being added is a native Dialog_SplitCaravan, cancel adding it to the window stack.
            //Otherwise, window being added is something else. Let it happen.
            return !(window is Dialog_SplitCaravan) || window is CaravanSplittingProxy;
        }
    }

    /// <summary>
    /// When a Dialog_SplitCaravan would be constructed, cancel and construct a CaravanSplittingProxy instead.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_SplitCaravan), MethodType.Constructor)]
    [HarmonyPatch(new[] { typeof(Caravan) })]
    class CancelDialogSplitCaravanCtor
    {
        static bool Prefix(Caravan caravan)
        {
            //When not playing multiplayer, don't modify behavior.
            if (Multiplayer.Client == null) return true;

            //If in the middle of creating a proxy, don't cancel.
            //This is needed since CaravanSplittingProxy uses Dialog_SplitCaravan as a base class.
            if (CaravanSplittingProxy.CreatingProxy)
            {
                return true;
            }

            //Otherwise cancel creation of the Dialog_SplitCaravan.
            //  If there's already an active session, open the window associated with it.
            //  Otherwise, create a new session.
            if (Multiplayer.WorldComp.splitSession != null)
            {
                Multiplayer.WorldComp.splitSession.OpenWindow();
            }
            else
            {
                CreateSplittingSession(caravan);
            }

            return false;
        }

        /// <summary>
        /// Factory method that creates a new CaravanSplittingSession and stores it to Multiplayer.WorldComp.splitSession
        /// Only one caravan split session can exist at a time.
        /// </summary>
        /// <param name="caravan"></param>
        [SyncMethod]
        public static void CreateSplittingSession(Caravan caravan)
        {
            //Start caravan splitting session here by calling new session constructor
            if (Multiplayer.WorldComp.splitSession == null)
            {
                Multiplayer.WorldComp.splitSession = new CaravanSplittingSession(caravan);
            }
        }
    }
}
