using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using Multiplayer.Common;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    static partial class UIRootPatch
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        static partial void SetMonoFontNames()
        {
            if (!Native.IsMonoRuntime)
                return;

            Text.fontStyles[1].font.fontNames = new[] { "arial", "arialbd", "ariali", "arialbi" };
        }
    }
}

namespace Multiplayer.Client.Util
{
    public static partial class MpUI
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        static partial void UpdateMonoPasswordCursor(TextEditor editor)
        {
            if (!Native.IsMonoRuntime)
                return;

            AccessTools.Field(typeof(TextEditor), "m_RevealCursor").SetValue(editor, true);
            editor.UpdateScrollOffsetIfNeeded(new Event());
        }
    }
}

namespace Multiplayer.Client.Desyncs
{
    public partial class SaveableDesyncInfo
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        partial void SetMonoGraphicsDeviceVendor(StringBuilder gpuFamily)
        {
            gpuFamily.Clear();
            gpuFamily.Append(SystemInfo.graphicsDeviceVendor);
        }
    }
}
