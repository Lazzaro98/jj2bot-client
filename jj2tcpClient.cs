using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;
namespace Jj2
{
    class jj2tcpClient
    {
        #region Connection state

        /*
         * JJ2 TCP protocol reference
         *
         * Client -> server
         * Join request (0x0F):
         *   [packetLength, 0x0F, udpBind[2], jj2Version[4], playerCount]
         *
         * Player details (0x0E):
         *   [packetLength, 0x0E, playerCount, playerId, teamAndCharacter,
         *    furColor[4], 0x11, playerId, 0x0A, 0x0D, 0x00, 0x00,
         *    playerName..., 0x00]
         *
         * Chat message (0x1B):
         *   [0x00, messageLength, 0x00, 0x1B, playerId, 0x00, text...]
         *
         * Server -> client
         * Server details (0x10):
         *   [packetLength, 0x10, socketId, playerId, levelNameLength,
         *    levelName..., levelCrc[4], tilesetCrc[4], gameMode, maxScore]
         */

        Socket jj2tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        EndPoint server;
        string playerName;
        readonly ushort localUdpPort;
        readonly Action<byte[]> udpPacketSender;
        byte myID = 0x01, mySockID;
        readonly byte[] plusCheck = { 0x00, 0x00, 0x00, 0x00 };
        public byte[] magicNumbers = { 0x00, 0x00, 0x00, 0x00 };
        public Server serv;
        Player[] players = new Player[32];
        RichTextBox chatLog, playerList, sender;
        bool? spec;
        uint spectatorMask;
        bool spectatorBlocked;
        bool? spectatorRequested;
        DateTime spectatorRequestTime;
        readonly object spectatorLock = new object();
        bool isPlusServer;
        bool playerDetailsSent;
        bool hasServerAssignedPlayerId;
        readonly byte myCharacter, myTeam;
        readonly byte[] myFur;
        readonly string serverPassword;
        readonly List<byte> receivedPacketBuffer = new List<byte>();
        readonly object sendLock = new object();
        JjAssetDownload assets;
        volatile bool assetsReady;
        int mapRevision;
        public int MapRevision { get { return Interlocked.CompareExchange(ref mapRevision, 0, 0); } }
        public bool TracePackets { get; set; }
        public JjLevelMap PreparedLevel { get { return assets == null ? null : assets.Level; } }

        private void SendTcp(byte[] packet)
        {
            lock (sendLock)
            {
                int offset = 0;
                while (offset < packet.Length)
                {
                    int sent = jj2tcpSocket.Send(packet, offset, packet.Length - offset, SocketFlags.None);
                    if (sent == 0) throw new SocketException((int)SocketError.ConnectionReset);
                    offset += sent;
                }
            }
        }

        private void PrepareAssets()
        {
            if (assets != null) return;
            assets = new JjAssetDownload(jj2udpClient.GameDirectory, SendTcp,
                delegate(string message) { PostAssetMessage(message, false); },
                FinishLoading, delegate(string error)
                {
                    Disconnect();
                    PostAssetMessage("Map download failed: " + error, true);
                });
            assets.Begin(serv.levelName, serv.levelCrc, serv.tilesetCrc);
        }

        private void PostAssetMessage(string message, bool error)
        {
            // Asset callbacks hold the transfer lock. Do not wait for the UI thread,
            // which may itself be disconnecting and disposing that transfer.
            try
            {
                chatLog.BeginInvoke((MethodInvoker)delegate
                {
                    if (error) AddLog(message + Environment.NewLine, Color.Red);
                    else AddMessage(message);
                });
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void FinishLoading()
        {
            if (isPlusServer)
            {
                SendTcp(Jj2TcpPackets.CreatePlusPing(plusCheck));
                udpPacketSender(Jj2UdpPackets.CreatePlusReadyResponse(magicNumbers));
                Thread.Sleep(100);
                // Preserve the original join request to leave spectator mode. A server
                // may not acknowledge a no-op, so do not block the user's first toggle.
                SendTcp(Jj2TcpPackets.CreateSpectatorCommand(false));
            }
            Interlocked.Increment(ref mapRevision);
            assetsReady = true;
        }

        #endregion

        #region Connection and outgoing packets

        /// <summary>
        /// Initializes the TCP client and starts the JJ2 join sequence.
        /// </summary>
        public jj2tcpClient(IPAddress ip, int port, ushort localUdpPort, Action<byte[]> udpPacketSender, string name, RichTextBox chatLog, RichTextBox players, RichTextBox sender, byte character, byte team, byte[] fur, string serverPassword = null)
        {
            server = new IPEndPoint(ip, port);
            serv = new Server(ip, port);
            this.localUdpPort = localUdpPort;
            this.udpPacketSender = udpPacketSender;
            playerName = name;
            this.chatLog = chatLog;
            this.playerList = players;
            this.sender = sender;
            myCharacter = character;
            myTeam = team;
            myFur = fur;
            this.serverPassword = serverPassword;
            sender.KeyDown += new KeyEventHandler(OnSenderKeyDown);
        }

        /// <summary>
        /// Opens the TCP socket and begins asynchronous packet reception.
        /// </summary>
        public void Connect()
        {
            jj2tcpSocket.Connect(server);
            StartListening();
        }

        /// <summary>
        /// Sends the initial JJ2 TCP join request packet.
        /// </summary>
        public void SendJoinRequest()
        {
            SendTcp(Jj2TcpPackets.CreateJoinRequest(localUdpPort, serverPassword));
            AddLog("Connecting to " + serv.ip + ":" + serv.port + Environment.NewLine, Color.Blue);
        }

        /// <summary>        /// Sends the negotiated JJ2+ extension and local player details after server details arrive.
        /// </summary>
        public void SendPlayerDetails(bool isPlusServer)
        {
            if (isPlusServer)
            {
                udpPacketSender(Jj2UdpPackets.CreatePlusHandshake());
                SendTcp(Jj2TcpPackets.CreatePlusPacket());
            }

            SendPlayerDetailsPacket(isPlusServer);
        }

        /// <summary>
        /// Sends the local player identity after the required JJ2+ negotiation step.
        /// </summary>
        private void SendPlayerDetailsPacket(bool isPlusServer)
        {
            SendTcp(Jj2TcpPackets.CreatePlayerDetails(myID, playerName, isPlusServer, myCharacter, myTeam, myFur));
            playerDetailsSent = true;
        }

        /// <summary>
        /// Executes the TCP connection and JJ2 join handshake in protocol order.
        /// </summary>
        public void Join()
        {
            Connect();
            SendJoinRequest();
        }

        /// <summary>
        /// Starts the next asynchronous TCP receive operation.
        /// </summary>
        public void StartListening()
        {
            byte[] buffer = new byte[500];
            jj2tcpSocket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref server, new AsyncCallback(OnReceive), buffer);
        }

        /// <summary>
        /// Sends the text from the chat input as a JJ2 chat packet.
        /// </summary>
        public void SendMessage()
        {
            if (!hasServerAssignedPlayerId)
            {
                AddLog("Chat is unavailable until the server assigns a player ID." + Environment.NewLine, Color.Red);
                return;
            }

            string msg = sender.Text;
            SendTcp(Jj2TcpPackets.CreateChatMessage(myID, msg));
            Color myC = Color.White;
            if (GetMyTeam() == 1) { myC = Color.Red; }
            else { myC = Color.Blue; }
            chatLog.SelectionFont = new Font("Fatboy Slim", 9, FontStyle.Bold);
            chatLog.SelectionColor = myC;
            AddLog(playerName + ": ", myC);
            AddMessage(msg);
            sender.Clear();
        }

        #endregion

        #region Incoming packet processing

        /// <summary>
        /// Splits a completed TCP receive buffer into JJ2 protocol packets.
        /// </summary>
        private void OnReceive(IAsyncResult aResult)
        {
            try
            {
                int size = jj2tcpSocket.EndReceiveFrom(aResult, ref server);
                if (size == 0)
                {
                    AddLog("The server closed the TCP connection." + Environment.NewLine, Color.Red);
                    SafeInvoke(delegate { sender.Enabled = false; });
                    Disconnect();
                    return;
                }

                byte[] receivedData = (byte[])aResult.AsyncState;
                List<byte[]> completePackets = new List<byte[]>();
                lock (receivedPacketBuffer)
                {
                    for (int i = 0; i < size; i++)
                    {
                        receivedPacketBuffer.Add(receivedData[i]);
                    }

                    while (TryReadPacket(completePackets))
                    {
                    }
                }

                foreach (byte[] packet in completePackets)
                {
                    HandleReceivedPacket(packet);
                }

                if (jj2tcpSocket.Connected)
                {
                    StartListening();
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException exception)
            {
                AddLog("TCP connection error: " + exception.SocketErrorCode + "." + Environment.NewLine, Color.Red);
                SafeInvoke(delegate { sender.Enabled = false; });
                Disconnect();
            }
        }

        /// <summary>
        /// Removes one complete length-prefixed TCP packet from the receive buffer when available.
        /// </summary>
        private bool TryReadPacket(List<byte[]> completePackets)
        {
            if (receivedPacketBuffer.Count == 0)
            {
                return false;
            }

            int packetLength;
            if (receivedPacketBuffer[0] == 0x00)
            {
                if (receivedPacketBuffer.Count < 3)
                {
                    return false;
                }

                packetLength = receivedPacketBuffer[1] | (receivedPacketBuffer[2] << 8);
                packetLength += 3;
            }
            else
            {
                packetLength = receivedPacketBuffer[0];
            }

            if (packetLength < 2)
            {
                receivedPacketBuffer.Clear();
                AddLog("Received an invalid TCP packet length." + Environment.NewLine, Color.Red);
                return false;
            }

            if (receivedPacketBuffer.Count < packetLength)
            {
                return false;
            }

            byte[] packet = receivedPacketBuffer.GetRange(0, packetLength).ToArray();
            receivedPacketBuffer.RemoveRange(0, packetLength);
            completePackets.Add(packet);
            return true;
        }

        /// <summary>
        /// Updates client state and UI for one decoded JJ2 TCP packet.
        /// </summary>
        public void HandleReceivedPacket(byte[] packet)
        {
            int p = GetPacketDataOffset(packet);
            byte packetID = 0x00;
            if (packet.Length >= 2)
            {
                if (packet[0] == 0x00 && packet.Length >= 4)
                {
                    packetID = packet[3];
                }
                else
                {
                    packetID = packet[1];
                }
            }

            switch (packetID)
            {
                case 0x10:
                    if (packet.Length < 5)
                    {
                        AddLog("Received incomplete server details." + Environment.NewLine, Color.Red);
                        break;
                    }

                    mySockID = packet[p++];
                    myID = packet[p++];
                    hasServerAssignedPlayerId = myID != 0xFF;
                    int levelNameLength = packet[p++];
                    if (!HasBytes(packet, p, levelNameLength + 10))
                    {
                        AddLog("Received incomplete server details." + Environment.NewLine, Color.Red);
                        break;
                    }

                    serv.levelName = Encoding.ASCII.GetString(packet, p, levelNameLength);
                    p += levelNameLength;
                    assetsReady = false;
                    if (assets != null) { assets.Dispose(); assets = null; }
                    // Embedded CRC32 of each file, not a CRC over its header.
                    serv.levelCrc = BitConverter.ToUInt32(packet, p);
                    serv.tilesetCrc = BitConverter.ToUInt32(packet, p + 4);
                    p += 8;
                    serv.gameMode = packet[p++];
                    serv.maxScore = packet[p++];
                    isPlusServer = packet.Length > p;
                    int plusCheckOffset = p;
                    int magicNumbersOffset = plusCheckOffset + plusCheck.Length;
                    if (isPlusServer &&
                        packet.Length >= plusCheckOffset + plusCheck.Length &&
                        packet.Length >= magicNumbersOffset + magicNumbers.Length)
                    {
                        Array.Copy(packet, plusCheckOffset, plusCheck, 0, plusCheck.Length);
                        Array.Copy(packet, magicNumbersOffset, magicNumbers, 0, magicNumbers.Length);
                    }

                    if (!playerDetailsSent)
                    {
                        SendPlayerDetails(isPlusServer);
                    }

                    AddLog("Current map: " + serv.levelName + Environment.NewLine, Color.Green);
                    break;

                case 0x12:
                    if (!HasBytes(packet, p, 1))
                    {
                        ReportIncompletePacket(packetID);
                        break;
                    }

                    serv.numOfPlayers = packet[p++];
                    int playerOffset = p;
                    int playerNameOffset = isPlusServer ? 15 : 7;
                    int playerRecordHeaderLength = playerNameOffset + 1;
                    bool playerListComplete = true;
                    for (int playerIndex = 0; playerIndex < serv.numOfPlayers; playerIndex++)
                    {
                        if (!HasBytes(packet, playerOffset, playerRecordHeaderLength))
                        {
                            playerListComplete = false;
                            break;
                        }

                        byte psockID = packet[playerOffset];
                        byte playerID = packet[playerOffset + 1];
                        byte charTeam = packet[playerOffset + 2];

                        // The name is preceded by a "0x11, playerID, 0x0A, 0x0D" sub-record marker, but some
                        // JJ2+ server builds insert one extra (unused) byte after it, shifting the name by one
                        // byte. Locate the marker and skip any non-printable filler instead of a fixed offset,
                        // so the name start lines up correctly regardless of that extra byte.
                        int nameStart = -1;
                        for (int i = playerOffset + 8; i < packet.Length - 1; i++)
                        {
                            if (packet[i] == 0x11 && packet[i + 1] == playerID)
                            {
                                int skip = i + 2;
                                if (skip + 1 < packet.Length && packet[skip] == 0x0A && packet[skip + 1] == 0x0D)
                                {
                                    skip += 2;
                                }
                                while (skip < packet.Length && (packet[skip] < 0x20 || packet[skip] > 0x7E))
                                {
                                    skip++;
                                }
                                nameStart = skip;
                                break;
                            }
                        }
                        if (nameStart < 0)
                        {
                            nameStart = playerOffset + playerNameOffset;
                        }

                        int nameEnd = nameStart;
                        while (nameEnd < packet.Length && packet[nameEnd] != 0x00)
                        {
                            nameEnd++;
                        }

                        if (nameEnd >= packet.Length || psockID >= players.Length || playerID >= players.Length)
                        {
                            playerListComplete = false;
                            break;
                        }

                        string pName = DecodeJj2Text(packet, nameStart, nameEnd).Replace("|", "");

                        Player newp = new Player(pName);
                        newp.playerID = playerID;
                        newp.sockID = psockID;
                        newp.pchar = charTeam & (isPlusServer ? 0x0F : 0x03);
                        newp.pTeam = isPlusServer ? packet[playerOffset + 3] : (charTeam >> 4) & 0x01;
                        newp.fur = new int[]
                        {
                            packet[playerOffset + (isPlusServer ? 4 : 3)],
                            packet[playerOffset + (isPlusServer ? 5 : 4)],
                            packet[playerOffset + (isPlusServer ? 6 : 5)],
                            packet[playerOffset + (isPlusServer ? 7 : 6)]
                        };
                        newp.connected = true;
                        players[playerID] = newp;
                        playerOffset = nameEnd + 1;
                    }

                    if (!playerListComplete)
                    {
                        ReportIncompletePacket(packetID);
                        break;
                    }

                    RefreshPlayerList();
                    break;

                case 0x13:
                    PrepareAssets();
                    break;

                case 0x14:
                    if (assets == null)
                    {
                        AddLog("Unrequested file transfer received." + Environment.NewLine, Color.Red);
                        Disconnect();
                    }
                    else assets.Accept(packet, p);
                    break;

                case 0x3F:
                    if (!HasBytes(packet, p, 8))
                    {
                        ReportIncompletePacket(packetID);
                        break;
                    }

                    //serv.version=packetStr.Substring(p,4);
                    p += 4;//skipping unknown data
                    serv.customMode = packet[p++];
                    serv.startHealth = packet[p++];
                    serv.maxHealth = packet[p++];
                    byte plusByte = packet[p++];
                    serv.plusOnly = ((int)plusByte) & 1;
                    serv.friendlyFire = (plusByte >> 1) & 1;
                    serv.noMovement = (plusByte >> 2) & 1;
                    serv.noBlink = (plusByte >> 3) & 1;
                    break;

                case 0x0D:
                    if (!HasBytes(packet, p, 2))
                    {
                        ReportIncompletePacket(packetID);
                        break;
                    }

                    byte disconnectMessage = packet[p];
                    byte disconnectedSocketId = packet[p + 1];
                    if (disconnectedSocketId == mySockID)
                    {
                        AddLog("You have been disconnected. ( " + GetDisconnectMessage(disconnectMessage) + " )" + Environment.NewLine, Color.Red);
                        SafeInvoke(delegate { sender.Enabled = false; });
                        Disconnect();
                        return;
                    }
                    else
                    {
                        int ppid = GetPlayerIdFromSocketId(disconnectedSocketId);
                        if (ppid < 0 || ppid >= players.Length || !players[ppid].connected)
                        {
                            break;
                        }

                        players[ppid].connected = false;
                        lock (spectatorLock) { spectatorMask &= ~(1u << disconnectedSocketId); }
                        RefreshPlayerList();
                        AddLog(players[ppid].pName + " has left the server. ( " + GetDisconnectMessage(disconnectMessage) + " )" + Environment.NewLine, Color.Green);
                    }
                    break;

                case 0x1B:
                    if (!HasBytes(packet, p, 2))
                    {
                        ReportIncompletePacket(packetID);
                        break;
                    }

                    int pid1 = packet[p];
                    bool isTeamChat = packet[p + 1] != 0x00;
                    int indexer = p + 2;
                    string msg = DecodeJj2Text(packet, indexer, packet.Length);
                    string namePrefix = pid1 < players.Length && players[pid1].connected ? players[pid1].pName : "Unknown player";
                    if (isTeamChat) namePrefix = "[" + namePrefix + "]";
                    if (pid1 < players.Length && players[pid1].connected)
                    {
                        if (players[pid1].pTeam == 1) { AddLog(namePrefix + ": ", Color.Red); }
                        if (players[pid1].pTeam == 0) { AddLog(namePrefix + ": ", Color.Blue); }
                    }
                    else
                    {
                        AddLog(namePrefix + ": ", Color.Gray);
                    }

                    AddMessage(msg);
                    break;

                case 0x40:
                    if (!HasBytes(packet, p, 1))
                    {
                        ReportIncompletePacket(packetID);
                        break;
                    }

                    msg = "";
                    indexer = p + 1;
                    int consoleMsgStart = indexer;
                    while (indexer < packet.Length && packet[indexer] != 0x00) indexer++;
                    msg = DecodeJj2Text(packet, consoleMsgStart, indexer);
                    // Console messages are prefixed with a "§<digit>" JJ2 color code (section sign + digit).
                    if (msg.Length > 1 && msg[0] == '\u00A7') msg = msg.Substring(2);
                    else if (msg.Length > 1 && msg[0] == '2') msg = msg.Substring(1);
                    UpdateSpectatorStatus(msg);
                    AddLog("Console: ", Color.Gray);
                    AddMessage(msg);
                    break;

                case 0x41:
                    // Spectating belongs to a socket, not a player (splitscreen shares a socket).
                    // 00 20 + socket bitmask, or 01 00 + socket ID + camera target.
                    lock (spectatorLock)
                    {
                        if (packet.Length - p == 6 && packet[p] == 0 && packet[p + 1] == 32)
                        {
                            spectatorMask = BitConverter.ToUInt32(packet, p + 2);
                            spec = (spectatorMask & (1u << mySockID)) != 0;
                            if (spectatorRequested == spec) spectatorRequested = null;
                        }
                        else if (packet.Length - p == 4 && packet[p] == 1 && packet[p + 1] == 0 &&
                            packet[p + 2] < 32 &&
                            (packet[p + 3] <= 31 || packet[p + 3] == 0xFE))
                        {
                            int id = packet[p + 2];
                            bool enabled = packet[p + 3] != 0xFE;
                            if (enabled) spectatorMask |= 1u << id;
                            else spectatorMask &= ~(1u << id);
                            if (id == mySockID)
                            {
                                spec = enabled;
                                spectatorRequested = null;
                                if (enabled) spectatorBlocked = false;
                            }
                        }
                    }
                    RefreshPlayerList();
                    break;

                case 0x11:
                    if (!HasBytes(packet, p, 2))
                    {
                        ReportIncompletePacket(packetID);
                        break;
                    }

                    byte ppsockID = packet[p++];
                    byte pnumOfPlayers = packet[p++];
                    int pbr = 0;
                    bool playerJoinComplete = true;
                    while (pbr++ < pnumOfPlayers && playerJoinComplete)
                    { //in case splitscreen joins
                        if (!HasBytes(packet, p, 14))
                        {
                            playerJoinComplete = false;
                            break;
                        }

                        byte pID = packet[p++];
                        byte joinedCharacter, joinedTeam;
                        if (isPlusServer)
                        {
                            joinedCharacter = (byte)(packet[p++] & 0x0F);
                            joinedTeam = packet[p++];
                        }
                        else
                        {
                            p += 2;
                            byte pTeamAndChar = packet[p++];
                            joinedCharacter = (byte)(pTeamAndChar & 3);
                            joinedTeam = (byte)((pTeamAndChar >> 4) & 1);
                        }
                        int[] fur = { packet[p++], packet[p++], packet[p++], packet[p++] };

                        // The name is normally preceded by a fixed 5-byte filler, but (as with the 0x12
                        // player-list packet) some JJ2+ builds insert extra bytes here, shifting the name
                        // and sometimes landing on the terminator -> an empty parsed name. Search for the
                        // "0x11, pID" sub-record marker that always precedes the name, same as case 0x12.
                        int fallbackNameStart = p + (isPlusServer ? 7 : 5);
                        int nameStart = -1;
                        for (int i = p; i < packet.Length - 1; i++)
                        {
                            if (packet[i] == 0x11 && packet[i + 1] == pID)
                            {
                                int skip = i + 2;
                                if (skip + 1 < packet.Length && packet[skip] == 0x0A && packet[skip + 1] == 0x0D)
                                {
                                    skip += 2;
                                }
                                while (skip < packet.Length && (packet[skip] < 0x20 || packet[skip] > 0x7E))
                                {
                                    skip++;
                                }
                                nameStart = skip;
                                break;
                            }
                        }
                        if (nameStart < 0) nameStart = fallbackNameStart;

                        p = nameStart;
                        int nameEnd = p;
                        while (nameEnd < packet.Length && packet[nameEnd] != 0x00)
                        {
                            nameEnd++;
                        }
                        string ppname = DecodeJj2Text(packet, p, nameEnd).Replace("|", "");
                        p = nameEnd;

                        if (p >= packet.Length || pID >= players.Length)
                        {
                            playerJoinComplete = false;
                            break;
                        }

                        p++;
                        if (string.IsNullOrEmpty(ppname)) ppname = "Player" + (pID + 1);
                        Player newp = new Player(ppname);
                        newp.sockID = ppsockID;
                        newp.playerID = pID;
                        newp.pTeam = joinedTeam;
                        newp.pchar = joinedCharacter;
                        newp.fur = fur;
                        newp.connected = true;
                        players[pID] = newp;
                        AddLog(ppname + " has joined the server." + Environment.NewLine, Color.Green);
                        RefreshPlayerList();
                    }//while

                    if (!playerJoinComplete)
                    {
                        ReportIncompletePacket(packetID);
                    }

                    break;

                case 0x16:
                    {
                        // Level change: contains the new level file name (printable ASCII ending in .j2l).
                        string text = Encoding.ASCII.GetString(packet);
                        int ext = text.IndexOf(".j2l", StringComparison.OrdinalIgnoreCase);
                        if (ext > 0)
                        {
                            int start = ext;
                            while (start > 0 && packet[start - 1] >= 0x20 && packet[start - 1] < 0x7F)
                            {
                                start--;
                            }

                            serv.levelName = text.Substring(start, ext + 4 - start);
                            AddLog("Map changed: " + serv.levelName + Environment.NewLine, Color.Green);
                        }
                    }

                    break;

                default:
                    if (TracePackets) LogUnhandledPacket(packetID, packet);
                    break;
            }
        }

        /// <summary>
        /// Optional diagnostics for unhandled TCP packets.
        /// </summary>
        private void LogUnhandledPacket(byte packetID, byte[] packet)
        {
            StringBuilder hex = new StringBuilder();
            for (int i = 0; i < packet.Length; i++)
            {
                hex.Append(packet[i].ToString("X2"));
                hex.Append(' ');
            }
            AddLog("[UNHANDLED 0x" + packetID.ToString("X2") + ", " + packet.Length + "B] " + hex.ToString() + Environment.NewLine, Color.Magenta);
        }

        /// <summary>
        /// Checks whether a packet contains a complete field at the requested offset.
        /// </summary>
        private static bool HasBytes(byte[] packet, int offset, int count)
        {
            return packet != null && offset >= 0 && count >= 0 && offset <= packet.Length - count;
        }

        /// <summary>
        /// Gets the first payload byte after a normal or extended TCP packet header.
        /// </summary>
        private static int GetPacketDataOffset(byte[] packet)
        {
            return packet != null && packet.Length >= 4 && packet[0] == 0x00 ? 4 : 2;
        }

        /// <summary>
        /// Reports a malformed server packet without allowing a receive callback to terminate the process.
        /// </summary>
        private void ReportIncompletePacket(byte packetID)
        {
            AddLog("Ignored incomplete packet 0x" + packetID.ToString("X2") + "." + Environment.NewLine, Color.Red);
        }

        /// <summary>
        /// Sends chat when Enter is pressed in the message input.
        /// </summary>
        void OnSenderKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyData == Keys.Enter)
            {
                SendMessage();
            }
        }

        #endregion

        #region Packet and UI helpers

        /// <summary>
        /// Creates the JJ2+ ping packet from server-provided magic bytes.
        /// </summary>
        /// <summary>
        /// Converts a JJ2 disconnect code into a user-facing explanation.
        /// </summary>
        public static string GetDisconnectMessage(byte msg)
        {
            string message = "Unknown reason.";
            switch (msg)
            {
                case 0x01:
                    message = "Server is full";
                    break;
                case 0x02:
                    message = "Version different";
                    break;
                case 0x04:
                    message = "Error during handshaking";
                    break;
                case 0x05:
                    message = "Feature not supported in shareware";
                    break;
                case 0x06:
                    message = "Error downloading level";
                    break;
                case 0x07:
                    message = "Connection lost";
                    break;
                case 0x08:
                    message = "Winsock error";
                    break;
                case 0x09:
                    message = "Connection timed out";
                    break;
                case 0x0A:
                    message = "Server stopped";
                    break;
                case 0x0B:
                    message = "Kicked off";
                    break;
                case 0x0C:
                    message = "Banned";
                    break;
                case 0x0D:
                    message = "Denied";
                    break;
                case 0x0E:
                    message = "Version of JJ2+ is different";
                    break;
                case 0x0F:
                    message = "Server kicked you for idling";
                    break;
                case 0x10:
                    message = "No downloads allowed";
                    break;
                case 0x11:
                    message = "Unauthorized file request";
                    break;
                case 0x12:
                    message = "No splitscreeners allowed";
                    break;
                default:
                    message = "Unknown error";
                    break;
            }
            return message;
        }

        /// <summary>
        /// Marshals a UI-touching action onto the control's thread if called from a background thread
        /// (e.g. the async TCP receive callback), preventing cross-thread corruption of the chat/player list.
        /// </summary>
        void SafeInvoke(MethodInvoker action)
        {
            if (chatLog.InvokeRequired)
            {
                try { chatLog.Invoke(action); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }
            else
            {
                action();
            }
        }

        /// <summary>
        /// Decodes a slice of raw packet bytes as a JJ2 text string. JJ2+ servers send chat/console text
        /// and player names as UTF-8 (so e.g. the "§" color-code symbol is two bytes). Decoding byte-by-byte
        /// instead (as older code did) mangles any multi-byte character into garbage like "Â§".
        /// </summary>
        static string DecodeJj2Text(byte[] packet, int start, int end)
        {
            int length = end - start;
            if (length <= 0) return "";
            return Encoding.UTF8.GetString(packet, start, length);
        }

        /// <summary>
        /// Appends formatted status text to the chat log.
        /// </summary>
        void AddLog(string msg, Color c)
        {
            SafeInvoke(delegate
            {
                chatLog.SelectionFont = new Font("Fatboy Slim", 9, FontStyle.Bold);
                chatLog.SelectionColor = c;
                chatLog.AppendText(msg);
                chatLog.SelectionColor = Color.White;
            });
        }

        /// <summary>
        /// Appends a chat message and applies JJ2 in-band color markers.
        /// </summary>
        public void AddMessage(string msg)
        {
            SafeInvoke(delegate
            {
                Color[] colors = new Color[7];
                colors[0] = Color.White;
                colors[1] = Color.Chartreuse;
                colors[2] = Color.Red;
                colors[3] = Color.Aqua;
                colors[4] = Color.Yellow;
                colors[5] = Color.DeepPink;
                colors[6] = Color.Gray;
                int p = 0;
                for (int i = 0; i < msg.Length; i++)
                {
                    if (msg[i] == '|') p = (p + 1) % 7;
                    else
                    {
                        chatLog.SelectionColor = colors[p];
                        chatLog.AppendText(msg.Substring(i, 1));//jj2 chat colors :D
                    }
                }
                chatLog.AppendText(Environment.NewLine);
            });
        }

        /// <summary>
        /// Adds one player entry to the player-list control.
        /// </summary>
        void AddPlayer(int i)
        {
            bool spectating = IsPlayerSpectating(i);
            Color C = Color.White;
            if (players[i].pTeam == 1) C = Color.Red;
            if (players[i].pTeam == 0) C = Color.Blue;
            if (spectating) C = Color.Gray;
            playerList.SelectionColor = C;
            playerList.AppendText(players[i].playerID + 1 + ". " + players[i].pName + Environment.NewLine);
        }

        private bool IsPlayerSpectating(int id)
        {
            lock (spectatorLock) { return (spectatorMask & (1u << players[id].sockID)) != 0; }
        }

        /// <summary>
        /// Rebuilds the visible player list from current player state.
        /// </summary>
        void RefreshPlayerList()
        {
            SafeInvoke(delegate
            {
                playerList.Clear();
                for (int i = 0; i < 32; i++)
                {
                    if (players[i].connected == true)
                    {
                        AddPlayer(i);
                    }
                }
            });
        }

        /// <summary>
        /// Finds a player identifier from a JJ2 socket identifier.
        /// </summary>
        /// <summary>
        /// Returns a copy of the connected players known from the TCP player list.
        /// </summary>
        public Player[] GetPlayersSnapshot()
        {
            lock (players)
            {
                System.Collections.Generic.List<Player> list = new System.Collections.Generic.List<Player>();
                for (int i = 0; i < players.Length; i++)
                {
                    if (players[i].connected)
                    {
                        Player player = players[i];
                        player.IsSpectating = IsPlayerSpectating(i);
                        list.Add(player);
                    }
                }

                return list.ToArray();
            }
        }

        public byte MyPlayerId { get { return myID; } }

        public int GetPlayerIdFromSocketId(byte sockID)
        {
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i].sockID == sockID) return players[i].playerID;
            }
            return -1;
        }

        /// <summary>
        /// Reports whether the TCP socket remains IsConnected.
        /// </summary>
        public bool IsConnected()
        {
            return jj2tcpSocket.Connected;
        }

        /// <summary>
        /// Indicates whether the server has assigned this connection's player ID for outgoing chat.
        /// </summary>
        public bool IsReadyForChat()
        {
            return hasServerAssignedPlayerId && assetsReady;
        }

        /// <summary>
        /// Indicates whether the connected server uses the JJ2+ protocol path.
        /// </summary>
        public bool IsPlusServer()
        {
            return isPlusServer;
        }

        /// <summary>
        /// Closes the TCP socket for this connection.
        /// </summary>
        public void Disconnect()
        {
            assetsReady = false;
            hasServerAssignedPlayerId = false;
            lock (spectatorLock) { spec = null; spectatorRequested = null; spectatorBlocked = false; spectatorMask = 0; }
            if (assets != null) assets.Dispose();
            jj2tcpSocket.Close();
        }

        /// <summary>
        /// Gets the local player's current team identifier.
        /// </summary>
        public int GetMyTeam()
        {
            return players[myID].pTeam;
        }

        /// <summary>
        /// Sends the retained JJ2 ready packet for future in-game use.
        /// </summary>
        public void SendReadyCommand()
        {
            SendTcp(Jj2TcpPackets.CreateReadyCommand(myID));
        }

        /// <summary>
        /// Sends the retained JJ2 spectator-mode packet for future in-game use.
        /// </summary>
        public void ToggleSpectatorMode()
        {
            if (!IsReadyForChat() || !isPlusServer || SpectatorPending) return;
            SetSpectatorMode(!IsSpectating);
        }

        public bool IsSpectating { get { lock (spectatorLock) { return spec == true; } } }
        public bool SpectatorPending
        {
            get { lock (spectatorLock) { return spectatorRequested.HasValue &&
                DateTime.UtcNow - spectatorRequestTime < TimeSpan.FromSeconds(5); } }
        }
        public string SpectatorStatus
        {
            get
            {
                lock (spectatorLock)
                {
                    if (SpectatorPending) return "changing...";
                    if (spectatorBlocked) return "OFF (server blocked)";
                    if (spectatorRequested.HasValue) return "unconfirmed";
                    return spec.HasValue ? (spec.Value ? "ON" : "OFF") : "unknown";
                }
            }
        }

        private void SetSpectatorMode(bool enabled)
        {
            lock (spectatorLock)
            {
                spectatorRequested = enabled;
                spectatorRequestTime = DateTime.UtcNow;
            }
            SendTcp(Jj2TcpPackets.CreateSpectatorCommand(enabled));
        }

        private void UpdateSpectatorStatus(string message)
        {
            string text = message.Replace("|", "");
            string ownName = playerName.Replace("|", "");
            lock (spectatorLock)
            {
                if (text.IndexOf("Spectating is not enabled", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    spec = false;
                    spectatorBlocked = true;
                    spectatorRequested = null;
                }
                else if (text.IndexOf("You are no longer SPECTATING", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.StartsWith(ownName + " is no longer SPECTATING", StringComparison.OrdinalIgnoreCase) ||
                    text.IndexOf("Spectator Mode has already been disabled", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    spec = false;
                    spectatorRequested = null;
                    spectatorBlocked = false;
                }
                else if (text.IndexOf("You are now SPECTATING", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.StartsWith(ownName + " is now SPECTATING", StringComparison.OrdinalIgnoreCase) ||
                    text.IndexOf("Spectator Mode has already been enabled", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    spec = true;
                    spectatorRequested = null;
                    spectatorBlocked = false;
                }
                else if (text.IndexOf("Error:", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    text.IndexOf("spectat", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    spectatorRequested = null;
                }
                if (spec.HasValue)
                {
                    if (spec.Value) spectatorMask |= 1u << mySockID;
                    else spectatorMask &= ~(1u << mySockID);
                }
            }
            RefreshPlayerList();
        }

        #endregion
    }
}