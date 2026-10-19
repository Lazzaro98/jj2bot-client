using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace Jj2
{
    public partial class Form1 : Form
    {
        /// <summary>
        /// Initializes the server browser form.
        /// </summary>
        public Form1()
        {
            InitializeComponent();
            ApplyTheme();
            try
            {
                gameExecutable = Properties.Settings.Default.GameExecutable;
                if (File.Exists(gameExecutable))
                    jj2udpClient.GameDirectory = Path.GetDirectoryName(gameExecutable);
            }
            catch (ConfigurationErrorsException ex)
            {
                MessageBox.Show(this, "Unable to load the saved game path: " + ex.Message,
                    "Game settings error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            FormClosed += delegate
            {
                if (hostTimer != null) hostTimer.Stop();
                if (hostProcess != null) hostProcess.Dispose();
            };
        }

        /// <summary>
        /// Applies the shared dark/gold premium theme to every control on this window.
        /// </summary>
        private void ApplyTheme()
        {
            BackColor = Jj2Theme.Background;
            Font = Jj2Theme.FontBody;

            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 56;
            header.BackColor = Jj2Theme.Surface;
            Label title = new Label();
            title.Text = "JJ2 BOT CLIENT";
            title.Font = Jj2Theme.FontTitle;
            title.ForeColor = Jj2Theme.TextPrimary;
            title.AutoSize = true;
            title.Location = new Point(16, 10);
            Label subtitle = new Label();
            subtitle.Text = "Server browser";
            subtitle.Font = Jj2Theme.FontSmall;
            subtitle.ForeColor = Jj2Theme.TextSecondary;
            subtitle.AutoSize = true;
            subtitle.Location = new Point(18, 33);
            Panel accentLine = new Panel();
            accentLine.BackColor = Jj2Theme.Accent;
            accentLine.Dock = DockStyle.Bottom;
            accentLine.Height = 2;
            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            header.Controls.Add(accentLine);
            Controls.Add(header);
            header.BringToFront();

            refreshServersButton.Parent.Controls.Remove(refreshServersButton);
            connectSelectedServerButton.Parent.Controls.Remove(connectSelectedServerButton);
            Jj2Button refreshButton = ReplaceWithThemedButton(refreshServersButton, Jj2Button.ButtonStyle.Outline);
            refreshButton.Click += refreshServersButton_Click;
            Jj2Button connectButton = ReplaceWithThemedButton(connectSelectedServerButton, Jj2Button.ButtonStyle.Accent);
            connectButton.Click += connectSelectedServerButton_Click;

            serverStatusLabel.ForeColor = Jj2Theme.TextSecondary;
            serverStatusLabel.Font = Jj2Theme.FontSmall;
            serverStatusLabel.BackColor = Color.Transparent;
            playerNameLabel.ForeColor = Jj2Theme.TextSecondary;
            playerNameLabel.Font = Jj2Theme.FontSmall;
            playerNameLabel.BackColor = Color.Transparent;

            string existingName = playerNameTextBox.Text;
            Jj2TextBox nameBox = new Jj2TextBox();
            nameBox.Location = new Point(playerNameTextBox.Location.X, playerNameTextBox.Location.Y - 3);
            nameBox.Size = new Size(playerNameTextBox.Width, 26);
            nameBox.Inner.MaxLength = 32;
            nameBox.Inner.Text = existingName;
            Controls.Remove(playerNameTextBox);
            Controls.Add(nameBox);
            playerNameTextBox = nameBox.Inner;

            serverListView.BackColor = Jj2Theme.Surface;
            serverListView.ForeColor = Jj2Theme.TextPrimary;
            serverListView.BorderStyle = BorderStyle.None;
            serverListBackgroundColor = Jj2Theme.Surface;
            serverListSelectionColor = Jj2Theme.SelectionHighlight;

            // ListView has no direct row-height property in Details view; the standard trick is
            // attaching an (otherwise unused) ImageList whose ImageSize forces taller rows, giving
            // the Game Type pill badge + padlock icon enough vertical room to render properly.
            rowHeightImageList = new ImageList();
            rowHeightImageList.ImageSize = new Size(1, 26);
            rowHeightImageList.ColorDepth = ColorDepth.Depth32Bit;
            serverListView.SmallImageList = rowHeightImageList;

            Label characterLabel = new Label();
            characterLabel.Text = "Character";
            characterLabel.ForeColor = Jj2Theme.TextSecondary;
            characterLabel.Font = Jj2Theme.FontSmall;
            characterLabel.BackColor = Color.Transparent;
            characterLabel.AutoSize = true;
            characterPicker = new CharacterPicker();
            Controls.Add(characterLabel);
            Controls.Add(characterPicker);

            ClientSize = new Size(860, 550);
            const int left = 16, right = 16, top = 72, bottom = 16;
            refreshButton.Location = new Point(left, top);
            connectButton.Location = new Point(refreshButton.Right + 8, top);
            serverStatusLabel.Location = new Point(connectButton.Right + 16, top + 6);

            nameBox.Size = new Size(220, 26);
            nameBox.Location = new Point(ClientSize.Width - right - nameBox.Width, top - 2);
            playerNameLabel.AutoSize = true;
            playerNameLabel.Location = new Point(nameBox.Left - playerNameLabel.Width - 8, top + 6);

            int characterRowTop = top + 38;
            characterPicker.Location = new Point(left + characterLabel.Width + 14, characterRowTop);
            characterLabel.Location = new Point(left, characterRowTop + (characterPicker.Height - characterLabel.Height) / 2);

            hostButton = new Jj2Button { Name = "hostServerButton", Text = "Host a server",
                Style = Jj2Button.ButtonStyle.Outline, Size = new Size(140, 30),
                Location = new Point(characterPicker.Right + 20, characterRowTop) };
            hostButton.Click += HostServer_Click;
            Controls.Add(hostButton);
            hostStatusLabel = new Label { AutoSize = false, ForeColor = Jj2Theme.TextSecondary,
                Font = Jj2Theme.FontSmall, Location = new Point(hostButton.Right + 12, characterRowTop + 6),
                Size = new Size(300, 34), Text = "Host using your installed JJ2/JJ2+." };
            Controls.Add(hostStatusLabel);

            serverListView.Location = new Point(left, characterRowTop + characterPicker.Height + 14);
            serverListView.Size = new Size(ClientSize.Width - left - right, ClientSize.Height - serverListView.Top - bottom);
            serverListView.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            playerNameLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            nameBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            serverStatusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        }

        private CharacterPicker characterPicker;
        private ImageList rowHeightImageList;
        private Jj2Button hostButton;
        private Label hostStatusLabel;
        private Process hostProcess;
        private Timer hostTimer;
        private DateTime hostStartedAt;
        private int hostPort;
        private bool hostReady;
        private bool hostTimeoutReported;
        private string gameExecutable;

        private void HostServer_Click(object sender, EventArgs e)
        {
            if (hostProcess != null)
            {
                if (!hostProcess.HasExited)
                {
                    if (hostReady)
                        ConnectToServer(new ListedServer { IpAddress = "127.0.0.1", Port = hostPort, Name = "Hosted server" });
                    return;
                }
                hostTimer.Stop();
                hostProcess.Dispose();
                hostProcess = null;
                hostButton.Text = "Host a server";
            }
            using (HostServerForm dialog = new HostServerForm(
                String.IsNullOrEmpty(gameExecutable) ? Path.Combine(jj2udpClient.GameDirectory, "Jazz2.exe") : gameExecutable))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    ProcessStartInfo startInfo = Jj2HostLauncher.CreateStartInfo(dialog.Options);
                    Jj2HostLauncher.EnsurePortAvailable(dialog.Options.Port);
                    hostProcess = Process.Start(startInfo);
                    if (hostProcess == null) throw new InvalidOperationException("JJ2 did not return a game process.");
                    gameExecutable = startInfo.FileName;
                    jj2udpClient.GameDirectory = startInfo.WorkingDirectory;
                    hostPort = dialog.Options.Port;
                    hostReady = false;
                    hostTimeoutReported = false;
                    hostStartedAt = DateTime.UtcNow;
                    hostButton.Enabled = false;
                    hostStatusLabel.Text = "Starting JJ2 server on port " + hostPort + "...";
                    if (hostTimer == null)
                    {
                        if (components == null) components = new Container();
                        hostTimer = new Timer(components) { Interval = 500 };
                        hostTimer.Tick += HostTimer_Tick;
                    }
                    hostTimer.Start();
                }
                catch (Win32Exception ex) { ShowHostError(ex.Message); return; }
                catch (ArgumentException ex) { ShowHostError(ex.Message); return; }
                catch (IOException ex) { ShowHostError(ex.Message); return; }
                catch (UnauthorizedAccessException ex) { ShowHostError(ex.Message); return; }
                catch (InvalidOperationException ex) { ShowHostError(ex.Message); return; }

                try
                {
                    Properties.Settings.Default.GameExecutable = gameExecutable;
                    Properties.Settings.Default.Save();
                }
                catch (ConfigurationErrorsException ex)
                {
                    MessageBox.Show(this, "JJ2 was launched, but its path could not be saved: " + ex.Message,
                        "Game settings error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void HostTimer_Tick(object sender, EventArgs e)
        {
            if (hostProcess.HasExited)
            {
                int exitCode = hostProcess.ExitCode;
                hostTimer.Stop();
                hostProcess.Dispose();
                hostProcess = null;
                hostButton.Enabled = true;
                hostButton.Text = "Host a server";
                hostStatusLabel.Text = "Hosted game closed (exit code " + exitCode + ").";
                if (!hostReady) ShowHostError("JJ2 closed before the server was ready. Check the selected game and level.");
                return;
            }
            try
            {
                if (Jj2HostLauncher.IsListening(hostPort))
                {
                    if (hostReady) return;
                    hostReady = true;
                    hostButton.Text = "Join hosted server";
                    hostButton.Enabled = true;
                    hostStatusLabel.Text = "Server listening on port " + hostPort + ".\nClose JJ2 to stop hosting.";
                    return;
                }
                if (hostReady)
                {
                    hostReady = false;
                    hostTimeoutReported = true;
                    hostButton.Enabled = false;
                    hostStatusLabel.Text = "Server stopped listening. Check JJ2.";
                }
            }
            catch (System.Net.NetworkInformation.NetworkInformationException ex)
            {
                if (hostTimeoutReported) return;
                hostTimeoutReported = true;
                hostReady = false;
                hostButton.Enabled = false;
                hostStatusLabel.Text = "Unable to check startup. Check JJ2.";
                ShowHostError("Unable to check server startup: " + ex.Message);
                return;
            }
            if (!hostTimeoutReported && (DateTime.UtcNow - hostStartedAt).TotalSeconds >= 30)
            {
                hostTimeoutReported = true;
                hostStatusLabel.Text = "Server startup not confirmed. Check JJ2.";
                ShowHostError("JJ2 is running but both server ports are not listening. Check the game window for errors. " +
                    "Close that JJ2 instance before trying again.");
            }
        }

        private void ShowHostError(string message)
        {
            MessageBox.Show(this, message, "Hosting error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        /// <summary>
        /// Swaps a stock Button for a themed Jj2Button with identical bounds, keeping the original's text.
        /// </summary>
        private Jj2Button ReplaceWithThemedButton(Button original, Jj2Button.ButtonStyle style)
        {
            Jj2Button themed = new Jj2Button();
            themed.Text = original.Text;
            themed.Location = original.Location;
            themed.Size = new Size(original.Width, 28);
            themed.Style = style;
            Controls.Add(themed);
            return themed;
        }


        BackgroundWorker serverListLoader;
        readonly Color[] jj2ChatColors =
        {
            Color.White,
            Color.Chartreuse,
            Color.Red,
            Color.Aqua,
            Color.Yellow,
            Color.DeepPink,
            Color.Gray
        };
        Color serverListBackgroundColor = Color.FromArgb(32, 32, 32);
        Color serverListSelectionColor = Color.FromArgb(54, 91, 136);

        /// <summary>
        /// Loads the public server list after the form becomes visible.
        /// </summary>
        private void Form1_Shown(object sender, EventArgs e)
        {
            LoadServerList();
        }

        /// <summary>
        /// Reloads the public server list on demand.
        /// </summary>
        private void refreshServersButton_Click(object sender, EventArgs e)
        {
            LoadServerList();
        }

        /// <summary>
        /// Opens a new connection window for the selected server.
        /// </summary>
        private void connectSelectedServerButton_Click(object sender, EventArgs e)
        {
            ConnectToSelectedServer();
        }

        /// <summary>
        /// Connects when a server row is double-clicked.
        /// </summary>
        private void serverListView_DoubleClick(object sender, EventArgs e)
        {
            ConnectToSelectedServer();
        }

        /// <summary>
        /// Draws the server-list header with clear column boundaries.
        /// </summary>
        private void serverListView_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (SolidBrush headerBrush = new SolidBrush(Jj2Theme.SurfaceAlt))
            {
                e.Graphics.FillRectangle(headerBrush, e.Bounds);
            }

            TextFormatFlags alignment = e.ColumnIndex >= 2 ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left;
            TextRenderer.DrawText(e.Graphics, e.Header.Text, Jj2Theme.FontHeading, e.Bounds, Jj2Theme.Accent,
                alignment | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            using (Pen pen = new Pen(Jj2Theme.Border))
            {
                e.Graphics.DrawRectangle(pen, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
            }
        }

        /// <summary>
        /// Lets DrawSubItem render each detail-view cell.
        /// </summary>
        private void serverListView_DrawItem(object sender, DrawListViewItemEventArgs e)
        {
        }

        /// <summary>
        /// Draws table cells, including JJ2 color markers in server names.
        /// </summary>
        private void serverListView_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            bool selected = e.Item.Selected;
            Color backgroundColor;
            if (selected)
            {
                backgroundColor = serverListSelectionColor;
            }
            else
            {
                // Subtle zebra striping so long server lists are easier to scan at a glance.
                backgroundColor = e.Item.Index % 2 == 0 ? serverListBackgroundColor : Jj2Theme.SurfaceAlt;
            }
            Color foregroundColor = Jj2Theme.TextPrimary;
            using (SolidBrush backgroundBrush = new SolidBrush(backgroundColor))
            {
                e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
            }

            if (selected)
            {
                using (SolidBrush accentBrush = new SolidBrush(Jj2Theme.Accent))
                {
                    e.Graphics.FillRectangle(accentBrush, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height);
                }
            }

            Rectangle textBounds = new Rectangle(e.Bounds.X + 8, e.Bounds.Y + 2, e.Bounds.Width - 12, e.Bounds.Height - 4);
            ListedServer server = e.Item.Tag as ListedServer;
            if (e.ColumnIndex == 0)
            {
                DrawJj2ColoredText(e.Graphics, e.SubItem.Text, textBounds, selected);
            }
            else if (e.ColumnIndex == 3)
            {
                DrawGameTypeCell(e.Graphics, textBounds, e.SubItem.Text, server);
            }
            else
            {
                TextFormatFlags alignment = e.ColumnIndex >= 2 ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left;
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, e.Item.Font, textBounds, foregroundColor,
                    alignment | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            using (Pen pen = new Pen(Jj2Theme.Border))
            {
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }
        }

        /// <summary>
        /// Draws the Game Type column as a color-coded "pill" badge, plus a padlock glyph when the
        /// server is private (password-protected) - there's spare width in this column for both.
        /// </summary>
        private void DrawGameTypeCell(Graphics graphics, Rectangle bounds, string text, ListedServer server)
        {
            Color modeColor = Jj2Theme.GameModeColor(server != null ? server.GameType : (byte)0);
            Size textSize = TextRenderer.MeasureText(graphics, text, serverListView.Font, bounds.Size,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            int badgeWidth = textSize.Width + 20;
            int badgeHeight = Math.Min(bounds.Height - 6, 20);
            Rectangle badgeBounds = new Rectangle(bounds.X, bounds.Y + (bounds.Height - badgeHeight) / 2, badgeWidth, badgeHeight);

            using (GraphicsPath path = Jj2Theme.RoundedRectPath(badgeBounds, badgeHeight / 2))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(40, modeColor)))
            using (Pen border = new Pen(modeColor))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.FillPath(fill, path);
                graphics.DrawPath(border, path);
            }

            TextRenderer.DrawText(graphics, text, serverListView.Font, badgeBounds, modeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            if (server != null && server.IsPrivate)
            {
                Jj2Theme.DrawPadlock(graphics, badgeBounds.Right + 10, bounds.Y + (bounds.Height - 16) / 2, Jj2Theme.Accent);
            }
        }

        /// <summary>
        /// Draws text using the JJ2 color-cycle convention controlled by pipe markers.
        /// </summary>
        private void DrawJj2ColoredText(Graphics graphics, string text, Rectangle bounds, bool selected)
        {
            int colorIndex = 0;
            int x = bounds.Left;
            int segmentStart = 0;
            for (int index = 0; index <= text.Length; index++)
            {
                if (index != text.Length && text[index] != '|')
                {
                    continue;
                }

                if (index > segmentStart)
                {
                    string segment = text.Substring(segmentStart, index - segmentStart);
                    Color color = jj2ChatColors[colorIndex];
                    Size size = TextRenderer.MeasureText(graphics, segment, Jj2Theme.FontServerName,
                        new Size(bounds.Right - x, bounds.Height), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    int textY = bounds.Top + Math.Max(0, (bounds.Height - size.Height) / 2);
                    TextRenderer.DrawText(graphics, segment, Jj2Theme.FontServerName, new Point(x, textY), color,
                        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    x += size.Width;
                }

                if (index < text.Length)
                {
                    colorIndex = (colorIndex + 1) % jj2ChatColors.Length;
                }
                segmentStart = index + 1;
            }
        }

        /// <summary>
        /// Starts a background operation that downloads the public server list.
        /// </summary>
        private void LoadServerList()
        {
            if (serverListLoader != null && serverListLoader.IsBusy)
            {
                return;
            }

            refreshServersButton.Enabled = false;
            serverStatusLabel.Text = "Loading server list...";
            serverListLoader = new BackgroundWorker();
            serverListLoader.DoWork += serverListLoader_DoWork;
            serverListLoader.RunWorkerCompleted += serverListLoader_RunWorkerCompleted;
            serverListLoader.RunWorkerAsync();
        }

        /// <summary>
        /// Downloads the server list outside the UI thread.
        /// </summary>
        private void serverListLoader_DoWork(object sender, DoWorkEventArgs e)
        {
            e.Result = Jj2ServerListClient.GetServerList();
        }

        /// <summary>
        /// Displays a completed server-list download or reports its error.
        /// </summary>
        private void serverListLoader_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            refreshServersButton.Enabled = true;
            if (e.Error != null)
            {
                serverStatusLabel.Text = "Unable to load server list.";
                MessageBox.Show(this, e.Error.Message, "Server list error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            PopulateServerList((IList<ListedServer>)e.Result);
        }

        /// <summary>
        /// Replaces the visible rows with the downloaded server entries.
        /// </summary>
        private void PopulateServerList(IList<ListedServer> servers)
        {
            serverListView.BeginUpdate();
            serverListView.Items.Clear();
            foreach (ListedServer server in servers)
            {
                ListViewItem item = new ListViewItem(server.Name);
                item.SubItems.Add(server.IpAddress + ":" + server.Port);
                item.SubItems.Add(server.PlayerCount + "/" + server.PlayerLimit);
                item.SubItems.Add(server.GameTypeName);
                item.Tag = server;
                serverListView.Items.Add(item);
            }
            serverListView.EndUpdate();
            serverStatusLabel.Text = servers.Count + " servers found.";
        }

        /// <summary>
        /// Connects the existing JJ2 client to the selected server endpoint.
        /// </summary>
        private void ConnectToSelectedServer()
        {
            ListedServer server;
            if (serverListView.SelectedItems.Count == 1)
            {
                server = (ListedServer)serverListView.SelectedItems[0].Tag;
            }
            else
            {
                server = new ListedServer();
                server.IpAddress = "127.0.0.1";
                server.Port = 10052;
                server.Name = "Local server";
            }
            ConnectToServer(server);
        }

        private void ConnectToServer(ListedServer server)
        {
            string playerName = playerNameTextBox.Text.Trim();
            if (playerName.Length == 0)
            {
                MessageBox.Show(this, "Enter a player name before connecting.",
                    "Player name required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                playerNameTextBox.Focus();
                return;
            }

            string password = null;
            if (server.IsPrivate)
            {
                using (PasswordPromptForm prompt = new PasswordPromptForm(server.Name))
                {
                    if (prompt.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }
                    password = prompt.Password;
                }
            }

            ServerConnectionForm connectionForm = new ServerConnectionForm(server, playerName, (byte)characterPicker.Selected, password);
            connectionForm.Show(this);
        }
    }
}