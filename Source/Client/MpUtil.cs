using Harmony;
using Multiplayer.Common;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public static class MpUtil
    {
        public static string FixedEllipsis()
        {
            int num = Mathf.FloorToInt(Time.realtimeSinceStartup) % 3;
            if (num == 0)
                return ".  ";
            if (num == 1)
                return ".. ";
            return "...";
        }

        public static IEnumerable<Type> AllModTypes()
        {
            foreach (ModContentPack mod in LoadedModManager.RunningMods)
                for (int i = 0; i < mod.assemblies.loadedAssemblies.Count; i++)
                    foreach (Type t in mod.assemblies.loadedAssemblies[i].GetTypes())
                        yield return t;
        }

        public unsafe static void MarkNoInlining(MethodBase method)
        {
            ushort* iflags = (ushort*)(method.MethodHandle.Value) + 1;
            *iflags |= (ushort)MethodImplOptions.NoInlining;
        }

        public static T UninitializedObject<T>()
        {
            return (T)FormatterServices.GetUninitializedObject(typeof(T));
        }

        // Copied from Harmony
        public static MethodBase GetOriginalMethod(HarmonyMethod attr)
        {
            if (attr.declaringType == null) return null;

            switch (attr.methodType)
            {
                case MethodType.Normal:
                    if (attr.methodName == null)
                        return null;
                    return AccessTools.DeclaredMethod(attr.declaringType, attr.methodName, attr.argumentTypes);

                case MethodType.Getter:
                    if (attr.methodName == null)
                        return null;
                    return AccessTools.DeclaredProperty(attr.declaringType, attr.methodName).GetGetMethod(true);

                case MethodType.Setter:
                    if (attr.methodName == null)
                        return null;
                    return AccessTools.DeclaredProperty(attr.declaringType, attr.methodName).GetSetMethod(true);

                case MethodType.Constructor:
                    return AccessTools.DeclaredConstructor(attr.declaringType, attr.argumentTypes);

                case MethodType.StaticConstructor:
                    return AccessTools.GetDeclaredConstructors(attr.declaringType)
                        .Where(c => c.IsStatic)
                        .FirstOrDefault();
            }

            return null;
        }

        // https://stackoverflow.com/a/27376368
        public static string GetLocalIpAddress()
        {
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.IP))
                {
                    socket.Connect("8.8.8.8", 65530);
                    IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                    return endPoint.Address.ToString();
                }
            }
            catch
            {
                return Dns.GetHostEntry(Dns.GetHostName()).AddressList.FirstOrDefault(i => i.AddressFamily == AddressFamily.InterNetwork).ToString();
            }
        }

        public static void CleanSteamNet(int channel)
        {
            while (SteamNetworking.IsP2PPacketAvailable(out uint size, channel))
                SteamNetworking.ReadP2PPacket(new byte[size], size, out uint sizeRead, out CSteamID remote, channel);
        }

        public static IEnumerable<SteamPacket> ReadSteamPackets()
        {
            while (SteamNetworking.IsP2PPacketAvailable(out uint size, 0))
            {
                byte[] data = new byte[size];
                SteamNetworking.ReadP2PPacket(data, size, out uint sizeRead, out CSteamID remote, 0);

                if (data.Length <= 0) continue;

                var reader = new ByteReader(data);
                byte info = reader.ReadByte();
                bool joinPacket = (info & 1) > 0;
                bool reliable = (info & 2) > 0;

                yield return new SteamPacket() { remote = remote, data = reader, joinPacket = joinPacket, reliable = reliable };
            }
        }
    }

    public struct SteamPacket
    {
        public CSteamID remote;
        public ByteReader data;
        public bool joinPacket;
        public bool reliable;
    }

    public static class SteamImages
    {
        public static Dictionary<int, Texture2D> cache = new Dictionary<int, Texture2D>();

        // Remember to flip it
        public static Texture2D GetTexture(int id)
        {
            if (cache.TryGetValue(id, out Texture2D tex))
                return tex;

            if (!SteamUtils.GetImageSize(id, out uint width, out uint height))
            {
                cache[id] = null;
                return null;
            }

            uint sizeInBytes = width * height * 4;
            byte[] data = new byte[sizeInBytes];

            if (!SteamUtils.GetImageRGBA(id, data, (int)sizeInBytes))
            {
                cache[id] = null;
                return null;
            }

            tex = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false);
            tex.LoadRawTextureData(data);
            tex.Apply();

            cache[id] = tex;

            return tex;
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class HotSwappableAttribute : Attribute
    {
    }

    public struct Container<T>
    {
        private readonly T _value;
        public T Inner => _value;

        public Container(T value)
        {
            _value = value;
        }

        public static implicit operator Container<T>(T value)
        {
            return new Container<T>(value);
        }
    }

}
