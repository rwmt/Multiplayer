using System;

namespace Multiplayer.Client;

public struct SessionDisconnectInfo
{
    public string titleTranslated;
    public string descTranslated;
    public string specialButtonTranslated;
    public Action specialButtonAction;
    public bool wideWindow;
}
