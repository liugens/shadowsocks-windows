using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using Microsoft.VisualBasic.FileIO;
using Shadowsocks.Model;
using Shadowsocks.Util;

namespace Shadowsocks.Controller
{
    public class AvailabilityStatistics
    {
        private const string GetGeolocationAndIspAPI = "http://ip-api.com/json";
        private static readonly string DateTimePattern = "yyyy-MM-dd HH:mm:ss";
        private const string StatisticsFilesName = "shadowsocks.availability.csv";
        private const string Delimiter = ",";
        private const int Timeout = 500;
        private const int DelayBeforeStart = 1000;
        private static readonly DateTime UnknownDateTime = new DateTime(1970, 1, 1);

        public static string AvailabilityStatisticsFile;

        private List<Server> _servers;
        private StatisticsStrategyConfiguration _config;
        private Timer _timer;
        private PingState _pingState;
        private GeolocationAndIsp _currentGeolocationAndIsp;

        public Dictionary<string, StatisticsData> Statistics { get; private set; }


        //static constructor to initialize every public static fields before refereced
        static AvailabilityStatistics()
        {
            var temppath = Utils.GetTempPath();
            AvailabilityStatisticsFile = Path.Combine(temppath, StatisticsFilesName);
        }

        public AvailabilityStatistics(Configuration config, StatisticsStrategyConfiguration statisticsConfig)
        {
            UpdateConfiguration(config, statisticsConfig);
        }

        public void UpdateConfiguration(Configuration config, StatisticsStrategyConfiguration statisticsConfig)
        {
            _pingState = new PingState();
            Statistics = null;
            _servers = config.configs;

            Set(statisticsConfig);
        }

        private bool Set(StatisticsStrategyConfiguration config)
        {
            _config = config;
            try
            {
                if (config.StatisticsEnabled)
                {
                    if (_timer?.Change(DelayBeforeStart, (int)TimeSpan.FromMinutes(_config.DataCollectionMinutes).TotalMilliseconds) == null)
                    {
                        _timer = new Timer(Run, null, DelayBeforeStart, (int)TimeSpan.FromMinutes(_config.DataCollectionMinutes).TotalMilliseconds);
                    }
                }
                else
                {
                    _timer?.Dispose();
                }
                return true;
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                return false;
            }
        }

        private void Run(object obj)
        {
            if (Statistics == null)
            {
                Statistics = new Dictionary<string, StatisticsData>();
                Dictionary<string, List<PingReply>> rawData = LoadRawData();
                StatisticsRawData(rawData);
            }
            if (_pingState.finished())
            {
                _pingState.reset(_servers, _config.RepeatTimesNum);
                PingTest(_pingState);
            }
        }

        private void PingTest(PingState state)
        {
            if (!state.next())
                return;
            Server server = state.currentServer();
            if (server.server == "")
            {
                PingTest(state);
                return;
            }

            IPAddress ipAddress = Dns.GetHostAddresses(server.server).First(ip => ip.AddressFamily == AddressFamily.InterNetwork);
            var ping = new Ping();
            ping.PingCompleted += PingCompleted;

            state.reply = new PingReply();
            state.reply.timestamp = DateTime.Now;
            state.reply.serverName = server.FriendlyName();

            ping.SendAsync(ipAddress, Timeout, state);

            GetGeolocationAndIsp(state);
        }

        private void PingCompleted(object sender, PingCompletedEventArgs e)
        {
            PingState state = (PingState)e.UserState;
            Server server = state.currentServer();
            if (state != _pingState)
                return;
            try
            {
                if (e.Error != null)
                {
                    Logging.LogUsefulException(e.Error);
                    return;
                }
                lock (state.reply)
                {
                    state.reply.status = e.Reply.Status.ToString();
                    state.reply.roundtripTime = (int)e.Reply.RoundtripTime;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An exception occured when eveluating {server.FriendlyName()}");
                Logging.LogUsefulException(ex);
            }
            finally
            {
                lock (state.reply)
                {
                    if (state.reply.status == null)
                        state.reply.status = "Unknown";
                    if (state.reply.geolocation != null)
                    {
                        onTestCompleted(state);
                    }
                }
            }
        }

        private void GetGeolocationAndIsp(PingState state)
        {
            WebClient client = new WebClient();
            client.DownloadStringCompleted += GetGeolocationAndIspForPingCompleted;
            client.DownloadStringAsync(new Uri(GetGeolocationAndIspAPI), state);
        }

        private void GetGeolocationAndIspForPingCompleted(object sender, DownloadStringCompletedEventArgs e)
        {
            PingState state = (PingState)e.UserState;
            Server server = state.currentServer();
            if (state != _pingState)
                return;
            try
            {
                if (e.Error != null)
                {
                    Logging.LogUsefulException(e.Error);
                    return;
                }
                GeolocationAndIsp gi = ParseAPIResult(e.Result);
                if (gi != null)
                {
                    lock (state.reply)
                    {
                        state.reply.geolocation = gi.geolocation;
                        state.reply.isp = gi.isp;
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.LogUsefulException(ex);
            }
            finally
            {
                lock (state.reply)
                {
                    if (state.reply.geolocation == null)
                        state.reply.geolocation = "Unknown";
                    if (state.reply.isp == null)
                        state.reply.isp = "Unknown";
                    if (state.reply.status != null)
                        onTestCompleted(state);
                }
            }
        }

        private GeolocationAndIsp ParseAPIResult(string json)
        {
            dynamic obj;
            if (!SimpleJson.SimpleJson.TryDeserializeObject(json, out obj))
                return null;
            string country = obj["country"];
            string city = obj["city"];
            string isp = obj["isp"];
            if (country == null || city == null || isp == null)
                return null;
            return new GeolocationAndIsp()
            {
                geolocation = country + " " + city,
                isp = isp
            };
        }

        private void WriteResultToFile(Server server, PingReply reply)
        {
            string[] lines;

            string line = string.Format("{0},{1},{2},{3},{4},{5}",
                reply.timestamp.ToString(DateTimePattern),
                WrapForCSV(reply.serverName),
                WrapForCSV(reply.status),
                reply.roundtripTime,
                WrapForCSV(reply.geolocation),
                WrapForCSV(reply.isp));

            if (!File.Exists(AvailabilityStatisticsFile))
            {
                string headerLine = "Timestamp,Server,Status,RoundtripTime,Geolocation,ISP";
                lines = new[] { headerLine, line };
            }
            else
            {
                lines = new[] { line };
            }
            try
            {
                File.AppendAllLines(AvailabilityStatisticsFile, lines);
            }
            catch (IOException e)
            {
                Logging.LogUsefulException(e);
            }
        }

        private string WrapForCSV(string s)
        {
            if (s == null)
                return null;
            bool quot = false;
            if (s.IndexOf("\"") >= 0)
            {
                quot = true;
                s = s.Replace("\"", "\"\"");
            }
            if (s.IndexOf(",") >= 0)
            {
                quot = true;
            }
            return quot ? "\"" + s + "\"" : s;
        }

        private void onTestCompleted(PingState state)
        {
            Server server = state.currentServer();
            PingReply reply = state.reply;

            WriteResultToFile(server, reply);

            if (FilterByISPAndHour(reply, _currentGeolocationAndIsp))
            {
                UpdateStatistics(Statistics, reply);
            }

            PingTest(state);
        }

        private Dictionary<string, List<PingReply>> LoadRawData()
        {
            Dictionary<string, List<PingReply>> rawData = new Dictionary<string, List<PingReply>>();
            try
            {
                var path = AvailabilityStatisticsFile;
                Logging.Debug($"loading statistics from {path}");
                if (!File.Exists(path))
                    return rawData;
                using (TextFieldParser parser = new TextFieldParser(path))
                {
                    parser.SetDelimiters(new string[] { "," });
                    parser.HasFieldsEnclosedInQuotes = true;
                    string[] colFields = parser.ReadFields(); // skip first line
                    while (!parser.EndOfData)
                    {
                        string[] fields = parser.ReadFields();
                        DateTime timestamp = ParseExactOrUnknown(fields[0]);
                        string serverName = fields[1];
                        string status = fields[2];
                        int roundtripTime;
                        int.TryParse(fields[3], out roundtripTime);
                        string geolocation = fields.Length > 4 ? fields[4] : null;
                        string isp = fields.Length > 5 ? fields[5] : null;
                        List<PingReply> list = null;
                        if (rawData.ContainsKey(serverName))
                            list = rawData[serverName];
                        else
                        {
                            list = new List<PingReply>();
                            rawData.Add(serverName, list);
                        }
                        PingReply pr = new PingReply()
                        {
                            timestamp = timestamp,
                            serverName = serverName,
                            status = status,
                            roundtripTime = roundtripTime,
                            geolocation = geolocation,
                            isp = isp
                        };
                        list.Add(pr);
                    }
                }
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
            }
            return rawData;
        }

        private void StatisticsRawData(Dictionary<string, List<PingReply>> rawData)
        {
            if (_config.ByIsp)
            {
                WebClient client = new WebClient();
                client.DownloadStringCompleted += GetGeolocationAndIspForFilterCompleted;
                client.DownloadStringAsync(new Uri(GetGeolocationAndIspAPI), rawData);
            }
            else
            {
                Statistics = StatisticsRawData(rawData, new Filter(FilterByHour), null);
            }
        }

        delegate bool Filter(PingReply reply, object userState);

        private void UpdateStatistics(Dictionary<string, StatisticsData> statistics, PingReply reply)
        {
            StatisticsData st;
            if (statistics.ContainsKey(reply.serverName))
                st = statistics[reply.serverName];
            else
            {
                st = new StatisticsData()
                {
                    timestamp = DateTime.Now,
                    minRoundtripTime = int.MaxValue
                };
                statistics.Add(reply.serverName, st);
            }
            st.timestamp = DateTime.Now;
            st.packageTotal++;
            if (reply.status == IPStatus.Success.ToString())
            {
                st.roundtripTimeTotal += reply.roundtripTime;
                if (reply.roundtripTime < st.minRoundtripTime)
                    st.minRoundtripTime = reply.roundtripTime;
                if (reply.roundtripTime > st.maxRoundtripTime)
                    st.maxRoundtripTime = reply.roundtripTime;
            }
            else
            {
                st.packageLoss++;
                if (reply.status == IPStatus.TimedOut.ToString())
                    st.packageTimeout++;
            }
        }

        private Dictionary<string, StatisticsData> StatisticsRawData(Dictionary<string, List<PingReply>> rawData, Filter filter, object userState)
        {
            Dictionary<string, StatisticsData> statistics = new Dictionary<string, StatisticsData>();
            foreach (IEnumerable<PingReply> list in rawData.Values)
            {
                foreach (PingReply reply in list)
                {
                    if (!filter(reply, userState))
                        continue;
                    UpdateStatistics(statistics, reply);
                }
            }
            return statistics;
        }

        private void GetGeolocationAndIspForFilterCompleted(object sender, DownloadStringCompletedEventArgs e)
        {
            Dictionary<string, List<PingReply>> rawData = (Dictionary<string, List<PingReply>>)e.UserState;
            try
            {
                if (e.Error != null)
                {
                    Logging.LogUsefulException(e.Error);
                    return;
                }
                GeolocationAndIsp gi = ParseAPIResult(e.Result);
                if (gi == null)
                {
                    gi = new GeolocationAndIsp()
                    {
                        geolocation = "Unknown",
                        isp = "Unknown"
                    };
                }
                _currentGeolocationAndIsp = gi;
                Statistics = StatisticsRawData(rawData, new Filter(FilterByISPAndHour), gi);
            }
            catch (Exception ex)
            {
                Logging.LogUsefulException(ex);
            }
        }

        private bool FilterByHour(PingReply reply, object userState)
        {
            if (!_config.ByHourOfDay)
                return true;
            return reply.timestamp != UnknownDateTime || reply.timestamp.Hour.Equals(DateTime.Now.Hour);
        }

        private bool FilterByISP(PingReply reply, object userState)
        {
            if (_config.ByIsp)
                return true;
            GeolocationAndIsp gi = (GeolocationAndIsp)userState;
            if (gi == null)
                return false;
            return (reply.geolocation == gi.geolocation || reply.geolocation == "Unknown") &&
                (reply.isp == gi.isp || reply.isp == "Unknown");
        }

        private bool FilterByISPAndHour(PingReply reply, object userState)
        {
            if (FilterByISP(reply, userState))
                return FilterByHour(reply, userState);
            else
                return false;
        }

        public static DateTime ParseExactOrUnknown(string str)
        {
            DateTime dateTime;
            return !DateTime.TryParseExact(str, DateTimePattern, null, DateTimeStyles.None, out dateTime) ? UnknownDateTime : dateTime;
        }

        class PingState
        {
            public List<Server> servers;
            public int index;
            public int repeatTimes;
            public PingReply reply;

            public PingState()
            {
                reset();
            }

            public Server currentServer()
            {
                return servers[index / repeatTimes];
            }

            public bool finished()
            {
                return servers == null || (index >= 0 && (index / repeatTimes) >= servers.Count);
            }

            public bool next()
            {
                if (finished())
                    return false;
                index++;
                return !finished();
            }

            public void reset()
            {
                reset(null, 0);
            }

            public void reset(List<Server> servers, int repeatTimes)
            {
                this.servers = servers;
                this.repeatTimes = repeatTimes;
                this.index = -1;
                this.reply = null;
            }
        }

        public class PingReply
        {
            public DateTime timestamp;
            public string serverName;
            public string status;
            public int roundtripTime;
            public string geolocation;
            public string isp;
        }

        class GeolocationAndIsp
        {
            public string geolocation;
            public string isp;
        }

        public class StatisticsData
        {
            public DateTime timestamp;
            public int packageTotal;
            public int packageLoss;
            public int packageTimeout;
            public int roundtripTimeTotal;
            public int minRoundtripTime;
            public int maxRoundtripTime;

        }

    }
}
