using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Multiplayer.Client
{
    public interface IHasLogger
    {
        public SyncLogger Log { get; }
    }
}
