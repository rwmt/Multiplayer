namespace Multiplayer.Common;

public interface IChatSource
{
    void SendMsg(string msg);
    void SendRawMsg(string msg);
}
