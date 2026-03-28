using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Multiplayer.Common.Util
{
    public static class TomlSettingsCommon
    {
        public static string Serialize(ServerSettings settings)
        {
            var scribe = new SimpleTomlScribe { mode = SimpleTomlMode.Saving };
            ScribeLike.provider = scribe;

            settings.ExposeData();

            return scribe.ToToml();
        }

        public static void Save(ServerSettings settings, string filename)
        {
            File.WriteAllText(filename, Serialize(settings));
        }
    }

    internal enum SimpleTomlMode
    {
        Loading,
        Saving
    }

    internal class SimpleTomlScribe : ScribeLike.Provider
    {
        private readonly Dictionary<string, string> data = new();
        private readonly List<KeyValuePair<string, string>> entries = new();
        public SimpleTomlMode mode;

        public override void Look<T>(ref T value, string label, T defaultValue, bool forceSave)
        {
            if (mode == SimpleTomlMode.Loading)
            {
                if (data.TryGetValue(label, out var raw))
                    value = ParseValue<T>(raw);
                else
                    value = defaultValue;
            }
            else
            {
                entries.Add(new KeyValuePair<string, string>(label, FormatValue(value)));
            }
        }

        private static T ParseValue<T>(string raw)
        {
            var type = typeof(T);

            if (type == typeof(string))
                return (T)(object)Unquote(raw);

            if (type == typeof(bool))
                return (T)(object)raw.Equals("true", StringComparison.OrdinalIgnoreCase);

            if (type == typeof(int))
                return (T)(object)int.Parse(raw, CultureInfo.InvariantCulture);

            if (type == typeof(float))
                return (T)(object)float.Parse(raw, CultureInfo.InvariantCulture);

            if (type.IsEnum)
                return (T)Enum.Parse(type, Unquote(raw));

            return (T)Convert.ChangeType(raw, type, CultureInfo.InvariantCulture);
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 && value.StartsWith("\"") && value.EndsWith("\""))
            {
                value = value.Substring(1, value.Length - 2);
                value = value.Replace("\\\"", "\"").Replace("\\\\", "\\");
            }

            return value;
        }

        private static string FormatValue<T>(T value)
        {
            if (value == null) return "\"\"";
            if (value is bool boolean) return boolean ? "true" : "false";
            if (value is int integer) return integer.ToString(CultureInfo.InvariantCulture);
            if (value is float single) return single.ToString(CultureInfo.InvariantCulture);
            if (typeof(T).IsEnum) return Quote(value.ToString()!);
            if (value is string str) return Quote(str);
            return Quote(value.ToString()!);
        }

        private static string Quote(string value) =>
            "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        public string ToToml()
        {
            var sb = new StringBuilder();
            for (var i = 0; i < entries.Count; i++)
                sb.AppendLine(entries[i].Key + " = " + entries[i].Value);
            return sb.ToString();
        }
    }
}