using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net;
using System.Text;

namespace Shadowsocks.Controller
{
    public interface ISocket
    {
        IAsyncResult BeginConnect(EndPoint remoteEP, AsyncCallback callback, object state);

        IAsyncResult BeginConnect(EndPoint remoteEP, Uri uri, AsyncCallback callback, object state);

        IAsyncResult BeginReceive(byte[] buffer, int offset, int size, SocketFlags socketFlags, AsyncCallback callback, object state);

        IAsyncResult BeginSend(byte[] buffer, int offset, int size, SocketFlags socketFlags, AsyncCallback callback, object state);

        void Close();

        void EndConnect(IAsyncResult asyncResult);

        int EndReceive(IAsyncResult asyncResult);

        int EndSend(IAsyncResult asyncResult);

        void SetSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName, bool optionValue);

        void Shutdown(SocketShutdown how);
    }
}
