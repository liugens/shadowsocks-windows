using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;

using Newtonsoft.Json;

using Shadowsocks.Model;

namespace Shadowsocks.Controller.Strategy
{
    using Statistics = Dictionary<string, List<StatisticsRecord>>;

    internal class StatisticsStrategy : IStrategy, IDisposable
    {
        private readonly ShadowsocksController _controller;
        private Server _currentServer;
        private readonly Timer _timer;
        private AvailabilityStatistics Service => _controller.availabilityStatistics;
        StatisticsStrategyConfiguration config;
        private int ChoiceKeptMilliseconds;

        public StatisticsStrategy(ShadowsocksController controller)
        {
            _controller = controller;
            config = _controller.GetCurrentStatisticsStrategyConfiguration();
            ChoiceKeptMilliseconds = (int)TimeSpan.FromMinutes(config.ChoiceKeptMinutes).TotalMilliseconds;
            var servers = controller.GetCurrentConfiguration().configs;
            var randomIndex = new Random().Next() % servers.Count;
            _currentServer = servers[randomIndex];  //choose a server randomly at first
            _timer = new Timer(ReloadStatisticsAndChooseAServer);
        }

        private void ReloadStatisticsAndChooseAServer(object obj)
        {
            Logging.Debug("Reloading statistics and choose a new server....");
            var servers = _controller.GetCurrentConfiguration().configs;
            ChooseNewServer(servers);
        }

        class ServerScore
        {
            public Server server;
            public float score;
        }

        //return the score by data
        //server with highest score will be choosen
        //private float? GetScore(string identifier, List<StatisticsRecord> records)
        //{
        //    StatisticsStrategyConfiguration config = _controller.GetCurrentStatisticsStrategyConfiguration();

        //    float? score = null;

        //    var averageRecord = new StatisticsRecord(identifier,
        //        records.Where(record => record.MaxInboundSpeed != null).Select(record => record.MaxInboundSpeed.Value).ToList(),
        //        records.Where(record => record.MaxOutboundSpeed != null).Select(record => record.MaxOutboundSpeed.Value).ToList(),
        //        records.Where(record => record.AverageLatency != null).Select(record => record.AverageLatency.Value).ToList());
        //    averageRecord.SetResponse(records.Select(record => record.AverageResponse).ToList());

        //    foreach (var calculation in config.Calculations)
        //    {
        //        var name = calculation.Key;
        //        var field = typeof (StatisticsRecord).GetField(name);
        //        dynamic value = field?.GetValue(averageRecord);
        //        var factor = calculation.Value;
        //        if (value == null || factor.Equals(0)) continue;
        //        score = score ?? 0;
        //        score += value * factor;
        //    }

        //    if (score != null)
        //    {
        //        Logging.Debug($"Highest score: {score} {JsonConvert.SerializeObject(averageRecord, Formatting.Indented)}");
        //    }
        //    return score;
        //}

        private ServerScore GetScore(Server server)
        {
            ConcurrentDictionary<string, StatisticsGroup> serverStatisticsGroups = Service._serverStatisticsGroups;
            StatisticsGroup group;
            ServerScore score = null;
            if (serverStatisticsGroups.TryGetValue(server.Identifier(), out group))
            {
                score = new ServerScore { server = server, score = .0F };
                //TODO:
            }
            return score;
        }

        private void ChooseNewServer(List<Server> servers)
        {
            try
            {
                List<ServerScore> list = new List<ServerScore>(servers.Count);
                foreach(Server server in servers)
                {
                    ServerScore score = GetScore(server);
                    if (score != null)
                    {
                        list.Add(score);
                    }
                }

                if (list.Count < 2)
                {
                    LogWhenEnabled("no enough statistics data or all factors in calculations are 0");
                    return;
                }

                var bestResult = list
                    .Aggregate((server1, server2) => server1.score > server2.score ? server1 : server2);

                LogWhenEnabled($"Switch to server: {bestResult.server.FriendlyName()} by statistics: score {bestResult.score}");
                _currentServer = bestResult.server;
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
            }
        }

        private void LogWhenEnabled(string log)
        {
            if (_controller.GetCurrentStrategy()?.ID == ID) //output when enabled
            {
                Console.WriteLine(log);
            }
        }

        public string ID => "com.shadowsocks.strategy.scbs";

        public string Name => I18N.GetString("Choose by statistics");

        public Server GetAServer(IStrategyCallerType type, IPEndPoint localIPEndPoint)
        {
            if (_currentServer == null)
            {
                ChooseNewServer(_controller.GetCurrentConfiguration().configs);
            }
            return _currentServer;  //current server cached for CachedInterval
        }

        public void ReloadServers()
        {
            config = _controller.GetCurrentStatisticsStrategyConfiguration();
            ChoiceKeptMilliseconds = (int)TimeSpan.FromMinutes(config.ChoiceKeptMinutes).TotalMilliseconds;
            ChooseNewServer(_controller.GetCurrentConfiguration().configs);
            _timer?.Change(0, ChoiceKeptMilliseconds);
        }

        public void SetFailure(Server server)
        {
            Logging.Debug($"failure: {server.FriendlyName()}");
        }

        public void UpdateLastRead(Server server)
        {
        }

        public void UpdateLastWrite(Server server)
        {
        }

        public void UpdateLatency(Server server, TimeSpan latency)
        {
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}
