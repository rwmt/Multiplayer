using Multiplayer.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Multiplayer.Client
{
    public class ClientConnectionState : MpConnectionState
    {
        public MultiplayerSession Session => Multiplayer.session;

        public ClientConnectionState(IConnection connection) : base(connection)
        {
        }
    }
}
