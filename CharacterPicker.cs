using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Jj2
{
    /// <summary>
    /// Playable character chosen on connect. Byte values match the server-facing convention already
    /// used by <see cref="Jj2TcpPackets.CreatePlayerDetails"/> (0 = Jazz, 1 = Spaz, 2 = Lori).
    /// </summary>
    enum Jj2Character : byte { Jazz = 0, Spaz = 1, Lori = 2 }

    /// <summary>
    /// One selectable, text-only character toggle. Intentionally plain (no drawn mascot/silhouette):
    /// we don't have the real Jazz/Spaz/Lori sprites extracted from the game's Anims.j2a, and a made-up
    /// drawing would be misleading, so this just labels the choice instead of faking the artwork.
    /// </summary>
    class CharacterToggle : Control
    {
        public Jj2Character Character { get; private set; }
        private readonly string label;
        private readonly Color tint;
        public bool Selected { get; set; }

        public CharacterToggle(Jj2Character character, string label, Color tint)
        {
            Character = character;
            this.label = label;
            this.tint = tint;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Hand;
            Size = new Size(72, 30);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);

            using (GraphicsPath path = Jj2Theme.RoundedRectPath(rect, 6))
            {
                using (SolidBrush backBrush = new SolidBrush(Selected ? Color.FromArgb(70, tint) : Jj2Theme.Surface))
                {
                    g.FillPath(backBrush, path);
                }
                using (Pen border = new Pen(Selected ? tint : Jj2Theme.Border, Selected ? 2f : 1f))
                {
                    g.DrawPath(border, path);
                }
            }

            TextRenderer.DrawText(g, label, Jj2Theme.FontHeading, rect, tint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>
    /// Hosts the Jazz / Spaz / Lori text toggles and raises <see cref="SelectionChanged"/> when the
    /// user picks a different one.
    /// </summary>
    class CharacterPicker : Panel
    {
        public event EventHandler SelectionChanged;
        public Jj2Character Selected { get; private set; }

        private readonly CharacterToggle[] toggles;

        public CharacterPicker()
        {
            BackColor = Color.Transparent;
            Selected = Jj2Character.Spaz;

            toggles = new[]
            {
                new CharacterToggle(Jj2Character.Jazz, "Jazz", Color.FromArgb(104, 200, 110)),
                new CharacterToggle(Jj2Character.Spaz, "Spaz", Color.FromArgb(235, 120, 155)),
                new CharacterToggle(Jj2Character.Lori, "Lori", Color.FromArgb(235, 195, 90))
            };

            int x = 0;
            foreach (CharacterToggle toggle in toggles)
            {
                toggle.Location = new Point(x, 0);
                toggle.Click += ToggleOnClick;
                Controls.Add(toggle);
                x += toggle.Width + 6;
            }
            Size = new Size(x - 6, toggles[0].Height);
            UpdateSelectionVisual();
        }

        private void ToggleOnClick(object sender, EventArgs e)
        {
            CharacterToggle toggle = (CharacterToggle)sender;
            if (Selected == toggle.Character)
            {
                return;
            }
            Selected = toggle.Character;
            UpdateSelectionVisual();
            if (SelectionChanged != null)
            {
                SelectionChanged(this, EventArgs.Empty);
            }
        }

        private void UpdateSelectionVisual()
        {
            foreach (CharacterToggle toggle in toggles)
            {
                toggle.Selected = toggle.Character == Selected;
                toggle.Invalidate();
            }
        }
    }
}
