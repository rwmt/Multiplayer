using Verse;
using Verse.Profile;

namespace Multiplayer.Client.Saving;

public class ConvertToSp
{
    public static void DoConvert()
    {
        LongEventHandler.QueueLongEvent(() =>
        {
            SaveReplay();
            PrepareSingleplayer();
            PrepareLoading();
        }, "Play", "MpConvertingToSp", true, null);
    }

    private static void SaveReplay()
    {
        const string suffix = "-preconvert";
        var saveName = $"{GenFile.SanitizedFileName(Multiplayer.session.gameName)}{suffix}";
        MultiplayerSession.SaveGameToFile_Overwrite(saveName, false);
    }

    private static void PrepareSingleplayer()
    {
        Find.GameInfo.permadeathMode = false;
    }

    private static void PrepareLoading()
    {
        Multiplayer.StopMultiplayer();

        var doc = SaveLoad.SaveGameToDoc();
        MemoryUtility.ClearAllMapsAndWorld();

        Current.Game = new Game
        {
            InitData = new GameInitData
            {
                gameToLoad = "play"
            }
        };

        LoadPatch.gameToLoad = new TempGameData(doc, new byte[0]);
    }
}
