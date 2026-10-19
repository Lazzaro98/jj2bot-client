using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Jj2
{
    /// <summary>
    /// Shared dark/gold "premium" visual theme used by every window in the app.
    /// </summary>
    static class Jj2Theme
    {
        public static readonly Color Background = Color.FromArgb(16, 18, 26);
        public static readonly Color Surface = Color.FromArgb(24, 28, 40);
        public static readonly Color SurfaceAlt = Color.FromArgb(30, 35, 50);
        public static readonly Color Border = Color.FromArgb(52, 58, 78);
        public static readonly Color Accent = Color.FromArgb(230, 168, 61);
        public static readonly Color AccentHover = Color.FromArgb(245, 189, 90);
        public static readonly Color AccentPressed = Color.FromArgb(200, 142, 42);
        public static readonly Color Success = Color.FromArgb(84, 214, 141);
        public static readonly Color Danger = Color.FromArgb(226, 94, 94);
        public static readonly Color TextPrimary = Color.FromArgb(238, 241, 247);
        public static readonly Color TextSecondary = Color.FromArgb(150, 160, 182);
        public static readonly Color SelectionHighlight = Color.FromArgb(62, 70, 96);

        public static readonly Font FontTitle = new Font("Segoe UI Semibold", 13f, FontStyle.Bold);
        public static readonly Font FontHeading = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold);
        public static readonly Font FontBody = new Font("Segoe UI", 9f);
        public static readonly Font FontSmall = new Font("Segoe UI", 8f);
        public static readonly Font FontServerName = new Font("Segoe UI", 10f);

        /// <summary>
        /// Clips a control to a rounded-rectangle region so it reads as a modern "card" / "pill" shape.
        /// </summary>
        public static void ApplyRoundedRegion(Control control, int radius)
        {
            if (control.Width <= 0 || control.Height <= 0)
            {
                return;
            }

            GraphicsPath path = RoundedRectPath(new Rectangle(0, 0, control.Width, control.Height), radius);
            control.Region = new Region(path);
        }

        public static GraphicsPath RoundedRectPath(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            if (d <= 0 || d >= bounds.Width || d >= bounds.Height)
            {
                path.AddRectangle(bounds);
                return path;
            }

            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>
        /// Draws a thin accent-colored dot used as a connection-status indicator.
        /// </summary>
        public static void DrawStatusDot(Graphics g, Rectangle cellBounds, Color color)
        {
            int size = 9;
            Rectangle r = new Rectangle(cellBounds.X, cellBounds.Y + (cellBounds.Height - size) / 2, size, size);
            using (SolidBrush glow = new SolidBrush(Color.FromArgb(70, color)))
            {
                g.FillEllipse(glow, Rectangle.Inflate(r, 3, 3));
            }
            using (SolidBrush b = new SolidBrush(color))
            {
                g.FillEllipse(b, r);
            }
        }

        /// <summary>
        /// Removes the JJ2 chat color-cycle markers ('|') from a server/player name so it reads as
        /// plain text wherever it isn't custom-drawn with the color cycling (e.g. window titles, labels).
        /// </summary>
        public static string StripColorMarkers(string text)
        {
            return string.IsNullOrEmpty(text) ? text : text.Replace("|", "");
        }

        /// <summary>
        /// Styles a CheckBox to match the dark/gold theme (flat border, themed check color) instead of
        /// the stock system checkbox, which looks out of place on a dark background.
        /// </summary>
        public static void StyleCheckBox(CheckBox box, Color backColor)
        {
            box.FlatStyle = FlatStyle.Flat;
            box.BackColor = backColor;
            box.ForeColor = TextSecondary;
            box.Font = FontSmall;
            box.FlatAppearance.BorderSize = 1;
            box.FlatAppearance.BorderColor = Border;
            box.FlatAppearance.CheckedBackColor = Color.FromArgb(70, Accent);
            box.FlatAppearance.MouseOverBackColor = SurfaceAlt;
            box.FlatAppearance.MouseDownBackColor = SelectionHighlight;
        }

        /// <summary>
        /// Draws a small padlock glyph (used to flag password-protected/private servers in the list).
        /// </summary>
        public static void DrawPadlock(Graphics g, int x, int y, Color color)
        {
            using (Pen pen = new Pen(color, 1.4f))
            using (SolidBrush brush = new SolidBrush(color))
            {
                Rectangle body = new Rectangle(x, y + 4, 10, 8);
                g.FillRectangle(brush, body);
                g.DrawArc(pen, x + 1, y - 2, 8, 8, 180, 180);
            }
        }

        /// <summary>
        /// Picks a distinct accent color per JJ2 game mode, used to color-code the game-type badge.
        /// </summary>
        public static Color GameModeColor(byte gameType)
        {
            switch (gameType)
            {
                case 1: return Color.FromArgb(104, 178, 94);   // Cooperative - green
                case 2: return Color.FromArgb(226, 94, 94);    // Battle - red
                case 3: return Color.FromArgb(142, 150, 230);  // Race - violet
                case 4: return Color.FromArgb(230, 168, 61);   // Treasure - gold
                case 5: return Color.FromArgb(90, 170, 224);   // CTF - blue
                default: return TextSecondary;                 // Unknown - neutral
            }
        }
    }

    /// <summary>
    /// Flat, rounded-corner button with hover/press feedback, used instead of the stock WinForms button
    /// everywhere in the UI for a consistent "premium" look.
    /// </summary>
    class Jj2Button : Button
    {
        public enum ButtonStyle { Accent, Outline, Danger }

        private bool hovering;
        private bool pressed;
        public ButtonStyle Style { get; set; }

        public Jj2Button()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent;
            ForeColor = Jj2Theme.TextPrimary;
            Font = Jj2Theme.FontHeading;
            Cursor = Cursors.Hand;
            Style = ButtonStyle.Accent;
            MouseEnter += delegate { hovering = true; Invalidate(); };
            MouseLeave += delegate { hovering = false; pressed = false; Invalidate(); };
            MouseDown += delegate { pressed = true; Invalidate(); };
            MouseUp += delegate { pressed = false; Invalidate(); };
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Jj2Theme.ApplyRoundedRegion(this, Math.Min(8, Height / 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);

            Color fill, text;
            switch (Style)
            {
                case ButtonStyle.Outline:
                    fill = pressed ? Jj2Theme.SelectionHighlight : (hovering ? Jj2Theme.SurfaceAlt : Jj2Theme.Surface);
                    text = Jj2Theme.TextPrimary;
                    break;
                case ButtonStyle.Danger:
                    fill = pressed ? Color.FromArgb(170, 60, 60) : (hovering ? Color.FromArgb(235, 110, 110) : Jj2Theme.Danger);
                    text = Color.FromArgb(30, 10, 10);
                    break;
                default:
                    fill = pressed ? Jj2Theme.AccentPressed : (hovering ? Jj2Theme.AccentHover : Jj2Theme.Accent);
                    text = Color.FromArgb(26, 18, 4);
                    break;
            }

            if (!Enabled)
            {
                fill = Jj2Theme.SurfaceAlt;
                text = Jj2Theme.TextSecondary;
            }

            using (GraphicsPath path = Jj2Theme.RoundedRectPath(rect, Math.Min(8, Height / 2)))
            using (SolidBrush brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
                if (Style == ButtonStyle.Outline)
                {
                    using (Pen pen = new Pen(Jj2Theme.Border))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }

            TextRenderer.DrawText(g, Text, Font, rect, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>
    /// Simple rounded "card" container panel used to group related controls with a subtle border.
    /// </summary>
    class Jj2Panel : Panel
    {
        public int CornerRadius = 10;

        public Jj2Panel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Jj2Theme.Surface;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Jj2Theme.RoundedRectPath(rect, CornerRadius))
            using (SolidBrush brush = new SolidBrush(BackColor))
            using (Pen pen = new Pen(Jj2Theme.Border))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
        }
    }

    /// <summary>
    /// Flat dark text box (wraps a borderless TextBox inside a themed rounded card) for a consistent input look.
    /// </summary>
    class Jj2TextBox : Panel
    {
        public readonly TextBox Inner;

        public Jj2TextBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Jj2Theme.SurfaceAlt;
            Padding = new Padding(8, 6, 8, 6);
            Inner = new TextBox();
            Inner.BorderStyle = BorderStyle.None;
            Inner.BackColor = Jj2Theme.SurfaceAlt;
            Inner.ForeColor = Jj2Theme.TextPrimary;
            Inner.Font = Jj2Theme.FontBody;
            Inner.Dock = DockStyle.Fill;
            Inner.Enter += delegate { Invalidate(); };
            Inner.Leave += delegate { Invalidate(); };
            Controls.Add(Inner);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Jj2Theme.RoundedRectPath(rect, 6))
            using (SolidBrush brush = new SolidBrush(BackColor))
            using (Pen pen = new Pen(Inner.Focused ? Jj2Theme.Accent : Jj2Theme.Border))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }
    }
}
