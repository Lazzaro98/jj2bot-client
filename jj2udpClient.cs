using System;
using System.Net;
using System.Net.Sockets;
using System.Windows.Forms;

namespace Jj2
{
    /// <summary>
    /// Handles the UDP side of a JJ2 client connection.
    /// </summary>
    class jj2udpClient
    {
        #region Connection state

        // UDP packet templates captured from the JJ2 protocol.
        private readonly byte[] playerStatus = { 0x3D, 0xA7, 0x01, 0x00, 0x01, 0xB5, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00, 0x1E, 0x00, 0x00, 0x00 };

        private readonly UdpClient jj2udpSocket;
        private readonly jj2tcpClient tcpClient;
        private readonly byte character;
        private System.Timers.Timer statusTimer;
        private System.Timers.Timer readyCheckTimer;
        private byte udpc;
        private readonly byte udpPacketCount = 0x03;

        #endregion

        #region Connection and outgoing packets

        /// <summary>
        /// Binds a local UDP port, connects it to the server, and starts the paired TCP client.
        /// </summary>
        public jj2udpClient(IPAddress ip, int tcpport, int udpport, string name, RichTextBox chatLog, RichTextBox playerList, RichTextBox sender, byte character, byte team, byte[] fur, string serverPassword = null)
        {
            if (character > 2) throw new ArgumentOutOfRangeException("character");
            this.character = character;
            jj2udpSocket = new UdpClient(udpport);
            jj2udpSocket.Connect(ip, tcpport);
            IPEndPoint localEndPoint = (IPEndPoint)jj2udpSocket.Client.LocalEndPoint;
            tcpClient = new jj2tcpClient(ip, tcpport, (ushort)localEndPoint.Port, SendPacket, name, chatLog, playerList, sender, character, team, fur, serverPassword);
            StartListening();

            // Some plus servers never send us an inbound UDP packet (e.g. a quiet/idle server with
            // no other active players), so the "start gameplay on first inbound 0x02" trigger never
            // fires and we never send anything back ourselves. That silence eventually gets us
            // disconnected as timed out. This timer proactively starts our own gameplay/heartbeat
            // loop as soon as we're ready, independent of any inbound traffic.
            readyCheckTimer = new System.Timers.Timer(250);
            readyCheckTimer.Elapsed += readyCheck_Tick;
            readyCheckTimer.Start();

            tcpClient.Join();
        }

        /// <summary>
        /// Polls for readiness and starts the gameplay loop as soon as we're ready, so we don't
        /// rely solely on an inbound UDP packet to bootstrap our own outbound traffic.
        /// </summary>
        void readyCheck_Tick(object sender, EventArgs e)
        {
            if (!tcpClient.IsReadyForChat())
            {
                return;
            }

            readyCheckTimer.Stop();

            if (tcpClient.IsPlusServer())
            {
                StartGameplay();
            }
            else
            {
                // Vanilla servers otherwise only get their status heartbeat started reactively,
                // from an inbound 0x04/0x06 UDP packet - which some servers never send first.
                StartStatusTimer();
            }
        }

        /// <summary>
        /// Convenience overload keeping the previous defaults (Spaz, blue team, default fur).
        /// </summary>
        public jj2udpClient(IPAddress ip, int tcpport, int udpport, string name, RichTextBox chatLog, RichTextBox playerList, RichTextBox sender)
            : this(ip, tcpport, udpport, name, chatLog, playerList, sender, 0x01, 0x00, null)
        {
        }

        /// <summary>
        /// Starts receiving the next UDP datagram.
        /// </summary>
        public void StartListening()
        {
            jj2udpSocket.BeginReceive(new AsyncCallback(OnReceive), null);
        }

        /// <summary>
        /// Sends a UDP datagram to the JJ2 server.
        /// </summary>
        public void SendPacket(byte[] paket)
        {
            jj2udpSocket.Send(paket, paket.Length);
        }

        /// <summary>
        /// Sends the current player-status packet with the next UDP sequence number.
        /// </summary>
        public void SendPlayerStatus()
        {
            playerStatus[playerStatus.Length - 3] = (byte)((playerStatus[playerStatus.Length - 3] + 1) % 5);
            playerStatus[5] = playerStatus[5] == 0xB5 ? (byte)0xB6 : (byte)0xB5;
            CalculateUdpChecksum(playerStatus);
            SendPacket(playerStatus);
        }

        /// <summary>
        /// Sends the two UDP keep-alive packets required after the TCP handshake.
        /// </summary>
        public void SendKeepAlive()
        {
            byte[] keepAlive1 = { 0x76, 0x81, 0x09, udpc };
            udpc = Convert.ToByte(((udpc + 1) % 255));
            byte[] keepAlive2 = { 0x00, 0x00, 0x09, udpc, 0x00, 0x00, 0x00, 0x00 };
            udpc = Convert.ToByte(((udpc + 1) % 255));
            for (int i = 0; i < 4; i++)
            {
                keepAlive2[4 + i] = tcpClient.magicNumbers[i];
            }
            CalculateUdpChecksum(keepAlive2);
            SendPacket(keepAlive1);
            SendPacket(keepAlive2);
        }

        /// <summary>
        /// Starts the periodic player-status timer.
        /// </summary>
        public void StartStatusTimer()
        {
            if (tcpClient.IsPlusServer() || !tcpClient.IsReadyForChat())
            {
                return;
            }

            if (statusTimer == null)
            {
                statusTimer = new System.Timers.Timer();
                statusTimer.Elapsed += timer_Tick;
                statusTimer.Interval = 125;
            }

            statusTimer.Start();
        }

        #region Gameplay (movement) packets

        /// <summary>
        /// Folder with Jazz2.exe; levels and tilesets are read from it (and its cache subfolder).
        /// </summary>
        public static string GameDirectory = @"C:\games\Jazz2+";

        /// <summary>
        /// Last error while preparing gameplay (level load), for diagnostics.
        /// </summary>
        public string LastGameplayError;
        private System.Threading.Thread gameplayThread;
        private volatile bool gameplayRunning;
        private readonly object gameplayLock = new object();
        private byte gpSeqA, gpSeqB;
        private int moveX, moveY;
        private bool jumpHeld, fireHeld;
        private JjLevelMap levelMap;
        private string loadedLevelName;
        private int loadedMapRevision;
        private JjBotBody body;
        private byte lastAnim;
        private int animTick;
        private int fireCooldown;
        private ushort shotCounter;
        private readonly Random spawnRandom = new Random();
        private readonly int[] deathReceivedAt = new int[256];
        private readonly bool[] deathReceived = new bool[256];

        /// <summary>
        /// The loaded level collision map, or null if the level file was not found.
        /// </summary>
        public JjLevelMap Level { get { return levelMap; } }

        /// <summary>
        /// Current simulated position and state of the bot rabbit.
        /// </summary>
        public JjBotBody Body { get { return body; } }

        /// <summary>
        /// Sets the held controls: dx/dy in -1..1 (dy=1 is down), plus jump and fire buttons.
        /// </summary>
        public void SetControls(int dx, int dy, bool jump, bool fire)
        {
            lock (gameplayLock)
            {
                moveX = Math.Max(-1, Math.Min(1, dx));
                moveY = Math.Max(-1, Math.Min(1, dy));
                jumpHeld = jump;
                fireHeld = fire;
            }
        }

        /// <summary>
        /// Teleports the simulated rabbit to the given pixel position.
        /// </summary>
        public void SetPosition(int x, int y)
        {
            lock (gameplayLock)
            {
                if (body != null)
                {
                    body.ResetPosition(x, y);
                    fireCooldown = 0;
                }
            }
        }

        private void LoadLevelAndSpawn()
        {
            string levelName = tcpClient.serv.levelName;
            loadedLevelName = levelName;
            loadedMapRevision = tcpClient.MapRevision;
            if (!string.IsNullOrEmpty(levelName))
            {
                JjLevelMap prepared = tcpClient.PreparedLevel;
                levelMap = prepared != null && string.Equals(System.IO.Path.GetFileName(prepared.LevelFile),
                    System.IO.Path.GetFileName(levelName), StringComparison.OrdinalIgnoreCase)
                    ? prepared : JjLevelMap.Load(GameDirectory, levelName);
                string message = levelMap != null
                    ? "Level map loaded: " + levelName + " (" + levelMap.WidthTiles + "x" + levelMap.HeightTiles + " tiles)"
                    : "Level map NOT loaded (" + JjLevelMap.LastError + ")";
                System.Threading.ThreadPool.QueueUserWorkItem(delegate { tcpClient.AddMessage(message); });
            }

            lock (gameplayLock)
            {
                body = new JjBotBody(levelMap, 0, 0, character);
                Spawn();
                Array.Clear(deathReceived, 0, deathReceived.Length);
            }
        }

        private void Spawn()
        {
            float x = 2016, y = 998;
            if (levelMap != null)
            {
                // Multiplayer start (31), otherwise Jazz (29), Spaz (30) or Lori (32) start.
                int[] startIds = { 31, 29, 30, 32 };
                foreach (int id in startIds)
                {
                    System.Collections.Generic.List<int[]> starts = levelMap.FindEvents(id);
                    if (starts.Count > 0)
                    {
                        int[] t = starts[spawnRandom.Next(starts.Count)];
                        x = t[0] * 32 + 16;
                        y = t[1] * 32 + 16;
                        break;
                    }
                }
            }

            body.ResetPosition(x, y);
            fireCooldown = 0;
            animTick = 0;
            lastAnim = body.Animation;
        }

        private void HandleServerEvent(byte[] packet)
        {
            // 0x07 batches records: uint32 payload length, event opcode, payload.
            // Validate the whole envelope before applying any state changes.
            int offset = 4;
            while (offset < packet.Length)
            {
                if (packet.Length - offset < 5 ||
                    BitConverter.ToUInt32(packet, offset) > packet.Length - offset - 5)
                {
                    tcpClient.AddMessage("Received malformed server event batch.");
                    return;
                }
                offset += 5 + (int)BitConverter.ToUInt32(packet, offset);
            }
            if (packet.Length < 4) return;
            for (offset = 4; offset < packet.Length;)
            {
                int length = (int)BitConverter.ToUInt32(packet, offset);
                HandleDeathEvent(packet, packet[3], offset + 4, length);
                offset += 5 + length;
            }
        }

        private void HandleDeathEvent(byte[] packet, byte sequence, int offset, int length)
        {
            // Suicide/forced death: 0x0F + two player IDs.
            // Combat hit: 0x0C + object ID, packed health/player, hit type.
            byte opcode = packet[offset];
            if (opcode != 0x0F && opcode != 0x0C) return;
            int payloadLength = opcode == 0x0F ? 2 : 4;
            if (length != payloadLength)
            {
                tcpClient.AddMessage("Received malformed death event.");
                return;
            }
            int victim;
            if (opcode == 0x0C)
            {
                if ((packet[offset + 3] >> 5) != 0) return;
                victim = packet[offset + 3] & 31;
            }
            else
            {
                victim = packet[offset + 1];
                if (victim >= 32)
                {
                    tcpClient.AddMessage("Received death event with invalid player ID.");
                    return;
                }
            }
            if (victim != tcpClient.MyPlayerId || tcpClient.IsSpectating) return;
            lock (gameplayLock)
            {
                if (body == null) return;
                int now = Environment.TickCount;
                if (deathReceived[sequence] && unchecked(now - deathReceivedAt[sequence]) < 2000) return;
                deathReceived[sequence] = true;
                deathReceivedAt[sequence] = now;
                Spawn();
            }
        }

        private readonly System.Collections.Generic.Dictionary<int, RemotePlayer> remotePlayers = new System.Collections.Generic.Dictionary<int, RemotePlayer>();

        /// <summary>
        /// Decodes a server-to-client gameplay state packet (18 bytes) describing another player.
        /// </summary>
        private void ParseRemotePlayer(byte[] p)
        {
            if (p.Length < 16)
            {
                return;
            }

            int key = p[6] & 0x1F;
            int rawX = p[10] | (p[11] << 8);
            RemotePlayer rp = new RemotePlayer();
            rp.Key = key;
            rp.X = rawX & 0x7FFF;
            rp.Y = (p[12] | (p[13] << 8)) & 0x7FFF;
            rp.FacingRight = (rawX & 0x8000) != 0;
            rp.Anim = p[14];
            rp.Updated = Environment.TickCount;
            lock (remotePlayers)
            {
                RemotePlayer old;
                if (remotePlayers.TryGetValue(key, out old))
                {
                    int dt = rp.Updated - old.Updated;
                    if (dt >= 8 && dt <= 1000)
                    {
                        rp.PrevX = old.X;
                        rp.PrevY = old.Y;
                        rp.Interval = dt;
                    }
                    else
                    {
                        rp.PrevX = old.PrevX;
                        rp.PrevY = old.PrevY;
                        rp.Interval = dt < 8 ? old.Interval : 0;
                        if (dt < 8)
                        {
                            rp.Updated = old.Updated;
                        }
                    }
                }

                remotePlayers[key] = rp;
            }
        }

        /// <summary>
        /// Latest known state of every other player seen in gameplay packets.
        /// </summary>
        public RemotePlayer[] GetRemotePlayers()
        {
            lock (remotePlayers)
            {
                RemotePlayer[] result = new RemotePlayer[remotePlayers.Count];
                remotePlayers.Values.CopyTo(result, 0);
                return result;
            }
        }

        public jj2tcpClient Tcp { get { return tcpClient; } }

        /// <summary>
        /// Starts sending gameplay packets at 70 Hz. Safe to call repeatedly.
        /// </summary>
        public void StartGameplay()
        {
            if (!tcpClient.IsReadyForChat()) return;
            lock (gameplayLock)
            {
                if (gameplayRunning)
                {
                    return;
                }

                gameplayRunning = true;
            }

            gameplayThread = new System.Threading.Thread(() =>
            {
                try
                {
                    LoadLevelAndSpawn();
                }
                catch (Exception ex)
                {
                    LastGameplayError = ex.ToString();
                    if (body == null)
                    {
                        body = new JjBotBody(levelMap, 2016, 998, character);
                    }
                }

                GameplayLoop();
            });
            gameplayThread.IsBackground = true;
            gameplayThread.Start();
        }

        /// <summary>
        /// Stops the gameplay packet loop.
        /// </summary>
        public void StopGameplay()
        {
            gameplayRunning = false;
        }

        private void GameplayLoop()
        {
            const double tickMs = 1000.0 / 70.0;
            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            double next = clock.Elapsed.TotalMilliseconds;
            int ticks = 0;
            while (gameplayRunning)
            {
                if (++ticks % 35 == 0)
                {
                    string current = tcpClient.serv.levelName;
                    if (tcpClient.IsReadyForChat() && !string.IsNullOrEmpty(current) &&
                        (!string.Equals(current, loadedLevelName, StringComparison.OrdinalIgnoreCase) ||
                         loadedMapRevision != tcpClient.MapRevision))
                    {
                        try
                        {
                            LoadLevelAndSpawn();
                            lock (remotePlayers)
                            {
                                remotePlayers.Clear();
                            }
                        }
                        catch (Exception ex)
                        {
                            LastGameplayError = ex.ToString();
                        }

                        next = clock.Elapsed.TotalMilliseconds;
                    }
                }

                SendGameplayPacket();
                next += tickMs;
                double wait = next - clock.Elapsed.TotalMilliseconds;
                if (wait > 2)
                {
                    System.Threading.Thread.Sleep((int)(wait - 1));
                }

                while (clock.Elapsed.TotalMilliseconds < next && gameplayRunning)
                {
                    System.Threading.Thread.SpinWait(200);
                }

                if (clock.Elapsed.TotalMilliseconds - next > 200)
                {
                    next = clock.Elapsed.TotalMilliseconds;
                }
            }
        }

        // Number of animation frames and ticks per frame for each observed animation id.
        private int FramesOf(byte anim)
        {
            switch (anim)
            {
                case 61: return character == 0 ? 1 : character == 2 ? 8 : 5;
                case 54: return 8;
                case 60: return 8;
                case 11: return 3;
                case 12: return 8;
                case 2: return 7;
                case 9: return 8;
                case 21: return 1;
                case 25: return character == 2 ? 28 : 8;
                case 39: return 3;
                case 62: return 3;
                case 64: return 7;
                case 7: return 2;
                case 14: return 2;
                case 15: return 2;
                default: return 2;
            }
        }

        private static int TicksPerFrame(byte anim)
        {
            switch (anim)
            {
                case 54: return 4;
                case 60: return 5;
                case 11: return 6;
                case 12: return 3;
                case 2: return 3;
                case 9: return 6;
                case 25: return 3;
                case 39: return 5;
                case 62: return 2;
                case 64: return 3;
                default: return 9;
            }
        }

        /// <summary>
        /// Advances the simulation one tick and sends one 17-byte client gameplay packet (type 0x01).
        /// </summary>
        public void SendGameplayPacket()
        {
            if (!tcpClient.IsReadyForChat()) return;
            bool spectating = tcpClient.IsSpectating;
            byte[] p = new byte[17];
            byte[] shot = null;
            lock (gameplayLock)
            {
                if (body == null)
                {
                    return;
                }

                if (!spectating) body.Step(moveX, moveY, jumpHeld, fireHeld);
                if (fireCooldown > 0) fireCooldown--;
                bool firing = !spectating && fireHeld && body.CanFire;
                if (firing && fireCooldown == 0)
                {
                    shot = Jj2UdpPackets.CreateBlasterShot(++gpSeqA, ++shotCounter, tcpClient.MyPlayerId,
                        (int)Math.Round(body.X), (int)Math.Round(body.Y), body.FacingRight,
                        moveY < 0, body.OnGround && moveY > 0);
                    fireCooldown = 36;
                }
                gpSeqA++;
                gpSeqB++;

                if (body.Animation != lastAnim)
                {
                    lastAnim = body.Animation;
                    animTick = 0;
                }
                else
                {
                    animTick++;
                }

                int x = Math.Max(0, (int)Math.Round(body.X));
                int y = Math.Max(0, (int)Math.Round(body.Y));
                p[2] = 0x01;
                p[3] = gpSeqA;
                p[4] = 0x01;
                p[5] = gpSeqB;
                p[7] = spectating ? (byte)0 : (byte)(((moveY & 0x0F) << 4) | (moveX & 0x0F));
                p[8] = spectating ? (byte)0 : (byte)((jumpHeld ? 0x08 : 0) | (firing ? 0x02 : 0));
                int xv = (x & 0x7FFF) | (body.FacingRight ? 0x8000 : 0);
                p[9] = (byte)(xv & 0xFF);
                p[10] = (byte)(xv >> 8);
                p[11] = (byte)(y & 0xFF);
                p[12] = (byte)(((y >> 8) & 0x7F) | (firing ? 0x80 : 0));
                p[13] = body.Animation;
                p[14] = (byte)((animTick / TicksPerFrame(body.Animation)) % FramesOf(body.Animation));
                p[15] = spectating ? (byte)0 : unchecked((byte)(sbyte)Math.Max(-127, Math.Min(127, (int)(body.VX * 4))));
                p[16] = spectating ? (byte)0 : unchecked((byte)(sbyte)Math.Max(-127, Math.Min(127, (int)(body.VY * 4))));
            }

            CalculateUdpChecksum(p);
            try
            {
                if (shot != null) SendPacket(shot);
                SendPacket(p);
            }
            catch (SocketException ex)
            {
                if (gameplayRunning) GameplaySendFailed(ex);
            }
            catch (ObjectDisposedException ex)
            {
                if (gameplayRunning) GameplaySendFailed(ex);
            }
        }

        private void GameplaySendFailed(Exception error)
        {
            string message = "Gameplay send failed: " + error.Message;
            LastGameplayError = message;
            gameplayRunning = false;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate { tcpClient.AddMessage(message); });
            tcpClient.Disconnect();
        }

        #endregion

        /// <summary>
        /// Sends one status update for each timer interval.
        /// </summary>
        void timer_Tick(object sender, EventArgs e)
        {
            SendPlayerStatus();
        }

        #endregion

        #region Incoming packet processing

        /// <summary>
        /// Reports whether the paired TCP connection is still active.
        /// </summary>
        public bool IsConnected()
        {
            return tcpClient.IsConnected();
        }

        /// <summary>
        /// Reports whether the paired TCP client received its server-assigned player ID.
        /// </summary>
        public bool IsReadyForChat()
        {
            return tcpClient.IsReadyForChat();
        }

        /// <summary>
        /// Sends the current chat input through the paired TCP client.
        /// </summary>
        public void SendMessage()
        {
            tcpClient.SendMessage();
        }

        /// <summary>
        /// Closes the UDP and paired TCP sockets for this connection.
        /// </summary>
        public void Disconnect()
        {
            StopGameplay();
            if (readyCheckTimer != null)
            {
                readyCheckTimer.Stop();
                readyCheckTimer.Dispose();
            }
            if (statusTimer != null)
            {
                statusTimer.Stop();
                statusTimer.Dispose();
                statusTimer = null;
            }

            jj2udpSocket.Close();
            tcpClient.Disconnect();
        }

        /// <summary>
        /// Processes a completed asynchronous UDP receive.
        /// </summary>
        public void OnReceive(IAsyncResult r)
        {
            try
            {
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] receivedData = jj2udpSocket.EndReceive(r, ref remoteEndPoint);
                HandleReceivedPacket(receivedData);
            }
            catch (ObjectDisposedException)
            {
                // The socket was closed by Disconnect(); stop listening.
                return;
            }
            catch (SocketException)
            {
                // A connected UDP socket reports ICMP "port unreachable" as a receive error (WSAECONNRESET).
                // Dropping out here would silently stop answering server pings, so the server would time us out.
            }
            catch (Exception)
            {
                // A malformed packet must not end the receive loop either.
            }

            RestartListening();
        }

        /// <summary>
        /// Re-arms the UDP receive unless the socket was closed.
        /// </summary>
        private void RestartListening()
        {
            try
            {
                StartListening();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
        }

        /// <summary>
        /// Dispatches a received UDP packet by its JJ2 packet identifier.
        /// </summary>
        public void HandleReceivedPacket(byte[] packet)
        {
            if (packet == null || packet.Length < 3)
            {
                return;
            }

            byte packetID = packet[2];
            switch (packetID)
            {
                case 0x07:
                    HandleServerEvent(packet);
                    break;
                case 0x02:
                    ParseRemotePlayer(packet);
                    if (tcpClient.IsPlusServer())
                    {
                        StartGameplay();
                    }
                    break;
                case 0x04:
                    tcpClient.AddMessage("ponged");
                    StartStatusTimer();
                    break;
                case 0x06:
                    if (packet.Length < 17)
                    {
                        return;
                    }

                    int serverNameSize = (int)packet[16];
                    if (serverNameSize > packet.Length - 17)
                    {
                        return;
                    }

                    string qserverName = "";
                    for (int i = 0; i < serverNameSize; i++)
                    {
                        qserverName += Convert.ToChar(packet[17 + i]);
                    }
                    tcpClient.serv.serverName = qserverName;
                    tcpClient.AddMessage("Server name: " + qserverName);
                    StartStatusTimer();
                    break;
                case 0x09:
                    if (packet.Length < 9)
                    {
                        return;
                    }

                    byte[] packet1 =
                    {
                        0x00, 0x00, 0x09, udpPacketCount, 0x00,
                        packet[5], packet[6], packet[7], packet[8]
                    };
                    CalculateUdpChecksum(packet1);
                    SendPacket(packet1);
                    break;
            }
        }

        #endregion

        #region Packet helpers

        /// <summary>
        /// Writes the JJ2 UDP checksum into the first two bytes of a packet.
        /// </summary>
        public static void CalculateUdpChecksum(byte[] buffer)
        {
            int x = 1, y = 1;
            for (int i = 2; i < buffer.Length; ++i)
            {
                x += buffer[i];
                y += x;
            }
            buffer[0] = (byte)(x % 251);
            buffer[1] = (byte)(y % 251);
        }

        #endregion
    }
}