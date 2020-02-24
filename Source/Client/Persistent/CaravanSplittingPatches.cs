using Verse;
using RimWorld.Planet;
using HarmonyLib;

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
                Multiplayer.WorldComp.splitSession.OpenWindow(true);
            }
            else
            {
                CaravanSplittingSession.CreateSplittingSession(caravan);
            }

            return false;
        }
    }
    
    /// <summary>
    /// When a Dialog_SplitCaravan would be constructed, cancel and construct a CaravanSplittingProxy instead.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_SplitCaravan), nameof(Dialog_SplitCaravan.PostOpen))]
    class CancelDialogSplitCaravanPostOpen
    {
        static bool Prefix()
        {
            //When not playing multiplayer, don't modify behavior.
            //Otherwise prevent the Dialog_SplitCaravan.PostOpen from executing.
            //This is needed to prevent the Dialog_SplitCaravan.CalculateAndRecacheTransferables from being called,
            //  since if it gets called the Dialog_SplitCaravan tranferrable list is replaced with a new one, 
            //  breaking the session's reference to the current list.
            return Multiplayer.Client == null;    
        }
    }
}
