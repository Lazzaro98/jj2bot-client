using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;
using Jj2;

static class HotPotatoTests
{
    static int checks;
    static void Assert(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("Hot Potato: " + message);
    }
    static List<HotPotatoPlayer> Players(double time, params int[] xs)
    {
        List<HotPotatoPlayer> players = new List<HotPotatoPlayer>();
        for (int i = 0; i < xs.Length; i++)
            players.Add(new HotPotatoPlayer { Id = (byte)(i + 1), X = xs[i], Y = 100,
                HasPosition = true, PositionTime = time });
        return players;
    }
    static int FreePort()
    {
        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
    static void Wait(Func<bool> condition, string message)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(4);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            Thread.Sleep(10);
        }
        Assert(false, message);
    }
    static void Move(StandaloneServerTests.Client client, byte sequence, int x)
    {
        byte[] packet = new byte[17];
        packet[2] = 1; packet[3] = sequence; packet[4] = 1;
        packet[9] = (byte)x; packet[10] = (byte)(x >> 8); packet[11] = 100;
        jj2udpClient.CalculateUdpChecksum(packet);
        client.Udp.Send(packet, packet.Length);
    }
    public static void Run(string directory, string level)
    {
        HotPotatoRound round = new HotPotatoRound(delegate { return 0; });
        bool rejected = false;
        try { round.Start(0, Players(0, 100)); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected && !round.Running, "fewer than two players started a round");
        List<HotPotatoChange> changes = round.Start(0, Players(0, 100, 120, 500));
        Assert(round.Running && round.Holder == 1 && round.Deadline == 15 && changes.Count == 1, "initial random holder/fuse");
        round.Step(0.99, Players(0.99, 100, 120, 500));
        Assert(round.Holder == 1, "initial cooldown was ignored");
        round.Step(1, Players(1, 100, 120, 500));
        Assert(round.Holder == 2 && round.Deadline == 15, "pass reset the fuse or missed proximity");
        round.Step(1.5, Players(1.5, 100, 120, 125));
        Assert(round.Holder == 2, "global transfer cooldown was ignored");
        round.Step(2, Players(2, 100, 120, 500));
        Assert(round.Holder == 2, "touching players ping-ponged without separating");
        round.Step(3, Players(3, 20, 120, 500));
        round.Step(4, Players(4, 100, 120, 500));
        Assert(round.Holder == 1 && round.Deadline == 15, "separated pair could not pass back");
        changes = round.Step(15, Players(15, 100, 120, 500));
        Assert(round.Running && round.Holder == 2 && round.RemainingPlayers == 2 && round.Deadline == 30,
            "expiry did not eliminate before passing and start a new fuse");
        Assert(changes.Exists(delegate(HotPotatoChange c) { return c.Kind == HotPotatoChangeKind.Eliminated && c.Player == 1; }),
            "expiry did not report elimination");
        round.Step(30, Players(30, 100, 120, 500));
        Assert(!round.Running && round.Finished && round.Winner == 3 && !round.Holder.HasValue, "last survivor did not win");
        round.Reset();
        Assert(!round.Running && !round.Finished && !round.Winner.HasValue && round.RemainingPlayers == 0, "reset leaked round state");
        round.Start(40, Players(40, 100, 120, 500));
        List<HotPotatoPlayer> disconnected = Players(41, 100, 120, 500);
        disconnected.RemoveAt(0);
        round.Step(41, disconnected);
        Assert(round.Holder == 2 && round.Deadline == 56 && round.RemainingPlayers == 2, "departing holder did not receive a fresh replacement fuse");
        disconnected.RemoveAt(1);
        round.Step(42, disconnected);
        Assert(round.Winner == 2 && round.Finished, "disconnect/spectate did not award last survivor");
        round.Reset(); round.Start(0, Players(0, 100, 120));
        round.Step(2, Players(0, 100, 120));
        Assert(round.Holder == 1, "stale positions transferred the potato");
        round.Step(3, new List<HotPotatoPlayer>());
        Assert(round.Finished && !round.Winner.HasValue, "empty round did not stop");
        round.Start(10, Players(10, 100, 500));
        round.Step(11, Players(11, 100, 500, 101));
        Assert(round.RemainingPlayers == 2 && round.Holder == 1, "late join entered the active round");
        round.Reset();
        HotPotatoRound last = new HotPotatoRound(delegate(int count) { return count - 1; });
        last.Start(0, Players(0, 100, 500));
        Assert(last.Holder == 2, "injected random selection was ignored");
        last.Reset();
        round.Start(0, Players(0, 100, 500));
        List<HotPotatoPlayer> missing = Players(2, 100, 120);
        missing[1].HasPosition = false;
        round.Step(2, missing);
        Assert(round.Holder == 1, "missing position participated in a pass");
        round.Step(2, Players(2, 100, 133));
        Assert(round.Holder == 1, "out-of-radius player received the potato");
        round.Step(3, Players(3, 100, 132));
        Assert(round.Holder == 2, "radius boundary did not pass");
        round.Reset(); round.Start(0, Players(0, 100, 500, 900));
        round.Step(2, Players(2, 100, 500));
        Assert(round.Holder == 1 && round.Deadline == 15 && round.RemainingPlayers == 2,
            "a departing non-holder rerolled the fuse/holder");

        byte[] raw = new byte[17]; raw[2] = 1; raw[4] = 1; raw[6] = 0x38;
        byte[] position = Jj2ServerPackets.Position(raw, 4);
        byte[] sugar = Jj2ServerPackets.SugarRush(position, true);
        Assert(sugar[7] == 0x3A && position[7] == 0x38 && Jj2ServerPackets.ValidUdp(sugar), "Sugar Rush changed unrelated bits/input/checksum");
        Assert(Jj2ServerPackets.SugarRush(sugar, false)[7] == 0x38, "Sugar Rush did not clear");
        byte[] captured = { 0xC3, 0x7A, 2, 0x27, 1, 0x5E, 0xA0, 2, 0, 1, 0xE0, 0x80, 0xE6, 3, 0x3D, 2, 0, 0 };
        Assert(Jj2ServerPackets.ValidUdp(captured) &&
            Convert.ToBase64String(Jj2ServerPackets.SugarRush(Jj2ServerPackets.SugarRush(captured, false), true)) ==
            Convert.ToBase64String(captured), "native reference Sugar Rush capture was not reproduced byte-exactly");
        byte[] spectator = Jj2ServerPackets.Spectator(4, true);
        Assert(BitConverter.ToString(spectator) == "06-41-01-00-04-04" &&
            Jj2ServerPackets.Spectator(4, false)[5] == 0xFE, "native forced-spectator packet shape");
        Assert(BitConverter.ToString(Jj2ServerPackets.Respawn(1, 0xE2)) == "02-BA-07-E2-02-00-00-00-0F-01-01",
            "spectator exit respawn differs from the native reference capture");
        byte[] states = Jj2ServerPackets.ClientStates(1u << 31 | 6, 4, 6);
        Assert(states.Length == 19 && BitConverter.ToUInt32(states, 5) == (1u << 31 | 6) &&
            BitConverter.ToUInt32(states, 10) == 4 && BitConverter.ToUInt32(states, 15) == 6,
            "dynamic native client masks/31st slot");
        byte[] manifest = Jj2ServerPackets.ScriptList(new byte[] { 1, 2, 3, 4 }, new byte[] { 1, 2, 3 });
        Assert(manifest[0] == manifest.Length && manifest[1] == 0x5A &&
            manifest[2] == 1 && manifest[3] == 2 && manifest[4] == 3 && manifest[5] == 4,
            "native script manifest framing and challenge");
        Assert(manifest[6] == 2 && manifest[7] == 1 && manifest[8] == 0 &&
            manifest[9] == 0 && manifest[10] == 0 && manifest[11] == 0,
            "manifest advertises one module, no dependencies and native reserved byte");
        Assert(BitConverter.ToUInt32(manifest, 12) == 0x55BC801Du &&
            manifest[16] == Jj2ServerPackets.HotPotatoModule.Length &&
            System.Text.Encoding.ASCII.GetString(manifest, 17, manifest[16]) == Jj2ServerPackets.HotPotatoModule &&
            manifest.Length == 17 + manifest[16], "manifest full-file CRC and exact whitelisted filename");
        Assert(JjAssetDownload.Crc32(System.Text.Encoding.ASCII.GetBytes("123456789")) == 0xCBF43926u,
            "script CRC uses standard whole-file IEEE CRC32");
        byte[] hud = Jj2ServerPackets.HotPotatoHud(4, 12.001);
        Assert(hud.Length == 7 && hud[1] == 0x56 && hud[2] == 1 && hud[3] == 1 &&
            hud[4] == 4 && BitConverter.ToUInt16(hud, 5) == 841,
            "script stream addresses module one and rounds fractional ticks up");
        Assert(BitConverter.ToUInt16(Jj2ServerPackets.HotPotatoHud(4, 20), 5) == 1050 &&
            BitConverter.ToUInt16(Jj2ServerPackets.HotPotatoHud(4, -1), 5) == 0,
            "script countdown clamps to the authoritative 15-second fuse");
        hud = Jj2ServerPackets.HotPotatoHud(null, 15);
        Assert(hud[4] == 255 && BitConverter.ToUInt16(hud, 5) == 0,
            "inactive script state clears holder and countdown");

        List<string> logs = new List<string>();
        Action<string> log = delegate(string text) { lock (logs) logs.Add(text); };
        Jj2ServerOptions options = new Jj2ServerOptions { AssetDirectory = directory, LevelPath = level,
            Port = FreePort(), MaxPlayers = 4, ServerName = "Hot Potato test", HotPotato = true };
        using (Jj2StandaloneServer server = new Jj2StandaloneServer(options, log, delegate { return 0; }))
        {
            server.Start();
            server.ExecuteAdmin("/start");
            Assert(server.ModeStatus.Contains("ready"), "invalid start changed server mode state");
            using (StandaloneServerTests.Client a = new StandaloneServerTests.Client(server.Port, "Alpha"))
            using (StandaloneServerTests.Client b = new StandaloneServerTests.Client(server.Port, "Bravo"))
            {
                byte[] module = StandaloneServerTests.Download(a, Jj2ServerPackets.HotPotatoModule);
                using (Stream resource = typeof(Jj2StandaloneServer).Assembly.GetManifestResourceStream("Jj2.HotPotatoHud"))
                using (MemoryStream bundled = new MemoryStream())
                {
                    resource.CopyTo(bundled);
                    Assert(Convert.ToBase64String(module) == Convert.ToBase64String(bundled.ToArray()),
                        "bundled HUD downloads intact through the native whitelisted file transport");
                }
                a.Send(Jj2ServerPackets.Tcp(0x57, new byte[] { 1, 1 }));
                hud = a.Until(0x56);
                Assert(hud[4] == 255 && BitConverter.ToUInt16(hud, 5) == 0,
                    "initialized client immediately receives inactive HUD state");
                server.ExecuteAdmin("announcement-check");
                string announcement = System.Text.Encoding.ASCII.GetString(a.Until(0x40));
                if (!announcement.Contains("announcement-check"))
                    announcement = System.Text.Encoding.ASCII.GetString(a.Until(0x40));
                Assert(announcement.Contains("announcement-check"), "admin announcement missing");
                Move(a, 1, 100); Move(b, 1, 500);
                server.ExecuteAdmin("/start");
                Assert(server.ModeStatus.Contains("Alpha"), "local admin did not start round");
                do { hud = a.Until(0x56); } while (hud[4] != a.Id);
                int initialTicks = BitConverter.ToUInt16(hud, 5);
                Assert(initialTicks > 980 && initialTicks <= 1050, "round start publishes the holder and current native fuse");
                a.Send(Jj2TcpPackets.CreateChatMessage(a.Id, "/start"));
                string response;
                do { response = System.Text.Encoding.ASCII.GetString(a.Until(0x40)); }
                while (!response.Contains("Unsupported player command"));
                Assert(response.Contains("local-admin only") && server.ModeStatus.Contains("Alpha"), "client obtained round controls");
                byte[] marker = b.ReceiveUdp(2);
                for (int i = 0; i < 30 && (marker[7] & 2) == 0; i++) marker = b.ReceiveUdp(2);
                Assert((marker[6] & 31) == a.Id && (marker[7] & 2) != 0, "holder did not get native Sugar Rush bit");
                Thread.Sleep(1050);
                Move(a, 2, 100); Move(b, 2, 120);
                Wait(delegate { return server.ModeStatus.Contains("Bravo"); }, "real-socket proximity pass failed");
                do { hud = a.Until(0x56); } while (hud[4] != b.Id);
                Assert(BitConverter.ToUInt16(hud, 5) > 0 &&
                    BitConverter.ToUInt16(hud, 5) <= initialTicks - 70,
                    "HUD transfer keeps the original countdown instead of restarting at 15 seconds");
                using (StandaloneServerTests.Client late = new StandaloneServerTests.Client(server.Port, "Late"))
                {
                    byte[] spec = late.Until(0x41);
                    while (spec.Length != 6 || spec[4] != late.Id) spec = late.Until(0x41);
                    Assert(spec[5] == late.Id, "late player was not held in spectator mode");
                    late.Send(Jj2TcpPackets.CreateSpectatorCommand(false));
                    byte[] denied = late.Until(0x41);
                    Assert(denied[5] == late.Id, "late player escaped spectator restriction");
                    late.Send(Jj2ServerPackets.Tcp(0x42, new byte[] { 0x22 }));
                    late.Send(Jj2TcpPackets.CreateChatMessage(late.Id, "/status"));
                    string status;
                    do { status = System.Text.Encoding.ASCII.GetString(late.Until(0x40)); }
                    while (!status.Contains("Hot Potato:"));
                    Assert(server.PlayerCount == 3, "unsupported native spectator control disconnected a client");
                    server.ExecuteAdmin("/reset");
                    Wait(delegate { return server.ModeStatus.Contains("ready"); }, "admin reset did not reset engine");
                    do { hud = a.Until(0x56); } while (hud[4] != 255);
                    Assert(BitConverter.ToUInt16(hud, 5) == 0, "admin reset explicitly clears the native countdown");
                    server.ExecuteAdmin("/start");
                    Assert(server.ModeStatus.Contains("3 remaining"), "new round did not restore waiting players");
                }
                b.Send(Jj2TcpPackets.CreateSpectatorCommand(true));
                Wait(delegate { return server.ModeStatus.StartsWith("Winner: Alpha"); }, "spectating did not remove round participant");
                server.ExecuteAdmin("/not-a-command");
                lock (logs) Assert(logs.Exists(delegate(string s) { return s.StartsWith("Unsupported admin command"); }), "unsupported command returned false success");
                a.Dispose();
                Wait(delegate { return server.PlayerCount == 1; }, "winner did not disconnect");
                using (StandaloneServerTests.Client replacement = new StandaloneServerTests.Client(server.Port, "Replacement"))
                    Assert(server.ModeStatus.StartsWith("Winner: Alpha"), "reused player ID changed the recorded winner");
            }
        }
        using (HostServerForm host = new HostServerForm(directory))
        {
            ComboBox modes = (ComboBox)host.Controls.Find("hostMode", true)[0];
            Assert(modes.SelectedIndex == 0 && modes.Items.Count == 2, "Battle was not default/Hot Potato opt-in missing");
        }
        Console.WriteLine("PASS: " + checks + " Hot Potato deterministic state, native packet, real-socket and mode UI checks");
    }
}
