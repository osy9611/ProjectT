using System.Collections;
using System.Collections.Generic;
namespace ProjectT.Server.Sockets
{
    public enum eConnectState
    {
        None,

        Connecting,
        Connected,

        Disconnecting,
        Disconnected,
        Returned
    }
}