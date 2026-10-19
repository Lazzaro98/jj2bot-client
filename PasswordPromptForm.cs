using System;
using System.Drawing;
using System.Windows.Forms;

namespace Jj2
{
    /// <summary>
    /// Small themed modal that collects a server password before connecting to a private server.
    /// </summary>
    class PasswordPromptForm : Form
    {
        private readonly Jj2TextBox passwordBox;

        public string Password { get { return passwordBox.Inner.Text; } }

        public PasswordPromptForm(string serverName)
        {
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Text = "Private server";
            ClientSize = new Size(360, 170);
            BackColor = Jj2Theme.Background;
            Font = Jj2Theme.FontBody;

            Label icon = new Label();
            icon.Text = "\U0001F512";
            icon.Font = new Font("Segoe UI Emoji", 20f);
            icon.ForeColor = Jj2Theme.Accent;
            icon.AutoSize = true;
            icon.Location = new Point(18, 18);
            icon.BackColor = Color.Transparent;

            Label title = new Label();
            title.Text = "This server requires a password";
            title.Font = Jj2Theme.FontHeading;
            title.ForeColor = Jj2Theme.TextPrimary;
            title.AutoSize = true;
            title.Location = new Point(60, 18);
            title.BackColor = Color.Transparent;

            Label subtitle = new Label();
            subtitle.Text = Jj2Theme.StripColorMarkers(serverName);
            subtitle.Font = Jj2Theme.FontSmall;
            subtitle.ForeColor = Jj2Theme.TextSecondary;
            subtitle.AutoSize = true;
            subtitle.Location = new Point(60, 40);
            subtitle.BackColor = Color.Transparent;
            subtitle.MaximumSize = new Size(ClientSize.Width - 76, 0);

            passwordBox = new Jj2TextBox();
            passwordBox.Location = new Point(18, 78);
            passwordBox.Size = new Size(ClientSize.Width - 36, 30);
            passwordBox.Inner.UseSystemPasswordChar = true;
            passwordBox.Inner.MaxLength = 64;

            Jj2Button cancelButton = new Jj2Button();
            cancelButton.Style = Jj2Button.ButtonStyle.Outline;
            cancelButton.Text = "Cancel";
            cancelButton.Size = new Size(110, 32);
            cancelButton.Location = new Point(ClientSize.Width - 18 - 110 - 8 - 110, 122);
            cancelButton.DialogResult = DialogResult.Cancel;

            Jj2Button connectButton = new Jj2Button();
            connectButton.Style = Jj2Button.ButtonStyle.Accent;
            connectButton.Text = "Connect";
            connectButton.Size = new Size(110, 32);
            connectButton.Location = new Point(ClientSize.Width - 18 - 110, 122);
            connectButton.DialogResult = DialogResult.OK;

            Controls.Add(icon);
            Controls.Add(title);
            Controls.Add(subtitle);
            Controls.Add(passwordBox);
            Controls.Add(cancelButton);
            Controls.Add(connectButton);

            AcceptButton = connectButton;
            CancelButton = cancelButton;
            Shown += delegate { passwordBox.Inner.Focus(); };
        }
    }
}
