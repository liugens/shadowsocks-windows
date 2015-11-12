using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading;
using System.Security.Cryptography;

namespace Shadowsocks.Controller
{
    public class WebSocket : Socket, ISocket
    {
        public Uri RemoteUri { get; private set; }

        public bool IsClosed { get; private set; }

        private string WebSocketKey { get; set; }

        private bool _recvClosePkg = false;
        private bool _sentClosePkg = false;

        public WebSocket(SocketInformation socketInformation)
            : base(socketInformation)
        { }

        public WebSocket(AddressFamily addressFamily, SocketType socketType, ProtocolType protocolType)
            : base(addressFamily, socketType, protocolType)
        { }

        public IAsyncResult BeginConnect(EndPoint remoteEP, Uri uri, AsyncCallback callback, object state)
        {
            this.RemoteUri = uri;
            return base.BeginConnect(remoteEP, new AsyncCallback(handshakeConnectCallback), new object[] { callback, state });
        }

        new public void EndConnect(IAsyncResult ar)
        {
            AsyncResult ars = (AsyncResult)ar;
            if (ars.Error != null)
                throw ars.Error;
        }

        new public IAsyncResult BeginReceive(byte[] buffer, int offset, int size,
            SocketFlags socketFlags, AsyncCallback callback, object state)
        {
            if (_sentClosePkg || _recvClosePkg)
                throw new Exception("WebSocket closed");
            return base.BeginReceive(buffer, offset, size, socketFlags,
                new AsyncCallback(receiveCallback), new object[] {
                callback, state, buffer, offset, size, socketFlags
            });
        }

        new public int EndReceive(IAsyncResult ar)
        {
            AsyncResult ars = (AsyncResult)ar;
            if (ars.Error != null)
                throw ars.Error;
            return ars.Size;
        }

        new public IAsyncResult BeginSend(byte[] buffer, int offset, int size,
            SocketFlags socketFlags, AsyncCallback callback, object state)
        {
            if (_sentClosePkg || _recvClosePkg)
                throw new Exception("WebSocket closed");
            byte[] data = encodeFrame(buffer, offset, size);
            return base.BeginSend(data, 0, data.Length, socketFlags, new AsyncCallback(sendCallback), new object[] {
                callback, state, buffer, offset, size, data
            });
        }

        new public int EndSend(IAsyncResult ar)
        {
            AsyncResult ars = (AsyncResult)ar;
            if (ars.Error != null)
                throw ars.Error;
            return ars.Size;
        }

        new public void Close()
        {
            try
            {
                IsClosed = true;
                byte[] data = encodeFrame(null, 0, 0, 8);
                base.BeginSend(data, 0, data.Length, 0, new AsyncCallback(closeSendCallback), null);
            }
            catch (Exception e)
            {
                try { base.Shutdown(SocketShutdown.Both); }
                catch { }
                try { base.Close(); }
                catch { }
            }
        }

        protected static void randBytes(byte[] buf, int offset, int length)
        {
            byte[] temp = new byte[length];
            RNGCryptoServiceProvider rngServiceProvider = new RNGCryptoServiceProvider();
            rngServiceProvider.GetBytes(temp);
            temp.CopyTo(buf, offset);
        }

        private void handshakeConnectCallback(IAsyncResult ar)
        {
            object[] arr = (object[])ar.AsyncState;
            try
            {
                base.EndConnect(ar);

                byte[] temp = new byte[16];
                randBytes(temp, 0, temp.Length);

                WebSocketKey = Convert.ToBase64String(temp);

                StringBuilder s = new StringBuilder();
                s.AppendLine($"GET {RemoteUri.PathAndQuery} HTTP/1.1");
                s.AppendLine($"Host: {RemoteUri.Host}");
                s.AppendLine("Connection: Upgrade");
                s.AppendLine("Pragma: no-cache");
                s.AppendLine("Cache-Control: no-cache");
                s.AppendLine("Upgrade: websocket");
                s.AppendLine($"Origin: {new Uri(RemoteUri, "/").ToString()}");
                s.AppendLine("Sec-WebSocket-Version: 13");
                s.AppendLine("User-Agent: Mozilla/5.0 (Windows NT 6.3; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/46.0.2490.71 Safari/537.36");
                s.AppendLine("Accept-Encoding: gzip, deflate, sdch");
                s.AppendLine("Accept-Language: zh-CN,zh;q=0.8,en-US;q=0.6,en;q=0.4");
                s.AppendLine($"Sec-WebSocket-Key: {WebSocketKey}");
                s.AppendLine("Sec-WebSocket-Extensions: permessage-deflate; client_max_window_bits");
                s.AppendLine();
                byte[] sendBuffer = Encoding.ASCII.GetBytes(s.ToString());
                base.BeginSend(sendBuffer, 0, sendBuffer.Length, 0, new AsyncCallback(handshakeSendCallback), arr);
            }
            catch (Exception e)
            {
                ((AsyncCallback)arr[0])(new AsyncResult()
                {
                    AsyncState = arr[1],
                    AsyncWaitHandle = ar.AsyncWaitHandle,
                    CompletedSynchronously = ar.CompletedSynchronously,
                    IsCompleted = ar.IsCompleted,
                    Error = e
                });
            }
        }

        private void handshakeSendCallback(IAsyncResult ar)
        {
            object[] arr = (object[])ar.AsyncState;
            try
            {
                base.EndSend(ar);

                byte[] recvBuffer = new byte[4096];

                base.BeginReceive(recvBuffer, 0, recvBuffer.Length, 0,
                    new AsyncCallback(handshakeReceiveCallback),
                    new object[] { arr[0], arr[1], recvBuffer });
            }
            catch (Exception e)
            {
                ((AsyncCallback)arr[0])(new AsyncResult()
                {
                    AsyncState = arr[1],
                    AsyncWaitHandle = ar.AsyncWaitHandle,
                    CompletedSynchronously = ar.CompletedSynchronously,
                    IsCompleted = ar.IsCompleted,
                    Error = e
                });
            }
        }

        private void handshakeReceiveCallback(IAsyncResult ar)
        {
            object[] arr = (object[])ar.AsyncState;
            try
            {
                int bytesRead = base.EndReceive(ar);
                bool succ = false;
                if (bytesRead > 0)
                {
                    string request = Encoding.UTF8.GetString((byte[])arr[2], 0, bytesRead);
                    string[] lines = request.Split(new char[] { '\r', '\n' },
                        StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length > 0 && lines[0].IndexOf(" 101 ") > 0)
                    {
                        for (int i = 1; i < lines.Length; i++)
                        {
                            string[] kv = lines[i].Split(new char[] { ':' }, 2);
                            if (kv.Length == 2)
                            {
                                if (kv[0] == "Sec-WebSocket-Accept")
                                {
                                    string acceptKey = kv[1].Trim();
                                    byte[] data = Encoding.ASCII.GetBytes(
                                        WebSocketKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11");
                                    byte[] result;
                                    SHA1 sha = new SHA1CryptoServiceProvider();
                                    result = sha.ComputeHash(data);
                                    string k = Convert.ToBase64String(result);
                                    succ = k == acceptKey;
                                    break;
                                }
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                }
                if (succ)
                {
                    ((AsyncCallback)arr[0])(new AsyncResult()
                    {
                        AsyncState = arr[1],
                        AsyncWaitHandle = ar.AsyncWaitHandle,
                        CompletedSynchronously = ar.CompletedSynchronously,
                        IsCompleted = ar.IsCompleted,
                        Error = null
                    });
                }
                else
                {
                    ((AsyncCallback)arr[0])(new AsyncResult()
                    {
                        AsyncState = arr[1],
                        AsyncWaitHandle = ar.AsyncWaitHandle,
                        CompletedSynchronously = ar.CompletedSynchronously,
                        IsCompleted = ar.IsCompleted,
                        Error = new Exception("Failed to handshake")
                    });
                }
            }
            catch (Exception e)
            {
                ((AsyncCallback)arr[0])(new AsyncResult()
                {
                    AsyncState = arr[1],
                    AsyncWaitHandle = ar.AsyncWaitHandle,
                    CompletedSynchronously = ar.CompletedSynchronously,
                    IsCompleted = ar.IsCompleted,
                    Error = e
                });
            }
        }

        private int decodeFrame(byte[] buffer, int offset, int size, out int opcode)
        {
            int fin = 0,
                rsv = 0,
                mask = 0,
                payload_len = 0;
            byte[] mask_key = null;
            int i = offset;
            opcode = 0;
            if (size > i)
            {
                fin = (buffer[i] >> 7) & 0x1;
                rsv = (buffer[i] >> 4) & 0x7;
                opcode = buffer[i] & 0xf;
                i++;
            }
            else
                return -1;

            // 解析第二个字节
            if (size > i)
            {
                mask = (buffer[i] >> 7) & 0x1;
                payload_len = buffer[i] & 0x7f;
                i++;
            }
            else
                return -1;

            // 处理特殊长度126和127
            if (payload_len == 126)
            {
                if (size >= i + 2)
                {
                    payload_len = buffer[i++];
                    payload_len <<= 8;
                    payload_len |= buffer[i++];
                    payload_len &= 0xffff;
                }
                else
                    return -1;
            }
            else if (payload_len == 127)
            {
                if (size >= i + 8)
                {
                    i += 4;
                    payload_len = buffer[i++];
                    payload_len <<= 8;
                    payload_len |= buffer[i++];
                    payload_len <<= 8;
                    payload_len |= buffer[i++];
                    payload_len <<= 8;
                    payload_len |= buffer[i++];
                    payload_len &= 0x7fffffff;
                }
                else
                    return -1;
            }

            // 是否使用掩码
            if (mask != 0)
            {
                if (size >= i + 4)
                {
                    mask_key = new byte[4];
                    mask_key[0] = buffer[i++];
                    mask_key[1] = buffer[i++];
                    mask_key[2] = buffer[i++];
                    mask_key[3] = buffer[i++];
                }
                else
                    return -1;
            }

            // 读取 Payload Data
            if (size >= i + payload_len)
            {
                Buffer.BlockCopy(buffer, i, buffer, offset, payload_len);
            }
            else
                return -1;

            if (payload_len > 0 && mask != 0)
            {
                int j;
                //对数据和掩码做异或运算
                for (j = 0; j < payload_len; j++)
                    buffer[offset + j] ^= mask_key[j % 4];
            }
            return payload_len;
        }

        private int encodeFrameLen(int dataLen)
        {
            int retval;
            retval = 2;
            if (dataLen > 126)
            {
                if (dataLen >= 0x7FFF)
                    retval += 8;
                else
                    retval += 2;
            }
            retval += 4;
            retval += dataLen;
            return retval;
        }

        private byte[] encodeFrame(byte[] buffer, int offset, int len, int opcode = 1)
        {
            int size;
            byte[] buf;

            size = encodeFrameLen(len);
            buf = new byte[size];

            int i = 0;
            buf[i++] = (byte)((0x80 | (opcode & 0xf)) & 0x8f);
            if (len > 126)
            {
                if (len > 0x7FFF)
                {
                    buf[i++] = 0xff;
                    buf[i++] = 0x00;
                    buf[i++] = 0x00;
                    buf[i++] = 0x00;
                    buf[i++] = 0x00;
                    buf[i++] = (byte)(((uint)len >> 24) & 0xff);
                    buf[i++] = (byte)(((uint)len >> 16) & 0xff);
                    buf[i++] = (byte)(((uint)len >> 8) & 0xff);
                    buf[i++] = (byte)(((uint)len >> 0) & 0xff);
                }
                else
                {
                    buf[i++] = 0xfe;
                    buf[i++] = (byte)(((uint)len >> 8) & 0xff);
                    buf[i++] = (byte)(((uint)len >> 0) & 0xff);
                }
            }
            else
                buf[i++] = (byte)((0x80 | ((uint)len & 0x7f)) & 0xff);

            byte[] mask_key = new byte[4];
            randBytes(mask_key, 0, mask_key.Length);
            Buffer.BlockCopy(mask_key, 0, buf, i, 4);
            i += 4;

            if (len > 0)
            {
                Buffer.BlockCopy(buffer, offset, buf, i, len);
                for (int j = 0; j < len; j++)
                    buf[i + j] ^= mask_key[j % 4];
            }
            return buf;
        }

        private void receiveCallback(IAsyncResult ar)
        {
            object[] arr = (object[])ar.AsyncState;
            try
            {
                int bytesRead = base.EndReceive(ar);
                int opcode = 0;
                bytesRead = decodeFrame((byte[])arr[2], (int)arr[3], bytesRead, out opcode);
                if (opcode == 8)
                {
                    _recvClosePkg = true;
                    bytesRead = 0;
                }
                else if (opcode != 1 && opcode != 2)
                {
                    ar = base.BeginReceive((byte[])arr[2], (int)arr[3], (int)arr[4], (SocketFlags)arr[5],
                        new AsyncCallback(receiveCallback), arr);
                    return;
                }
                ((AsyncCallback)arr[0])(new AsyncResult()
                {
                    AsyncState = arr[1],
                    AsyncWaitHandle = ar.AsyncWaitHandle,
                    CompletedSynchronously = ar.CompletedSynchronously,
                    IsCompleted = ar.IsCompleted,
                    Error = null,
                    Size = bytesRead
                });
            }
            catch (Exception e)
            {
                ((AsyncCallback)arr[0])(new AsyncResult()
                {
                    AsyncState = arr[1],
                    AsyncWaitHandle = ar.AsyncWaitHandle,
                    CompletedSynchronously = ar.CompletedSynchronously,
                    IsCompleted = ar.IsCompleted,
                    Error = e
                });
            }
        }

        private void sendCallback(IAsyncResult ar)
        {
            object[] arr = (object[])ar.AsyncState;
            try
            {
                int bytesSend = base.EndSend(ar);
                ((AsyncCallback)arr[0])(new AsyncResult()
                {
                    AsyncState = arr[1],
                    AsyncWaitHandle = ar.AsyncWaitHandle,
                    CompletedSynchronously = ar.CompletedSynchronously,
                    IsCompleted = ar.IsCompleted,
                    Error = null,
                    Size = bytesSend
                });
            }
            catch (Exception e)
            {
                ((AsyncCallback)arr[0])(new AsyncResult()
                {
                    AsyncState = arr[1],
                    AsyncWaitHandle = ar.AsyncWaitHandle,
                    CompletedSynchronously = ar.CompletedSynchronously,
                    IsCompleted = ar.IsCompleted,
                    Error = e
                });
            }
        }

        private void closeSendCallback(IAsyncResult ar)
        {
            try
            {
                base.EndSend(ar);
                _sentClosePkg = true;
                if (_sentClosePkg && _recvClosePkg)
                {
                    try { base.Shutdown(SocketShutdown.Both); }
                    catch { }
                    try { base.Close(); }
                    catch { }
                }
                else
                {
                    byte[] data = new byte[1024];
                    base.BeginReceive(data, 0, data.Length, 0,
                        new AsyncCallback(closeReceiveCallback), data);
                }
            }
            catch (Exception e)
            {
                try { base.Shutdown(SocketShutdown.Both); }
                catch { }
                try { base.Close(); }
                catch { }
            }
        }

        private void closeReceiveCallback(IAsyncResult ar)
        {
            byte[] data = (byte[])ar.AsyncState;
            try
            {
                int bytesRead = base.EndReceive(ar);
                int opcode = 0;
                bytesRead = decodeFrame(data, 0, bytesRead, out opcode);
                if (opcode == 8)
                {
                    try { base.Shutdown(SocketShutdown.Both); }
                    catch { }
                    try { base.Close(); }
                    catch { }
                }
                else
                {
                    throw new Exception("unexpect frame");
                }
            }
            catch (Exception e)
            {
                try { base.Shutdown(SocketShutdown.Both); }
                catch { }
                try { base.Close(); }
                catch { }
            }
        }

        class AsyncResult : IAsyncResult
        {
            public object AsyncState { get; set; }

            public WaitHandle AsyncWaitHandle { get; set; }

            public bool CompletedSynchronously { get; set; }

            public bool IsCompleted { get; set; }

            public Exception Error { get; set; }

            public int Size { get; set; }
        }
    }
}
