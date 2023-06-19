using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Multiplayer.Common.Util;

[HotSwappable]
public static class ZipExtensions
{
    public static byte[] GetBytes(this ZipArchive zip, string path)
    {
        return zip.GetEntry(path)!.GetBytes();
    }

    public static string GetString(this ZipArchive zip, string path)
    {
        return Encoding.UTF8.GetString(zip.GetBytes(path));
    }

    public static byte[] GetBytes(this ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using MemoryStream ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public static void AddEntry(this ZipArchive zip, string path, byte[] bytes)
    {
        using var stream = zip.CreateEntry(path).Open();
        stream.Write(bytes, 0, bytes.Length);
    }

    public static void AddEntry(this ZipArchive zip, string path, string text)
    {
        zip.AddEntry(path, Encoding.UTF8.GetBytes(text));
    }

    public static IEnumerable<ZipArchiveEntry> GetEntries(this ZipArchive zip, string pathPattern)
    {
        pathPattern = Regex.Escape(pathPattern).Replace("\\*", ".*");
        var regex = new Regex(pathPattern);
        foreach (var entry in zip.Entries)
            if (regex.IsMatch(entry.FullName))
                yield return entry;
    }
}
