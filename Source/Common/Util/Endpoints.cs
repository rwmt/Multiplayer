using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Multiplayer.Common.Util
{
    public static class Endpoints
    {
        // https://stackoverflow.com/a/27376368
        public static string? GetLocalIpAddress()
        {
            try
            {
                using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.IP);
                socket.Connect("8.8.8.8", 65530);
                var endPoint = socket.LocalEndPoint as IPEndPoint;
                return endPoint?.Address.ToString();
            }
            catch
            {
                return Dns.GetHostEntry(Dns.GetHostName()).AddressList.FirstOrDefault(i => i.AddressFamily == AddressFamily.InterNetwork)?.ToString();
            }
        }

        // From IPEndPoint in .NET Core
        public static bool TryParse(string s, uint defaultPort, [NotNullWhen(true)] out IPEndPoint? result)
        {
            s = s.Trim();
            int addressLength = s.Length;
            int lastColonPos = s.LastIndexOf(':');

            if (lastColonPos > 0)
            {
                if (s[lastColonPos - 1] == ']' || s[..lastColonPos].LastIndexOf(':') == -1)
                    addressLength = lastColonPos;
            }

            if (IPAddress.TryParse(s[..addressLength], out IPAddress address))
            {
                uint port = defaultPort;
                if (addressLength == s.Length ||
                    uint.TryParse(s[(addressLength + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out port) && port <= IPEndPoint.MaxPort)
                {
                    result = new IPEndPoint(address, (int)port);
                    return true;
                }
            }

            result = null;
            return false;
        }
    }
}
