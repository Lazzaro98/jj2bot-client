using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Jj2
{
    class HostServerForm : Form
    {
        private readonly Jj2TextBox directoryBox;
        private readonly Jj2TextBox levelBox;
        private readonly Jj2TextBox nameBox;
        private readonly NumericUpDown portBox;
        private readonly NumericUpDown playersBox;
        private readonly ComboBox modeBox;
        public Jj2ServerOptions Options { get; private set; }

        public HostServerForm(string directory)
        {
            Text = "Host a server";
            ClientSize = new Size(570, 420);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Jj2Theme.Background;
            ForeColor = Jj2Theme.TextPrimary;
            Font = Jj2Theme.FontBody;

            directoryBox = AddTextBox("Level and tileset folder (no game executable needed)", 18, directory);
            levelBox = AddTextBox("Level (.j2l)", 82, "");
            directoryBox.Width = levelBox.Width = 440;
            Jj2Button browseGame = AddButton("Browse...", 466, 40, 86);
            browseGame.Click += delegate
            {
                using (FolderBrowserDialog picker = new FolderBrowserDialog())
                {
                    picker.Description = "Select the folder containing your JJ2 levels and tilesets";
                    picker.SelectedPath = directoryBox.Inner.Text;
                    picker.ShowNewFolderButton = false;
                    if (picker.ShowDialog(this) == DialogResult.OK)
                    {
                        directoryBox.Inner.Text = picker.SelectedPath;
                        levelBox.Inner.Text = "";
                        SelectDefaultLevel();
                    }
                }
            };
            Jj2Button browseLevel = AddButton("Browse...", 466, 104, 86);
            browseLevel.Click += delegate
            {
                using (OpenFileDialog picker = new OpenFileDialog())
                {
                    picker.Title = "Select the level to host";
                    picker.Filter = "JJ2 levels (*.j2l)|*.j2l";
                    if (Directory.Exists(directoryBox.Inner.Text))
                        picker.InitialDirectory = directoryBox.Inner.Text;
                    if (picker.ShowDialog(this) == DialogResult.OK) levelBox.Inner.Text = picker.FileName;
                }
            };
            nameBox = AddTextBox("Server name", 146, "JJ2 Bot Server");
            nameBox.Inner.MaxLength = 30;

            AddLabel("Mode", 18, 210);
            modeBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList,
                Name = "hostMode", Location = new Point(18, 232), Width = 240 };
            modeBox.Items.AddRange(new object[] { "Battle sandbox (default)", "Hot Potato (15-second fuse)" });
            modeBox.SelectedIndex = 0;
            Controls.Add(modeBox);
            AddLabel("Port (TCP + UDP)", 278, 210);
            portBox = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 10052,
                Location = new Point(278, 232), Width = 130 };
            Controls.Add(portBox);
            AddLabel("Max players", 430, 210);
            playersBox = new NumericUpDown { Minimum = 2, Maximum = 31, Value = 8,
                Location = new Point(430, 232), Width = 122 };
            Controls.Add(playersBox);

            Label notice = AddLabel("Standalone JJ2+ server. Hot Potato: proximity passes, Sugar Rush marker,\n" +
                "elimination to spectator, last player wins. Start rounds in the server window.\n" +
                "Battle combat, pickups, CTF scoring, scripts and public listing are not implemented.\n" +
                "Use trusted LAN/VPN peers: passwords are not supported.\n" +
                "Allow the TCP + UDP port in your firewall. No Jazz2.exe is started.", 18, 278);
            notice.Font = Jj2Theme.FontSmall;
            notice.ForeColor = Jj2Theme.TextSecondary;
            Jj2Button cancel = AddButton("Cancel", 310, 372, 110);
            cancel.DialogResult = DialogResult.Cancel;
            Jj2Button host = AddButton("Start server", 430, 372, 122);
            host.Style = Jj2Button.ButtonStyle.Accent;
            host.Click += Start_Click;
            CancelButton = cancel;
            AcceptButton = host;
            SelectDefaultLevel();
        }

        private void SelectDefaultLevel()
        {
            if (!Directory.Exists(directoryBox.Inner.Text)) return;
            string level = Path.Combine(directoryBox.Inner.Text, "bbswing.j2l");
            if (File.Exists(level)) levelBox.Inner.Text = level;
        }

        private Label AddLabel(string text, int x, int y)
        {
            Label label = new Label { Text = text, AutoSize = true, Location = new Point(x, y) };
            Controls.Add(label);
            return label;
        }

        private Jj2TextBox AddTextBox(string label, int y, string text)
        {
            AddLabel(label, 18, y);
            Jj2TextBox box = new Jj2TextBox { Location = new Point(18, y + 22), Size = new Size(534, 28) };
            box.Inner.Text = text;
            Controls.Add(box);
            return box;
        }

        private Jj2Button AddButton(string text, int x, int y, int width)
        {
            Jj2Button button = new Jj2Button { Text = text, Location = new Point(x, y),
                Size = new Size(width, 30), Style = Jj2Button.ButtonStyle.Outline };
            Controls.Add(button);
            return button;
        }

        private void Start_Click(object sender, EventArgs e)
        {
            Options = new Jj2ServerOptions
            {
                AssetDirectory = directoryBox.Inner.Text.Trim(),
                LevelPath = levelBox.Inner.Text.Trim(),
                ServerName = nameBox.Inner.Text.Trim(),
                Port = (int)portBox.Value,
                MaxPlayers = (int)playersBox.Value,
                HotPotato = modeBox.SelectedIndex == 1
            };
            try
            {
                Options.Validate();
                DialogResult = DialogResult.OK;
            }
            catch (ArgumentException ex) { ShowError(ex.Message); }
            catch (InvalidDataException ex) { ShowError(ex.Message); }
            catch (IOException ex) { ShowError(ex.Message); }
            catch (UnauthorizedAccessException ex) { ShowError(ex.Message); }
            catch (System.Net.NetworkInformation.NetworkInformationException ex) { ShowError(ex.Message); }
            catch (InvalidOperationException ex) { ShowError(ex.Message); }
        }

        private void ShowError(string message)
        {
            MessageBox.Show(this, message, "Cannot host server", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
