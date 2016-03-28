using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Newtonsoft.Json;
using Shadowsocks.Model;
using Shadowsocks.Util;

namespace Shadowsocks.Controller
{
    public sealed class AvailabilityStatistics : IDisposable
    {
        private const string StatisticsFilesName = "shadowsocks.availability.v2.csv";
        public static string AvailabilityStatisticsFile;

        // Static Singleton Initialization
        public static AvailabilityStatistics Instance { get; } = new AvailabilityStatistics();

        //static constructor to initialize every public static fields before refereced
        static AvailabilityStatistics()
        {
            AvailabilityStatisticsFile = Utils.GetTempPath(StatisticsFilesName);
        }

        private ShadowsocksController _controller;
        public StatisticsStrategyConfiguration _config;
        private Timer _recorder; //analyze and save cached records to RawStatistics and filter records
        private Timer _speedMonior;
        private readonly TimeSpan DelayBeforeStart = TimeSpan.FromSeconds(1);
        private readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(2);
        private readonly TimeSpan MonitorInterval = TimeSpan.FromSeconds(1);

        private readonly ConcurrentDictionary<string, ServerStatistics> _serverStatistics = new ConcurrentDictionary<string, ServerStatistics>();
        public ConcurrentDictionary<string, StatisticsGroup> _serverStatisticsGroups = new ConcurrentDictionary<string, StatisticsGroup>();

        private AvailabilityStatistics()
        {
            LoadStatisticsGroups();
        }

        public void UpdateConfiguration(ShadowsocksController controller)
        {
            _controller = controller;
            _config = _controller.GetCurrentStatisticsStrategyConfiguration();
            _serverStatistics.Clear();
            try
            {
                if (_config.StatisticsEnabled)
                {
                    if (_recorder == null)
                    {
                        _recorder = new Timer(SaveRecord, null, DelayBeforeStart, TimeSpan.FromMinutes(_config.DataCollectionMinutes));
                    }
                    else
                    {
                        _recorder.Change(DelayBeforeStart, TimeSpan.FromMinutes(_config.DataCollectionMinutes));
                    }
                    if (_speedMonior == null)
                    {
                        _speedMonior = new Timer(UpdateSpeed, null, DelayBeforeStart, MonitorInterval);
                    }
                    else
                    {
                        _speedMonior.Change(DelayBeforeStart, MonitorInterval);
                    }
                }
                else
                {
                    if (_recorder != null)
                    {
                        _recorder.Dispose();
                        _recorder = null;
                    }
                    if (_speedMonior != null)
                    {
                        _speedMonior.Dispose();
                        _speedMonior = null;
                    }
                }
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
            }
        }

        public void Dispose()
        {
            if (_recorder != null)
            {
                _recorder.Dispose();
                _recorder = null;
            }
            if (_speedMonior != null)
            {
                _speedMonior.Dispose();
                _speedMonior = null;
            }
        }

        public void UpdateLatency(Server server, int latency)
        {
            ServerStatistics st = _serverStatistics.GetOrAdd(server.Identifier(), (k) => new ServerStatistics(server));
            st.UpdateLatency(server, latency);
        }

        public void UpdateInboundCounter(Server server, long n)
        {
            ServerStatistics st = _serverStatistics.GetOrAdd(server.Identifier(), (k) => new ServerStatistics(server));
            st.UpdateInboundCounter(server, n);
        }

        public void UpdateOutboundCounter(Server server, long n)
        {
            ServerStatistics st = _serverStatistics.GetOrAdd(server.Identifier(), (k) => new ServerStatistics(server));
            st.UpdateOutboundCounter(server, n);
        }

        private void UpdateSpeed(object _)
        {
            foreach (KeyValuePair<string, ServerStatistics> kv in _serverStatistics)
            {
                kv.Value.UpdateSpeed(MonitorInterval.TotalSeconds);
            }
        }

        private void SaveRecord(object _)
        {
            List<StatisticsRecord> records = new List<StatisticsRecord>();
            foreach (KeyValuePair<string, ServerStatistics> kv in _serverStatistics)
            {
                StatisticsRecord record = kv.Value.UpdateRecord();

                if (record != null && !record.IsEmpty)
                {
                    if (_config.Ping)
                    {
                        MyPing ping = new MyPing(kv.Value.server, _config.RepeatTimesNum);
                        ping.Completed += ping_Completed;
                        ping.Start(record);
                    }
                    else
                    {
                        records.Add(record);
                    }
                }
            }

            if(records.Count > 0)
            {
                Save(records);
            }
        }

        private void ping_Completed(object sender, MyPing.CompletedEventArgs e)
        {
            Server server = e.Server;
            StatisticsRecord record = (StatisticsRecord)e.UserState;
            if (e.Error == null)
            {
                record.SetResponse(e.RoundtripTime);
                Logging.Debug($"Ping {server.FriendlyName()} {e.RoundtripTime.Count} times, {(100 - record.ping.LossPercent * 100)}% packages loss, min {record.ping.Min} ms, max {record.ping.Max} ms, avg {record.ping.Average} ms");
            }
            if (!record.IsEmpty)
            {
                Save(record);
            }
        }

        private const string AvailabilityStatisticsFileHeader = "Timestamp,Server Identifier,Latency Average,Min Latency,Max Latency,Inbound Average Speed(KiB/S),Min Inbound Speed(KiB/S),Max Inbound Speed(KiB/S),Outbound Average Speed(KiB/S),Min Outbound Speed(KiB/S),Max Outbound Speed(KiB/S),Ping Times,Loss,Loss(%),Average Response(ms),Min Response(ms),Max Response(ms)";

        private void LoadStatisticsGroups()
        {
            try
            {
                var path = AvailabilityStatisticsFile + ".group.json";
                if (!File.Exists(path))
                    return;
                var content = File.ReadAllText(path);
                _serverStatisticsGroups = JsonConvert.DeserializeObject<ConcurrentDictionary<string, StatisticsGroup>>(content);
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
            }
            finally
            {
                _serverStatisticsGroups = new ConcurrentDictionary<string, StatisticsGroup>();
            }
        }

        private void SaveStatisticsGroups()
        {
            try
            {
                string content = JsonConvert.SerializeObject(_serverStatisticsGroups, Formatting.Indented);
                File.WriteAllText(AvailabilityStatisticsFile + ".group.json", content);
            }
            catch (IOException e)
            {
                Logging.LogUsefulException(e);
            }
        }

        private void Save(StatisticsRecord record)
        {
            Logging.Debug($"save statistics to {AvailabilityStatisticsFile}");
            if (record == null)
                return;
            try
            {
                StatisticsGroup group = _serverStatisticsGroups.GetOrAdd(record.ServerIdentifier, (k) => new StatisticsGroup());
                group.Update(record);
                SaveStatisticsGroups();
                StringBuilder content = new StringBuilder();
                if (!File.Exists(AvailabilityStatisticsFile))
                {
                    content.AppendLine(AvailabilityStatisticsFileHeader);
                }
                content.AppendLine(record.ToCSVLine());
                File.AppendAllText(AvailabilityStatisticsFile, content.ToString());
            }
            catch (IOException e)
            {
                Logging.LogUsefulException(e);
            }
        }

        private void Save(List<StatisticsRecord> records)
        {
            Logging.Debug($"save statistics to {AvailabilityStatisticsFile}");
            if (records.Count == 0)
                return;
            try
            {
                StringBuilder content = new StringBuilder();
                if (!File.Exists(AvailabilityStatisticsFile))
                {
                    content.AppendLine(AvailabilityStatisticsFileHeader);
                }
                foreach (StatisticsRecord record in records)
                {
                    content.AppendLine(record.ToCSVLine());
                    StatisticsGroup group = _serverStatisticsGroups.GetOrAdd(record.ServerIdentifier, (k) => new StatisticsGroup());
                    group.Update(record);
                }
                File.AppendAllText(AvailabilityStatisticsFile, content.ToString());
                SaveStatisticsGroups();
            }
            catch (IOException e)
            {
                Logging.LogUsefulException(e);
            }
        }

        class MyPing
        {
            //arguments for ICMP tests
            public const int TimeoutMilliseconds = 500;

            public EventHandler<CompletedEventArgs> Completed;
            private Server server;

            private int repeat;
            private IPAddress ip;
            private Ping ping;
            private List<int?> RoundtripTime;

            public MyPing(Server server, int repeat)
            {
                this.server = server;
                this.repeat = repeat;
                RoundtripTime = new List<int?>(repeat);
                ping = new Ping();
                ping.PingCompleted += Ping_PingCompleted;
            }

            public void Start(object userstate)
            {
                if (server.server == "")
                {
                    FireCompleted(new Exception("Invalid Server"), userstate);
                    return;
                }
                new Task(() => ICMPTest(0, userstate)).Start();
            }

            private void ICMPTest(int delay, object userstate)
            {
                try
                {
                    Logging.Debug($"Ping {server.FriendlyName()}");
                    if (ip == null)
                    {
                        ip = Dns.GetHostAddresses(server.server)
                                .First(
                                    ip =>
                                        ip.AddressFamily == AddressFamily.InterNetwork ||
                                        ip.AddressFamily == AddressFamily.InterNetworkV6);
                    }
                    repeat--;
                    if (delay > 0)
                        Thread.Sleep(delay);
                    ping.SendAsync(ip, TimeoutMilliseconds, userstate);
                }
                catch (Exception e)
                {
                    Logging.Error($"An exception occured while eveluating {server.FriendlyName()}");
                    Logging.LogUsefulException(e);
                    FireCompleted(e, userstate);
                }
            }

            private void Ping_PingCompleted(object sender, PingCompletedEventArgs e)
            {
                try
                {
                    if (e.Reply.Status == IPStatus.Success)
                    {
                        Logging.Debug($"Ping {server.FriendlyName()} {e.Reply.RoundtripTime} ms");
                        RoundtripTime.Add((int?)e.Reply.RoundtripTime);
                    }
                    else
                    {
                        Logging.Debug($"Ping {server.FriendlyName()} timeout");
                        RoundtripTime.Add(null);
                    }
                    TestNext(e.UserState);
                }
                catch (Exception ex)
                {
                    Logging.Error($"An exception occured while eveluating {server.FriendlyName()}");
                    Logging.LogUsefulException(ex);
                    FireCompleted(ex, e.UserState);
                }
            }

            private void TestNext(object userstate)
            {
                if (repeat > 0)
                {
                    //Do ICMPTest in a random frequency
                    int delay = TimeoutMilliseconds + new Random().Next() % TimeoutMilliseconds;
                    new Task(() => ICMPTest(delay, userstate)).Start();
                }
                else
                {
                    FireCompleted(null, userstate);
                }
            }

            private void FireCompleted(Exception error, object userstate)
            {
                Completed?.Invoke(this, new CompletedEventArgs
                {
                    Error = error,
                    Server = server,
                    RoundtripTime = RoundtripTime,
                    UserState = userstate
                });
            }

            public class CompletedEventArgs : EventArgs
            {
                public Exception Error;
                public Server Server;
                public List<int?> RoundtripTime;
                public object UserState;
            }
        }

    }
}
