using System.IO;
using UnityEngine;

namespace MultiplayerLoader;

public partial class Multiplayer
{
    partial void InitializeVersionRuntime() =>
        ReflectionOnlyUnityResolver.Install(Path.GetDirectoryName(typeof(Application).Assembly.Location));
}
