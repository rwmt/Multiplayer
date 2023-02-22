using Multiplayer.Common;
using Tomlyn;
using Tomlyn.Model;

namespace Server;

public static class TomlSettings
{
    public static ServerSettings Load(string filename)
    {
        var toml = new TomlScribe
        {
            mode = TomlScribeMode.Loading,
            root = Toml.ToModel(File.ReadAllText(filename))
        };

        ScribeLike.provider = toml;

        var settings = new ServerSettings();
        settings.ExposeData();

        return settings;
    }

    public static void Save(ServerSettings settings, string filename)
    {
        var toml = new TomlScribe { mode = TomlScribeMode.Saving };
        ScribeLike.provider = toml;

        settings.ExposeData();

        File.WriteAllText(filename, Toml.FromModel(toml.root));
    }
}

class TomlScribe : ScribeLike.Provider
{
    public TomlTable root = new();
    public TomlScribeMode mode;

    public override void Look<T>(ref T value, string label, T defaultValue, bool forceSave)
    {
        if (mode == TomlScribeMode.Loading)
        {
            if (root.ContainsKey(label))
            {
                if (typeof(T).IsEnum)
                    value = (T)Enum.Parse(typeof(T), (string)root[label]);
                else if (root[label] is IConvertible convertible)
                    value = (T)convertible.ToType(typeof(T), null);
                else
                    value = (T)root[label];
            }
            else
            {
                value = defaultValue;
            }
        }
        else if (mode == TomlScribeMode.Saving)
        {
            if (typeof(T).IsEnum)
                root[label] = value.ToString()!;
            else
                root[label] = value;
        }
    }
}

enum TomlScribeMode
{
    Saving, Loading
}
