using System;
using System.Text;

namespace Jj2
{
    /// <summary>
    /// Creates JJ2 TCP packets from named protocol fields instead of raw byte literals in client code.
    /// </summary>
    static class Jj2TcpPackets
    {
        // Jazz2Stats uses this as its newest configured JJ2+ protocol version (v6.99).
        private const uint LatestPlusVersion = 0x00060099;

        // [length, 0x0F, UDP binding, JJ2 version, player count].
        private static readonly byte[] JoinRequestTemplate =
        {
            0x09, 0x0F, 0x00, 0x00, 0x32, 0x34, 0x20, 0x20, 0x01
        };

        /// <summary>
        /// Creates the initial join request with the UDP port bound by this client.
        /// </summary>
        public static byte[] CreateJoinRequest(ushort udpPort)
        {
            return CreateJoinRequest(udpPort, null);
        }

        /// <summary>
        /// Creates the initial join request, optionally appending a password for private servers.
        /// NOTE: the original JJ2 password hand-off has not been confirmed against a real
        /// password-protected server yet. This appends the password as a null-terminated ASCII
        /// string after the fixed header (matching the variable-length-field convention used by
        /// every other JJ2 packet in this client, e.g. player name in 0x0E) as a best-effort guess -
        /// needs live verification and may need adjusting if a real private server rejects it.
        /// </summary>
        public static byte[] CreateJoinRequest(ushort udpPort, string password)
        {
            byte[] packet = CopyTemplate(JoinRequestTemplate);
            packet[2] = (byte)udpPort;
            packet[3] = (byte)(udpPort >> 8);

            if (string.IsNullOrEmpty(password))
            {
                return packet;
            }

            byte[] passwordBytes = Encoding.ASCII.GetBytes(password);
            byte[] extended = new byte[packet.Length + passwordBytes.Length + 1];
            Array.Copy(packet, extended, packet.Length);
            Array.Copy(passwordBytes, 0, extended, packet.Length, passwordBytes.Length);
            extended[extended.Length - 1] = 0x00;
            extended[0] = (byte)(extended.Length);
            return extended;
        }

        /// <summary>
        /// Creates the JJ2+ extension packet with the latest supported protocol version.
        /// </summary>
        public static byte[] CreatePlusPacket()
        {
            byte[] packet = { 0x08, 0x3F, 0x20, 0x03, 0x00, 0x00, 0x00, 0x00 };
            byte[] versionBytes = BitConverter.GetBytes(LatestPlusVersion);
            Array.Copy(versionBytes, 0, packet, 4, versionBytes.Length);
            return packet;
        }

        /// <summary>
        /// Creates the player-details packet expected by vanilla or JJ2+ servers.
        /// <summary>
        /// Creates the player-details packet expected by vanilla or JJ2+ servers.
        /// Character: 0 = Jazz, 1 = Spaz, 2 = Lori (JJ2+ servers accept up to 4 bits: 0-15). Team: 0 = blue/none, 1 = red.
        /// </summary>
        public static byte[] CreatePlayerDetails(byte playerId, string playerName, bool isPlusServer, byte character, byte team, byte[] fur)
        {
            byte[] playerNameBytes = Encoding.ASCII.GetBytes(playerName);
            byte[] furColors = fur != null && fur.Length == 4 ? fur : new byte[] { 0x10, 0x18, 0x20, 0x28 };

            if (!isPlusServer)
            {
                // Vanilla packs team (bit 4) and character (bits 0-1) into a single byte.
                byte teamAndCharacter = (byte)(((team & 0x01) << 4) | (character & 0x03));
                byte[] vanillaPacket = new byte[playerNameBytes.Length + 10];
                byte[] vanillaHeader =
                {
                    (byte)vanillaPacket.Length, 0x0E, 0x01, playerId, teamAndCharacter,
                    furColors[1], furColors[2], furColors[3], 0x11
                };

                Array.Copy(vanillaHeader, vanillaPacket, vanillaHeader.Length);
                Array.Copy(playerNameBytes, 0, vanillaPacket, vanillaHeader.Length, playerNameBytes.Length);
                return vanillaPacket;
            }

            // JJ2+ uses separate bytes: character (4 bits) then team, matching the server's full roster (0x12) format.
            byte[] packet = new byte[playerNameBytes.Length + 17];
            byte[] header =
            {
                (byte)packet.Length, 0x0E, 0x01, playerId, (byte)(character & 0x0F), team,
                furColors[0], furColors[1], furColors[2], furColors[3], 0x11, playerId, 0x0A, 0x0D, 0x00, 0x00
            };

            Array.Copy(header, packet, header.Length);
            Array.Copy(playerNameBytes, 0, packet, header.Length, playerNameBytes.Length);
            return packet;
        }

        /// <summary>
        /// Backward-compatible overload using the previous defaults (Spaz, blue team, default fur).
        /// </summary>
        public static byte[] CreatePlayerDetails(byte playerId, string playerName, bool isPlusServer)
        {
            return CreatePlayerDetails(playerId, playerName, isPlusServer, 0x01, 0x00, null);
        }

        /// <summary>
        /// Creates a chat packet for the supplied player identifier and text.
        /// </summary>
        public static byte[] CreateChatMessage(byte playerId, string message)
        {
            byte[] messageBytes = Encoding.ASCII.GetBytes(message);
            byte[] packet = new byte[messageBytes.Length + 6];
            byte[] header = { 0x00, (byte)(messageBytes.Length + 3), 0x00, 0x1B, playerId, 0x00 };

            Array.Copy(header, packet, header.Length);
            Array.Copy(messageBytes, 0, packet, header.Length, messageBytes.Length);
            return packet;
        }

        /// <summary>
        /// Creates the retained ready-command packet for the supplied player identifier.
        /// </summary>
        public static byte[] CreateReadyCommand(byte playerId)
        {
            return CreateConsoleCommand(playerId, "/ready");
        }

        /// <summary>
        /// Creates a spectator-mode packet for the requested state.
        /// </summary>
        public static byte[] CreateSpectatorCommand(bool enabled)
        {
            return new byte[] { 0x03, 0x42, enabled ? (byte)0x21 : (byte)0x20 };
        }

        /// <summary>
        /// Creates the JJ2+ ping packet from the negotiated server magic bytes.
        /// </summary>
        public static byte[] CreatePlusPing(byte[] magicNumbers)
        {
            if (magicNumbers == null || magicNumbers.Length != 4)
            {
                throw new ArgumentException("JJ2+ ping requires exactly four magic bytes.", "magicNumbers");
            }

            return new byte[] { 0x06, 0x1A, magicNumbers[0], magicNumbers[1], magicNumbers[2], magicNumbers[3] };
        }

        private static byte[] CreateConsoleCommand(byte playerId, string command)
        {
            byte[] commandBytes = Encoding.ASCII.GetBytes(command);
            byte[] packet = new byte[commandBytes.Length + 6];
            byte[] header = { 0x00, (byte)(commandBytes.Length + 3), 0x00, 0x1B, playerId, 0x00 };

            Array.Copy(header, packet, header.Length);
            Array.Copy(commandBytes, 0, packet, header.Length, commandBytes.Length);
            return packet;
        }

        private static byte[] CopyTemplate(byte[] template)
        {
            byte[] packet = new byte[template.Length];
            Array.Copy(template, packet, template.Length);
            return packet;
        }
    }

    /// <summary>
    /// Creates UDP packets that are part of the JJ2+ connection handshake.
    /// </summary>
    static class Jj2UdpPackets
    {
        /// <summary>
        /// Captured C->S 0x08 envelope containing a 16-byte 0x22 blaster event.
        /// Object IDs reserve ten bits per player's projectile counter.
        /// </summary>
        public static byte[] CreateBlasterShot(byte sequence, ushort shot, byte playerId,
            int x, int y, bool facingRight, bool aimUp, bool crouching)
        {
            if (playerId > 31) throw new ArgumentOutOfRangeException("playerId");
            byte[] packet = new byte[25];
            packet[2] = 0x08;
            packet[3] = sequence;
            packet[4] = 16;
            packet[8] = 0x22;
            int objectId = (playerId << 10) | (shot & 1023);
            WriteWord(packet, 9, objectId);
            packet[11] = aimUp ? (byte)0x60 : (byte)0x20;
            WriteWord(packet, 12, Math.Max(0, Math.Min(32767, x + (aimUp ? 9 : 0))));
            WriteWord(packet, 14, Math.Max(0, Math.Min(32767, y + (aimUp ? -8 : crouching ? 8 : -1))));
            WriteWord(packet, 16, aimUp ? 0 : (facingRight ? 1536 : -1536));
            WriteWord(packet, 18, aimUp ? -1536 : 0);
            packet[22] = aimUp ? (byte)0x17 : (byte)0x23;
            packet[23] = 1;
            packet[24] = 0x31;
            CalculateUdpChecksum(packet);
            return packet;
        }

        private static void WriteWord(byte[] packet, int offset, int value)
        {
            packet[offset] = (byte)value;
            packet[offset + 1] = (byte)(value >> 8);
        }

        /// <summary>
        /// Creates the JJ2+ UDP acknowledgement sent after receiving server details.
        /// </summary>
        public static byte[] CreatePlusHandshake()
        {
            return new byte[] { 0x0A, 0x15, 0x09, 0x00 };
        }

        /// <summary>
        /// Creates the UDP readiness response sent after the TCP start-level packet.
        /// </summary>
        public static byte[] CreatePlusReadyResponse(byte[] challenge)
        {
            if (challenge == null || challenge.Length != 4)
            {
                throw new ArgumentException("JJ2+ UDP readiness requires exactly four challenge bytes.", "challenge");
            }

            byte[] packet = { 0x00, 0x00, 0x09, 0xC0, challenge[0], challenge[1], challenge[2], challenge[3] };
            CalculateUdpChecksum(packet);
            return packet;
        }

        private static void CalculateUdpChecksum(byte[] packet)
        {
            int x = 1;
            int y = 1;
            for (int i = 2; i < packet.Length; i++)
            {
                x += packet[i];
                y += x;
            }

            packet[0] = (byte)(x % 251);
            packet[1] = (byte)(y % 251);
        }
    }
}
