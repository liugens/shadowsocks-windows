namespace Shadowsocks.View
{
    partial class TestServerSpeedForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(TestServerSpeedForm));
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
            this.ButtonsPanel = new System.Windows.Forms.Panel();
            this.ButtonsGroupPanel = new System.Windows.Forms.Panel();
            this.TestServersButton = new System.Windows.Forms.Button();
            this.StatusIcons = new System.Windows.Forms.ImageList(this.components);
            this.ServersDataGridView = new System.Windows.Forms.DataGridView();
            this.SelectColumn = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.ServerNameColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ConnectionTimeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.SpeedColumn = new Shadowsocks.View.DataGridViewProgressColumn();
            this.ButtonsPanel.SuspendLayout();
            this.ButtonsGroupPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ServersDataGridView)).BeginInit();
            this.SuspendLayout();
            // 
            // ButtonsPanel
            // 
            this.ButtonsPanel.Controls.Add(this.ButtonsGroupPanel);
            this.ButtonsPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPanel.Location = new System.Drawing.Point(0, 397);
            this.ButtonsPanel.Name = "ButtonsPanel";
            this.ButtonsPanel.Size = new System.Drawing.Size(488, 34);
            this.ButtonsPanel.TabIndex = 0;
            // 
            // ButtonsGroupPanel
            // 
            this.ButtonsGroupPanel.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.ButtonsGroupPanel.Controls.Add(this.TestServersButton);
            this.ButtonsGroupPanel.Location = new System.Drawing.Point(138, 2);
            this.ButtonsGroupPanel.Name = "ButtonsGroupPanel";
            this.ButtonsGroupPanel.Size = new System.Drawing.Size(213, 31);
            this.ButtonsGroupPanel.TabIndex = 2;
            // 
            // TestServersButton
            // 
            this.TestServersButton.Location = new System.Drawing.Point(3, 3);
            this.TestServersButton.Name = "TestServersButton";
            this.TestServersButton.Size = new System.Drawing.Size(207, 25);
            this.TestServersButton.TabIndex = 0;
            this.TestServersButton.Text = "Test Servers...";
            this.TestServersButton.UseVisualStyleBackColor = true;
            this.TestServersButton.Click += new System.EventHandler(this.TestServersButton_Click);
            // 
            // StatusIcons
            // 
            this.StatusIcons.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("StatusIcons.ImageStream")));
            this.StatusIcons.TransparentColor = System.Drawing.Color.Transparent;
            this.StatusIcons.Images.SetKeyName(0, "ssw128.png");
            this.StatusIcons.Images.SetKeyName(1, "ss_gray128.png");
            // 
            // ServersDataGridView
            // 
            this.ServersDataGridView.AllowUserToAddRows = false;
            this.ServersDataGridView.AllowUserToDeleteRows = false;
            dataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            dataGridViewCellStyle1.BackColor = System.Drawing.SystemColors.Control;
            dataGridViewCellStyle1.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            dataGridViewCellStyle1.ForeColor = System.Drawing.SystemColors.WindowText;
            dataGridViewCellStyle1.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle1.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle1.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            this.ServersDataGridView.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle1;
            this.ServersDataGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.ServersDataGridView.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.SelectColumn,
            this.ServerNameColumn,
            this.ConnectionTimeColumn,
            this.SpeedColumn});
            dataGridViewCellStyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            dataGridViewCellStyle2.BackColor = System.Drawing.SystemColors.Window;
            dataGridViewCellStyle2.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            dataGridViewCellStyle2.ForeColor = System.Drawing.SystemColors.ControlText;
            dataGridViewCellStyle2.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle2.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle2.WrapMode = System.Windows.Forms.DataGridViewTriState.False;
            this.ServersDataGridView.DefaultCellStyle = dataGridViewCellStyle2;
            this.ServersDataGridView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ServersDataGridView.Location = new System.Drawing.Point(0, 0);
            this.ServersDataGridView.Name = "ServersDataGridView";
            this.ServersDataGridView.ReadOnly = true;
            this.ServersDataGridView.RowHeadersVisible = false;
            this.ServersDataGridView.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.ServersDataGridView.Size = new System.Drawing.Size(488, 397);
            this.ServersDataGridView.TabIndex = 1;
            // 
            // SelectColumn
            // 
            this.SelectColumn.Frozen = true;
            this.SelectColumn.HeaderText = "";
            this.SelectColumn.Name = "SelectColumn";
            this.SelectColumn.ReadOnly = true;
            this.SelectColumn.Width = 30;
            // 
            // ServerNameColumn
            // 
            this.ServerNameColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.ServerNameColumn.FillWeight = 35F;
            this.ServerNameColumn.HeaderText = "Server Name";
            this.ServerNameColumn.Name = "ServerNameColumn";
            this.ServerNameColumn.ReadOnly = true;
            // 
            // ConnectionTimeColumn
            // 
            this.ConnectionTimeColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.ConnectionTimeColumn.FillWeight = 25F;
            this.ConnectionTimeColumn.HeaderText = "Connection Time";
            this.ConnectionTimeColumn.Name = "ConnectionTimeColumn";
            this.ConnectionTimeColumn.ReadOnly = true;
            // 
            // SpeedColumn
            // 
            this.SpeedColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.SpeedColumn.FillWeight = 25F;
            this.SpeedColumn.HeaderText = "Speed";
            this.SpeedColumn.Name = "SpeedColumn";
            this.SpeedColumn.ReadOnly = true;
            this.SpeedColumn.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.SpeedColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Automatic;
            // 
            // TestServerSpeedForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(488, 431);
            this.Controls.Add(this.ServersDataGridView);
            this.Controls.Add(this.ButtonsPanel);
            this.Name = "TestServerSpeedForm";
            this.Text = "Test Server Speed...";
            this.Load += new System.EventHandler(this.ServerTesterForm_Load);
            this.ButtonsPanel.ResumeLayout(false);
            this.ButtonsGroupPanel.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.ServersDataGridView)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel ButtonsPanel;
        private System.Windows.Forms.ImageList StatusIcons;
        private System.Windows.Forms.Button TestServersButton;
        private System.Windows.Forms.Panel ButtonsGroupPanel;
        private System.Windows.Forms.DataGridView ServersDataGridView;
        private System.Windows.Forms.DataGridViewCheckBoxColumn SelectColumn;
        private DataGridViewProgressColumn SpeedColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn ConnectionTimeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn ServerNameColumn;
    }
}