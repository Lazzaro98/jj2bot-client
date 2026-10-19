using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Jj2
{
    /// <summary>
    /// Latest position and state of another player, decoded from a server gameplay packet.
    /// </summary>
    class RemotePlayer
    {
        public int Key;
        public int X, Y;
        public bool FacingRight;
        public byte Anim;
        public int Updated;
        public float PrevX, PrevY;
        public int Interval;

        /// <summary>
        /// Interpolates from the previous sample to the latest one over the interval between them.
        /// Never extrapolates, so it cannot overshoot; it lags by at most one packet interval.
        /// </summary>
        public PointF Predict(int now)
        {
            int t = now - Updated;
            if (Interval <= 0 || t >= Interval)
            {
                return new PointF(X, Y);
            }

            float f = Math.Max(0, t) / (float)Interval;
            return new PointF(PrevX + (X - PrevX) * f, PrevY + (Y - PrevY) * f);
        }
    }

    /// <summary>
    /// Live minimap of the current level showing the bot and all other players.
    /// Embeddable control; call SetClient whenever the connection changes.
    /// </summary>
    class MapView : UserControl
    {
        private const int Divisor = 2;

        private jj2udpClient client;
        private readonly Timer timer = new Timer();
        private readonly Panel canvas = new DoubleBufferedPanel();
        private readonly Jj2Button spectatorButton = new Jj2Button();
        private readonly Label infoLabel = new Label();
        private Bitmap mapBitmap;
        private JjLevelMap mapForBitmap;
        private List<Point> flagTiles = new List<Point>();
        private float zoom = 1f;

        public void SetClient(jj2udpClient newClient)
        {
            client = newClient;
            mapForBitmap = null;
            if (mapBitmap != null)
            {
                mapBitmap.Dispose();
                mapBitmap = null;
            }
            infoLabel.Text = "";
            canvas.Invalidate();
            spectatorButton.Text = "Spectator: --";
            spectatorButton.Enabled = false;
        }

        public MapView()
        {
            BackColor = Color.FromArgb(20, 24, 31);

            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 30;
            top.BackColor = Jj2Theme.Surface;
            spectatorButton.Text = "Spectator: --";
            spectatorButton.SetBounds(4, 2, 182, 26);
            spectatorButton.TabStop = false;
            spectatorButton.Enabled = false;
            spectatorButton.Click += delegate
            {
                if (client == null) return;
                try { client.Tcp.ToggleSpectatorMode(); }
                catch (System.Net.Sockets.SocketException ex)
                {
                    client.Tcp.AddMessage("Spectator switch failed: " + ex.SocketErrorCode);
                    client.Disconnect();
                }
                catch (ObjectDisposedException)
                {
                    client.Tcp.AddMessage("Spectator switch failed: connection closed.");
                }
            };
            infoLabel.AutoSize = true;
            infoLabel.Location = new Point(194, 8);
            infoLabel.ForeColor = Jj2Theme.TextSecondary;
            infoLabel.Font = Jj2Theme.FontSmall;
            infoLabel.BackColor = Color.Transparent;
            top.Controls.Add(spectatorButton);
            top.Controls.Add(infoLabel);

            canvas.Dock = DockStyle.Fill;
            canvas.Paint += canvas_Paint;
            canvas.MouseWheel += canvas_MouseWheel;
            canvas.MouseEnter += delegate { canvas.Focus(); };
            Controls.Add(canvas);
            Controls.Add(top);

            timer.Interval = 40;
            timer.Tick += timer_Tick;
            timer.Start();
            Disposed += delegate
            {
                timer.Stop();
                timer.Dispose();
                if (mapBitmap != null)
                {
                    mapBitmap.Dispose();
                }
            };
        }

        private void canvas_MouseWheel(object sender, MouseEventArgs e)
        {
            zoom = Math.Max(1f, Math.Min(12f, zoom * (e.Delta > 0 ? 1.25f : 0.8f)));
        }

        private void timer_Tick(object sender, EventArgs e)
        {
            if (client == null)
            {
                canvas.Invalidate();
                return;
            }

            spectatorButton.Enabled = client.IsReadyForChat() && client.Tcp.IsPlusServer() &&
                !client.Tcp.SpectatorPending;
            spectatorButton.Text = "Spectator: " + (client.Tcp.IsPlusServer() ? client.Tcp.SpectatorStatus : "N/A");
            spectatorButton.Style = client.Tcp.IsSpectating ? Jj2Button.ButtonStyle.Accent : Jj2Button.ButtonStyle.Outline;
            spectatorButton.Invalidate();
            JjLevelMap level = client.Level;
            if (level != null && level != mapForBitmap)
            {
                Bitmap old = mapBitmap;
                mapBitmap = level.RenderBitmap(Divisor);
                mapForBitmap = level;
                flagTiles = new List<Point>();
                foreach (int[] t in level.FindEvents(244))
                {
                    flagTiles.Add(new Point(t[0], t[1]));
                }

                if (old != null)
                {
                    old.Dispose();
                }
            }

            canvas.Invalidate();
        }

        private string NameOf(int key, out Color color)
        {
            color = Color.Yellow;
            foreach (Player p in client.Tcp.GetPlayersSnapshot())
            {
                if (p.playerID == key)
                {
                    color = p.pTeam == 1 ? Color.FromArgb(255, 90, 90) : p.pTeam == 0 ? Color.FromArgb(90, 150, 255) : Color.Yellow;
                    return p.pName;
                }
            }

            return "#" + key;
        }

        private void canvas_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.FromArgb(20, 24, 31));
            if (client == null || mapBitmap == null || mapForBitmap == null)
            {
                g.DrawString("Waiting for the level to load...", Font, Brushes.White, 10, 10);
                return;
            }

            // World pixels -> screen: scale s, offset (ox, oy).
            float fit = Math.Min((float)canvas.Width / mapForBitmap.WidthPx, (float)canvas.Height / mapForBitmap.HeightPx);
            float s = fit * zoom;
            float ox = (canvas.Width - mapForBitmap.WidthPx * s) / 2, oy = (canvas.Height - mapForBitmap.HeightPx * s) / 2;
            JjBotBody body = client.Body;

            g.InterpolationMode = s >= 1f / Divisor ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(mapBitmap, new RectangleF(ox, oy, mapForBitmap.WidthPx * s, mapForBitmap.HeightPx * s));
            g.SmoothingMode = SmoothingMode.AntiAlias;

            foreach (Point t in flagTiles)
            {
                g.FillRectangle(Brushes.Gold, ox + (t.X * 32 + 8) * s, oy + (t.Y * 32) * s, Math.Max(3, 16 * s), Math.Max(3, 32 * s));
            }


            float r = Math.Max(4f, 14 * s);
            int now = Environment.TickCount;
            foreach (RemotePlayer rp in client.GetRemotePlayers())
            {
                if (now - rp.Updated > 5000)
                {
                    continue;
                }

                PointF pp = rp.Predict(now);
                Color c;
                string name = NameOf(rp.Key, out c);
                float sx = ox + pp.X * s, sy = oy + pp.Y * s;
                using (SolidBrush b = new SolidBrush(c))
                {
                    g.FillEllipse(b, sx - r, sy - r, r * 2, r * 2);
                }

                g.DrawEllipse(Pens.Black, sx - r, sy - r, r * 2, r * 2);
                DrawFacing(g, sx, sy, r, rp.FacingRight);
                g.DrawString(name, Font, Brushes.White, sx - r, sy - r - 16);
            }

            if (body != null)
            {
                float sx = ox + body.X * s, sy = oy + body.Y * s;
                g.FillEllipse(Brushes.White, sx - r, sy - r, r * 2, r * 2);
                g.DrawEllipse(Pens.Red, sx - r, sy - r, r * 2, r * 2);
                Color ownColor;
                string ownName = NameOf(client.Tcp.MyPlayerId, out ownColor);
                g.DrawString(ownName, Font, Brushes.White, sx - r, sy - r - 16);
                infoLabel.Text = System.IO.Path.GetFileName(mapForBitmap.LevelFile) + "  [" + (int)body.X + ", " + (int)body.Y + "]";
            }
        }

        private static void DrawFacing(Graphics g, float x, float y, float r, bool right)
        {
            float d = right ? r : -r;
            g.DrawLine(Pens.Black, x, y, x + d, y);
        }

        private class DoubleBufferedPanel : Panel
        {
            public DoubleBufferedPanel()
            {
                DoubleBuffered = true;
                SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            }
        }
    }
}
