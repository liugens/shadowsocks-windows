using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Shadowsocks.Model
{
    // Simple processed records for a short period of time
    public class StatisticsRecord
    {
        public class Record
        {
            public long Average;
            public long Min;
            public long Max;
        }

        public class PingRecord : Record
        {
            public int Times;
            public int Loss;
            public float LossPercent;
        }

        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string ServerIdentifier { get; set; }
        public Record latency;
        public Record inbound;
        public Record outbound;
        public PingRecord ping;

        public bool IsEmpty
        {
            get { return latency == null && inbound == null && outbound == null && ping == null; }
        }

        public StatisticsRecord()
        {
        }

        public StatisticsRecord(string identifier)
        {
            ServerIdentifier = identifier;
        }

        public void SetResponse(ICollection<int?> responseRecords)
        {
            if (responseRecords == null) return;
            var records = responseRecords.Where(response => response != null).Select(response => response.Value).ToList();
            if (!records.Any()) return;
            ping = new PingRecord
            {
                Average = (long)records.Average(),
                Min = records.Min(),
                Max = records.Max(),
                Times = responseRecords.Count,
                Loss = responseRecords.Count(response => response == null),
                LossPercent = responseRecords.Count(response => response != null) / (float)responseRecords.Count
            };
        }

        public string ToCSVLine()
        {
            string content = $"{Timestamp},{ServerIdentifier},{latency?.Average},{latency?.Min},{latency?.Max},{inbound?.Average},{inbound?.Min},{inbound?.Max},{outbound?.Average},{outbound?.Min},{outbound?.Max},{ping?.Times},{ping?.Loss},{(ping == null ? "" : (100 - 100 * ping.LossPercent).ToString("F2"))},{ping?.Average},{ping?.Min},{ping?.Max}";
            return content;
        }
    }

    public class Statistics
    {
        public long total;
        public long min = long.MaxValue;
        public long max;
        public long count;

        public bool IsEmpty
        {
            get { return count == 0; }
        }

        public void Update(long v)
        {
            lock (this)
            {
                total += v;
                count++;
                if (v < min) min = v;
                if (v > max) max = v;
            }
        }

        public StatisticsRecord.Record CalcRecord()
        {
            StatisticsRecord.Record record = null;
            if (!IsEmpty)
            {
                record = new StatisticsRecord.Record();
                lock (this)
                {
                    record.Average = (total / count);
                    record.Max = max;
                    record.Min = min;

                    total = 0;
                    count = 0;
                    min = long.MaxValue;
                    max = 0;
                }
            }
            return record;
        }
    }

    public class ServerStatistics
    {
        public Server server;
        public Statistics latency;
        public Statistics inboundSpeed;
        public Statistics outboundSpeed;

        public long inboundTotal;
        public long outboundTotal;

        public ServerStatistics(Server server)
        {
            this.server = server;
            latency = new Statistics();
            inboundSpeed = new Statistics();
            outboundSpeed = new Statistics();
        }

        public void UpdateLatency(Server server, int latency)
        {
            this.latency.Update(latency);
        }

        public void UpdateInboundCounter(Server server, long n)
        {
            Interlocked.Add(ref inboundTotal, n);
        }

        public void UpdateOutboundCounter(Server server, long n)
        {
            Interlocked.Add(ref outboundTotal, n);
        }

        public void UpdateSpeed(double seconds)
        {
            long inbound = Interlocked.Exchange(ref inboundTotal, 0);
            long outbound = Interlocked.Exchange(ref outboundTotal, 0);
            int currentInboundSpeed = GetSpeedInKiBPerSecond(inbound, seconds);
            int currentOutboundSpeed = GetSpeedInKiBPerSecond(outbound, seconds);
            if (currentInboundSpeed > 0)
            {
                inboundSpeed.Update(currentInboundSpeed);
            }
            if (currentOutboundSpeed > 0)
            {
                outboundSpeed.Update(currentOutboundSpeed);
            }
            Shadowsocks.Controller.Logging.Debug(
                $"{server.Identifier()}: current/max inbound {currentInboundSpeed}/{inboundSpeed.max} KiB/s, current/max outbound {currentOutboundSpeed}/{outboundSpeed.max} KiB/s");
        }

        public StatisticsRecord UpdateRecord()
        {
            StatisticsRecord record = null;

            lock (this)
            {
                if (!latency.IsEmpty || !inboundSpeed.IsEmpty || !outboundSpeed.IsEmpty)
                {
                    record = new StatisticsRecord(server.Identifier());
                    if (!latency.IsEmpty)
                        record.latency = latency.CalcRecord();
                    if (!inboundSpeed.IsEmpty)
                        record.inbound = inboundSpeed.CalcRecord();
                    if (!outboundSpeed.IsEmpty)
                        record.outbound = outboundSpeed.CalcRecord();
                }
            }

            return record;
        }

        private static int GetSpeedInKiBPerSecond(long bytes, double seconds)
        {
            var result = (int)(bytes / seconds) / 1024;
            return result;
        }
    }

    public class StatisticsTotal
    {
        public Statistics latencyAverage;
        public Statistics latencyMin;
        public Statistics latencyMax;
        public Statistics inboundAverage;
        public Statistics inboundMin;
        public Statistics inboundMax;
        public Statistics outboundAverage;
        public Statistics outboundMin;
        public Statistics outboundMax;
        public Statistics pingAverage;
        public Statistics pingMin;
        public Statistics pingMax;
        public Statistics pingLossPercent;

        public StatisticsTotal()
        {
            latencyAverage = new Statistics();
            latencyMin = new Statistics();
            latencyMax = new Statistics();
            inboundAverage = new Statistics();
            inboundMin = new Statistics();
            inboundMax = new Statistics();
            outboundAverage = new Statistics();
            outboundMin = new Statistics();
            outboundMax = new Statistics();
            pingAverage = new Statistics();
            pingMin = new Statistics();
            pingMax = new Statistics();
            pingLossPercent = new Statistics();
        }

        public void Update(StatisticsRecord record)
        {
            if (record.latency != null)
            {
                latencyAverage.Update(record.latency.Average);
                latencyMin.Update(record.latency.Min);
                latencyMax.Update(record.latency.Max);
            }
            if (record.inbound != null)
            {
                inboundAverage.Update(record.inbound.Average);
                inboundMin.Update(record.inbound.Min);
                inboundMax.Update(record.inbound.Max);
            }
            if (record.outbound != null)
            {
                outboundAverage.Update(record.outbound.Average);
                outboundMin.Update(record.outbound.Min);
                outboundMax.Update(record.outbound.Max);
            }
            if (record.ping != null)
            {
                pingAverage.Update(record.ping.Average);
                pingMin.Update(record.ping.Min);
                pingMax.Update(record.ping.Max);
                pingLossPercent.Update((long)(record.ping.LossPercent * 100));
            }
        }
    }

    public class StatisticsGroup
    {
        public StatisticsTotal total;
        public StatisticsTotal[] hours;

        public StatisticsGroup()
        {
            total = new StatisticsTotal();
            hours = new StatisticsTotal[24];
            for(int i =0;i < 24;i++)
            {
                hours[i] = new StatisticsTotal();
            }
        }

        public void Update(StatisticsRecord record)
        {
            total.Update(record);
            hours[record.Timestamp.Hour].Update(record);
        }
    }
}
