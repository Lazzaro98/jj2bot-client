using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;

namespace Jj2
{
    class Jj2HostOptions
    {
        public string ExecutablePath { get; set; }
        public string LevelPath { get; set; }
        public string ServerName { get; set; }
        public string Mode { get; set; }
        public int Port { get; set; }
        public int MaxPlayers { get; set; }
        public bool ListPublicly { get; set; }
    }

    static class Jj2HostLauncher
    {
        public static ProcessStartInfo CreateStartInfo(Jj2HostOptions options)
        {
            if (options == null) throw new ArgumentNullException("options");
            if (String.IsNullOrWhiteSpace(options.ExecutablePath) ||
                !Path.IsPathRooted(options.ExecutablePath) ||
                !String.Equals(Path.GetExtension(options.ExecutablePath), ".exe", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(options.ExecutablePath))
                throw new ArgumentException("Select an existing JJ2/JJ2+ executable.");

            string executable = Path.GetFullPath(options.ExecutablePath);
            string directory = Path.GetDirectoryName(executable);
            string prefix = directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (String.IsNullOrWhiteSpace(options.LevelPath) || !Path.IsPathRooted(options.LevelPath))
                throw new ArgumentException("Select a level from the selected game's folder.");
            string level = Path.GetFullPath(options.LevelPath);
            if (!level.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !String.Equals(Path.GetExtension(level), ".j2l", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(level))
                throw new ArgumentException("The .j2l level must exist inside the selected game's folder.");
            string relativeLevel = level.Substring(prefix.Length);
            if (relativeLevel.StartsWith("-", StringComparison.Ordinal))
                throw new ArgumentException("The level filename cannot start with '-'.");
            // JJ2 treats quotes around a level as part of its filename, unlike its named options.
            foreach (char c in relativeLevel)
                if (Char.IsWhiteSpace(c))
                    throw new ArgumentException("JJ2's level launcher requires a level filename and subfolders without spaces.");
            if (String.IsNullOrWhiteSpace(options.ServerName) || options.ServerName.Length > 30)
                throw new ArgumentException("Enter a server name of 1-30 characters.");
            foreach (char c in options.ServerName)
                if (c < 32 || c > 126 || c == '"' || c == '\\')
                    throw new ArgumentException("Use printable ASCII for the server name, without quotes or backslashes.");
            switch (options.Mode)
            {
                case "battle": case "capture": case "coop": case "race": case "treasure": break;
                default: throw new ArgumentException("Select a supported game mode.");
            }
            if (options.Port < 1 || options.Port > 65535)
                throw new ArgumentException("Port must be between 1 and 65535.");
            if (options.MaxPlayers < 2 || options.MaxPlayers > 32)
                throw new ArgumentException("Player limit must be between 2 and 32.");

            return new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = directory,
                UseShellExecute = false,
                Arguments = relativeLevel + " -server -" + options.Mode +
                    " -port=" + options.Port.ToString(CultureInfo.InvariantCulture) +
                    " -maxPlayers " + options.MaxPlayers.ToString(CultureInfo.InvariantCulture) +
                    " -serverName=\"" + options.ServerName + "\" -windowed" +
                    (options.ListPublicly ? " -list" : "")
            };
        }

        public static void EnsurePortAvailable(int port)
        {
            IPGlobalProperties network = IPGlobalProperties.GetIPGlobalProperties();
            foreach (IPEndPoint endpoint in network.GetActiveTcpListeners())
                if (endpoint.Port == port)
                    throw new InvalidOperationException("TCP port " + port + " is already in use. Choose another port.");
            foreach (IPEndPoint endpoint in network.GetActiveUdpListeners())
                if (endpoint.Port == port)
                    throw new InvalidOperationException("UDP port " + port + " is already in use. Choose another port.");
        }

        public static bool IsListening(int port)
        {
            IPGlobalProperties network = IPGlobalProperties.GetIPGlobalProperties();
            bool tcp = false, udp = false;
            foreach (IPEndPoint endpoint in network.GetActiveTcpListeners())
                if (endpoint.Port == port) tcp = true;
            foreach (IPEndPoint endpoint in network.GetActiveUdpListeners())
                if (endpoint.Port == port) udp = true;
            return tcp && udp;
        }
    }
}
