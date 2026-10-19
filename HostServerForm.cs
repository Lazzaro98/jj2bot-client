using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Jj2
{
    class HostServerForm : Form
    {
        private readonly Jj2TextBox executableBox;
        private readonly Jj2TextBox levelBox;
        private readonly Jj2TextBox nameBox;
        private readonly ComboBox modeBox;
        private readonly NumericUpDown portBox;
        private readonly NumericUpDown playersBox;
        private readonly CheckBox listBox;
        private readonly string[] modes = { "capture", "battle", "coop", "race", "treasure" };

        public Jj2HostOptions Options { get; private set; }

        public HostServerForm(string executable)
        {
            Text = "Host a server";
            ClientSize = new Size(570, 400);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Jj2Theme.Background;
            ForeColor = Jj2Theme.TextPrimary;
            Font = Jj2Theme.FontBody;

            executableBox = AddTextBox("JJ2/JJ2+ executable", 18, executable);
            levelBox = AddTextBox("Level (.j2l)", 82, "");
            executableBox.Width = levelBox.Width = 440;
            Jj2Button browseGame = AddButton("Browse...", 466, 40, 86);
            browseGame.Click += delegate
            {
                using (OpenFileDialog picker = new OpenFileDialog())
                {
                    picker.Title = "Select your JJ2/JJ2+ executable";
                    picker.Filter = "JJ2 executable (*.exe)|*.exe";
                    if (picker.ShowDialog(this) == DialogResult.OK)
                    {
                        executableBox.Inner.Text = picker.FileName;
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
                    picker.Title = "Select a level inside your game's folder";
                    picker.Filter = "JJ2 levels (*.j2l)|*.j2l";
                    if (File.Exists(executableBox.Inner.Text))
                        picker.InitialDirectory = Path.GetDirectoryName(executableBox.Inner.Text);
                    if (picker.ShowDialog(this) == DialogResult.OK) levelBox.Inner.Text = picker.FileName;
                }
            };
            nameBox = AddTextBox("Server name", 146, "JJ2 Bot Server");
            nameBox.Inner.MaxLength = 30;

            AddLabel("Game mode", 18, 210);
            modeBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(18, 232), Size = new Size(240, 26) };
            modeBox.Items.AddRange(new object[] { "Capture the Flag", "Battle", "Cooperative", "Race", "Treasure Hunt" });
            modeBox.SelectedIndex = 0;
            Controls.Add(modeBox);
            AddLabel("Port (TCP + UDP)", 278, 210);
            portBox = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 10052,
                Location = new Point(278, 232), Width = 130 };
            Controls.Add(portBox);
            AddLabel("Max players", 430, 210);
            playersBox = new NumericUpDown { Minimum = 2, Maximum = 32, Value = 8,
                Location = new Point(430, 232), Width = 122 };
            Controls.Add(playersBox);

            listBox = new CheckBox { Text = "Publish on the public server list", AutoSize = true,
                Location = new Point(18, 272) };
            Controls.Add(listBox);
            Label notice = AddLabel("Friends need your IP and port. Internet hosting may require router port\n" +
                "forwarding and firewall access for both TCP and UDP. JJ2+ is recommended.", 18, 302);
            notice.Font = Jj2Theme.FontSmall;
            notice.ForeColor = Jj2Theme.TextSecondary;
            Jj2Button cancel = AddButton("Cancel", 310, 352, 110);
            cancel.DialogResult = DialogResult.Cancel;
            Jj2Button host = AddButton("Start server", 430, 352, 122);
            host.Style = Jj2Button.ButtonStyle.Accent;
            host.Click += Start_Click;
            CancelButton = cancel;
            AcceptButton = host;
            SelectDefaultLevel();
        }

        private void SelectDefaultLevel()
        {
            if (!File.Exists(executableBox.Inner.Text)) return;
            string level = Path.Combine(Path.GetDirectoryName(executableBox.Inner.Text), "bbswing.j2l");
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
            Options = new Jj2HostOptions
            {
                ExecutablePath = executableBox.Inner.Text.Trim(),
                LevelPath = levelBox.Inner.Text.Trim(),
                ServerName = nameBox.Inner.Text.Trim(),
                Mode = modes[modeBox.SelectedIndex],
                Port = (int)portBox.Value,
                MaxPlayers = (int)playersBox.Value,
                ListPublicly = listBox.Checked
            };
            try
            {
                Jj2HostLauncher.CreateStartInfo(Options);
                Jj2HostLauncher.EnsurePortAvailable(Options.Port);
                DialogResult = DialogResult.OK;
            }
            catch (ArgumentException ex) { ShowError(ex.Message); }
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
