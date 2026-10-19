using System;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Jj2
{
    /// <summary>
    /// Hosts one independent JJ2 server connection and its chat interface.
    /// </summary>
    partial class ServerConnectionForm : Form
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const int VkLeft = 0x25, VkUp = 0x26, VkRight = 0x27, VkDown = 0x28, VkSpace = 0x20, VkControl = 0x11;

        private readonly ListedServer server;
        private readonly string playerName;
        private readonly byte character;
        private readonly string serverPassword;
        private readonly Timer connectionStateTimer;
        private readonly Timer controlsTimer;
        private readonly CheckBox keyboardControlBox;
        private readonly MapView mapView;
        private jj2udpClient client;

        private Panel headerPanel;
        private Panel statusDotPanel;
        private Jj2Panel mapCard;
        private Jj2Panel chatCard;
        private Jj2Panel playerCard;
        private Jj2Panel inputCard;

        /// <summary>
        /// Creates a chat window for one selected server and player identity.
        /// </summary>
        public ServerConnectionForm(ListedServer server, string playerName, byte character, string serverPassword = null)
        {
            this.server = server;
            this.playerName = playerName;
            this.character = character;
            this.serverPassword = serverPassword;
            InitializeComponent();
            Text = "Jj2 Client - " + Jj2Theme.StripColorMarkers(server.Name);

            connectionStateTimer = new Timer();
            connectionStateTimer.Interval = 500;
            connectionStateTimer.Tick += connectionStateTimer_Tick;

            keyboardControlBox = new CheckBox();
            keyboardControlBox.Text = "Arrows / Space / Ctrl";
            keyboardControlBox.AutoSize = true;
            keyboardControlBox.Checked = true;
            mapView = new MapView();
            new ToolTip().SetToolTip(keyboardControlBox, "Arrows move; Space jumps (press again for Spaz double jump; hold in air for Jazz/Lori copter); Ctrl fires the blaster; Up+Ctrl aims up; Down in air = buttstomp; hold Down briefly then Space = Spaz sidekick; Down on vine = drop. Chat must not be focused.");

            ApplyTheme();

            serverLabel.Text = Jj2Theme.StripColorMarkers(server.Name);
            MinimumSize = new Size(640, 440);
            ClientSize = new Size(820, 560);
            connectionToggleButton.TabStop = false;
            sendButton.TabStop = false;
            Resize += delegate { LayoutControls(); };
            LayoutControls();

            controlsTimer = new Timer();
            controlsTimer.Interval = 15;
            controlsTimer.Tick += controlsTimer_Tick;
        }

        /// <summary>
        /// Applies the shared dark/gold premium theme: a header bar with a live status dot, themed
        /// buttons, and every content area wrapped in a rounded "card" panel.
        /// </summary>
        private void ApplyTheme()
        {
            BackColor = Jj2Theme.Background;
            Font = Jj2Theme.FontBody;

            headerPanel = new Panel();
            headerPanel.BackColor = Jj2Theme.Surface;
            Controls.Add(headerPanel);

            serverLabel.Font = Jj2Theme.FontHeading;
            serverLabel.ForeColor = Jj2Theme.TextPrimary;
            serverLabel.BackColor = Color.Transparent;
            headerPanel.Controls.Add(serverLabel);

            Label addressLabel = new Label();
            addressLabel.AutoSize = true;
            addressLabel.Font = Jj2Theme.FontSmall;
            addressLabel.ForeColor = Jj2Theme.TextSecondary;
            addressLabel.BackColor = Color.Transparent;
            addressLabel.Text = server.IpAddress + ":" + server.Port;
            headerPanel.Controls.Add(addressLabel);
            this.addressLabel = addressLabel;

            statusDotPanel = new Panel();
            statusDotPanel.BackColor = Color.Transparent;
            statusDotPanel.Paint += delegate (object s, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Jj2Theme.DrawStatusDot(e.Graphics, new Rectangle(0, 0, statusDotPanel.Width, statusDotPanel.Height), currentStatusColor);
            };
            headerPanel.Controls.Add(statusDotPanel);

            connectionStatusLabel.ForeColor = Jj2Theme.TextSecondary;
            connectionStatusLabel.Font = Jj2Theme.FontSmall;
            connectionStatusLabel.BackColor = Color.Transparent;
            headerPanel.Controls.Add(connectionStatusLabel);

            keyboardControlBox.ForeColor = Jj2Theme.TextSecondary;
            Jj2Theme.StyleCheckBox(keyboardControlBox, Jj2Theme.Surface);
            headerPanel.Controls.Add(keyboardControlBox);

            connectionToggleButton.Style = Jj2Button.ButtonStyle.Danger;
            headerPanel.Controls.Add(connectionToggleButton);

            Panel accentLine = new Panel();
            accentLine.BackColor = Jj2Theme.Accent;
            accentLine.Dock = DockStyle.Bottom;
            accentLine.Height = 2;
            headerPanel.Controls.Add(accentLine);

            mapCard = WrapInCard(mapView, 8);
            chatCard = WrapInCard(chatLog, 8);
            playerCard = WrapInCard(playerList, 8);
            inputCard = WrapInCard(messageInput, 6);

            chatLog.BackColor = Jj2Theme.Surface;
            chatLog.ForeColor = Jj2Theme.TextPrimary;
            chatLog.BorderStyle = BorderStyle.None;
            playerList.BackColor = Jj2Theme.Surface;
            playerList.ForeColor = Jj2Theme.TextPrimary;
            playerList.BorderStyle = BorderStyle.None;
            messageInput.BackColor = Jj2Theme.SurfaceAlt;
            messageInput.ForeColor = Jj2Theme.TextPrimary;
            messageInput.BorderStyle = BorderStyle.None;
            messageInput.Font = Jj2Theme.FontBody;

            sendButton.Style = Jj2Button.ButtonStyle.Accent;
        }

        private Label addressLabel;
        private Color currentStatusColor = Jj2Theme.TextSecondary;

        /// <summary>
        /// Updates the glowing header dot + status text color to reflect the current connection state.
        /// </summary>
        private void SetStatusColor(Color color)
        {
            currentStatusColor = color;
            if (statusDotPanel != null)
            {
                statusDotPanel.Invalidate();
            }
        }

        /// <summary>
        /// Wraps a plain content control in a themed rounded "card" panel with inner padding.
        /// </summary>
        private Jj2Panel WrapInCard(Control content, int padding)
        {
            Jj2Panel card = new Jj2Panel();
            card.Padding = new Padding(padding);
            Controls.Remove(content);
            content.Dock = DockStyle.Fill;
            card.Controls.Add(content);
            Controls.Add(card);
            return card;
        }

        private void LayoutControls()
        {
            const int m = 10, side = 190, rowH = 30, headerH = 62;
            int w = ClientSize.Width, h = ClientSize.Height;
            int left = w - 2 * m - side - 8;
            int inputY = h - m - rowH;

            headerPanel.SetBounds(0, 0, w, headerH);
            serverLabel.SetBounds(16, 9, headerPanel.Width - 3 * m - side, 18);
            addressLabel.SetBounds(16, 28, headerPanel.Width - 3 * m - side, 14);
            statusDotPanel.SetBounds(16, 44, 12, 12);
            connectionStatusLabel.SetBounds(32, 42, headerPanel.Width - 3 * m - side - 32, 16);
            connectionToggleButton.SetBounds(w - m - side, 14, side, rowH);
            keyboardControlBox.SetBounds(w - m - side, 14 + rowH + 8, side, 18);

            int top = headerH + 8;
            int mapH = (int)((inputY - 8 - top) * 0.58);
            mapCard.SetBounds(m, top, left, mapH);
            chatCard.SetBounds(m, top + mapH + 8, left, inputY - 8 - (top + mapH + 8));
            playerCard.SetBounds(w - m - side, top, side, inputY - 8 - top);
            inputCard.SetBounds(m, inputY, left, rowH);
            sendButton.SetBounds(w - m - side, inputY, side, rowH);
        }

        /// <summary>
        /// Opens the server connection once the window is displayed.
        /// </summary>
        private void ServerConnectionForm_Shown(object sender, EventArgs e)
        {
            ConnectToServer();
            connectionStateTimer.Start();
            controlsTimer.Start();
        }

        private static bool IsKeyHeld(int vKey)
        {
            return (GetAsyncKeyState(vKey) & 0x8000) != 0;
        }

        /// <summary>
        /// Feeds the keyboard state to the bot while this window is active and the chat box is not focused.
        /// </summary>
        private void controlsTimer_Tick(object sender, EventArgs e)
        {
            if (client == null)
            {
                return;
            }

            bool active = keyboardControlBox.Checked && Form.ActiveForm == this && !messageInput.Focused &&
                client.IsReadyForChat() && !client.Tcp.IsSpectating;
            if (active)
            {
                int dx = (IsKeyHeld(VkRight) ? 1 : 0) - (IsKeyHeld(VkLeft) ? 1 : 0);
                int dy = (IsKeyHeld(VkDown) ? 1 : 0) - (IsKeyHeld(VkUp) ? 1 : 0);
                client.SetControls(dx, dy, IsKeyHeld(VkSpace), IsKeyHeld(VkControl));
            }
            else
            {
                client.SetControls(0, 0, false, false);
            }

            JjBotBody body = client.Body;
            if (body != null)
            {
                Text = "Jj2 Client - " + Jj2Theme.StripColorMarkers(server.Name) + "  [" + (int)body.X + ", " + (int)body.Y + "]";
            }
        }

        /// <summary>
        /// Opens a fresh TCP and UDP connection to this window's selected server.
        /// </summary>
        private void ConnectToServer()
        {
            IPAddress ip;
            if (!IPAddress.TryParse(server.IpAddress, out ip))
            {
                connectionStatusLabel.Text = "Connection failed: invalid server IP address.";
                SetDisconnectedControls();
                return;
            }

            try
            {
                TextBox.CheckForIllegalCrossThreadCalls = false;
                client = new jj2udpClient(ip, server.Port, 0, playerName, chatLog, playerList, messageInput, character, 0x00, null, serverPassword);
                connectionStatusLabel.Text = "Connected as " + playerName + ".";
                SetStatusColor(Jj2Theme.Success);
                mapView.SetClient(client);
                connectionToggleButton.Text = "Disconnect";
                connectionToggleButton.Style = Jj2Button.ButtonStyle.Danger;
                connectionToggleButton.Invalidate();
                messageInput.Enabled = false;
                sendButton.Enabled = false;
            }
            catch (SocketException ex)
            {
                connectionStatusLabel.Text = "Connection failed.";
                SetDisconnectedControls();
                MessageBox.Show(this, ex.Message, "Connection failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Closes this window's current server connection and enables reconnecting.
        /// </summary>
        private void DisconnectFromServer()
        {
            if (client != null)
            {
                client.Disconnect();
                client = null;
            }
            mapView.SetClient(null);

            connectionStatusLabel.Text = "Disconnected.";
            SetStatusColor(Jj2Theme.Danger);
            SetDisconnectedControls();
        }

        /// <summary>
        /// Updates controls after a local or server-initiated disconnect.
        /// </summary>
        private void SetDisconnectedControls()
        {
            connectionToggleButton.Text = "Reconnect";
            connectionToggleButton.Style = Jj2Button.ButtonStyle.Accent;
            connectionToggleButton.Invalidate();
            messageInput.Enabled = false;
            sendButton.Enabled = false;
        }

        /// <summary>
        /// Sends the current message through this window's independent connection.
        /// </summary>
        private void sendButton_Click(object sender, EventArgs e)
        {
            if (client != null)
            {
                client.SendMessage();
            }
        }

        /// <summary>
        /// Disconnects the active connection or creates a new connection after a disconnect.
        /// </summary>
        private void connectionToggleButton_Click(object sender, EventArgs e)
        {
            if (client != null && client.IsConnected())
            {
                DisconnectFromServer();
            }
            else
            {
                DisconnectFromServer();
                ConnectToServer();
            }
        }

        /// <summary>
        /// Detects a server-initiated disconnect and exposes the reconnect action.
        /// </summary>
        private void connectionStateTimer_Tick(object sender, EventArgs e)
        {
            if (client != null && !client.IsConnected())
            {
                DisconnectFromServer();
            }
            else if (client != null && client.IsReadyForChat())
            {
                messageInput.Enabled = true;
                sendButton.Enabled = true;
            }
            else
            {
                messageInput.Enabled = false;
                sendButton.Enabled = false;
            }
        }

        /// <summary>
        /// Closes the network connection when this chat window is closed.
        /// </summary>
        private void ServerConnectionForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            connectionStateTimer.Stop();
            connectionStateTimer.Dispose();
            controlsTimer.Stop();
            controlsTimer.Dispose();
            DisconnectFromServer();
        }
    }
}
