using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Jj2
{
    // Captured from JJ2+: 0x15 requests a filename; 0x14 carries piece 0 (metadata),
    // then consecutive uint32 piece numbers followed by raw file bytes.
    sealed class JjAssetDownload : IDisposable
    {
        internal const int MaxFileSize = 32 * 1024 * 1024;
        readonly string directory;
        readonly Action<byte[]> send;
        readonly Action<string> log;
        readonly Action completed;
        readonly Action<string> failed;
        readonly object sync = new object();
        Timer timeout;
        string requested, levelPath, tilesetName;
        uint levelCrc, tilesetCrc, expectedCrc, nextPiece;
        int size;
        MemoryStream contents;
        bool stopped;

        public JjLevelMap Level { get; private set; }

        public JjAssetDownload(string directory, Action<byte[]> send, Action<string> log,
            Action completed, Action<string> failed)
        {
            this.directory = directory;
            this.send = send;
            this.log = log;
            this.completed = completed;
            this.failed = failed;
        }

        public void Begin(string levelName, uint levelChecksum, uint tilesetChecksum)
        {
            lock (sync)
            {
                try
                {
                    levelCrc = levelChecksum;
                    tilesetCrc = tilesetChecksum;
                    string name = SafeName(levelName, ".j2l");
                    levelPath = FindMatching(name, levelCrc);
                    if (levelPath == null) Request(name, levelCrc);
                    else PrepareTileset();
                }
                catch (Exception ex)
                {
                    if (!(ex is InvalidDataException) && !(ex is IOException) && !(ex is UnauthorizedAccessException) &&
                        !(ex is ArgumentException)) throw;
                    Fail(ex.Message);
                }
            }
        }

        public void Accept(byte[] packet, int offset)
        {
            lock (sync)
            {
                if (stopped) return;
                try
                {
                    if (requested == null || packet.Length - offset < 4)
                        throw new InvalidDataException("Unrequested or truncated file packet.");
                    uint piece = BitConverter.ToUInt32(packet, offset);
                    offset += 4;
                    if (piece == 0)
                    {
                        if (contents != null || packet.Length - offset < 5)
                            throw new InvalidDataException("Unexpected file header.");
                        size = BitConverter.ToInt32(packet, offset);
                        int nameLength = packet[offset + 4];
                        offset += 5;
                        if (size < 262 || size > MaxFileSize || packet.Length - offset != nameLength ||
                            !string.Equals(requested, Encoding.ASCII.GetString(packet, offset, nameLength),
                                StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("Invalid file size or unexpected filename.");
                        contents = new MemoryStream(size);
                        nextPiece = 1;
                    }
                    else
                    {
                        int count = packet.Length - offset;
                        if (contents == null || piece != nextPiece || count <= 0 || count > size - contents.Length)
                            throw new InvalidDataException("Unexpected file piece or size overflow.");
                        contents.Write(packet, offset, count);
                        nextPiece++;
                        if (contents.Length == size)
                        {
                            FinishFile();
                            return;
                        }
                    }
                    timeout.Change(15000, Timeout.Infinite);
                }
                catch (Exception ex)
                {
                    if (!(ex is InvalidDataException) && !(ex is IOException) && !(ex is UnauthorizedAccessException) &&
                        !(ex is ArgumentException)) throw;
                    Fail(ex.Message);
                }
            }
        }

        string FindMatching(string name, uint crc)
        {
            string[] candidates = {
                CachePath(name, crc), Path.Combine(directory, name),
                Path.Combine(Path.Combine(directory, "cache"), name)
            };
            foreach (string path in candidates)
            {
                if (!File.Exists(path)) continue;
                FileInfo info = new FileInfo(path);
                if (info.Length > MaxFileSize || info.Length < 262) continue;
                if (ValidFile(File.ReadAllBytes(path), name, crc)) return path;
            }
            return null;
        }

        internal static string SafeName(string name, string extension)
        {
            if (string.IsNullOrEmpty(name)) throw new InvalidDataException("Empty asset filename.");
            if (string.IsNullOrEmpty(Path.GetExtension(name))) name += extension;
            if (name.Length > 240 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                name.Contains("/") || name.Contains("\\") || name.EndsWith(".") ||
                name.EndsWith(" ") || !name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid asset filename: " + name);
            foreach (char c in name)
                if (c < 32 || c > 126) throw new InvalidDataException("Non-ASCII asset filename.");
            return name;
        }

        void Request(string name, uint crc)
        {
            requested = name;
            expectedCrc = crc;
            byte[] filename = Encoding.ASCII.GetBytes(name);
            byte[] packet = new byte[filename.Length + 3];
            packet[0] = (byte)packet.Length;
            packet[1] = 0x15;
            packet[2] = (byte)filename.Length;
            Array.Copy(filename, 0, packet, 3, filename.Length);
            if (timeout == null)
                timeout = new Timer(delegate { lock (sync) { if (!stopped && requested != null) Fail("File transfer timed out: " + requested); } },
                    null, Timeout.Infinite, Timeout.Infinite);
            timeout.Change(15000, Timeout.Infinite);
            log("Downloading " + name + "...");
            send(packet);
        }

        void FinishFile()
        {
            byte[] data = contents.ToArray();
            contents.Dispose();
            contents = null;
            if (!ValidFile(data, requested, expectedCrc))
                throw new InvalidDataException("CRC or file header validation failed: " + requested);
            string path = CachePath(requested, expectedCrc);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                File.WriteAllBytes(temporary, data);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            log("Downloaded " + requested + " (" + size + " bytes, CRC verified).");
            requested = null;
            timeout.Change(Timeout.Infinite, Timeout.Infinite);
            if (levelPath == null)
            {
                levelPath = path;
                PrepareTileset();
            }
            else FinishMap(path);
        }

        string CachePath(string name, uint crc)
        {
            return Path.Combine(Path.Combine(Path.Combine(Path.Combine(directory, "cache"), "bot"),
                crc.ToString("X8")), name);
        }

        void PrepareTileset()
        {
            tilesetName = SafeName(JjLevelMap.ReadTilesetName(levelPath), ".j2t");
            string path = FindMatching(tilesetName, tilesetCrc);
            if (path == null) Request(tilesetName, tilesetCrc);
            else FinishMap(path);
        }

        void FinishMap(string tilesetPath)
        {
            Level = JjLevelMap.LoadFiles(levelPath, tilesetPath);
            if (Level == null) throw new InvalidDataException("Could not load downloaded map: " + JjLevelMap.LastError);
            stopped = true;
            if (timeout != null) timeout.Dispose();
            log("Map ready: " + Path.GetFileName(levelPath) + " (" + Level.WidthTiles + "x" + Level.HeightTiles + " tiles).");
            completed();
        }

        internal static bool ValidFile(byte[] data, string name, uint expected)
        {
            if (data.Length < 262 || data.Length > MaxFileSize ||
                Encoding.ASCII.GetString(data, 180, 4) !=
                    (name.EndsWith(".j2l", StringComparison.OrdinalIgnoreCase) ? "LEVL" : "TILE") ||
                BitConverter.ToInt32(data, 222) != data.Length)
                return false;
            uint embedded = BitConverter.ToUInt32(data, 226);
            if (expected != 0 && embedded != expected) return false;
            uint crc = 0xFFFFFFFF;
            for (int i = 262; i < data.Length; i++)
            {
                crc ^= data[i];
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0);
            }
            return ~crc == embedded;
        }

        void Fail(string reason)
        {
            Dispose();
            failed(reason);
        }

        public void Dispose()
        {
            lock (sync)
            {
                stopped = true;
                if (timeout != null) timeout.Dispose();
                if (contents != null) { contents.Dispose(); contents = null; }
            }
        }
    }
}
