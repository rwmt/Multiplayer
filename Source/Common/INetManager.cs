namespace Multiplayer.Common;

public interface INetManager
{
    public bool Start();
    public void Tick();
    public void Stop();
}
