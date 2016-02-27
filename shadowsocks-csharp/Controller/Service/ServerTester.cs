using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Timers;
using Shadowsocks.Encryption;
using Shadowsocks.Model;
using Timer = System.Timers.Timer;

namespace Shadowsocks.Controller.Service
{
    public class ServerTesterEventArgs : EventArgs
    {
        /// <summary>
        /// value is null when no error
        /// </summary>
        public Exception Error;

        /// <summary>
        /// consumed time on connect server
        /// </summary>
        public long ConnectionTime;

        /// <summary>
        /// total size downloaded
        /// </summary>
        public long DownloadTotal;

        /// <summary>
        /// consumed milliseconds on download
        /// </summary>
        public long DownloadMilliseconds;

        /// <summary>
        /// average speed per second
        /// </summary>
        public long DownloadSpeed;
    }

    public class ServerTesterProgressEventArgs : EventArgs
    {
        /// <summary>
        /// cancel download
        /// </summary>
        public bool Cancel;

        /// <summary>
        /// total size need download.
        /// zero when no Content-Length include in response header
        /// </summary>
        public long Total;

        /// <summary>
        /// size downloaded
        /// </summary>
        public long Download;

        /// <summary>
        /// milliseconds from download start
        /// </summary>
        public long Milliseconds;
    }

    public class ServerTesterTimeoutException : Exception
    {
        public bool Connected { get; private set; }

        public ServerTesterTimeoutException(bool connected, string msg)
            : base(msg)
        {
            Connected = connected;
        }
    }

    public class ServerTesterCancelException : Exception
    {
        public ServerTesterCancelException(string msg)
            : base(msg)
        {
        }
    }

    public class ServerTester
    {
        // TODO: Customization
        public static int DownloadLengthMin = 1048576, DownloadLengthMax = 1572864;
        public static int DownloadTimeoutMin = 4000, DownloadTimeoutMax = 6000;
        private static readonly Random Random = new Random();
        public readonly double Quantity = Random.NextDouble();
        public readonly int DownloadLength;
        public readonly double DownloadTimeout;
        // TODO: Customization, HTTPS
        public string DownloadUrl = "http://dl-ssl.google.com/update2/installers/ChromeStandaloneSetup.exe";

        public event EventHandler<ServerTesterEventArgs> Completed;
        public event EventHandler<ServerTesterProgressEventArgs> Progress;
        private readonly Server server;

        private long connectionTime;
        private Timer timer;
        private Socket remote;
        private IEncryptor encryptor;
        private DateTime startTime;
        private bool connected;
        private volatile int closed;
        private const int BufferSize = 8192;
        private readonly byte[] recvBuffer = new byte[BufferSize];
        private readonly byte[] decryptBuffer = new byte[BufferSize];
        private long contentLength;
        private long recvTotal;
        private int statusCode;
        private bool headerFinish;

        public ServerTester(Server server)
        {
            this.server = server;
            DownloadLength = (int) (DownloadLengthMin + (DownloadLengthMax - DownloadLengthMin) * Quantity);
            DownloadTimeout = DownloadTimeoutMin + (DownloadTimeoutMax - DownloadTimeoutMin) * Quantity;
        }

        public void Start()
        {
            try
            {
                closed = 0;
                encryptor = EncryptorFactory.GetEncryptor(server.method, server.password, server.auth, false);
                StartConnect();
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
                FireCompleted(e);
            }
        }

        public void Close()
        {
#pragma warning disable 420
            if (Interlocked.CompareExchange(ref closed, 1, 0) == 1) return;
#pragma warning restore 420
            if (timer != null)
            {
                timer.Stop();
                timer.Dispose();
                timer = null;
            }
            if (remote != null)
            {
                try
                {
                    remote.Shutdown(SocketShutdown.Both);
                    remote.Close();
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                }
                finally
                {
                    remote = null;
                }
            }
            if (encryptor != null)
            {
                encryptor.Dispose();
                encryptor = null;
            }
        }

        private void FireCompleted(Exception e)
        {
            Completed?.Invoke(this, new ServerTesterEventArgs { Error = e });
        }

        private void FireCompleted(Exception e, long connectionTime, long downloadTotalSize, DateTime startTime)
        {
            if (Completed != null)
            {
                long milliseconds = (long)(DateTime.Now - startTime).TotalMilliseconds;
                long speed = milliseconds > 0 ? (downloadTotalSize * 1000) / milliseconds : 0;
                Completed(this, new ServerTesterEventArgs
                {
                    Error = e,
                    ConnectionTime = connectionTime,
                    DownloadTotal = downloadTotalSize,
                    DownloadMilliseconds = milliseconds,
                    DownloadSpeed = speed
                });
            }
        }

        private void StartConnect()
        {
            if (closed == 1) return;
            try
            {
                connected = false;

                IPAddress ipAddress;
                bool parsed = IPAddress.TryParse(server.server, out ipAddress);
                if (!parsed)
                {
                    IPHostEntry ipHostInfo = Dns.GetHostEntry(server.server);
                    ipAddress = ipHostInfo.AddressList[0];
                }
                IPEndPoint remoteEP = new IPEndPoint(ipAddress, server.server_port);

                remote = new Socket(ipAddress.AddressFamily,
                    SocketType.Stream, ProtocolType.Tcp);
                remote.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);

                startTime = DateTime.Now;
                timer = new Timer(DownloadTimeout) { AutoReset = false};
                timer.Elapsed += TimeoutExpired;
                timer.Start();

                remote.BeginConnect(remoteEP, ConnectCallback, timer);
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
                FireCompleted(e);
            }
        }

        private void TimeoutExpired(object sender, ElapsedEventArgs e)
        {
            if (closed == 1 || connected) return;
            Logging.Info($"{server.FriendlyName()} timed out");
            Close();
            FireCompleted(new ServerTesterTimeoutException(false, "Connect Server Timeout"));
        }

        private void ConnectCallback(IAsyncResult ar)
        {
            if (closed == 1) return;
            try
            {
                timer.Elapsed -= TimeoutExpired;
                timer.Enabled = false;
                timer.Dispose();
                timer = null;

                remote.EndConnect(ar);

                connected = true;

                connectionTime = (long)(DateTime.Now - startTime).TotalMilliseconds;
                StartDownload();
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                FireCompleted(e);
            }
        }

        private void StartDownload()
        {
            if (closed == 1) return;
            try
            {
                int bytesToSend;
                byte[] request = BuildRequestData(new Uri(DownloadUrl));
                byte[] buffer = new byte[request.Length + IVEncryptor.ONETIMEAUTH_BYTES + IVEncryptor.AUTH_BYTES + 32];
                encryptor.Encrypt(request, request.Length, buffer, out bytesToSend);
                startTime = DateTime.Now;
                contentLength = 0;
                recvTotal = 0;
                headerFinish = false;
                statusCode = 0;
                remote.BeginSend(buffer, 0, bytesToSend, 0, SendCallback, null);
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
                FireCompleted(e);
            }
        }

        private void SendCallback(IAsyncResult ar)
        {
            if (closed == 1) return;
            try
            {
                remote.EndSend(ar);
                startTime = DateTime.Now;
                remote.BeginReceive(recvBuffer, 0, BufferSize, 0, ReceiveCallback, null);
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
                FireCompleted(e);
            }
        }

        private void ReceiveCallback(IAsyncResult ar)
        {
            if (closed == 1) return;
            try
            {
                int bytesRead = remote.EndReceive(ar);
                if (bytesRead > 0)
                {
                    int bytesLen;
                    encryptor.Decrypt(recvBuffer, bytesRead, decryptBuffer, out bytesLen);
                    if (!headerFinish)
                    {
                        int offset = 0;
                        headerFinish = ParseResponseHeader(decryptBuffer,
                            ref offset, bytesLen, ref statusCode, ref contentLength);
                        if (statusCode != 200)
                        {
                            Close();
                            FireCompleted(new Exception($"server response {statusCode}"));
                            return;
                        }
                        bytesLen -= offset;
                    }
                    recvTotal += bytesLen;
                    if (Progress != null)
                    {
                        ServerTesterProgressEventArgs args = new ServerTesterProgressEventArgs()
                        {
                            Cancel = false,
                            Total = contentLength,
                            Download = recvTotal,
                            Milliseconds = (long)(DateTime.Now - startTime).TotalMilliseconds
                        };
                        Progress(this, args);
                        if (args.Cancel)
                        {
                            Close();
                            FireCompleted(new ServerTesterCancelException("Cancelled"),
                                connectionTime, recvTotal, startTime);
                            return;
                        }
                    }
                    if (contentLength > 0 && (recvTotal == contentLength || recvTotal >= DownloadLength))
                    {
                        Close();
                        FireCompleted(null, connectionTime, recvTotal, startTime);
                        return;
                    }
                    remote.BeginReceive(recvBuffer, 0, BufferSize, 0, ReceiveCallback, null);
                }
                else
                {
                    Close();
                    FireCompleted(contentLength == 0 || recvTotal == contentLength ? null
                        : new Exception("Server close the connection"), connectionTime, recvTotal, startTime);
                }
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
                FireCompleted(e);
            }
        }

        private string ReadLine(byte[] data, ref int offset, int len)
        {
            if (offset >= len)
                return null;
            int i = offset;
            while (i < len && data[i++] != '\n') { }
            string line = Encoding.UTF8.GetString(data, offset, i - offset).Trim();
            offset = i;
            return line;
        }

        private bool ParseResponseHeader(byte[] data, ref int offset, int len, ref int statusCode, ref long contentLength)
        {
            string line;
            if (statusCode == 0)
            {
                line = ReadLine(data, ref offset, len);
                if (line == null || !line.StartsWith("HTTP/"))
                    return false;
                string[] arr = line.Split(' ');
                if (arr.Length < 3)
                    return false;
                statusCode = Convert.ToInt32(arr[1]);
            }
            while ((line = ReadLine(data, ref offset, len)) != null)
            {
                if (line == "") return true;
                if (!line.StartsWith("Content-Length", StringComparison.InvariantCultureIgnoreCase)) continue;
                string[] arr = line.Split(':');
                contentLength = Convert.ToInt64(arr[1].Trim());
            }
            return false;
        }

        private static byte[] BuildRequestData(Uri uri)
        {
            if (!string.Equals(uri.Scheme, "HTTP", StringComparison.InvariantCultureIgnoreCase))
                throw new Exception("Unsupport scheme, expect HTTP");

            string path = uri.PathAndQuery;
            string host = uri.Host;
            int port = uri.Port;
            string requestStr = $@"GET {path} HTTP/1.1
Host: {host}{(port == 80 ? string.Empty : ":" + port)}
Connection: close

";
            byte[] requestBytes = Encoding.ASCII.GetBytes(requestStr);
            byte[] domainBytes = Encoding.ASCII.GetBytes(host);
            byte[] request = new byte[4 + domainBytes.Length + requestBytes.Length];
            int i = 0;
            request[i++] = 0x03;
            request[i++] = (byte)domainBytes.Length;
            Buffer.BlockCopy(domainBytes, 0, request, i, domainBytes.Length);
            i += domainBytes.Length;
            request[i++] = (byte)((port >> 8) & 0xff);
            request[i++] = (byte)(port & 0xff);
            Buffer.BlockCopy(requestBytes, 0, request, i, requestBytes.Length);
            return request;
        }

    }
}
