using System.Globalization;
using System.Net;

namespace Multiplayer.Common.Util
{
    public static class Endpoints
    {
        // From IPEndPoint in .NET Core
        public static bool TryParse(string s, uint defaultPort, out IPEndPoint result)
        {
            s = s.Trim();
            int addressLength = s.Length;
            int lastColonPos = s.LastIndexOf(':');

            if (lastColonPos > 0)
            {
                if (s[lastColonPos - 1] == ']')
                    addressLength = lastColonPos;
                else if (s.Substring(0, lastColonPos).LastIndexOf(':') == -1)
                    addressLength = lastColonPos;
            }

            if (IPAddress.TryParse(s.Substring(0, addressLength), out IPAddress address))
            {
                uint port = defaultPort;
                if (addressLength == s.Length ||
                    uint.TryParse(s.Substring(addressLength + 1), NumberStyles.None, CultureInfo.InvariantCulture, out port) && port <= IPEndPoint.MaxPort)
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
