using System;
using System.Runtime.CompilerServices;

namespace Multiplayer.Common.Util;

public struct Blackhole : INotifyCompletion
{
    public bool IsCompleted => false;
    public void GetResult() { }
    public void OnCompleted(Action continuation) { }
    public Blackhole GetAwaiter() => this;
}
