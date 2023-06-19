using System;
using System.IO;
using RimWorld;
using Verse;

namespace Multiplayer.Client;

public class RenameFileWindow : AbstractTextInputWindow
{
    private FileInfo file;
    private Action success;

    public RenameFileWindow(FileInfo file, Action success = null)
    {
        title = "MpFileRename".Translate();

        this.file = file;
        this.success = success;

        curText = Path.GetFileNameWithoutExtension(file.Name);
    }

    public override bool Accept()
    {
        if (curText.Length == 0)
            return false;

        string newPath = Path.Combine(file.Directory.FullName, curText + file.Extension);

        if (newPath == file.FullName)
            return true;

        try
        {
            file.MoveTo(newPath);
            Close();
            success?.Invoke();

            return true;
        }
        catch (IOException e)
        {
            Messages.Message(e is DirectoryNotFoundException ? "Error renaming." : "File already exists.", MessageTypeDefOf.RejectInput, false);
            return false;
        }
    }

    public override bool Validate(string str)
    {
        return str.Length < 30;
    }
}
