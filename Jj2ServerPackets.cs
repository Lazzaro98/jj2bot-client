using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace Jj2
{
    static class Jj2ServerPackets
    {
        public static byte[] Tcp(byte opcode, byte[] body, bool extended = false)
        {
            int header = !extended && body.Length + 2 <= 255 ? 2 : 4;
            if (body.Length > 65534) throw new ArgumentException("TCP payload is too large.");
            byte[] packet = new byte[body.Length + header];
            if (header == 2) packet[0] = (byte)packet.Length;
            else
            {
                packet[1] = (byte)(body.Length + 1);
                packet[2] = (byte)((body.Length + 1) >> 8);
            }
            packet[header - 1] = opcode;
            Array.Copy(body, 0, packet, header, body.Length);
            return packet;
        }

        public static byte[] ReadTcp(NetworkStream stream, int maxLength = 8192)
        {
            int first = stream.ReadByte();
            if (first < 0) return null;
            int length = first, prefix = 1;
            byte low = 0, high = 0;
            if (first == 0)
            {
                int a = stream.ReadByte(), b = stream.ReadByte();
                if (a < 0 || b < 0) throw new EndOfStreamException("Truncated TCP header.");
                low = (byte)a; high = (byte)b;
                length = 3 + a + 256 * b;
                prefix = 3;
            }
            if (length < prefix + 1 || length > maxLength)
                throw new InvalidDataException("Invalid or oversized client TCP frame.");
            byte[] packet = new byte[length];
            packet[0] = (byte)first;
            if (prefix == 3) { packet[1] = low; packet[2] = high; }
            for (int offset = prefix; offset < length;)
            {
                int count = stream.Read(packet, offset, length - offset);
                if (count == 0) throw new EndOfStreamException("Truncated TCP packet.");
                offset += count;
            }
            return packet;
        }

        public static byte[] Console(string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            byte[] body = new byte[bytes.Length + 1];
            Array.Copy(bytes, 0, body, 1, bytes.Length);
            return Tcp(0x40, body);
        }

        public static byte[] Udp(byte opcode, byte sequence, byte[] body)
        {
            byte[] packet = new byte[body.Length + 4];
            packet[2] = opcode; packet[3] = sequence;
            Array.Copy(body, 0, packet, 4, body.Length);
            jj2udpClient.CalculateUdpChecksum(packet);
            return packet;
        }

        public static bool ValidUdp(byte[] packet)
        {
            if (packet.Length < 4) return false;
            byte[] copy = (byte[])packet.Clone();
            jj2udpClient.CalculateUdpChecksum(copy);
            return packet[0] == copy[0] && packet[1] == copy[1];
        }

        public static bool SupportsVersion(string version)
        {
            return version == "24  " || version == "23  ";
        }

        public static byte[] Pong(byte[] request)
        {
            if (request == null || request.Length < 4 || request[2] != 3)
                throw new ArgumentException("A browser ping requires opcode 0x03 and a sequence byte.");
            // Native browser pings carry a sequence and optional four-byte timestamp.
            // The version follows the timestamp; bit 0x40 disables incompatible entries.
            byte[] response = new byte[9];
            response[2] = 4;
            Array.Copy(request, 3, response, 3, Math.Min(5, request.Length - 3));
            response[8] = 0x82;
            if (request.Length < 12 || !SupportsVersion(Encoding.ASCII.GetString(request, 8, 4)))
                response[8] |= 0x40;
            jj2udpClient.CalculateUdpChecksum(response);
            return response;
        }

        public static byte[] Discovery(string name, byte playerCount, byte playerLimit, uint uptime)
        {
            byte[] text = Encoding.ASCII.GetBytes(name);
            byte[] reply = new byte[20 + text.Length];
            reply[2] = 6;
            // Unlike 0x04, 0x06 has a reserved zero byte, not the query sequence.
            Array.Copy(BitConverter.GetBytes(uptime), 0, reply, 4, 4);
            Array.Copy(Encoding.ASCII.GetBytes("24  "), 0, reply, 8, 4);
            reply[12] = playerCount; reply[13] = 1; reply[14] = 2;
            reply[15] = playerLimit; reply[16] = (byte)text.Length;
            Array.Copy(text, 0, reply, 17, text.Length);
            reply[reply.Length - 1] = 0xC0;
            jj2udpClient.CalculateUdpChecksum(reply);
            return reply;
        }

        public static byte[] Position(byte[] client, byte playerId)
        {
            if (client.Length != 17 || client[2] != 1 || client[4] != 1)
                throw new InvalidDataException("Only single-player movement packets are supported.");
            byte[] packet = new byte[18];
            Array.Copy(client, packet, 6);
            packet[2] = 2;
            packet[6] = (byte)(0x60 | playerId);
            Array.Copy(client, 6, packet, 7, 11);
            jj2udpClient.CalculateUdpChecksum(packet);
            return packet;
        }

        public static byte[] SugarRush(byte[] position, bool enabled)
        {
            if (position == null || position.Length != 18 || position[2] != 2)
                throw new ArgumentException("A single-player server position packet is required.");
            byte[] packet = (byte[])position.Clone();
            // Captured from native JJ2+ startSugarRush: remote-player status, bit 1.
            packet[7] = (byte)((packet[7] & ~2) | (enabled ? 2 : 0));
            jj2udpClient.CalculateUdpChecksum(packet);
            return packet;
        }

        public const string HotPotatoModule = "jj2bot_hotpotato_v1.mut";

        public static byte[] ScriptList(byte[] challenge, byte[] script)
        {
            using (MemoryStream body = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(body))
            {
                writer.Write(challenge, 0, 4);
                writer.Write((byte)2);
                writer.Write((byte)1);
                writer.Write((ushort)0);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write(JjAssetDownload.Crc32(script));
                byte[] name = Encoding.ASCII.GetBytes(HotPotatoModule);
                writer.Write((byte)name.Length);
                writer.Write(name);
                return Tcp(0x5A, body.ToArray());
            }
        }

        public static byte[] HotPotatoHud(byte? holder, double secondsLeft)
        {
            ushort ticks = holder.HasValue
                ? (ushort)Math.Max(0, Math.Min(1050, Math.Ceiling(secondsLeft * 70))) : (ushort)0;
            return Tcp(0x56, new byte[] { 1, 1, holder ?? (byte)255, (byte)ticks, (byte)(ticks >> 8) });
        }

        public static byte[] Spectator(byte socketId, bool enabled)
        {
            return Tcp(0x41, new byte[] { 1, 0, socketId, enabled ? socketId : (byte)0xFE });
        }

        public static byte[] Respawn(byte playerId, byte sequence)
        {
            // Native JJ2+ emits this self-death event when leaving spectator mode.
            return Udp(7, sequence, new byte[] { 2, 0, 0, 0, 0x0F, playerId, playerId });
        }

        public static byte[] ClientStates(uint active, uint downloading, uint plus)
        {
            byte[] body = new byte[17];
            body[1] = 32; body[7] = 1; body[12] = 2;
            Array.Copy(BitConverter.GetBytes(active), 0, body, 3, 4);
            Array.Copy(BitConverter.GetBytes(downloading), 0, body, 8, 4);
            Array.Copy(BitConverter.GetBytes(plus), 0, body, 13, 4);
            return Tcp(0x44, body);
        }
    }
}
