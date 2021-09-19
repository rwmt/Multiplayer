using Multiplayer.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Multiplayer.Client
{
    public abstract class ClientBaseState : MpConnectionState
    {
        public MultiplayerSession Session => Multiplayer.session;

        public ClientBaseState(ConnectionBase connection) : base(connection)
        {
        }
    }
}
