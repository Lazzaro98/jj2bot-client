using System;
using System.Drawing;
using System.Net;
using System.Reflection;
using System.Windows.Forms;
using Jj2;

class SpectatorListTests
{
    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static void Row(RichTextBox list, string name, bool spectator, Color color)
    {
        int offset = list.Text.IndexOf(name, StringComparison.Ordinal);
        Assert(offset >= 0, "Missing player: " + name);
        list.Select(offset, name.Length);
        Assert(list.SelectionColor.ToArgb() == color.ToArgb(), "Incorrect color: " + name);
        string line = list.Lines[list.GetLineFromCharIndex(offset)];
        Assert(line.EndsWith(". " + name, StringComparison.Ordinal), "Unexpected name label: " + name);
    }

    [STAThread] static void Main()
    {
        using (var chat = new RichTextBox())
        using (var list = new RichTextBox())
        using (var input = new RichTextBox())
        {
            var client = new jj2tcpClient(IPAddress.Loopback, 10053, 0, delegate { },
                "Local", chat, list, input, 1, 0, null);
            try
            {
                typeof(jj2tcpClient).GetField("mySockID", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(client, (byte)1);
                client.HandleReceivedPacket(new byte[] { 8, 0x41, 0, 32, 2, 0, 0, 128 });
                var players = (Player[])typeof(jj2tcpClient).GetField("players",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(client);
                players[0] = new Player("Active") { playerID = 0, sockID = 0, pTeam = 1, connected = true };
                players[1] = new Player("Local") { playerID = 1, sockID = 1, pTeam = 0, connected = true };
                players[31] = new Player("Remote") { playerID = 31, sockID = 31, pTeam = 1, connected = true };
                client.HandleReceivedPacket(new byte[] { 8, 0x41, 0, 32, 2, 0, 0, 128 });
                Row(list, "Active", false, Color.Red);
                Row(list, "Local", true, Color.Gray);
                Row(list, "Remote", true, Color.Gray);
                Assert(client.IsSpectating, "Initial local state");
                Assert(client.GetPlayersSnapshot()[2].IsSpectating, "Snapshot state");

                client.HandleReceivedPacket(new byte[] { 6, 0x41, 1, 0, 31, 0xFE });
                Row(list, "Remote", false, Color.Red);
                Assert(client.IsSpectating, "Remote update changed local state");
                client.HandleReceivedPacket(new byte[] { 6, 0x41, 1, 0, 0, 1 });
                Row(list, "Active", true, Color.Gray);
                client.HandleReceivedPacket(new byte[] { 6, 0x41, 1, 0, 1, 0xFE });
                Row(list, "Local", false, Color.Blue);
                Assert(!client.IsSpectating, "Local state did not clear");
                client.HandleReceivedPacket(new byte[] { 6, 0x41, 1, 0, 0, 0xFF });
                Row(list, "Active", true, Color.Gray);
                client.HandleReceivedPacket(new byte[] { 6, 0x41, 1, 0, 32, 0 });
                Row(list, "Active", true, Color.Gray);
                client.HandleReceivedPacket(new byte[] { 8, 0x41, 0, 32, 0, 0, 0, 0 });
                Row(list, "Active", false, Color.Red);
                Row(list, "Remote", false, Color.Red);
                // Zeal: four bots share socket 1; player 5 owns spectator socket 2.
                players[2] = new Player("Bot 2") { playerID = 2, sockID = 1, pTeam = 0, connected = true };
                players[5] = new Player("Player") { playerID = 5, sockID = 2, pTeam = 0, connected = true };
                client.HandleReceivedPacket(new byte[] { 8, 0x41, 0, 32, 4, 0, 0, 0 });
                Row(list, "Bot 2", false, Color.Blue);
                Row(list, "Player", true, Color.Gray);
                Assert(!client.IsSpectating, "Different socket changed our local state");
                client.HandleReceivedPacket(new byte[] { 6, 0x41, 1, 0, 2, 0xFE });
                Row(list, "Player", false, Color.Blue);
                client.HandleReceivedPacket(new byte[] { 6, 0x41, 1, 0, 1, 0 });
                Row(list, "Local", true, Color.Gray);
                Row(list, "Bot 2", true, Color.Gray);
                Assert(client.IsSpectating, "Shared socket toggle not reflected");
                Console.WriteLine("PASS: spectator list snapshots, live toggles, colors, labels and invalid updates");
            }
            finally { client.Disconnect(); }
        }
    }
}
