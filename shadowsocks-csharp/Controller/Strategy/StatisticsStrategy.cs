using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using Shadowsocks.Model;

namespace Shadowsocks.Controller.Strategy
{
    class StatisticsStrategy : IStrategy
    {
        private readonly ShadowsocksController _controller;
        private Server _currentServer;
        private readonly Timer _timer;
        private int ChoiceKeptMilliseconds
            => (int) TimeSpan.FromMinutes(_controller.StatisticsConfiguration.ChoiceKeptMinutes).TotalMilliseconds;

        public StatisticsStrategy(ShadowsocksController controller)
        {
            _controller = controller;
            var servers = controller.GetCurrentConfiguration().configs;
            var randomIndex = new Random().Next() % servers.Count();
            _currentServer = servers[randomIndex];  //choose a server randomly at first
            _timer = new Timer(ReloadStatisticsAndChooseAServer);
        }

        private void ReloadStatisticsAndChooseAServer(object obj)
        {
            Logging.Debug("Reloading statistics and choose a new server....");
            var servers = _controller.GetCurrentConfiguration().configs;
            ChooseNewServer(servers);
        }

        //return the score by data
        //server with highest score will be choosen
        private float GetScore(string serverName, AvailabilityStatistics.StatisticsData st)
        {
            var config = _controller.StatisticsConfiguration;
            if (st == null)
                return 0;
            var successTimes = st.packageTotal - st.packageLoss;
            var timedOutTimes = st.packageTimeout;
            float loss = timedOutTimes * 100 / (successTimes + timedOutTimes);
            int avg = st.roundtripTimeTotal / successTimes;
            float factor;
            float score = 0;
            if (!config.Calculations.TryGetValue("PackageLoss", out factor)) factor = 0;
            score += loss * factor;
            if (!config.Calculations.TryGetValue("AverageResponse", out factor)) factor = 0;
            score += avg * factor;
            if (!config.Calculations.TryGetValue("MinResponse", out factor)) factor = 0;
            score += st.minRoundtripTime * factor;
            if (!config.Calculations.TryGetValue("MaxResponse", out factor)) factor = 0;
            score += st.maxRoundtripTime * factor;
            Logging.Debug($"{serverName}  {SimpleJson.SimpleJson.SerializeObject(st)}");
            return score;
        }

        private void ChooseNewServer(List<Server> servers)
        {
            Dictionary<string, AvailabilityStatistics.StatisticsData> statistics
                = _controller.availabilityStatistics?.Statistics;
            if (statistics == null || servers.Count == 0)
            {
                return;
            }
            try
            {
                var bestResult = (from server in servers
                                  let name = server.FriendlyName()
                                  let st = statistics[name]
                                  where statistics.ContainsKey(name)
                                  select new
                                  {
                                      server,
                                      score = GetScore(name, st)
                                  }
                                  ).Aggregate((result1, result2) => result1.score > result2.score ? result1 : result2);

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

        public string Name => I18N.GetString("Choose By Total Package Loss");

        public Server GetAServer(IStrategyCallerType type, IPEndPoint localIPEndPoint)
        {
            var oldServer = _currentServer;
            if (oldServer == null)
            {
                ChooseNewServer(_controller.GetCurrentConfiguration().configs);
            }
            if (oldServer != _currentServer)
            {
            }
            return _currentServer;  //current server cached for CachedInterval
        }

        public void ReloadServers()
        {
            ChooseNewServer(_controller.GetCurrentConfiguration().configs);
            _timer?.Change(0, ChoiceKeptMilliseconds);
        }

        public void SetFailure(Server server)
        {
            Logging.Debug($"failure: {server.FriendlyName()}");
        }

        public void UpdateLastRead(Server server)
        {
            //TODO: combine this part of data with ICMP statics
        }

        public void UpdateLastWrite(Server server)
        {
            //TODO: combine this part of data with ICMP statics
        }

        public void UpdateLatency(Server server, TimeSpan latency)
        {
            //TODO: combine this part of data with ICMP statics
        }

    }
}
