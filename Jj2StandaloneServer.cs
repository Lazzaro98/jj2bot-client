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
        public bool HotPotato { get; set; }

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

    sealed class HostedPlayerInfo
    {
        public byte Id { get; internal set; }
        public string Name { get; internal set; }
        public string State { get; internal set; }
        public int Wins { get; internal set; }
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
            public bool RoundEliminated, RoundWaiting;
            public bool Respawning;
            public bool LastLifeAlive = true, RespawnSawDead;
            public byte CameraTarget;
            public int Wins;
            public volatile bool HudReady;
            public bool HudWarning;
            public double ReadyTime;
            public double PositionTime;
            public byte[] PendingRespawn;
            public double RespawnUntil;
            public string Name;
            public string RequestedName;
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
        readonly object gameSync = new object();
        readonly HotPotatoRound hotPotato;
        readonly Dictionary<byte, Peer> peers = new Dictionary<byte, Peer>();
        readonly Dictionary<string, byte[]> assets = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        readonly List<Thread> workers = new List<Thread>();
        readonly string levelName;
        readonly byte[] level, tileset;
        TcpListener listener;
        UdpClient udp;
        Thread acceptThread, udpThread;
        volatile bool running;
        int udpSequence;
        DateTime lastPulse;
        string winnerName;
        Peer winnerPeer;
        int roundNumber, lastCountdown;
        readonly System.Diagnostics.Stopwatch uptime = new System.Diagnostics.Stopwatch();

        public int Port { get { return options.Port; } }
        public bool IsRunning { get { return running; } }
        public bool TracePackets { get; set; }
        public bool IsHotPotato { get { return options.HotPotato; } }
        double GameTime { get { return uptime.Elapsed.TotalSeconds; } }
        byte NextSequence() { return unchecked((byte)Interlocked.Increment(ref udpSequence)); }
        public string ModeStatus
        {
            get
            {
                if (!IsHotPotato) return "Battle sandbox";
                lock (gameSync)
                {
                    if (hotPotato.Running)
                        return "Hot Potato: " + PlayerName(hotPotato.Holder) + " | " +
                            Math.Max(0, (int)Math.Ceiling(hotPotato.Deadline - GameTime)) + "s | " +
                            hotPotato.RemainingPlayers + " remaining";
                    if (hotPotato.Finished)
                        return hotPotato.Winner.HasValue ? "Winner: " + winnerName + ". Use /start for a new round."
                            : "Round ended without a winner. Use /start.";
                    return "Hot Potato ready. At least 2 players, then /start.";
                }
            }
        }
        public int PlayerCount
        {
            get { lock (sync) { int count = 0; foreach (Peer p in peers.Values) if (p.Record != null) count++; return count; } }
        }

        public HostedPlayerInfo[] GetPlayers()
        {
            lock (gameSync)
            {
                List<HostedPlayerInfo> result = new List<HostedPlayerInfo>();
                foreach (Peer peer in Snapshot())
                {
                    if (peer.Record == null) continue;
                    string state = !peer.Ready ? "Loading" : peer.RoundEliminated ? "Eliminated - next round" :
                        peer.RoundWaiting ? "Waiting for next round" : peer.Spectator ? "Spectating" :
                        peer.Respawning ? (hotPotato.Running && hotPotato.Holder == peer.Id ? "Respawning (potato)" : "Respawning") :
                        hotPotato.Running && hotPotato.Holder == peer.Id ?
                        "HOLDING POTATO" : winnerPeer == peer ? "Winner" : hotPotato.Running ? "Playing" : "Lobby";
                    result.Add(new HostedPlayerInfo { Id = peer.Id, Name = peer.Name, State = state, Wins = peer.Wins });
                }
                result.Sort(delegate(HostedPlayerInfo a, HostedPlayerInfo b) { return a.Id.CompareTo(b.Id); });
                return result.ToArray();
            }
        }

        public Jj2StandaloneServer(Jj2ServerOptions options, Action<string> log, Func<int, int> chooseHolder = null)
        {
            options.Validate();
            if (log == null) throw new ArgumentNullException("log");
            this.options = new Jj2ServerOptions { AssetDirectory = Path.GetFullPath(options.AssetDirectory),
                LevelPath = Path.GetFullPath(options.LevelPath), ServerName = options.ServerName,
                Port = options.Port, MaxPlayers = options.MaxPlayers, HotPotato = options.HotPotato };
            Random random = new Random();
            hotPotato = new HotPotatoRound(chooseHolder ?? random.Next);
            this.log = log;
            levelName = Path.GetFileName(options.LevelPath);
            level = ReadAsset(options.LevelPath, levelName);
            string tilesetName = JjAssetDownload.SafeName(JjLevelMap.ReadTilesetName(level), ".j2t");
            string tilesetPath = JjLevelMap.FindFile(options.AssetDirectory, tilesetName);
            if (tilesetPath == null) throw new FileNotFoundException("Required tileset not found: " + tilesetName);
            tileset = ReadAsset(tilesetPath, tilesetName);
            assets.Add(levelName, level);
            assets.Add(tilesetName, tileset);
            if (IsHotPotato)
            {
                using (Stream resource = typeof(Jj2StandaloneServer).Assembly.GetManifestResourceStream("Jj2.HotPotatoHud"))
                {
                    if (resource == null) throw new InvalidDataException("The bundled Hot Potato HUD module is missing.");
                    using (MemoryStream script = new MemoryStream())
                    {
                        resource.CopyTo(script);
                        assets.Add(Jj2ServerPackets.HotPotatoModule, script.ToArray());
                    }
                }
            }
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
            uptime.Restart();
            acceptThread = new Thread(AcceptLoop) { IsBackground = true };
            udpThread = new Thread(UdpLoop) { IsBackground = true };
            acceptThread.Start();
            udpThread.Start();
            log("Standalone TCP/UDP server listening on port " + Port + ": " + options.ServerName);
            log("Map: " + levelName + ". Sandbox: movement, chat, downloads and visual blaster relay.");
            log("No authoritative combat, pickups, CTF scoring, arbitrary map scripts or public-list registration.");
            if (IsHotPotato) log("Bundled Hot Potato HUD downloads automatically to native JJ2+ clients.");
            log(ModeStatus);
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
                    if (running) Announce("[Leave] " + peer.Name + " left the server.");
                    Broadcast(Jj2ServerPackets.Tcp(0x0D, new byte[] { 7, peer.Id, (byte)'2', (byte)'4', 32, 32 }, true), peer);
                }
                // Reserve the ID until the leave notification is sent, so a late
                // disconnect cannot accidentally target a new client reusing it.
                lock (gameSync)
                {
                    peer.Ready = false;
                    peer.Position = null;
                    if (running && IsHotPotato)
                    {
                        List<HotPotatoPlayer> remaining = RoundPlayers(false);
                        remaining.RemoveAll(delegate(HotPotatoPlayer player) { return player.Id == peer.Id; });
                        ApplyChanges(hotPotato.Step(GameTime, remaining));
                    }
                    lock (sync) { peers.Remove(peer.Id); workers.Remove(Thread.CurrentThread); }
                    RetargetSpectators();
                }
                if (running)
                {
                    BroadcastClientStates();
                }
            }
        }

        void HandleTcp(Peer peer, byte[] packet)
        {
            int p = packet[0] == 0 ? 4 : 2;
            byte opcode = packet[p - 1];
            if (TracePackets) log("TCP " + BitConverter.ToString(packet, 0, Math.Min(packet.Length, 64)));
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
                    if (!Jj2ServerPackets.SupportsVersion(version)) throw new InvalidDataException("Unsupported JJ2 version.");
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
                        writer.Write(new byte[] { 6, 0, 6, 0, 1, 0xCA, 0xB7, 0xDB, 0xF4 });
                        writer.Write(IsHotPotato ? new byte[] { 2, 1, 0, 0, 0, 1 }
                            : new byte[] { 0, 0xD8, 0x43, 0x5A, 0x0E, 1 });
                        Send(peer, Jj2ServerPackets.Tcp(0x10, body.ToArray()));
                    }
                    break;
                case 0x3F:
                    if (packet.Length - p != 6 || peer.Record != null)
                        throw new InvalidDataException("Invalid JJ2+ negotiation.");
                    peer.Plus = true;
                    Send(peer, Jj2ServerPackets.Tcp(0x3F, new byte[] { 6, 0, 6, 0, 0, 0, 0,
                        IsHotPotato ? (byte)0x81 : (byte)0x80, IsHotPotato ? (byte)6 : (byte)7 }));
                    if (IsHotPotato)
                        Send(peer, Jj2ServerPackets.ScriptList(peer.Challenge, assets[Jj2ServerPackets.HotPotatoModule]));
                    break;
                case 0x57:
                    if (!IsHotPotato || packet.Length - p < 2 || packet[p] != 1 || packet[p + 1] != 1)
                        throw new InvalidDataException("Invalid Hot Potato HUD acknowledgement.");
                    lock (gameSync)
                    {
                        if (packet.Length - p == 2)
                        {
                            peer.HudReady = true;
                            log("[HUD] Initialized for " + (peer.Name ?? ("player " + peer.Id)) + ".");
                            SendHud(peer);
                        }
                        else if (packet.Length - p == 4 && peer.HudReady && peer.Ready &&
                            packet[p + 2] == 2 && packet[p + 3] <= 1)
                        {
                            peer.LastLifeAlive = packet[p + 3] != 0;
                            if (!peer.Spectator)
                            {
                                if (packet[p + 3] == 0)
                                {
                                    BeginRespawn(peer);
                                    peer.RespawnSawDead = true;
                                }
                                else CompleteRespawn(peer);
                            }
                        }
                        else throw new InvalidDataException("Invalid player lifecycle message.");
                    }
                    break;
                case 0x0E:
                    ReceiveIdentity(peer, packet, p);
                    break;
                case 0x1A:
                    if (peer.Record == null || packet.Length - p != 4 || !Equal(packet, p, peer.Challenge, 0, 4))
                        throw new InvalidDataException("Invalid ready challenge.");
                    bool firstReady = !peer.Ready;
                    peer.ReadyTime = GameTime;
                    peer.Ready = true;
                    peer.Tcp.ReceiveTimeout = 0;
                    // Captured JJ2+ session settings and the end-of-object-state marker
                    // let native clients leave their loading screen and start sending movement.
                    BroadcastClientStates();
                    Send(peer, Jj2ServerPackets.Tcp(0x4C, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0 }));
                    Send(peer, Jj2ServerPackets.Tcp(0x52, new byte[] { 0 }));
                    Send(peer, Jj2ServerPackets.Tcp(0x55, new byte[] { 0x20, 3, 0x58, 2 }));
                    Send(peer, Jj2ServerPackets.Tcp(0x4A, new byte[] { 2, 0, 10, 0, 0, 0, 3 }));
                    Send(peer, Jj2ServerPackets.Tcp(0x18, new byte[] { 0 }));
                    // The early initialization marker permits map loading; the post-load
                    // acknowledgement is also required before native players can spawn.
                    Send(peer, Jj2ServerPackets.Tcp(0x51, new byte[0]));
                    if (firstReady)
                    {
                        Send(peer, Jj2ServerPackets.Console("[Welcome] You are " + peer.Name + ". Type !help or !rules."));
                        if (peer.Name != peer.RequestedName)
                            Send(peer, Jj2ServerPackets.Console("[Name] Your requested name was adjusted to " + peer.Name + "."));
                    }
                    if (IsHotPotato)
                    {
                        lock (gameSync)
                        {
                            if (hotPotato.Running && !hotPotato.Contains(peer.Id))
                            {
                                peer.RoundWaiting = true;
                                SetSpectator(peer, true);
                            }
                            Send(peer, Jj2ServerPackets.Console(ModeStatus));
                            SendHud(peer);
                        }
                    }
                    if (firstReady) Announce("[Join] " + peer.Name +
                        (peer.RoundWaiting ? " is watching until the next round." : " is ready to play."));
                    break;
                case 0x15:
                    if (packet.Length - p < 2 || packet[p] != packet.Length - p - 1 ||
                        ++peer.Requests > 4) throw new InvalidDataException("Invalid or excessive file request.");
                    string requested = Encoding.ASCII.GetString(packet, p + 1, packet[p]);
                    if (peer.Record == null && (!IsHotPotato || requested != Jj2ServerPackets.HotPotatoModule))
                        throw new InvalidDataException("Only the advertised HUD module may be requested before player identity.");
                    byte[] data;
                    if (!assets.TryGetValue(requested, out data)) throw new InvalidDataException("File is not part of this server's map.");
                    SendAsset(peer, requested, data);
                    break;
                case 0x1B:
                    if (!peer.Ready || packet.Length - p < 2 || packet.Length - p > 514)
                        throw new InvalidDataException("Invalid chat packet.");
                    string text = Encoding.ASCII.GetString(packet, p + 2, packet.Length - p - 2).TrimEnd('\0');
                    if (text.StartsWith("/", StringComparison.Ordinal) || text.StartsWith("!", StringComparison.Ordinal))
                        PlayerCommand(peer, text.Substring(1).Trim());
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
                    if (!peer.Ready || packet.Length - p != 1)
                        throw new InvalidDataException("Invalid spectator request: " + BitConverter.ToString(packet));
                    if (packet[p] == 0xFE && peer.Spectator) break;
                    if (packet[p] < 32 && peer.Spectator)
                    {
                        lock (gameSync) Watch(peer, packet[p]);
                        break;
                    }
                    if (packet[p] != 0x20 && packet[p] != 0x21)
                    {
                        ReportUnsupported(peer, 0x42, "TCP spectator request " + packet[p].ToString("X2"));
                        break;
                    }
                    lock (gameSync)
                    {
                        RequestSpectator(peer, packet[p] == 0x21);
                    }
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
            string requestedName = Encoding.ASCII.GetString(packet, nameStart, packet.Length - nameStart - 1);
            foreach (char c in requestedName)
                if (c < 32 || c > 126) throw new InvalidDataException("Use a printable ASCII player name.");
            byte[] record;
            lock (gameSync)
            {
                peer.RequestedName = requestedName;
                string baseName = requestedName.Replace("|", "").Trim();
                if (baseName.Length == 0) baseName = "Player";
                peer.Name = baseName;
                for (int suffix = 2; NameExists(peer.Name); suffix++)
                {
                    string ending = suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    peer.Name = baseName.Substring(0, Math.Min(baseName.Length, 32 - ending.Length)) + ending;
                }
                byte[] name = Encoding.ASCII.GetBytes(peer.Name);
                record = new byte[16 + name.Length];
                record[0] = peer.Id; record[1] = peer.Id; record[2] = packet[p + 2]; record[3] = packet[p + 3];
                Array.Copy(packet, p + 4, record, 4, 4);
                record[8] = 0x11; record[9] = peer.Id; record[10] = 10; record[11] = 13;
                Array.Copy(name, 0, record, 15, name.Length);
                lock (sync) peer.Record = record;
            }
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
            log("[Connect] " + peer.Name + " (#" + peer.Id + ") is loading the level.");
        }

        bool NameExists(string name)
        {
            foreach (Peer peer in Snapshot())
                if (peer.Record != null && String.Equals(peer.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        void PlayerCommand(Peer peer, string command)
        {
            string[] parts = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string verb = parts.Length == 0 ? "" : parts[0].ToLowerInvariant();
            lock (gameSync)
            {
                if (verb == "status" && parts.Length == 1) Send(peer, Jj2ServerPackets.Console(ModeStatus));
                else if (verb == "help" && parts.Length == 1)
                    Send(peer, Jj2ServerPackets.Console("!rules | !players | !status | !spec | !play | !watch ID | !respawn. Host starts rounds."));
                else if (verb == "rules" && parts.Length == 1)
                {
                    Send(peer, Jj2ServerPackets.Console(IsHotPotato ?
                        "[Rules] Touch another player to pass. The 15-second fuse NEVER resets on a pass." :
                        "[Rules] Battle sandbox: movement/chat only; authoritative combat is not implemented."));
                    if (IsHotPotato) Send(peer, Jj2ServerPackets.Console(
                        "[Rules] Fuse expiry eliminates you. Normal death respawns you; your fuse keeps ticking."));
                    if (IsHotPotato) Send(peer, Jj2ServerPackets.Console("[Rules] Last survivor wins. Late joins wait for the host's next round."));
                }
                else if (verb == "players" && parts.Length == 1)
                    foreach (HostedPlayerInfo player in GetPlayers())
                        Send(peer, Jj2ServerPackets.Console("#" + player.Id + " " + player.Name + " - " + player.State + " | wins: " + player.Wins));
                else if ((verb == "spec" || verb == "spectate") && parts.Length == 1) RequestSpectator(peer, true);
                else if (verb == "play" && parts.Length == 1) RequestSpectator(peer, false);
                else if (verb == "respawn" && parts.Length == 1)
                {
                    if (peer.Spectator) Send(peer, Jj2ServerPackets.Console("[Respawn] You are spectating. Use !play between rounds."));
                    else if (peer.Respawning) Send(peer, Jj2ServerPackets.Console("[Respawn] Already respawning; please wait."));
                    else BeginRespawn(peer);
                }
                else if (verb == "watch" && parts.Length == 2)
                {
                    byte target;
                    if (Byte.TryParse(parts[1], out target)) Watch(peer, target);
                    else Send(peer, Jj2ServerPackets.Console("[Watch] Use !players, then !watch ID."));
                }
                else Send(peer, Jj2ServerPackets.Console("Unsupported player command. Try !help. Round controls are local-admin only."));
            }
        }

        void RequestSpectator(Peer peer, bool enabled)
        {
            if (IsHotPotato && !enabled && hotPotato.Running &&
                (peer.RoundEliminated || peer.RoundWaiting || !hotPotato.Contains(peer.Id)))
            {
                Send(peer, Jj2ServerPackets.Spectator(peer.Id, true, peer.CameraTarget));
                Send(peer, Jj2ServerPackets.Console("[Round] Wait for the next Hot Potato round to rejoin."));
                return;
            }
            bool changed = peer.Spectator != enabled;
            peer.RoundEliminated = peer.RoundWaiting = false;
            SetSpectator(peer, enabled);
            if (changed) Announce("[Spectate] " + peer.Name + (enabled ? " is now watching. !play rejoins between rounds." : " is returning to play."));
            UpdateRound();
            RetargetSpectators();
        }

        void Watch(Peer viewer, byte targetId)
        {
            foreach (Peer target in Snapshot())
                if (target.Id == targetId && target != viewer && target.Ready && !target.Spectator && !target.Respawning)
                {
                    if (!viewer.Spectator) RequestSpectator(viewer, true);
                    viewer.CameraTarget = targetId;
                    Broadcast(Jj2ServerPackets.Spectator(viewer.Id, true, targetId), null);
                    Send(viewer, Jj2ServerPackets.Console("[Watch] Following " + target.Name + ". !watch ID changes target."));
                    return;
                }
            Send(viewer, Jj2ServerPackets.Console("[Watch] That player is not available. Use !players."));
        }

        void RetargetSpectators()
        {
            Peer[] all = Snapshot();
            foreach (Peer viewer in all)
            {
                if (!viewer.Ready || !viewer.Spectator) continue;
                byte target = viewer.Id;
                bool currentValid = false;
                foreach (Peer candidate in all)
                {
                    if (!candidate.Ready || candidate.Spectator || candidate.Respawning) continue;
                    if (target == viewer.Id) target = candidate.Id;
                    if (candidate.Id == viewer.CameraTarget) currentValid = true;
                }
                if (!currentValid && viewer.CameraTarget != target)
                {
                    viewer.CameraTarget = target;
                    Broadcast(Jj2ServerPackets.Spectator(viewer.Id, true, target), null);
                }
            }
        }

        void BeginRespawn(Peer peer)
        {
            if (!peer.Ready || peer.Respawning || peer.Spectator) return;
            peer.Respawning = true;
            peer.RespawnSawDead = !peer.LastLifeAlive;
            peer.Position = null;
            peer.PendingRespawn = null;
            byte[] death = Jj2ServerPackets.Respawn(peer.Id, NextSequence());
            foreach (Peer destination in Snapshot()) if (destination.Ready) SendUdp(destination, death);
            Announce("[Respawn] " + peer.Name + " died and is respawning." +
                (hotPotato.Running && hotPotato.Holder == peer.Id ? " The potato fuse keeps ticking!" : ""));
            RetargetSpectators();
        }

        void CompleteRespawn(Peer peer)
        {
            if (!peer.Ready || !peer.Respawning || peer.Spectator || (peer.HudReady && !peer.RespawnSawDead)) return;
            peer.Respawning = false;
            peer.Position = null;
            peer.PositionTime = 0;
            peer.PendingRespawn = null;
            BroadcastPlayerRecord(peer);
            Announce("[Respawn] " + peer.Name + " is back in play.");
            RetargetSpectators();
        }

        void BroadcastPlayerRecord(Peer peer)
        {
            byte[] roster = new byte[peer.Record.Length + 1];
            roster[0] = 1;
            Array.Copy(peer.Record, 0, roster, 1, peer.Record.Length);
            Broadcast(Jj2ServerPackets.Tcp(0x12, roster), null);
        }

        void BroadcastClientStates()
        {
            uint active = 0, downloading = 0, plus = 0;
            foreach (Peer peer in Snapshot())
                if (peer.Record != null)
                {
                    active |= 1u << peer.Id;
                    if (!peer.Ready) downloading |= 1u << peer.Id;
                    if (peer.Plus) plus |= 1u << peer.Id;
                }
            Broadcast(Jj2ServerPackets.ClientStates(active, downloading, plus), null);
        }

        void SetSpectator(Peer peer, bool enabled)
        {
            bool wasSpectator = peer.Spectator;
            if (wasSpectator == enabled)
            {
                Broadcast(Jj2ServerPackets.Spectator(peer.Id, enabled, peer.CameraTarget), null);
                return;
            }
            peer.Spectator = enabled;
            peer.Position = null;
            peer.PendingRespawn = null;
            peer.Respawning = !enabled;
            peer.RespawnSawDead = !peer.LastLifeAlive;
            peer.CameraTarget = peer.Id;
            Broadcast(Jj2ServerPackets.Spectator(peer.Id, enabled, peer.CameraTarget), null);
            if (wasSpectator && !enabled)
            {
                peer.PendingRespawn = Jj2ServerPackets.Respawn(peer.Id, NextSequence());
                peer.RespawnUntil = GameTime + 3;
                SendUdp(peer, peer.PendingRespawn);
                BroadcastPlayerRecord(peer);
            }
            RetargetSpectators();
        }

        string PlayerName(byte? id)
        {
            if (!id.HasValue) return "nobody";
            foreach (Peer peer in Snapshot()) if (peer.Id == id.Value) return peer.Name ?? ("Player " + id);
            return "Player " + id.Value;
        }

        List<HotPotatoPlayer> RoundPlayers(bool includeRestorable)
        {
            List<HotPotatoPlayer> players = new List<HotPotatoPlayer>();
            foreach (Peer peer in Snapshot())
            {
                if (!peer.Ready || peer.Record == null ||
                    (peer.Spectator && !(includeRestorable && (peer.RoundEliminated || peer.RoundWaiting)))) continue;
                byte[] position = peer.Position;
                players.Add(new HotPotatoPlayer { Id = peer.Id, HasPosition = position != null && !peer.Respawning,
                    X = position == null ? 0 : BitConverter.ToUInt16(position, 10) & 0x7FFF,
                    Y = position == null ? 0 : BitConverter.ToUInt16(position, 12) & 0x7FFF,
                    PositionTime = peer.PositionTime });
            }
            return players;
        }

        void Announce(string message)
        {
            log(message);
            Broadcast(Jj2ServerPackets.Console(message), null);
        }

        void ApplyChanges(List<HotPotatoChange> changes)
        {
            foreach (HotPotatoChange change in changes)
            {
                switch (change.Kind)
                {
                    case HotPotatoChangeKind.Holder:
                        RefreshMarkers();
                        if (change.Player.HasValue)
                            Announce("Hot Potato: " + PlayerName(change.Player) + " holds the potato! " +
                                Math.Max(0, (int)Math.Ceiling(hotPotato.Deadline - GameTime)) + " seconds.");
                        break;
                    case HotPotatoChangeKind.Eliminated:
                        foreach (Peer peer in Snapshot())
                            if (peer.Id == change.Player)
                            {
                                peer.RoundEliminated = true;
                                SetSpectator(peer, true);
                                TrySend(peer, Jj2ServerPackets.Console("[Out] Your fuse expired. Watch until the next round; !watch ID changes the camera."));
                            }
                        Announce("Hot Potato: " + PlayerName(change.Player) + " was eliminated!");
                        break;
                    case HotPotatoChangeKind.Winner:
                        winnerName = PlayerName(change.Player);
                        winnerPeer = null;
                        foreach (Peer peer in Snapshot())
                            if (peer.Id == change.Player) { winnerPeer = peer; peer.Wins++; }
                        Announce("[Winner] Hot Potato winner: " + winnerName + "! Host: Start round. Players: !play to rejoin.");
                        break;
                    case HotPotatoChangeKind.Empty:
                        Announce("Hot Potato ended: no players remain. Use /start for a new round.");
                        break;
                }
            }
        }

        void UpdateRound()
        {
            if (!IsHotPotato) return;
            lock (gameSync) ApplyChanges(hotPotato.Step(GameTime, RoundPlayers(false)));
        }

        void RestoreRoundPlayers()
        {
            foreach (Peer peer in Snapshot())
                if (peer.RoundEliminated || peer.RoundWaiting)
                {
                    peer.RoundEliminated = peer.RoundWaiting = false;
                    SetSpectator(peer, false);
                }
        }

        public void ExecuteAdmin(string input)
        {
            if (!running) { log("Server is stopped."); return; }
            string text = (input ?? "").Trim();
            if (text.Length == 0) return;
            if (text.Length > 240) { log("Admin input must be at most 240 characters."); return; }
            foreach (char c in text)
                if (c < 32 || c > 126) { log("Admin input must be printable ASCII."); return; }
            if (!text.StartsWith("/", StringComparison.Ordinal)) { Announce("[Server] " + text); return; }
            lock (gameSync)
            {
                switch (text.ToLowerInvariant())
                {
                    case "/help":
                        log("Admin: text announces to everyone; /status, /help" + (IsHotPotato ? ", /start, /reset." : "."));
                        return;
                    case "/status": log(ModeStatus); return;
                    case "/start":
                        if (!IsHotPotato) { log("Unsupported in Battle mode: select Hot Potato when hosting."); return; }
                        if (hotPotato.Running) { log("A round is already running. Use /reset first."); return; }
                        List<HotPotatoPlayer> eligible = RoundPlayers(true);
                        if (eligible.Count < 2) { log("Hot Potato needs at least two active non-spectator players."); return; }
                        RestoreRoundPlayers();
                        winnerName = null;
                        winnerPeer = null;
                        roundNumber++;
                        lastCountdown = 0;
                        Announce("[Round] Round " + roundNumber + " started! Touch to pass; the fuse never resets on a pass.");
                        ApplyChanges(hotPotato.Start(GameTime, eligible));
                        return;
                    case "/reset":
                        if (!IsHotPotato) { log("Unsupported in Battle mode: select Hot Potato when hosting."); return; }
                        hotPotato.Reset();
                        winnerName = null;
                        winnerPeer = null;
                        lastCountdown = 0;
                        RefreshMarkers();
                        RestoreRoundPlayers();
                        Announce("Hot Potato reset. Use /start when at least two players are ready.");
                        return;
                    default: log("Unsupported admin command. Use /help."); return;
                }
            }
        }

        byte[] MarkPosition(Peer peer, byte[] position)
        {
            byte[] packet = IsHotPotato ? Jj2ServerPackets.SugarRush(position, hotPotato.Holder == peer.Id)
                : (byte[])position.Clone();
            if (peer.Respawning) packet[6] = peer.Id;
            packet[3] = NextSequence();
            jj2udpClient.CalculateUdpChecksum(packet);
            return packet;
        }

        void RefreshMarkers()
        {
            byte[] hud = IsHotPotato
                ? Jj2ServerPackets.HotPotatoHud(hotPotato.Holder, hotPotato.Deadline - GameTime) : null;
            if (hud != null)
                foreach (Peer destination in Snapshot())
                    if (destination.Ready && destination.HudReady) TrySend(destination, hud);
            foreach (Peer source in Snapshot())
            {
                byte[] position = source.Position;
                if (!source.Ready || source.Spectator || position == null) continue;
                byte[] packet = MarkPosition(source, position);
                foreach (Peer destination in Snapshot())
                    if (destination != source && destination.Ready) SendUdp(destination, packet);
            }
        }

        void SendHud(Peer peer)
        {
            if (IsHotPotato && peer.Ready && peer.HudReady)
                TrySend(peer, Jj2ServerPackets.HotPotatoHud(hotPotato.Holder, hotPotato.Deadline - GameTime));
        }

        static bool Spawned(byte[] position)
        {
            return (BitConverter.ToUInt16(position, 10) & 0x7FFF) != 0 ||
                (BitConverter.ToUInt16(position, 12) & 0x7FFF) != 0;
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
            if (packet[2] == 5)
            {
                byte[] reply = Jj2ServerPackets.Discovery(options.ServerName, (byte)PlayerCount,
                    (byte)options.MaxPlayers, (uint)uptime.Elapsed.TotalSeconds);
                udp.Send(reply, reply.Length, endpoint);
                return;
            }
            if (packet[2] == 3)
            {
                byte[] reply = Jj2ServerPackets.Pong(packet);
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
                lock (gameSync)
                {
                    if (!sender.Ready || sender.Spectator) return;
                    sender.Position = state;
                    sender.PositionTime = GameTime;
                    if (Spawned(state) && !sender.HudReady)
                        CompleteRespawn(sender);
                    if (Spawned(state) && !sender.Respawning)
                        sender.PendingRespawn = null;
                    UpdateRound();
                    if (sender.Spectator) return;
                    byte[] marked = MarkPosition(sender, state);
                    foreach (Peer peer in Snapshot())
                        if (peer != sender && peer.Ready) SendUdp(peer, marked);
                }
            }
            else if (packet[2] == 8 && packet.Length == 13 && BitConverter.ToUInt32(packet, 4) == 4 &&
                packet[8] == 0x0C && packet[11] == sender.Id)
            {
                lock (gameSync) BeginRespawn(sender);
            }
            else if (packet[2] == 8 && packet.Length == 11 && BitConverter.ToUInt32(packet, 4) == 2 &&
                packet[8] == 0x0F && packet[9] == sender.Id && packet[10] == sender.Id)
            {
                lock (gameSync) BeginRespawn(sender);
            }
            else if (packet[2] == 8 && packet.Length == 25 && BitConverter.ToUInt32(packet, 4) == 16 &&
                packet[8] == 0x22 && ((packet[9] | packet[10] << 8) >> 10) == sender.Id)
            {
                byte[] relay = (byte[])packet.Clone();
                relay[2] = 7;
                relay[3] = NextSequence();
                jj2udpClient.CalculateUdpChecksum(relay);
                foreach (Peer peer in Snapshot()) if (peer != sender && peer.Ready) SendUdp(peer, relay);
            }
            else ReportUnsupported(sender, packet[2] + 256, "UDP");
        }

        void Pulse()
        {
            lastPulse = DateTime.UtcNow;
            UpdateRound();
            lock (gameSync)
            {
                int seconds = (int)Math.Ceiling(hotPotato.Deadline - GameTime);
                if (IsHotPotato && hotPotato.Running && seconds > 0 && seconds <= 3 && seconds != lastCountdown)
                {
                    lastCountdown = seconds;
                    Announce("[Timer] " + seconds + "s left! Potato: " + PlayerName(hotPotato.Holder));
                }
                if (seconds > 3) lastCountdown = 0;
            }
            if (IsHotPotato) lock (gameSync) RefreshMarkers();
            lock (gameSync)
                foreach (Peer peer in Snapshot())
                    if (peer.PendingRespawn != null)
                    {
                        if (GameTime <= peer.RespawnUntil) SendUdp(peer, peer.PendingRespawn);
                        else
                        {
                            peer.PendingRespawn = null;
                            log("Player " + peer.Id + ": spectator exit respawn was not confirmed by movement.");
                        }
                    }
            foreach (Peer peer in Snapshot())
            {
                if (IsHotPotato && peer.Ready && !peer.HudReady && !peer.HudWarning && GameTime - peer.ReadyTime > 3)
                {
                    peer.HudWarning = true;
                    string warning = "[HUD] Local stars/timer unavailable. Native JJ2+ clients: allow script downloads. !rules for help.";
                    log("Player " + peer.Id + ": " + warning);
                    TrySend(peer, Jj2ServerPackets.Console(warning));
                }
                if ((!peer.Ready && (lastPulse - peer.Accepted).TotalSeconds > 30) ||
                    (lastPulse - new DateTime(Interlocked.Read(ref peer.LastActivityTicks))).TotalSeconds > 60)
                {
                    log("Client " + peer.Id + " timed out."); peer.Tcp.Close(); continue;
                }
                if (peer.Record == null) continue;
                byte[] body = new byte[5];
                Array.Copy(peer.Challenge, 4, body, 1, 4);
                SendUdp(peer, Jj2ServerPackets.Udp(9, NextSequence(), body));
            }
        }

        void ReportUnsupported(Peer peer, int opcode, string transport)
        {
            lock (peer.Reported)
                if (!peer.Reported.Add(opcode)) return;
            log("Player " + peer.Id + ": unsupported " + transport + " opcode/format 0x" + (opcode & 255).ToString("X2") + ".");
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
