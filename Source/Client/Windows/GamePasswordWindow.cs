using Multiplayer.Common;
using Verse;

namespace Multiplayer.Client;

public class GamePasswordWindow : AbstractTextInputWindow
{
    public bool returnToServerBrowser;

    public GamePasswordWindow()
    {
        title = "MpGamePassword".Translate();
        doCloseX = false;
        closeOnCancel = false;
        closeOnClickedOutside = false;
        acceptBtnLabel = "MpConnectButton".Translate();
        closeBtnLabel = "CancelButton".Translate();
        passwordField = true;
    }

    public override bool Accept()
    {
        Multiplayer.Client.Send(Packets.Client_Username, curText, Multiplayer.username);
        Close(false);
        return true;
    }

    public override void OnCloseButton()
    {
        Multiplayer.StopMultiplayerAndClearAllWindows();

        if (returnToServerBrowser)
            Find.WindowStack.Add(new ServerBrowser());
    }
}
