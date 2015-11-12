using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shadowsocks.Controller;
using Shadowsocks.Encryption;
using System.Threading;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace test
{
    [TestClass]
    public class UnitTest
    {
        [TestMethod]
        public void TestCompareVersion()
        {
            Assert.IsTrue(UpdateChecker.Asset.CompareVersion("2.3.1.0", "2.3.1") == 0);
            Assert.IsTrue(UpdateChecker.Asset.CompareVersion("1.2", "1.3") < 0);
            Assert.IsTrue(UpdateChecker.Asset.CompareVersion("1.3", "1.2") > 0);
            Assert.IsTrue(UpdateChecker.Asset.CompareVersion("1.3", "1.3") == 0);
            Assert.IsTrue(UpdateChecker.Asset.CompareVersion("1.2.1", "1.2") > 0);
            Assert.IsTrue(UpdateChecker.Asset.CompareVersion("2.3.1", "2.4") < 0);
            Assert.IsTrue(UpdateChecker.Asset.CompareVersion("1.3.2", "1.3.1") > 0);
        }

        private void RunEncryptionRound(IEncryptor encryptor, IEncryptor decryptor)
        {
            byte[] plain = new byte[16384];
            byte[] cipher = new byte[plain.Length + 16 + IVEncryptor.ONETIMEAUTH_BYTES + IVEncryptor.AUTH_BYTES];
            byte[] plain2 = new byte[plain.Length + 16];
            int outLen = 0;
            int outLen2 = 0;
            var random = new Random();
            random.NextBytes(plain);
            encryptor.Encrypt(plain, plain.Length, cipher, out outLen);
            decryptor.Decrypt(cipher, outLen, plain2, out outLen2);
            Assert.AreEqual(plain.Length, outLen2);
            for (int j = 0; j < plain.Length; j++)
            {
                Assert.AreEqual(plain[j], plain2[j]);
            }
            encryptor.Encrypt(plain, 1000, cipher, out outLen);
            decryptor.Decrypt(cipher, outLen, plain2, out outLen2);
            Assert.AreEqual(1000, outLen2);
            for (int j = 0; j < outLen2; j++)
            {
                Assert.AreEqual(plain[j], plain2[j]);
            }
            encryptor.Encrypt(plain, 12333, cipher, out outLen);
            decryptor.Decrypt(cipher, outLen, plain2, out outLen2);
            Assert.AreEqual(12333, outLen2);
            for (int j = 0; j < outLen2; j++)
            {
                Assert.AreEqual(plain[j], plain2[j]);
            }
        }

        private static bool encryptionFailed = false;
        private static object locker = new object();

        [TestMethod]
        public void TestPolarSSLEncryption()
        {
            // run it once before the multi-threading test to initialize global tables
            RunSinglePolarSSLEncryptionThread();
            List<Thread> threads = new List<Thread>();
            for (int i = 0; i < 10; i++)
            {
                Thread t = new Thread(new ThreadStart(RunSinglePolarSSLEncryptionThread));
                threads.Add(t);
                t.Start();
            }
            foreach (Thread t in threads)
            {
                t.Join();
            }
            Assert.IsFalse(encryptionFailed);
        }

        private void RunSinglePolarSSLEncryptionThread()
        {
            try
            {
                for (int i = 0; i < 100; i++)
                {
                    IEncryptor encryptor;
                    IEncryptor decryptor;
                    encryptor = new PolarSSLEncryptor("aes-256-cfb", "barfoo!", false, false);
                    decryptor = new PolarSSLEncryptor("aes-256-cfb", "barfoo!", false, false);
                    RunEncryptionRound(encryptor, decryptor);
                }
            }
            catch
            {
                encryptionFailed = true;
                throw;
            }
        }

        [TestMethod]
        public void TestRC4Encryption()
        {
            // run it once before the multi-threading test to initialize global tables
            RunSingleRC4EncryptionThread();
            List<Thread> threads = new List<Thread>();
            for (int i = 0; i < 10; i++)
            {
                Thread t = new Thread(new ThreadStart(RunSingleRC4EncryptionThread));
                threads.Add(t);
                t.Start();
            }
            foreach (Thread t in threads)
            {
                t.Join();
            }
            Assert.IsFalse(encryptionFailed);
        }

        private void RunSingleRC4EncryptionThread()
        {
            try
            {
                for (int i = 0; i < 100; i++)
                {
                    var random = new Random();
                    IEncryptor encryptor;
                    IEncryptor decryptor;
                    encryptor = new PolarSSLEncryptor("rc4-md5", "barfoo!", false, false);
                    decryptor = new PolarSSLEncryptor("rc4-md5", "barfoo!", false, false);
                    RunEncryptionRound(encryptor, decryptor);
                }
            }
            catch
            {
                encryptionFailed = true;
                throw;
            }
        }

        [TestMethod]
        public void TestSodiumEncryption()
        {
            // run it once before the multi-threading test to initialize global tables
            RunSingleSodiumEncryptionThread();
            List<Thread> threads = new List<Thread>();
            for (int i = 0; i < 10; i++)
            {
                Thread t = new Thread(new ThreadStart(RunSingleSodiumEncryptionThread));
                threads.Add(t);
                t.Start();
            }
            foreach (Thread t in threads)
            {
                t.Join();
            }
            Assert.IsFalse(encryptionFailed);
        }

        private void RunSingleSodiumEncryptionThread()
        {
            try
            {
                for (int i = 0; i < 100; i++)
                {
                    var random = new Random();
                    IEncryptor encryptor;
                    IEncryptor decryptor;
                    encryptor = new SodiumEncryptor("salsa20", "barfoo!", false, false);
                    decryptor = new SodiumEncryptor("salsa20", "barfoo!", false, false);
                    RunEncryptionRound(encryptor, decryptor);
                }
            }
            catch
            {
                encryptionFailed = true;
                throw;
            }
        }

        private bool websocketSuccess = false;

        [TestMethod]
        public void TestWebSocket()
        {
            Thread t = new Thread(new ThreadStart(delegate() {
                try
                {
                    IPAddress ipAddress;
                    Uri uri = new Uri("ws://echo.websocket.org");
                    bool parsed = IPAddress.TryParse(uri.Host, out ipAddress);
                    if (!parsed)
                    {
                        IPHostEntry ipHostInfo = Dns.GetHostEntry(uri.Host);
                        ipAddress = ipHostInfo.AddressList[0];
                    }
                    IPEndPoint remoteEP = new IPEndPoint(ipAddress, uri.Port);
                    WebSocket ws = new WebSocket(ipAddress.AddressFamily,
                        SocketType.Stream, ProtocolType.Tcp);
                    ws.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
                    ws.BeginConnect(remoteEP, uri,
                        new AsyncCallback(ConnectCallback), ws);
                    while (!ws.IsClosed)
                        Thread.Sleep(1000);
                }
                catch { }
            }));
            t.Start();
            t.Join();
            Assert.IsTrue(websocketSuccess);
        }

        private void ConnectCallback(IAsyncResult ar)
        {
            ISocket ws = (ISocket)ar.AsyncState;
            try
            {
                // Complete the connection.
                ws.EndConnect(ar);
                byte[] data = System.Text.Encoding.ASCII.GetBytes("Hello World!");
                ws.BeginSend(data, 0, data.Length, 0, new AsyncCallback(SendCallback), ws);
            }
            catch 
            {
                try { ws.Close(); }
                catch { }
            }
        }

        private void SendCallback(IAsyncResult ar)
        {
            ISocket ws = (ISocket)ar.AsyncState;
            try
            {
                ws.EndSend(ar);
                byte[] buf = new byte[4096];
                ws.BeginReceive(buf, 0, buf.Length, 0,
                    new AsyncCallback(ReceiveCallback), new object[] { ws, buf });
            }
            catch
            {
                try { ws.Close(); }
                catch { }
            }
        }

        private void ReceiveCallback(IAsyncResult ar)
        {
            object[] obj = (object[])ar.AsyncState;
            ISocket ws = (ISocket)obj[0];
            byte[] buf = (byte[])obj[1];
            try
            {
                int bytesRead = ws.EndReceive(ar);
                if (bytesRead > 0)
                {
                    string msg = System.Text.Encoding.ASCII.GetString(buf, 0, bytesRead);
                    if (msg == "Hello World!")
                        websocketSuccess = true;
                }
                try { ws.Close(); }
                catch { }
            }
            catch
            {
                try { ws.Close(); }
                catch { }
            }
        }

    }
}
