using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using Shadowsocks.Controller;
using Shadowsocks.Model;
using SimpleJson;
using System.Net.NetworkInformation;
using Microsoft.VisualBasic.FileIO;
using System.IO;
using System.ComponentModel;

namespace Shadowsocks.View
{
    public partial class StatisticsStrategyConfigurationForm: Form
    {
        private readonly ShadowsocksController _controller;
        private StatisticsStrategyConfiguration _configuration;
        private DataTable _dataTable = new DataTable();
        private List<string> _servers;

        public StatisticsStrategyConfigurationForm(ShadowsocksController controller)
        {
            if (controller == null) return;
            InitializeComponent();
            _controller = controller;
            _controller.ConfigChanged += _controller_ConfigChanged;
            LoadConfiguration();
            Load += (sender, args) => InitData();
        }

        private void _controller_ConfigChanged(object sender, EventArgs e)
        {
            LoadConfiguration();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _controller.ConfigChanged -= _controller_ConfigChanged;
            base.OnClosing(e);
        }

        private void LoadConfiguration()
        {
            var configs = _controller.GetCurrentConfiguration().configs;
            _servers = configs.Select(server => server.FriendlyName()).ToList();
            _configuration = _controller.StatisticsConfiguration
                             ?? new StatisticsStrategyConfiguration();
            if (_configuration.Calculations == null)
            {
                _configuration = new StatisticsStrategyConfiguration();
            }
        }

        private void InitData()
        {
            bindingConfiguration.Add(_configuration);
            foreach (var kv in _configuration.Calculations)
            {
                var calculation = new CalculationControl(kv.Key, kv.Value);
                calculationContainer.Controls.Add(calculation);
            }

            _dataTable.Columns.Add("Timestamp", typeof (DateTime));
            _dataTable.Columns.Add("Package Loss", typeof (int));
            _dataTable.Columns.Add("Ping", typeof (int));

            serverSelector.DataSource = _servers;

            StatisticsChart.Series["Package Loss"].XValueMember = "Timestamp";
            StatisticsChart.Series["Package Loss"].YValueMembers = "Package Loss";
            StatisticsChart.Series["Ping"].XValueMember = "Timestamp";
            StatisticsChart.Series["Ping"].YValueMembers = "Ping";
            StatisticsChart.DataSource = _dataTable;
            loadChartData();
            StatisticsChart.DataBind();
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void OKButton_Click(object sender, EventArgs e)
        {
            foreach (CalculationControl calculation in calculationContainer.Controls)
            {
                _configuration.Calculations[calculation.Value] = calculation.Factor;
            }
            _controller?.SaveStrategyConfigurations(_configuration);
            _controller?.UpdateStatisticsConfiguration(StatisticsEnabledCheckBox.Checked);
            Close();
        }

        private void loadChartData()
        {
            string serverName = _servers[serverSelector.SelectedIndex];
            _dataTable.Rows.Clear();
            Dictionary<int, AvailabilityStatistics.StatisticsData> dataGroups
                = new Dictionary<int, AvailabilityStatistics.StatisticsData>();

            try
            {
                var path = AvailabilityStatistics.AvailabilityStatisticsFile;
                Logging.Debug($"loading statistics from {path}");
                if (!File.Exists(path))
                    return;
                using (TextFieldParser parser = new TextFieldParser(path))
                {
                    parser.SetDelimiters(new string[] { "," });
                    parser.HasFieldsEnclosedInQuotes = true;
                    string[] colFields = parser.ReadFields(); // skip first line
                    while (!parser.EndOfData)
                    {
                        string[] fields = parser.ReadFields();
                        int roundtripTime;
                        int.TryParse(fields[3], out roundtripTime);
                        string geolocation = fields.Length > 4 ? fields[4] : null;
                        string isp = fields.Length > 5 ? fields[5] : null;

                        AvailabilityStatistics.PingReply reply = new AvailabilityStatistics.PingReply()
                        {
                            timestamp = AvailabilityStatistics.ParseExactOrUnknown(fields[0]),
                            serverName = fields[1],
                            status = fields[2],
                            roundtripTime = roundtripTime,
                            geolocation = geolocation,
                            isp = isp
                        };

                        int key;
                        if (allMode.Checked)
                            key = reply.timestamp.DayOfYear;
                        else
                            key = reply.timestamp.Hour;

                        AvailabilityStatistics.StatisticsData st;
                        if (dataGroups.ContainsKey(key))
                            st = dataGroups[key];
                        else
                        {
                            st = new AvailabilityStatistics.StatisticsData()
                            {
                                timestamp = reply.timestamp
                            };
                            dataGroups.Add(key, st);
                        }
                        st.packageTotal++;
                        if (reply.status == IPStatus.Success.ToString())
                        {
                            st.roundtripTimeTotal += reply.roundtripTime;
                        }
                        else
                        {
                            st.packageLoss++;
                            if (reply.status == IPStatus.TimedOut.ToString())
                                st.packageTimeout++;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                return;
            }

            if (allMode.Checked)
            {
                StatisticsChart.ChartAreas["DataArea"].AxisX.LabelStyle.Format = "MM/dd/yyyy";
                StatisticsChart.ChartAreas["DataArea"].AxisX2.LabelStyle.Format = "MM/dd/yyyy";
            }
            else
            {
                StatisticsChart.ChartAreas["DataArea"].AxisX.LabelStyle.Format = "HH:00";
                StatisticsChart.ChartAreas["DataArea"].AxisX2.LabelStyle.Format = "HH:00";
            }
            foreach (AvailabilityStatistics.StatisticsData data in dataGroups.Values)
            {
                _dataTable.Rows.Add(data.timestamp, 
                    data.packageLoss * 100 / data.packageTotal, 
                    data.roundtripTimeTotal / (data.packageTotal - data.packageLoss));
            }
            StatisticsChart.DataBind();
        }

        private void serverSelector_SelectedIndexChanged(object sender, EventArgs e)
        {
            loadChartData();
        }

        private void chartModeSelector_Enter(object sender, EventArgs e)
        {

        }

        private void dayMode_CheckedChanged(object sender, EventArgs e)
        {
            loadChartData();
        }

        private void allMode_CheckedChanged(object sender, EventArgs e)
        {
            loadChartData();
        }

    }
}
