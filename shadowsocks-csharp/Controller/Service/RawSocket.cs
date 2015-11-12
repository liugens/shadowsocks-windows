using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net;
using System.Text;

namespace Shadowsocks.Controller
{
    public class RawSocket : Socket, ISocket
    {
        public RawSocket(SocketInformation socketInformation)
            : base(socketInformation)
        { }

        public RawSocket(AddressFamily addressFamily, SocketType socketType, ProtocolType protocolType)
            : base(addressFamily, socketType, protocolType)
        { }

        public IAsyncResult BeginConnect(EndPoint remoteEP, Uri uri, AsyncCallback callback, object state)
        {
            return BeginConnect(remoteEP, callback, state);
        }
    }
}
