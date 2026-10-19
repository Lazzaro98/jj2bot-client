using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace Jj2
{
    /// <summary>
    /// Identifies how a server was registered with the JJ2 list server.
    /// </summary>
    enum ServerLocation
    {
        Local,
        Mirror,
        Manual,
        Clone
    }

    /// <summary>
    /// Describes one server entry returned by the JJ2 public list server.
    /// </summary>
    class ListedServer
    {
        public int Priority { get; set; }
        public string IpAddress { get; set; }
        public int Port { get; set; }
        public ServerLocation Location { get; set; }
        public bool IsPrivate { get; set; }
        public byte GameType { get; set; }
        public string GameTypeName
        {
            get
            {
                switch (GameType)
                {
                    case 1:
                        return "Cooperative";
                    case 2:
                        return "Battle";
                    case 3:
                        return "Race";
                    case 4:
                        return "Treasure";
                    case 5:
                        return "Capture the Flag";
                    default:
                        return "Unknown";
                }
            }
        }
        public string Version { get; set; }
        public long Uptime { get; set; }
        public DateTime ListedAt { get; set; }
        public byte PlayerCount { get; set; }
        public byte PlayerLimit { get; set; }
        public string Name { get; set; }

        /// <summary>
        /// Parses one ASCII list-server row into a server entry.
        /// </summary>
        public static bool TryParse(string value, DateTime listedAt, out ListedServer server)
        {
            server = null;
            if (String.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string[] fields = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 8)
            {
                return false;
            }

            int endpointSeparator = fields[0].LastIndexOf(':');
            int playerStart = value.IndexOf('[');
            int playerSeparator = value.IndexOf('/', playerStart + 1);
            int playerEnd = value.IndexOf(']', playerSeparator + 1);
            if (endpointSeparator <= 0 || playerStart < 0 || playerSeparator < 0 || playerEnd < 0)
            {
                return false;
            }

            int port;
            long uptime;
            byte playerCount;
            byte playerLimit;
            if (!Int32.TryParse(fields[0].Substring(endpointSeparator + 1), out port) ||
                !Int64.TryParse(fields[5], out uptime) ||
                !Byte.TryParse(value.Substring(playerStart + 1, playerSeparator - playerStart - 1), out playerCount) ||
                !Byte.TryParse(value.Substring(playerSeparator + 1, playerEnd - playerSeparator - 1), out playerLimit))
            {
                return false;
            }

            int nameStart = playerEnd + 1;
            while (nameStart < value.Length && value[nameStart] == ' ')
            {
                nameStart++;
            }

            server = new ListedServer
            {
                Priority = 255,
                IpAddress = fields[0].Substring(0, endpointSeparator),
                Port = port,
                Location = ParseLocation(fields[1]),
                IsPrivate = String.Equals(fields[2], "private", StringComparison.OrdinalIgnoreCase),
                GameType = ParseGameType(fields[3]),
                Version = fields[4].StartsWith("v", StringComparison.OrdinalIgnoreCase) ? fields[4].Substring(1) : fields[4],
                Uptime = uptime,
                ListedAt = listedAt,
                PlayerCount = playerCount,
                PlayerLimit = playerLimit,
                Name = nameStart < value.Length ? value.Substring(nameStart) : String.Empty
            };
            return true;
        }

        /// <summary>
        /// Returns a concise display representation of this server.
        /// </summary>
        public override string ToString()
        {
            return IpAddress + ":" + Port + " - " + Name + " [" + PlayerCount + "/" + PlayerLimit + "]";
        }

        private static ServerLocation ParseLocation(string value)
        {
            switch (value.ToLowerInvariant())
            {
                case "mirror":
                    return ServerLocation.Mirror;
                case "manual":
                    return ServerLocation.Manual;
                case "clone":
                    return ServerLocation.Clone;
                default:
                    return ServerLocation.Local;
            }
        }

        private static byte ParseGameType(string value)
        {
            switch (value.ToLowerInvariant())
            {
                case "coop":
                    return 1;
                case "battle":
                case "teambattle":
                    return 2;
                case "race":
                    return 3;
                case "treasure":
                    return 4;
                case "capture":
                case "ctf":
                    return 5;
                default:
                    return 6;
            }
        }
    }

    /// <summary>
    /// Downloads and parses the public ASCII server list from the JJ2 list server.
    /// </summary>
    static class Jj2ServerListClient
    {
        private const string DefaultHost = "list.jazzjackrabbit.com";
        private const int DefaultPort = 10057;
        private const int DefaultTimeoutMilliseconds = 5000;
        private const int ReceiveBufferSize = 4096;

        /// <summary>
        /// Downloads and parses the current public JJ2 server list.
        /// </summary>
        public static IList<ListedServer> GetServerList()
        {
            return GetServerList(DefaultHost, DefaultPort, DefaultTimeoutMilliseconds);
        }

        /// <summary>
        /// Downloads and parses a server list from the specified list-server endpoint.
        /// </summary>
        public static IList<ListedServer> GetServerList(string host, int port, int timeoutMilliseconds)
        {
            return ParseServerList(DownloadServerList(host, port, timeoutMilliseconds));
        }

        /// <summary>
        /// Downloads the raw ASCII response from a JJ2 list-server endpoint.
        /// </summary>
        public static string DownloadServerList(string host, int port, int timeoutMilliseconds)
        {
            if (String.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentException("List server host is required.", "host");
            }
            if (port < 1 || port > 65535)
            {
                throw new ArgumentOutOfRangeException("port");
            }
            if (timeoutMilliseconds <= 0)
            {
                throw new ArgumentOutOfRangeException("timeoutMilliseconds");
            }

            using (TcpClient client = new TcpClient())
            {
                client.ReceiveTimeout = timeoutMilliseconds;
                client.SendTimeout = timeoutMilliseconds;
                client.Connect(host, port);

                using (NetworkStream stream = client.GetStream())
                using (MemoryStream response = new MemoryStream())
                {
                    byte[] buffer = new byte[ReceiveBufferSize];
                    int received;
                    while ((received = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        response.Write(buffer, 0, received);
                    }
                    return Encoding.ASCII.GetString(response.ToArray());
                }
            }
        }

        /// <summary>
        /// Parses all valid server rows from a raw ASCII list-server response.
        /// </summary>
        public static IList<ListedServer> ParseServerList(string listData)
        {
            List<ListedServer> servers = new List<ListedServer>();
            if (String.IsNullOrWhiteSpace(listData))
            {
                return servers;
            }

            DateTime listedAt = DateTime.Now;
            string[] lines = listData.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                ListedServer server;
                if (ListedServer.TryParse(line, listedAt, out server))
                {
                    servers.Add(server);
                }
            }
            return servers;
        }
    }
}
