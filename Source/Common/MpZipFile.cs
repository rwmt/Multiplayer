using System;
using System.IO;
using System.IO.Compression;

namespace Multiplayer.Common;

public static class MpZipFile
{
    public static ZipArchive Open(string path, ZipArchiveMode mode)
    {
        FileMode fileMode;
        FileAccess access;
        FileShare fileShare;

        switch (mode)
        {
            case ZipArchiveMode.Read:
                fileMode = FileMode.Open;
                access = FileAccess.Read;
                fileShare = FileShare.Read;
                break;

            case ZipArchiveMode.Create:
                fileMode = FileMode.CreateNew;
                access = FileAccess.Write;
                fileShare = FileShare.None;
                break;

            case ZipArchiveMode.Update:
                fileMode = FileMode.OpenOrCreate;
                access = FileAccess.ReadWrite;
                fileShare = FileShare.None;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var fs = new FileStream(path, fileMode, access, fileShare, bufferSize: 0x1000, useAsync: false);

        try
        {
            return new ZipArchive(fs, mode, leaveOpen: false);
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }
}
