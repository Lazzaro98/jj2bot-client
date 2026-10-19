using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Jj2;

class StandaloneServerTests
{
    static int checks;
    static void Assert(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }

    sealed class Client : IDisposable
    {
        public TcpClient Tcp = new TcpClient();
        public UdpClient Udp = new UdpClient(0);
        public byte Id;
        public byte[] Info;
        public Client(int port, string name)
        {
            Udp.Connect(IPAddress.Loopback, port);
            Udp.Client.ReceiveTimeout = 2000;
            Tcp.Connect(IPAddress.Loopback, port);
            Tcp.ReceiveTimeout = 3000;
            Send(Jj2TcpPackets.CreateJoinRequest((ushort)((IPEndPoint)Udp.Client.LocalEndPoint).Port));
            Info = Until(0x10); Id = Info[3];
            Send(Jj2TcpPackets.CreatePlusPacket());
            Send(Jj2TcpPackets.CreatePlayerDetails(Id, name, true));
            Until(0x13);
            byte[] challenge = new byte[4];
            Array.Copy(Info, 15 + Info[4], challenge, 0, 4);
            Send(Jj2TcpPackets.CreatePlusPing(challenge));
            Until(0x40);
        }
        public void Send(byte[] p) { Tcp.GetStream().Write(p, 0, p.Length); }
        public byte[] Until(int opcode)
        {
            for (int i = 0; i < 30; i++)
            {
                byte[] p = Jj2ServerPackets.ReadTcp(Tcp.GetStream(), 65538);
                if (p == null) throw new Exception("Server disconnected unexpectedly.");
                if (p[p[0] == 0 ? 3 : 1] == opcode) return p;
            }
            throw new Exception("Missing TCP opcode " + opcode);
        }
        public byte[] ReceiveUdp(int opcode)
        {
            for (int i = 0; i < 20; i++)
            {
                IPEndPoint ep = null;
                byte[] p = Udp.Receive(ref ep);
                if (p[2] == opcode) return p;
            }
            throw new Exception("Missing UDP opcode " + opcode);
        }
        public void Dispose() { Tcp.Close(); Udp.Close(); }
    }

    static int FreePort()
    {
        for (int i = 0; i < 30; i++)
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            try { using (UdpClient udp = new UdpClient(port)) return port; }
            catch (SocketException) { }
            finally { listener.Stop(); }
        }
        throw new Exception("Unable to choose a free test port.");
    }

    static byte[] Asset(string kind, byte[] info)
    {
        using (MemoryStream file = new MemoryStream())
        {
            byte[] header = new byte[262];
            Array.Copy(Encoding.ASCII.GetBytes(kind), 0, header, 180, 4);
            file.Write(header, 0, header.Length);
            for (int i = 0; i < 4; i++)
            {
                byte[] raw = i == 0 ? info : new byte[0], compressed;
                using (MemoryStream zip = new MemoryStream())
                {
                    zip.WriteByte(0x78); zip.WriteByte(0x9C);
                    using (DeflateStream ds = new DeflateStream(zip, CompressionMode.Compress, true)) ds.Write(raw, 0, raw.Length);
                    zip.Write(new byte[4], 0, 4);
                    compressed = zip.ToArray();
                }
                Array.Copy(BitConverter.GetBytes(compressed.Length), 0, header, 230 + 8 * i, 4);
                Array.Copy(BitConverter.GetBytes(raw.Length), 0, header, 234 + 8 * i, 4);
                file.Write(compressed, 0, compressed.Length);
            }
            byte[] data = file.ToArray();
            Array.Copy(header, data, header.Length);
            Array.Copy(BitConverter.GetBytes(data.Length), 0, data, 222, 4);
            uint crc = 0xFFFFFFFF;
            for (int i = 262; i < data.Length; i++)
            {
                crc ^= data[i];
                for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xEDB88320u);
            }
            Array.Copy(BitConverter.GetBytes(~crc), 0, data, 226, 4);
            return data;
        }
    }

    static byte[] Download(Client client, string name)
    {
        byte[] filename = Encoding.ASCII.GetBytes(name);
        byte[] request = new byte[filename.Length + 1];
        request[0] = (byte)filename.Length;
        Array.Copy(filename, 0, request, 1, filename.Length);
        client.Send(Jj2ServerPackets.Tcp(0x15, request));
        byte[] header = client.Until(0x14);
        int start = header[0] == 0 ? 4 : 2;
        Assert(BitConverter.ToUInt32(header, start) == 0, "Missing file metadata.");
        int size = BitConverter.ToInt32(header, start + 4);
        using (MemoryStream output = new MemoryStream())
        {
            uint piece = 1;
            while (output.Length < size)
            {
                byte[] packet = client.Until(0x14);
                start = packet[0] == 0 ? 4 : 2;
                Assert(BitConverter.ToUInt32(packet, start) == piece++, "Nonconsecutive download piece.");
                output.Write(packet, start + 4, packet.Length - start - 4);
            }
            Assert(output.Length == size, "Incorrect download size.");
            return output.ToArray();
        }
    }

    [STAThread] static void Main(string[] args)
    {
        string directory = Path.Combine(Path.GetTempPath(), "jj2-server-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string levelPath = Path.Combine(directory, "test level.j2l"), tilesetPath = Path.Combine(directory, "test.j2t");
        byte[] info = new byte[83];
        Array.Copy(Encoding.ASCII.GetBytes("test.j2t"), 0, info, 51, 8);
        byte[] level = Asset("LEVL", info), tileset = Asset("TILE", new byte[10000]);
        File.WriteAllBytes(levelPath, level); File.WriteAllBytes(tilesetPath, tileset);
        List<string> logs = new List<string>();
        Action<string> log = delegate(string s) { lock (logs) logs.Add(s); };
        try
        {
            Assert(JjAssetDownload.ValidFile(level, "test level.j2l", 0), "Invalid synthetic level fixture.");
            Assert(JjAssetDownload.ValidFile(tileset, "test.j2t", 0), "Invalid synthetic tileset fixture.");
            byte[] small = Jj2ServerPackets.Tcp(0x18, new byte[1]);
            Assert(small.Length == 3 && small[0] == 3 && small[1] == 0x18, "Normal TCP framing failed.");
            byte[] big = Jj2ServerPackets.Tcp(0x14, new byte[8196]);
            Assert(big[0] == 0 && big[3] == 0x14 && (big[1] | big[2] << 8) == big.Length - 3,
                "Extended TCP framing failed.");
            byte[] query = Jj2ServerPackets.Udp(5, 7, new byte[0]);
            Assert(Jj2ServerPackets.ValidUdp(query), "UDP checksum failed.");
            query[3]++;
            Assert(!Jj2ServerPackets.ValidUdp(query), "Corrupt UDP checksum accepted.");

            Jj2ServerOptions options = new Jj2ServerOptions { AssetDirectory = directory, LevelPath = levelPath,
                Port = FreePort(), MaxPlayers = 2, ServerName = "Sandbox test" };
            using (Jj2StandaloneServer server = new Jj2StandaloneServer(options, log))
            {
                server.Start();
                File.WriteAllBytes(levelPath, new byte[0]);
                Assert(server.IsRunning && server.PlayerCount == 0, "Empty standalone server did not start.");
                using (UdpClient browser = new UdpClient())
                {
                    browser.Connect(IPAddress.Loopback, options.Port);
                    browser.Client.ReceiveTimeout = 2000;
                    query = Jj2ServerPackets.Udp(5, 9, new byte[0]);
                    browser.Send(query, query.Length);
                    IPEndPoint ep = null;
                    byte[] reply = browser.Receive(ref ep);
                    Assert(reply[2] == 6 && reply[3] == 9 && reply[15] == 2 &&
                        Encoding.ASCII.GetString(reply, 17, reply[16]) == "Sandbox test", "Native discovery response failed.");
                }
                using (Client a = new Client(server.Port, "Alpha"))
                using (Client b = new Client(server.Port, "Bravo"))
                {
                    Assert(a.Id != b.Id && a.Id > 0 && b.Id > 0, "Duplicate or invalid player IDs.");
                    Assert(server.PlayerCount == 2, "Player roster count is wrong.");
                    byte[] joined = a.Until(0x11);
                    Assert(joined[2] == b.Id && Encoding.ASCII.GetString(joined).Contains("Bravo"),
                        "Live-join roster notification failed.");
                    using (TcpClient excess = new TcpClient())
                    {
                        excess.ReceiveTimeout = 2000;
                        excess.Connect(IPAddress.Loopback, server.Port);
                        Assert(excess.GetStream().ReadByte() == -1, "Player limit did not reject another connection.");
                    }
                    a.Send(Jj2TcpPackets.CreateChatMessage(b.Id, "hello"));
                    byte[] chat = b.Until(0x1B);
                    int start = chat[0] == 0 ? 4 : 2;
                    Assert(chat[start] == a.Id && Encoding.ASCII.GetString(chat, start + 2, chat.Length - start - 2) == "hello",
                        "Chat relay or server-owned sender identity failed.");
                    a.Send(Jj2TcpPackets.CreateChatMessage(a.Id, "/ready"));
                    Assert(Encoding.ASCII.GetString(a.Until(0x40)).Contains("not implemented"),
                        "Unsupported server command did not report its limitation.");
                    Assert(Convert.ToBase64String(Download(a, "test level.j2l")) == Convert.ToBase64String(level),
                        "Level download differs from the served snapshot.");
                    Assert(Convert.ToBase64String(Download(a, "test.j2t")) == Convert.ToBase64String(tileset),
                        "Tileset download differs from the served snapshot.");
                    byte[] movement = new byte[17];
                    movement[2] = 1; movement[3] = 6; movement[4] = 1;
                    movement[9] = 0x34; movement[10] = 0x92; movement[11] = 0x78; movement[12] = 0x06;
                    jj2udpClient.CalculateUdpChecksum(movement);
                    a.Udp.Send(movement, movement.Length);
                    byte[] state = b.ReceiveUdp(2);
                    Assert(state.Length == 18 && (state[6] & 31) == a.Id && state[10] == 0x34 &&
                        state[11] == 0x92 && state[12] == 0x78, "Position relay failed.");
                    byte[] shot = Jj2UdpPackets.CreateBlasterShot(7, 1, a.Id, 120, 200, true, false, false);
                    a.Udp.Send(shot, shot.Length);
                    byte[] eventPacket = b.ReceiveUdp(7);
                    Assert(eventPacket.Length == 25 && eventPacket[8] == 0x22 &&
                        Jj2ServerPackets.ValidUdp(eventPacket), "Visual blaster relay failed.");
                    a.Send(Jj2TcpPackets.CreateSpectatorCommand(true));
                    byte[] spectate = b.Until(0x41);
                    while (spectate.Length != 6) spectate = b.Until(0x41);
                    Assert(spectate[4] == a.Id && spectate[5] == a.Id, "Spectator broadcast failed.");
                    a.Send(Jj2ServerPackets.Tcp(0x15, new byte[] { 5, (byte)'.', (byte)'.', (byte)'\\', (byte)'x', (byte)'y' }));
                    byte[] left = b.Until(0x0D);
                    Assert(left[(left[0] == 0 ? 4 : 2) + 1] == a.Id,
                        "Unauthorized asset request did not disconnect the requesting player.");
                }
                server.Stop();
                Assert(!server.IsRunning, "Server failed to stop.");
                TcpListener released = new TcpListener(IPAddress.Any, options.Port);
                released.Start(); released.Stop();
                using (UdpClient releasedUdp = new UdpClient(options.Port))
                    Assert(true, "Both server ports should be released after Stop.");
            }
            using (HostServerForm dialog = new HostServerForm(directory))
            {
                Assert(dialog.AcceptButton != null && dialog.CancelButton != null, "Host dialog keyboard actions missing.");
                Assert(dialog.Options == null, "Host dialog accepted options without confirmation.");
            }
            using (Form1 browser = new Form1())
                Assert(browser.Controls.Find("hostServerButton", true).Length == 1, "Browser Host button missing.");
            using (RichTextBox chat = new RichTextBox())
            using (RichTextBox players = new RichTextBox())
            using (RichTextBox input = new RichTextBox())
            {
                jj2tcpClient client = new jj2tcpClient(IPAddress.Loopback, 10053, 0, delegate { },
                    "Test", chat, players, input, 0, 0, null);
                try
                {
                    typeof(jj2tcpClient).GetField("isPlusServer", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(client, true);
                    client.HandleReceivedPacket(new byte[] { 27, 0x11, 2, 1, 2, 1, 1, 16, 24, 32, 40,
                        0x11, 2, 10, 13, 0, 0, 0, 76, 97, 116, 101, 32, 66, 111, 116, 0 });
                    Player[] snapshot = client.GetPlayersSnapshot();
                    Assert(snapshot.Length == 1 && snapshot[0].pName == "Late Bot" &&
                        snapshot[0].pchar == 1 && snapshot[0].pTeam == 1 && snapshot[0].fur[0] == 16,
                        "Captured native JJ2+ live-join character/team fields were decoded incorrectly.");
                }
                finally { client.Disconnect(); }
            }
            lock (logs)
                Assert(logs.Exists(delegate(string s) { return s.Contains("not part of this server"); }),
                    "Unauthorized file request was not reported.");
            Console.WriteLine("PASS: " + checks + " standalone protocol, network, asset and UI checks");
        }
        finally { File.Delete(levelPath); File.Delete(tilesetPath); Directory.Delete(directory); }
        if (args.Length > 0) StandaloneInteropTests.Run(args[0], args.Length > 1 ? args[1] : null);
    }
}
