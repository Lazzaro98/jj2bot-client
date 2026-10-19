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

        public HostedServerForm(Jj2StandaloneServer server, Action connect)
        {
            this.server = server;
            Text = "Standalone JJ2 server";
            BackColor = Jj2Theme.Background;
            ForeColor = Jj2Theme.TextPrimary;
            Font = Jj2Theme.FontBody;
            ClientSize = new Size(720, 420);
            MinimumSize = new Size(580, 320);
            status = new Label { Text = "Starting standalone TCP/UDP server...", AutoSize = true,
                Location = new Point(16, 16) };
            output = new RichTextBox { ReadOnly = true, BackColor = Jj2Theme.Surface,
                ForeColor = Jj2Theme.TextPrimary, BorderStyle = BorderStyle.None,
                Location = new Point(16, 46), Size = new Size(688, 310),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            join = new Jj2Button { Text = "Join server", Style = Jj2Button.ButtonStyle.Accent,
                Location = new Point(16, 374), Size = new Size(130, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            stop = new Jj2Button { Text = "Stop server", Style = Jj2Button.ButtonStyle.Danger,
                Location = new Point(156, 374), Size = new Size(130, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            join.Click += delegate { connect(); };
            stop.Click += delegate { server.Stop(); UpdateStatus(); };
            Controls.Add(status); Controls.Add(output); Controls.Add(join); Controls.Add(stop);
            timer = new Timer { Interval = 500 };
            timer.Tick += delegate { UpdateStatus(); };
            Shown += delegate { UpdateStatus(); timer.Start(); };
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
            status.Text = server.IsRunning ? "Standalone server: port " + server.Port + " | Players: " + server.PlayerCount
                : "Server stopped. Close this window to host again.";
            join.Enabled = stop.Enabled = server.IsRunning;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); server.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
