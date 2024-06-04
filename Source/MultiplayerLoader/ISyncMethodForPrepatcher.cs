using System;
using Multiplayer.API;

namespace MultiplayerLoader;

public interface ISyncMethodForPrepatcher : ISyncMethod
{
    Type TargetType { get; }
}
