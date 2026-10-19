using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Jj2
{
    class Jj2ServerOptions
    {
        public string AssetDirectory { get; set; }
        public string LevelPath { get; set; }
        public string ServerName { get; set; }
        public int Port { get; set; }
        public int MaxPlayers { get; set; }

        public void Validate()
        {
            if (String.IsNullOrWhiteSpace(AssetDirectory) || !Path.IsPathRooted(AssetDirectory) ||
                !Directory.Exists(AssetDirectory))
                throw new ArgumentException("Select the folder containing your JJ2 levels and tilesets.");
            if (String.IsNullOrWhiteSpace(LevelPath) || !Path.IsPathRooted(LevelPath) || !File.Exists(LevelPath))
                throw new ArgumentException("Select an existing .j2l level.");
            JjAssetDownload.SafeName(Path.GetFileName(LevelPath), ".j2l");
            if (String.IsNullOrWhiteSpace(ServerName) || ServerName.Length > 30)
                throw new ArgumentException("Enter a server name of 1-30 characters.");
            foreach (char c in ServerName)
                if (c < 32 || c > 126) throw new ArgumentException("Use printable ASCII for the server name.");
            if (Port < 1 || Port > 65535) throw new ArgumentException("Port must be between 1 and 65535.");
            if (MaxPlayers < 2 || MaxPlayers > 31) throw new ArgumentException("Player limit must be between 2 and 31.");
        }
    }

    // Networking sandbox, not an authoritative recreation of JJ2's combat or CTF engine.
    sealed class Jj2StandaloneServer : IDisposable
    {
        sealed class Peer
        {
            public byte Id;
            public TcpClient Tcp;
            public volatile IPEndPoint Udp;
            public byte[] Challenge = new byte[8];
            public volatile byte[] Record;
            public volatile byte[] Position;
            public bool Plus;
            public volatile bool Ready, Spectator;
            public string Name;
            public long LastActivityTicks = DateTime.UtcNow.Ticks;
            public DateTime Accepted = DateTime.UtcNow;
            public int Requests;
            public HashSet<int> Reported = new HashSet<int>();
            public object SendLock = new object();
            public Thread Thread;
        }

        readonly Jj2ServerOptions options;
        readonly Action<string> log;
        readonly object sync = new object();
        readonly Dictionary<byte, Peer> peers = new Dictionary<byte, Peer>();
        readonly Dictionary<string, byte[]> assets = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        readonly List<Thread> workers = new List<Thread>();
        readonly string levelName;
        readonly byte[] level, tileset;
        TcpListener listener;
        UdpClient udp;
        Thread acceptThread, udpThread;
        volatile bool running;
        byte udpSequence;
        DateTime lastPulse;

        public int Port { get { return options.Port; } }
        public bool IsRunning { get { return running; } }
        public bool TracePackets { get; set; }
        public int PlayerCount
        {
            get { lock (sync) { int count = 0; foreach (Peer p in peers.Values) if (p.Record != null) count++; return count; } }
        }

        public Jj2StandaloneServer(Jj2ServerOptions options, Action<string> log)
        {
            options.Validate();
            if (log == null) throw new ArgumentNullException("log");
            this.options = new Jj2ServerOptions { AssetDirectory = Path.GetFullPath(options.AssetDirectory),
                LevelPath = Path.GetFullPath(options.LevelPath), ServerName = options.ServerName,
                Port = options.Port, MaxPlayers = options.MaxPlayers };
            this.log = log;
            levelName = Path.GetFileName(options.LevelPath);
            level = ReadAsset(options.LevelPath, levelName);
            string tilesetName = JjAssetDownload.SafeName(JjLevelMap.ReadTilesetName(level), ".j2t");
            string tilesetPath = JjLevelMap.FindFile(options.AssetDirectory, tilesetName);
            if (tilesetPath == null) throw new FileNotFoundException("Required tileset not found: " + tilesetName);
            tileset = ReadAsset(tilesetPath, tilesetName);
            assets.Add(levelName, level);
            assets.Add(tilesetName, tileset);
        }

        static byte[] ReadAsset(string path, string name)
        {
            FileInfo file = new FileInfo(path);
            if (file.Length < 262 || file.Length > JjAssetDownload.MaxFileSize)
                throw new InvalidDataException("Unsupported asset size: " + name);
            byte[] bytes = File.ReadAllBytes(path);
            if (!JjAssetDownload.ValidFile(bytes, name, 0))
                throw new InvalidDataException("Invalid asset header or checksum: " + name);
            return bytes;
        }

        public void Start()
        {
            if (running) throw new InvalidOperationException("Server is already running.");
            listener = new TcpListener(IPAddress.Any, Port);
            try
            {
                listener.Start(options.MaxPlayers);
                udp = new UdpClient(new IPEndPoint(IPAddress.Any, Port));
                udp.Client.ReceiveTimeout = 100;
                udp.Client.SendTimeout = 1000;
            }
            catch
            {
                listener.Stop();
                if (udp != null) udp.Close();
                throw;
            }
            running = true;
            acceptThread = new Thread(AcceptLoop) { IsBackground = true };
            udpThread = new Thread(UdpLoop) { IsBackground = true };
            acceptThread.Start();
            udpThread.Start();
            log("Standalone TCP/UDP server listening on port " + Port + ": " + options.ServerName);
            log("Map: " + levelName + ". Sandbox: movement, chat, downloads and visual blaster relay.");
            log("No authoritative combat, pickups, CTF scoring, scripts or public-list registration.");
        }

        Peer[] Snapshot()
        {
            lock (sync)
            {
                Peer[] copy = new Peer[peers.Count];
                peers.Values.CopyTo(copy, 0);
                return copy;
            }
        }

        void AcceptLoop()
        {
            try
            {
                while (running)
                {
                    TcpClient tcp = listener.AcceptTcpClient();
                    tcp.NoDelay = true;
                    tcp.ReceiveTimeout = 15000;
                    tcp.SendTimeout = 3000;
                    Peer peer = null;
                    lock (sync)
                    {
                        if (running && peers.Count < options.MaxPlayers)
                            for (byte id = 1; id < 32; id++)
                                if (!peers.ContainsKey(id))
                                {
                                    peer = new Peer { Id = id, Tcp = tcp };
                                    using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(peer.Challenge);
                                    peers.Add(id, peer);
                                    peer.Thread = new Thread(delegate() { ClientLoop(peer); }) { IsBackground = true };
                                    workers.Add(peer.Thread);
                                    peer.Thread.Start();
                                    break;
                                }
                    }
                    if (peer == null)
                    {
                        log("Rejected connection: server full or stopping.");
                        tcp.Close();
                    }
                }
            }
            catch (SocketException ex) { if (running) Fail("TCP listener failed: " + ex.Message); }
            catch (ObjectDisposedException) { if (running) Fail("TCP listener was unexpectedly closed."); }
        }

        void ClientLoop(Peer peer)
        {
            try
            {
                NetworkStream stream = peer.Tcp.GetStream();
                byte[] packet;
                while (running && (packet = Jj2ServerPackets.ReadTcp(stream)) != null)
                {
                    Interlocked.Exchange(ref peer.LastActivityTicks, DateTime.UtcNow.Ticks);
                    HandleTcp(peer, packet);
                }
            }
            catch (InvalidDataException ex) { if (running) log("Client " + peer.Id + ": " + ex.Message); }
            catch (IOException ex) { if (running) log("Client " + peer.Id + ": " + ex.Message); }
            catch (SocketException ex) { if (running) log("Client " + peer.Id + ": " + ex.Message); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException ex) { if (running) log("Client " + peer.Id + ": " + ex.Message); }
            finally
            {
                peer.Tcp.Close();
                if (peer.Record != null)
                {
                    log(peer.Name + " disconnected.");
                    Broadcast(Jj2ServerPackets.Tcp(0x0D, new byte[] { 7, peer.Id, (byte)'2', (byte)'4', 32, 32 }, true), peer);
                }
                // Reserve the ID until the leave notification is sent, so a late
                // disconnect cannot accidentally target a new client reusing it.
                lock (sync) { peers.Remove(peer.Id); workers.Remove(Thread.CurrentThread); }
            }
        }

        void HandleTcp(Peer peer, byte[] packet)
        {
            int p = packet[0] == 0 ? 4 : 2;
            byte opcode = packet[p - 1];
            if (TracePackets) log("TCP 0x" + opcode.ToString("X2") + " (" + packet.Length + " bytes).");
            if (peer.Udp == null && opcode != 0x0F)
                throw new InvalidDataException("A join request must be the first packet.");
            switch (opcode)
            {
                case 0x0F:
                    if (peer.Udp != null || packet.Length - p != 7 || packet[p + 6] != 1)
                        throw new InvalidDataException("Only one player per connection is supported.");
                    int port = packet[p] | packet[p + 1] << 8;
                    if (port == 0) throw new InvalidDataException("Invalid UDP port.");
                    string version = Encoding.ASCII.GetString(packet, p + 2, 4);
                    if (version != "24  " && version != "23  ") throw new InvalidDataException("Unsupported JJ2 version.");
                    peer.Udp = new IPEndPoint(((IPEndPoint)peer.Tcp.Client.RemoteEndPoint).Address, port);
                    using (MemoryStream body = new MemoryStream())
                    using (BinaryWriter writer = new BinaryWriter(body))
                    {
                        writer.Write(peer.Id); writer.Write(peer.Id);
                        byte[] name = Encoding.ASCII.GetBytes(levelName);
                        writer.Write((byte)name.Length); writer.Write(name);
                        writer.Write(BitConverter.ToUInt32(level, 226)); writer.Write(BitConverter.ToUInt32(tileset, 226));
                        writer.Write((byte)2); writer.Write((byte)10);
                        writer.Write(peer.Challenge);
                        writer.Write(new byte[] { 6, 0, 6, 0, 1, 0xCA, 0xB7, 0xDB, 0xF4, 0, 0xD8, 0x43, 0x5A, 0x0E, 1 });
                        Send(peer, Jj2ServerPackets.Tcp(0x10, body.ToArray()));
                    }
                    break;
                case 0x3F:
                    if (packet.Length - p != 6 || peer.Record != null)
                        throw new InvalidDataException("Invalid JJ2+ negotiation.");
                    peer.Plus = true;
                    Send(peer, Jj2ServerPackets.Tcp(0x3F, new byte[] { 6, 0, 6, 0, 0, 0, 0, 0x80, 7 }));
                    break;
                case 0x0E:
                    ReceiveIdentity(peer, packet, p);
                    break;
                case 0x1A:
                    if (peer.Record == null || packet.Length - p != 4 || !Equal(packet, p, peer.Challenge, 0, 4))
                        throw new InvalidDataException("Invalid ready challenge.");
                    peer.Ready = true;
                    peer.Tcp.ReceiveTimeout = 0;
                    // Captured JJ2+ session settings and the end-of-object-state marker
                    // let native clients leave their loading screen and start sending movement.
                    Send(peer, Jj2ServerPackets.Tcp(0x44, new byte[] { 0, 32, 0, 3, 0, 0, 0, 1, 0, 0, 0, 0, 2, 3, 0, 0, 0 }));
                    Send(peer, Jj2ServerPackets.Tcp(0x4C, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0 }));
                    Send(peer, Jj2ServerPackets.Tcp(0x52, new byte[] { 0 }));
                    Send(peer, Jj2ServerPackets.Tcp(0x55, new byte[] { 0x20, 3, 0x58, 2 }));
                    Send(peer, Jj2ServerPackets.Tcp(0x4A, new byte[] { 2, 0, 10, 0, 0, 0, 3 }));
                    Send(peer, Jj2ServerPackets.Tcp(0x18, new byte[] { 0 }));
                    Send(peer, Jj2ServerPackets.Console("Standalone sandbox: no damage, pickups, CTF scoring or scripts."));
                    break;
                case 0x15:
                    if (peer.Record == null || packet.Length - p < 2 || packet[p] != packet.Length - p - 1 ||
                        ++peer.Requests > 4) throw new InvalidDataException("Invalid or excessive file request.");
                    string requested = Encoding.ASCII.GetString(packet, p + 1, packet[p]);
                    byte[] data;
                    if (!assets.TryGetValue(requested, out data)) throw new InvalidDataException("File is not part of this server's map.");
                    SendAsset(peer, requested, data);
                    break;
                case 0x1B:
                    if (!peer.Ready || packet.Length - p < 2 || packet.Length - p > 514)
                        throw new InvalidDataException("Invalid chat packet.");
                    string text = Encoding.ASCII.GetString(packet, p + 2, packet.Length - p - 2).TrimEnd('\0');
                    if (text.StartsWith("/", StringComparison.Ordinal))
                        Send(peer, Jj2ServerPackets.Console("Server commands are not implemented in this sandbox."));
                    else
                    {
                        if (packet[p + 1] != 0)
                        {
                            Send(peer, Jj2ServerPackets.Console("Team chat is not implemented in this sandbox."));
                            break;
                        }
                        byte[] chat = new byte[packet.Length - p];
                        Array.Copy(packet, p, chat, 0, chat.Length); chat[0] = peer.Id;
                        Broadcast(Jj2ServerPackets.Tcp(0x1B, chat), peer);
                        log(peer.Name + ": " + text);
                    }
                    break;
                case 0x42:
                    if (!peer.Ready || packet.Length - p != 1 || (packet[p] != 0x20 && packet[p] != 0x21))
                        throw new InvalidDataException("Invalid spectator request.");
                    peer.Spectator = packet[p] == 0x21;
                    peer.Position = null;
                    Broadcast(Jj2ServerPackets.Tcp(0x41,
                        new byte[] { 1, 0, peer.Id, peer.Spectator ? peer.Id : (byte)0xFE }), null);
                    break;
                default:
                    ReportUnsupported(peer, opcode, "TCP");
                    break;
            }
        }

        void ReceiveIdentity(Peer peer, byte[] packet, int p)
        {
            if (!peer.Plus || peer.Record != null || packet.Length - p < 15 ||
                packet[p] != 1 || packet[p + 1] != peer.Id || packet[p + 2] > 2)
                throw new InvalidDataException("JJ2+ with a single Jazz, Spaz or Lori player is required.");
            int nameStart = -1;
            for (int i = p + 8; i + 3 < packet.Length; i++)
                if (packet[i] == 0x11 && packet[i + 1] == peer.Id && packet[i + 2] == 10 && packet[i + 3] == 13)
                {
                    nameStart = i + 4;
                    while (nameStart < packet.Length && packet[nameStart] == 0) nameStart++;
                    break;
                }
            if (nameStart < 0 || nameStart >= packet.Length - 1 || packet[packet.Length - 1] != 0 ||
                packet.Length - nameStart > 33)
                throw new InvalidDataException("Invalid player name.");
            peer.Name = Encoding.ASCII.GetString(packet, nameStart, packet.Length - nameStart - 1);
            foreach (char c in peer.Name)
                if (c < 32 || c > 126) throw new InvalidDataException("Use a printable ASCII player name.");
            byte[] name = Encoding.ASCII.GetBytes(peer.Name);
            byte[] record = new byte[16 + name.Length];
            record[0] = peer.Id; record[1] = peer.Id; record[2] = packet[p + 2]; record[3] = packet[p + 3];
            Array.Copy(packet, p + 4, record, 4, 4);
            record[8] = 0x11; record[9] = peer.Id; record[10] = 10; record[11] = 13;
            Array.Copy(name, 0, record, 15, name.Length);
            lock (sync) peer.Record = record;
            Send(peer, Jj2ServerPackets.Tcp(0x5B, new byte[] { 0, 0 }));
            SendRoster(peer);
            // JJ2+ live joins use the same separate character/team fields as 0x12,
            // with the socket followed by the number of players on that socket.
            byte[] join = new byte[record.Length + 1];
            join[0] = peer.Id; join[1] = 1; join[2] = peer.Id;
            Array.Copy(record, 1, join, 2, record.Length - 1);
            Broadcast(Jj2ServerPackets.Tcp(0x11, join), peer);
            Send(peer, Jj2ServerPackets.Tcp(0x13, new byte[0]));
            // Native JJ2+ waits for the game-initialization terminator (0x51)
            // before it loads the map and returns the TCP ready challenge.
            Send(peer, Jj2ServerPackets.Tcp(0x49, new byte[] { 0, 0xFF, 0xFF, 1, 0, 0 }));
            Send(peer, Jj2ServerPackets.Tcp(0x51, new byte[0]));
            uint spectatorMask = 0;
            foreach (Peer existing in Snapshot()) if (existing.Spectator) spectatorMask |= 1u << existing.Id;
            byte[] spectatorState = new byte[6];
            spectatorState[1] = 32;
            Array.Copy(BitConverter.GetBytes(spectatorMask), 0, spectatorState, 2, 4);
            Send(peer, Jj2ServerPackets.Tcp(0x41, spectatorState));
            log(peer.Name + " joined as player " + peer.Id + ".");
        }

        void SendRoster(Peer peer)
        {
            using (MemoryStream body = new MemoryStream())
            {
                Peer[] all = Snapshot();
                int count = 0;
                foreach (Peer p in all) if (p.Record != null) count++;
                body.WriteByte((byte)count);
                foreach (Peer p in all) if (p.Record != null) body.Write(p.Record, 0, p.Record.Length);
                Send(peer, Jj2ServerPackets.Tcp(0x12, body.ToArray()));
            }
        }

        void SendAsset(Peer peer, string name, byte[] data)
        {
            using (MemoryStream body = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(body))
            {
                writer.Write(0u); writer.Write(data.Length);
                byte[] filename = Encoding.ASCII.GetBytes(name);
                writer.Write((byte)filename.Length); writer.Write(filename);
                Send(peer, Jj2ServerPackets.Tcp(0x14, body.ToArray()));
            }
            uint piece = 1;
            for (int offset = 0; offset < data.Length; offset += 8192)
            {
                int count = Math.Min(8192, data.Length - offset);
                byte[] body = new byte[count + 4];
                Array.Copy(BitConverter.GetBytes(piece++), body, 4);
                Array.Copy(data, offset, body, 4, count);
                Send(peer, Jj2ServerPackets.Tcp(0x14, body));
            }
            log("Sent " + name + " to " + peer.Name + ".");
        }

        static bool Equal(byte[] a, int aOffset, byte[] b, int bOffset, int count)
        {
            if (a.Length - aOffset < count || b.Length - bOffset < count) return false;
            for (int i = 0; i < count; i++) if (a[aOffset + i] != b[bOffset + i]) return false;
            return true;
        }

        void UdpLoop()
        {
            try
            {
                while (running)
                {
                    try
                    {
                        IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, 0);
                        byte[] packet = udp.Receive(ref endpoint);
                        if (packet.Length <= 2048 && Jj2ServerPackets.ValidUdp(packet)) HandleUdp(packet, endpoint);
                    }
                    catch (SocketException ex)
                    {
                        if (ex.SocketErrorCode != SocketError.TimedOut && ex.SocketErrorCode != SocketError.ConnectionReset)
                            throw;
                    }
                    if ((DateTime.UtcNow - lastPulse).TotalMilliseconds >= 250) Pulse();
                }
            }
            catch (SocketException ex) { if (running) Fail("UDP listener failed: " + ex.Message); }
            catch (ObjectDisposedException) { if (running) Fail("UDP listener was unexpectedly closed."); }
        }

        void HandleUdp(byte[] packet, IPEndPoint endpoint)
        {
            if (TracePackets && packet[2] != 1)
                log("UDP " + BitConverter.ToString(packet, 0, Math.Min(packet.Length, 32)));
            if (packet[2] == 5 && packet.Length == 4)
            {
                byte[] name = Encoding.ASCII.GetBytes(options.ServerName);
                byte[] reply = new byte[20 + name.Length];
                reply[2] = 6; reply[3] = packet[3]; reply[4] = 2;
                Array.Copy(Encoding.ASCII.GetBytes("24  "), 0, reply, 8, 4);
                reply[12] = (byte)PlayerCount; reply[13] = 1; reply[14] = 2;
                reply[15] = (byte)options.MaxPlayers; reply[16] = (byte)name.Length;
                Array.Copy(name, 0, reply, 17, name.Length);
                reply[reply.Length - 1] = 0xC0;
                jj2udpClient.CalculateUdpChecksum(reply);
                udp.Send(reply, reply.Length, endpoint);
                return;
            }
            if (packet[2] == 3 && packet.Length <= 8)
            {
                byte[] reply = (byte[])packet.Clone();
                reply[2] = 4;
                jj2udpClient.CalculateUdpChecksum(reply);
                udp.Send(reply, reply.Length, endpoint);
                return;
            }
            Peer sender = null;
            foreach (Peer p in Snapshot())
            {
                if (p.Udp == null || !p.Udp.Address.Equals(endpoint.Address)) continue;
                if (p.Udp.Port == endpoint.Port) { sender = p; break; }
                int token = packet.Length == 8 ? 4 : packet.Length == 9 ? 5 : -1;
                if (packet[2] == 9 && token >= 0 && Equal(packet, token, p.Challenge, 4, 4))
                {
                    p.Udp = endpoint; sender = p; break;
                }
            }
            if (sender == null || sender.Record == null) return;
            Interlocked.Exchange(ref sender.LastActivityTicks, DateTime.UtcNow.Ticks);
            if (packet[2] == 9) return;
            if (!sender.Ready || sender.Spectator) return;
            if (packet[2] == 1)
            {
                if (packet.Length != 17 || packet[4] != 1) { ReportUnsupported(sender, 1, "UDP movement format"); return; }
                byte[] state = Jj2ServerPackets.Position(packet, sender.Id);
                state[3] = ++udpSequence;
                jj2udpClient.CalculateUdpChecksum(state);
                sender.Position = state;
                foreach (Peer peer in Snapshot())
                    if (peer != sender && peer.Ready) SendUdp(peer, state);
            }
            else if (packet[2] == 8 && packet.Length == 25 && BitConverter.ToUInt32(packet, 4) == 16 &&
                packet[8] == 0x22 && ((packet[9] | packet[10] << 8) >> 10) == sender.Id)
            {
                byte[] relay = (byte[])packet.Clone();
                relay[2] = 7;
                relay[3] = ++udpSequence;
                jj2udpClient.CalculateUdpChecksum(relay);
                foreach (Peer peer in Snapshot()) if (peer != sender && peer.Ready) SendUdp(peer, relay);
            }
            else ReportUnsupported(sender, packet[2] + 256, "UDP");
        }

        void Pulse()
        {
            lastPulse = DateTime.UtcNow;
            foreach (Peer peer in Snapshot())
            {
                if ((!peer.Ready && (lastPulse - peer.Accepted).TotalSeconds > 30) ||
                    (lastPulse - new DateTime(Interlocked.Read(ref peer.LastActivityTicks))).TotalSeconds > 60)
                {
                    log("Client " + peer.Id + " timed out."); peer.Tcp.Close(); continue;
                }
                if (peer.Record == null) continue;
                byte[] body = new byte[5];
                Array.Copy(peer.Challenge, 4, body, 1, 4);
                SendUdp(peer, Jj2ServerPackets.Udp(9, ++udpSequence, body));
            }
        }

        void ReportUnsupported(Peer peer, int opcode, string transport)
        {
            lock (peer.Reported)
                if (!peer.Reported.Add(opcode)) return;
            log("Player " + peer.Id + ": unsupported " + transport + " opcode/format 0x" + (opcode & 255).ToString("X2") + ".");
            if (peer.Ready) TrySend(peer, Jj2ServerPackets.Console("Some gameplay packets are unsupported by this standalone sandbox."));
        }

        void Send(Peer peer, byte[] packet)
        {
            lock (peer.SendLock)
            {
                NetworkStream stream = peer.Tcp.GetStream();
                stream.Write(packet, 0, packet.Length);
            }
        }

        void TrySend(Peer peer, byte[] packet)
        {
            try { Send(peer, packet); }
            catch (IOException ex) { if (running) log("Send to player " + peer.Id + " failed: " + ex.Message); peer.Tcp.Close(); }
            catch (SocketException ex) { if (running) log("Send to player " + peer.Id + " failed: " + ex.Message); peer.Tcp.Close(); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException ex) { if (running) log("Send to player " + peer.Id + " failed: " + ex.Message); peer.Tcp.Close(); }
        }

        void Broadcast(byte[] packet, Peer except)
        {
            foreach (Peer peer in Snapshot()) if (peer != except && peer.Record != null) TrySend(peer, packet);
        }

        void SendUdp(Peer peer, byte[] packet)
        {
            if (peer.Udp == null) return;
            try { udp.Send(packet, packet.Length, peer.Udp); }
            catch (SocketException ex) { if (running) log("UDP send failed: " + ex.Message); }
            catch (ObjectDisposedException) { }
        }

        void Fail(string error)
        {
            log(error);
            Stop();
        }

        public void Stop()
        {
            running = false;
            if (listener != null) listener.Stop();
            if (udp != null) udp.Close();
            Peer[] all = Snapshot();
            foreach (Peer peer in all) peer.Tcp.Close();
            Join(acceptThread); Join(udpThread);
            Thread[] threads;
            lock (sync) threads = workers.ToArray();
            foreach (Thread thread in threads) Join(thread);
        }

        static void Join(Thread thread)
        {
            if (thread != null && thread != Thread.CurrentThread) thread.Join(2000);
        }

        public void Dispose() { Stop(); }
    }
}
