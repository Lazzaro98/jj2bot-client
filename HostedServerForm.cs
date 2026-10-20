using System;
using System.Drawing;
using System.Windows.Forms;

namespace Jj2
{
    sealed class HostedServerForm : Form
    {
        readonly Jj2StandaloneServer server;
        readonly RichTextBox output;
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
            ClientSize = new Size(800, 470);
            MinimumSize = new Size(760, 340);
            status = new Label { Text = "Starting standalone TCP/UDP server...", AutoSize = true,
                Location = new Point(16, 16) };
            output = new RichTextBox { ReadOnly = true, BackColor = Jj2Theme.Surface,
                ForeColor = Jj2Theme.TextPrimary, BorderStyle = BorderStyle.None,
                Location = new Point(16, 46), Size = new Size(768, 300),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            join = new Jj2Button { Text = "Join server", Style = Jj2Button.ButtonStyle.Accent,
                Location = new Point(16, 424), Size = new Size(130, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            stop = new Jj2Button { Text = "Stop server", Style = Jj2Button.ButtonStyle.Danger,
                Location = new Point(156, 424), Size = new Size(130, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            join.Click += delegate { connect(); };
            stop.Click += delegate { server.Stop(); UpdateStatus(); };
            Controls.Add(status); Controls.Add(output); Controls.Add(join); Controls.Add(stop);
            Label prompt = new Label { Text = "Server announcement or /help:", AutoSize = true,
                Location = new Point(16, 354), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            adminInput = new TextBox { Name = "adminInput", MaxLength = 240,
                Location = new Point(16, 376), Size = new Size(658, 24),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            send = new Jj2Button { Text = "Send", Location = new Point(684, 372), Size = new Size(100, 30),
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
            output.AppendText(DateTime.Now.ToString("HH:mm:ss") + " " + text + Environment.NewLine);
        }

        void UpdateStatus()
        {
            status.Text = server.IsRunning ? "Port " + server.Port + " | Players: " + server.PlayerCount + " | " + server.ModeStatus
                : "Server stopped. Close this window to host again.";
            join.Enabled = stop.Enabled = server.IsRunning;
            adminInput.Enabled = send.Enabled = startRound.Enabled = resetRound.Enabled = server.IsRunning;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); server.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
