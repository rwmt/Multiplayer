using System;

namespace Multiplayer.Client.Patches
{
    /// <summary>
    /// Indicates that the patch should run during Mod instance construction
    /// (not in the static constructor of MultiplayerStatic)
    /// </summary>
    public class EarlyPatchAttribute : Attribute
    {
    }
}
