using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using Shadowsocks.Controller;
using Shadowsocks.Controller.Service;
using Shadowsocks.Model;
using Shadowsocks.Properties;

namespace Shadowsocks.View
{
    public partial class TestServerSpeedForm : Form
    {
        delegate void VoidMethod();

        private ShadowsocksController controller;
        private Configuration _modifiedConfiguration;
        private Queue<TesterState> queue;

        public TestServerSpeedForm(ShadowsocksController controller)
        {
            this.Font = System.Drawing.SystemFonts.MessageBoxFont;
            InitializeComponent();
            UpdateTexts();
            ServersDataGridView.CellClick += ServersDataGridView_CellClick;
            this.Icon = Icon.FromHandle(Resources.ssw128.GetHicon());
            this.controller = controller;
            LoadCurrentConfiguration();
            queue = new Queue<TesterState>();
        }

        private void UpdateTexts()
        {
            this.Text = I18N.GetString("Test Server Speed...");
            this.TestServersButton.Text = I18N.GetString("Test All");
        }

        private void LoadCurrentConfiguration()
        {
            _modifiedConfiguration = controller.GetConfigurationCopy();
            LoadServers(_modifiedConfiguration.configs);
        }

        private void LoadServers(List<Server> servers)
        {
            List<DataGridViewRow> rows = new List<DataGridViewRow>(servers.Count);
            int i = 0;
            foreach (Server server in servers)
            {
                DataGridViewRow row = new DataGridViewRow();
                row.Tag = new TesterState
                {
                    row = row,
                    server = server,
                    index = i++
                };
                row.Cells.Add(new DataGridViewTextBoxCell { Value = server.FriendlyName() });
                row.Cells.Add(new DataGridViewTextBoxCell { Value = "-" });
                row.Cells.Add(new DataGridViewProgressCell {
                    Value = "-",
                    ProgressEnable = false
                });
                row.Cells.Add(new DataGridViewDisableButtonCell
                {
                    Value = I18N.GetString("Test"),
                    Enabled = true
                });
                row.Cells[0].Style.Alignment = DataGridViewContentAlignment.MiddleLeft;
                rows.Add(row);
            }
            ServersDataGridView.Rows.Clear();
            ServersDataGridView.Rows.AddRange(rows.ToArray());
        }

        private void CancelTestServers()
        {
            foreach(DataGridViewRow row in ServersDataGridView.Rows)
            {
                TesterState state = (TesterState)row.Tag;
                if (state.tester != null)
                {
                    state.tester.Close();
                    state.tester = null;
                    state.row.Cells[1].Value = I18N.GetString("Cancelled");
                    state.row.Cells[2].Value = I18N.GetString("Cancelled");
                    ((DataGridViewProgressCell)state.row.Cells[2]).ProgressEnable = false;
                    state.row.Cells[3].Value = I18N.GetString("Retest");
                    ((DataGridViewDisableButtonCell)state.row.Cells[3]).Enabled = true;
                    ServersDataGridView.InvalidateCell(state.row.Cells[3]);
                    Logging.Debug($"cancel test {state.server.FriendlyName()}");
                }
            }
        }

        private void ServerTesterForm_Load(object sender, EventArgs e)
        {

        }

        protected override void OnClosing(CancelEventArgs e)
        {
            CancelTestServers();
            base.OnClosing(e);
        }

        private void TestServersButton_Click(object sender, EventArgs e)
        {
            if (ServersDataGridView.Rows.Count == 0) return;
            if (TestServersButton.Tag == null)
            {
                DataGridViewRow row = ServersDataGridView.Rows[0];
                StartTest(row);
                TestServersButton.Text = I18N.GetString("Cancel All");
                TestServersButton.Tag = true;
            }
            else
            {
                this.TestServersButton.Text = I18N.GetString("Test Servers");
                TestServersButton.Tag = null;
                CancelTestServers();
            }
        }

        private void ServersDataGridView_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex == 3)
            {
                DataGridViewRow row = ServersDataGridView.Rows[e.RowIndex];
                if (((DataGridViewDisableButtonCell)row.Cells[3]).Enabled)
                {
                    StartTest(row);
                }
            }
        }

        private void StartTest(DataGridViewRow row)
        {
            TesterState state = (TesterState)row.Tag;
            if (state.tester != null)
            {
                return;
            }
            Logging.Debug($"Start test {state.server.FriendlyName()}");
            state.tester = new ServerTester(state.server, state);
            state.tester.Completed += Tester_Completed;
            state.tester.Progress += Tester_Progress;
            state.row.Cells[1].Value = "-";
            state.row.Cells[2].Value = "-";
            state.row.Cells[3].Value = I18N.GetString("Testing...");
            ((DataGridViewProgressCell)state.row.Cells[2]).ProgressEnable = true;
            ((DataGridViewDisableButtonCell)state.row.Cells[3]).Enabled = false;

            state.tester.Start();
        }

        private void Tester_Progress(object sender, ServerTesterProgressEventArgs e)
        {
            ServerTester tester = (ServerTester)sender;
            TesterState state = (TesterState)tester.userState;
            state.row.Cells[1].Value = e.ConnectionTime.ToString() + "ms";
            long total = (e.Total == 0 || tester.DownloadLength < e.Total) ? tester.DownloadLength : e.Total;
            if (total > 0)
            {
                int percentage = (int)(e.Download * 100 / total);
                state.row.Cells[2].Value = percentage.ToString();
            }
        }

        private void Tester_Completed(object sender, ServerTesterEventArgs e)
        {
            ServerTester tester = (ServerTester)sender;
            TesterState state = (TesterState)tester.userState;
            state.tester = null;
            if (e.Error == null)
            {
                Logging.Debug($"{state.server.FriendlyName()}: ConnectionTime={e.ConnectionTime}ms, Speed={e.DownloadSpeed}B/s");

                state.connectionTime = e.ConnectionTime;
                state.speed = e.DownloadSpeed;

                state.row.Cells[1].Value = e.ConnectionTime.ToString() + "ms";
                state.row.Cells[2].Value = GetSizeShort(e.DownloadSpeed) + "/s";
            }
            else
            {
                Logging.Debug($"failed test {state.server.FriendlyName()}: {e.Error}");
                if (e.Error is ServerTesterTimeoutException)
                {
                    state.row.Cells[1].Value = I18N.GetString("Timeout");
                    state.row.Cells[2].Value = I18N.GetString("Timeout");
                }
                else if (e.Error is ServerTesterCancelException)
                {
                    state.row.Cells[1].Value = I18N.GetString("Cancelled");
                    state.row.Cells[2].Value = I18N.GetString("Cancelled");
                }
                else
                {
                    state.row.Cells[1].Value = e.Error.Message;
                    state.row.Cells[2].Value = e.Error.Message;
                }
            }

            state.row.Cells[3].Value = I18N.GetString("Retest");
            ((DataGridViewProgressCell)state.row.Cells[2]).ProgressEnable = false;
            ((DataGridViewDisableButtonCell)state.row.Cells[3]).Enabled = true;
            ServersDataGridView.InvalidateCell(state.row.Cells[3]);

            if (state.row.Index < ServersDataGridView.Rows.Count - 1)
            {
                DataGridViewRow row = ServersDataGridView.Rows[state.row.Index + 1];
                StartTest(row);
            }
            else
            {
                if (this.TestServersButton.InvokeRequired)
                {
                    this.TestServersButton.Invoke(new VoidMethod(delegate ()
                    {
                        this.TestServersButton.Text = I18N.GetString("Test Servers");
                    }));
                }
                TestServersButton.Tag = null;
                //TODO: sort datagridview
            }
        }

        private static readonly string[] Units = { null, "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB", "BB", "NB", "DB", "CB" };

        public static string GetSize(long size, string bytes = "Bytes")
        {
            double n = size;
            byte i = 0;
            while (n > 1000)
            {
                n /= 1024;
                i++;
            }
            return i == 0 ? size.ToString("N0") + ' ' + bytes
                          : n.ToString("N") + ' ' + Units[i] + " (" + size.ToString("N0") + ' ' + bytes + ')';
        }

        public static string GetSize(double size, string bytes = "Bytes")
        {
            var n = size;
            byte i = 0;
            while (n > 1000)
            {
                n /= 1024;
                i++;
            }
            return n.ToString("N") + ' ' + (i == 0 ? bytes : Units[i] + " (" + size.ToString("N") + ' ' + bytes + ')');
        }

        public static string GetSizeShort(long size, string bytes = "Bytes")
        {
            var n = size;
            byte i = 0;
            while (n > 1000)
            {
                n /= 1024;
                i++;
            }
            return n + (i == 0 ? ' ' + bytes : Units[i].Substring(0, 1));
        }

        class TesterState
        {
            public DataGridViewRow row;
            public Server server;
            public int index;
            public long connectionTime;
            public long speed;
            public ServerTester tester;
        }
    }
}
