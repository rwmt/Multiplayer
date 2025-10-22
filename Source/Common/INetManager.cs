namespace Multiplayer.Common;

public interface INetManager
{
    public bool Start();
    public void Tick();
    public void Stop();

    // TODO Almost certainly not necessary. Probably should just be replaced with Stop. For now left to simplify
    //   transition.
    public void OnServerStop();
}
