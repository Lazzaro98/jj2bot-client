using System;
using System.Drawing;
using System.Windows.Forms;

namespace Jj2
{
    sealed class HostedServerForm : Form
    {
        readonly Jj2StandaloneServer server;
        readonly RichTextBox output;
        readonly ListView players;
        readonly Label status;
        readonly Jj2Button join, stop;
        readonly Timer timer;
        readonly TextBox adminInput;
        readonly Jj2Button send, startRound, resetRound;

        public HostedServerForm(Jj2StandaloneServer server, Action connect)
        {
            this.server = server;
            Text = "Standalone JJ2 server";
            BackColor = Jj2Theme.Background;
            ForeColor = Jj2Theme.TextPrimary;
            Font = Jj2Theme.FontBody;
            ClientSize = new Size(1100, 470);
            MinimumSize = new Size(960, 420);
            status = new Label { Text = "Starting standalone TCP/UDP server...", AutoSize = true,
                Location = new Point(16, 16) };
            output = new RichTextBox { ReadOnly = true, BackColor = Jj2Theme.Surface,
                ForeColor = Jj2Theme.TextPrimary, BorderStyle = BorderStyle.None,
                Name = "serverLog", Dock = DockStyle.Fill };
            players = new ListView { Name = "hostedPlayers", View = View.Details, FullRowSelect = true,
                HideSelection = false, MultiSelect = false, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
                BackColor = Jj2Theme.Surface, ForeColor = Jj2Theme.TextPrimary };
            players.Columns.Add("#", 32);
            players.Columns.Add("Player", 130);
            players.Columns.Add("State", 190);
            players.Columns.Add("Wins", 45);
            SplitContainer content = new SplitContainer { Location = new Point(16, 46), Size = new Size(1068, 300),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Jj2Theme.Background, SplitterDistance = 654, Panel1MinSize = 300, Panel2MinSize = 280 };
            content.Panel1.Controls.Add(output);
            content.Panel2.Controls.Add(players);
            join = new Jj2Button { Text = "Join server", Style = Jj2Button.ButtonStyle.Accent,
                Location = new Point(16, 424), Size = new Size(130, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            stop = new Jj2Button { Text = "Stop server", Style = Jj2Button.ButtonStyle.Danger,
                Location = new Point(156, 424), Size = new Size(130, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            join.Click += delegate { connect(); };
            stop.Click += delegate { server.Stop(); UpdateStatus(); };
            Controls.Add(status); Controls.Add(content); Controls.Add(join); Controls.Add(stop);
            Label prompt = new Label { Text = "Server announcement or /help:", AutoSize = true,
                Location = new Point(16, 354), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            adminInput = new TextBox { Name = "adminInput", MaxLength = 240,
                Location = new Point(16, 376), Size = new Size(958, 24),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            send = new Jj2Button { Text = "Send", Location = new Point(984, 372), Size = new Size(100, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            startRound = new Jj2Button { Text = "Start round", Location = new Point(306, 424), Size = new Size(130, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left, Visible = server.IsHotPotato };
            resetRound = new Jj2Button { Text = "Reset round", Location = new Point(446, 424), Size = new Size(130, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left, Visible = server.IsHotPotato };
            send.Click += delegate { SubmitAdmin(); };
            adminInput.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SubmitAdmin(); }
            };
            startRound.Click += delegate { server.ExecuteAdmin("/start"); UpdateStatus(); };
            resetRound.Click += delegate { server.ExecuteAdmin("/reset"); UpdateStatus(); };
            Controls.AddRange(new Control[] { prompt, adminInput, send, startRound, resetRound });
            timer = new Timer { Interval = 500 };
            timer.Tick += delegate { UpdateStatus(); };
            Shown += delegate { UpdateStatus(); timer.Start(); };
        }

        void SubmitAdmin()
        {
            server.ExecuteAdmin(adminInput.Text);
            adminInput.Clear();
            UpdateStatus();
        }

        public void AppendLog(string text)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((MethodInvoker)delegate { AppendLog(text); }); }
                catch (InvalidOperationException) { }
                return;
            }
            if (output.TextLength > 100000) output.Clear();
            output.SelectionStart = output.TextLength;
            output.SelectionColor = text.StartsWith("[Winner]") ? Color.Gold :
                text.StartsWith("[Round]") || text.StartsWith("[Timer]") || text.StartsWith("Hot Potato:") ? Color.LightGreen :
                text.StartsWith("[Respawn]") || text.StartsWith("[Spectate]") ? Color.LightSkyBlue :
                text.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("unsupported", StringComparison.OrdinalIgnoreCase) >= 0 ? Color.Salmon : Jj2Theme.TextPrimary;
            output.AppendText(DateTime.Now.ToString("HH:mm:ss") + " " + text + Environment.NewLine);
            output.ScrollToCaret();
        }

        void UpdateStatus()
        {
            status.Text = server.IsRunning ? "Port " + server.Port + " | Players: " + server.PlayerCount + " | " + server.ModeStatus
                : "Server stopped. Close this window to host again.";
            join.Enabled = stop.Enabled = server.IsRunning;
            adminInput.Enabled = send.Enabled = startRound.Enabled = resetRound.Enabled = server.IsRunning;
            startRound.Enabled = server.IsRunning && !server.ModeStatus.StartsWith("Hot Potato:");
            HostedPlayerInfo[] snapshot = server.GetPlayers();
            players.BeginUpdate();
            try
            {
                for (int i = players.Items.Count - 1; i >= 0; i--)
                {
                    bool connected = false;
                    foreach (HostedPlayerInfo player in snapshot)
                        if (players.Items[i].Name == player.Id.ToString()) { connected = true; break; }
                    if (!connected) players.Items.RemoveAt(i);
                }
                foreach (HostedPlayerInfo player in snapshot)
                {
                    string key = player.Id.ToString();
                    ListViewItem item = players.Items[key];
                    if (item == null)
                    {
                        item = new ListViewItem(new[] { key, player.Name, player.State, player.Wins.ToString() }) { Name = key };
                        players.Items.Add(item);
                    }
                    item.SubItems[1].Text = player.Name;
                    item.SubItems[2].Text = player.State;
                    item.SubItems[3].Text = player.Wins.ToString();
                    item.ForeColor = player.State == "HOLDING POTATO" ? Color.Gold :
                        player.State.StartsWith("Eliminated") || player.State.StartsWith("Waiting") ? Color.Gray :
                        player.State == "Spectating" ? Color.LightSkyBlue : Jj2Theme.TextPrimary;
                }
            }
            finally { players.EndUpdate(); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); server.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
