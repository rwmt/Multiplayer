using System;
using Multiplayer.Common.Networking.Packet;
using Tomlyn;
using Tomlyn.Model;

namespace Multiplayer.Common.Util
{
    /// <summary>
    /// Minimal TOML saver for ServerSettings using the existing ExposeData pipeline.
    /// Placed in Common so bootstrap server logic can persist settings without duplicating keys.
    /// </summary>
    public static class TomlSettingsCommon
    {
        public static void Save(ServerSettings settings, string filename)
        {
            var toml = new TomlScribeCommon { Mode = TomlScribeMode.Saving };
            ScribeLike.provider = toml;

            settings.ExposeData();

            System.IO.File.WriteAllText(filename, Toml.FromModel(toml.Root));
        }
    }

    internal class TomlScribeCommon : ScribeLike.Provider
    {
        public TomlTable Root { get; } = new();
        public TomlScribeMode Mode { get; set; }

        public override void Look<T>(ref T value, string label, T defaultValue, bool forceSave)
        {
            if (Mode != TomlScribeMode.Saving)
                throw new InvalidOperationException("TomlScribeCommon only supports saving");

            if (typeof(T).IsEnum)
            {
                Root[label] = value!.ToString();
            }
            else
            {
                Root[label] = value;
            }
        }
    }

    internal enum TomlScribeMode
    {
        Saving
    }
}
