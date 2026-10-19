using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Jj2
{
    /// <summary>
    /// Reads a JJ2 level (.j2l) and its tileset (.j2t) and exposes the pixel collision mask of the sprite layer.
    /// Format notes follow the public graphixjjf parser. MLLE extra layers/tilesets are not supported.
    /// </summary>
    class JjLevelMap
    {
        private const int HeaderSize = 262;
        private const int VersionTsf = 515;

        private byte[] solid;
        private int[] eventIds;
        private int[] spriteTiles;
        private JjTileset tileset;

        public int WidthTiles { get; private set; }
        public int HeightTiles { get; private set; }
        public int WidthPx { get { return WidthTiles * 32; } }
        public int HeightPx { get { return HeightTiles * 32; } }
        public string LevelFile { get; private set; }
        public string TilesetFile { get; private set; }

        internal static string FindFile(string dir, string name)
        {
            string[] folders = { dir, Path.Combine(dir, "cache") };
            foreach (string folder in folders)
            {
                string path = Path.Combine(folder, name);
                if (File.Exists(path)) return path;
            }

            return null;
        }

        /// <summary>
        /// Loads a level from searchDir (the Jazz 2 folder, also searching its cache subfolder). Returns null if the level or tileset is missing/unreadable.
        /// </summary>
        public static JjLevelMap Load(string searchDir, string levelFileName)
        {
            try
            {
                string levelPath = FindFile(searchDir, levelFileName) ?? FindFile(searchDir, levelFileName + ".j2l");
                if (levelPath == null)
                {
                    LastError = "level not found: " + levelFileName;
                    return null;
                }
                JjLevelMap map = new JjLevelMap();
                map.LevelFile = levelPath;
                return map.Parse(levelPath, searchDir) ? map : null;
            }
            catch (Exception ex)
            {
                LastError = ex.ToString();
                return null;
            }
        }

        public static string LastError;

        /// <summary>
        /// True if the pixel is solid. Outside the level is solid on the sides/bottom and free above.
        /// </summary>
        public bool IsSolid(int x, int y)
        {
            if (y < 0)
            {
                return false;
            }

            if (x < 0 || x >= WidthPx || y >= HeightPx)
            {
                return true;
            }

            return solid[y * WidthPx + x] != 0;
        }

        /// <summary>
        /// Renders the sprite layer using the tileset's palette, graphics and transparency.
        /// </summary>
        public System.Drawing.Bitmap RenderBitmap(int divisor)
        {
            if (divisor < 1) throw new ArgumentOutOfRangeException("divisor");
            int w = Math.Max(1, WidthPx / divisor), h = Math.Max(1, HeightPx / divisor);
            if ((long)w * h * 4 > 64 * 1024 * 1024)
                throw new InvalidDataException("Map preview exceeds the bitmap memory budget.");
            var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h),
                System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            try
            {
                int[] row = new int[w];
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        long red = 0, green = 0, blue = 0;
                        int count = 0;
                        for (int dy = 0; dy < divisor && y * divisor + dy < HeightPx; dy++)
                        {
                            int py = y * divisor + dy;
                            for (int dx = 0; dx < divisor && x * divisor + dx < WidthPx; dx++)
                            {
                                int px = x * divisor + dx;
                                int tile = spriteTiles[(py >> 5) * WidthTiles + (px >> 5)];
                                int color = tileset.Pixel(tile & 0xFFFF,
                                    (tile & 0x10000) != 0 ? 31 - (px & 31) : px & 31,
                                    (tile & 0x20000) != 0 ? 31 - (py & 31) : py & 31);
                                int alpha = (int)((uint)color >> 24);
                                red += (((color >> 16) & 255) * alpha + 20 * (255 - alpha)) / 255;
                                green += (((color >> 8) & 255) * alpha + 24 * (255 - alpha)) / 255;
                                blue += ((color & 255) * alpha + 31 * (255 - alpha)) / 255;
                                count++;
                            }
                        }
                        row[x] = unchecked((int)0xFF000000) | ((int)(red / count) << 16) |
                            ((int)(green / count) << 8) | (int)(blue / count);
                    }
                    System.Runtime.InteropServices.Marshal.Copy(row, 0,
                        new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), w);
                }
            }
            finally { bmp.UnlockBits(data); }
            return bmp;
        }

        public System.Drawing.Bitmap RenderCollisionBitmap(int divisor)
        {
            int w = Math.Max(1, WidthPx / divisor), h = Math.Max(1, HeightPx / divisor);
            System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            System.Drawing.Imaging.BitmapData data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h),
                System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            int[] row = new int[w];
            int free = unchecked((int)0xFF14181F), rock = unchecked((int)0xFF8A94A6);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int count = 0;
                    for (int dy = 0; dy < divisor; dy++)
                    {
                        int rowStart = (y * divisor + dy) * WidthPx + x * divisor;
                        for (int dx = 0; dx < divisor; dx++)
                        {
                            count += solid[rowStart + dx] != 0 ? 1 : 0;
                        }
                    }

                    row[x] = count * 2 >= divisor * divisor ? rock : free;
                }

                System.Runtime.InteropServices.Marshal.Copy(row, 0, new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), w);
            }

            bmp.UnlockBits(data);
            return bmp;
        }

        public int EventAt(int x, int y)
        {
            int tx = x >> 5, ty = y >> 5;
            if (tx < 0 || ty < 0 || tx >= WidthTiles || ty >= HeightTiles)
            {
                return 0;
            }

            return eventIds[ty * WidthTiles + tx];
        }

        /// <summary>
        /// Tile coordinates of every tile whose event id (low 8 bits) equals id.
        /// </summary>
        public List<int[]> FindEvents(int id)
        {
            List<int[]> found = new List<int[]>();
            for (int ty = 0; ty < HeightTiles; ty++)
            {
                for (int tx = 0; tx < WidthTiles; tx++)
                {
                    if ((eventIds[ty * WidthTiles + tx] & 0xFF) == id)
                    {
                        found.Add(new[] { tx, ty });
                    }
                }
            }

            return found;
        }

        internal static string ReadTilesetName(string levelPath)
        {
            return ReadTilesetName(File.ReadAllBytes(levelPath));
        }

        internal static string ReadTilesetName(byte[] data)
        {
            byte[][] streams = ReadStreams(data);
            Reader info = new Reader(streams[0]);
            info.Skip(19 + 32);
            return info.FixedString(32);
        }

        internal static JjLevelMap LoadFiles(string levelPath, string tilesetPath)
        {
            try
            {
                JjLevelMap map = new JjLevelMap();
                map.LevelFile = levelPath;
                return map.Parse(levelPath, Path.GetDirectoryName(levelPath), tilesetPath) ? map : null;
            }
            catch (Exception ex)
            {
                LastError = ex.ToString();
                return null;
            }
        }

        private bool Parse(string levelPath, string dir, string tilesetPath = null)
        {
            byte[] data = File.ReadAllBytes(levelPath);
            if (data.Length < HeaderSize || Encoding.ASCII.GetString(data, 0, HeaderSize).IndexOf("MegaGames", StringComparison.Ordinal) < 0)
            {
                return false;
            }

            int version = BitConverter.ToUInt16(data, 220);
            int maxTiles = version == VersionTsf ? 4096 : 1024;
            byte[][] streams = ReadStreams(data);

            Reader info = new Reader(streams[0]);
            info.Skip(2 + 2 + 2 + 2 + 1 + 1 + 1);
            int animCount = info.U16();
            info.Skip(2); // splitscreen, multiplayer flags
            info.Skip(4); // buffer size
            info.Skip(32); // level name
            string tileset = info.FixedString(32);
            info.Skip(32 * 4); // bonus, next, secret, music
            info.Skip(512 * 16); // text strings

            int[] props = info.UIntArray(8);
            info.Skip(8); // type
            bool[] hasTiles = new bool[8];
            for (int i = 0; i < 8; i++) hasTiles[i] = info.U8() != 0;
            int[] widths = info.UIntArray(8);
            int[] widthReal = info.UIntArray(8);
            int[] heights = info.UIntArray(8);
            info.Skip(8 * 4); // z
            info.Skip(8); // detail
            info.Skip(8 * 4 * 6); // wave x/y, speed x/y, auto speed x/y
            info.Skip(8); // texture mode
            info.Skip(8 * 3); // texture colours
            int staticTiles = info.U16();
            int[] tileEvents = info.UIntArray(maxTiles);
            info.Skip(maxTiles); // flipped
            info.Skip(maxTiles); // tile type
            info.Skip(maxTiles); // tile used

            int[][] animFrames = new int[animCount][];
            for (int i = 0; i < animCount; i++)
            {
                animFrames[i] = ReadAnim(info);
            }

            const int spriteLayer = 3; // JCS layer 4
            if (!hasTiles[spriteLayer])
            {
                return false;
            }

            bool tileWidth = (props[spriteLayer] & 1) != 0;
            int layerWidth = widths[spriteLayer];
            int stride = tileWidth ? widthReal[spriteLayer] : layerWidth;
            int wordsPerRow = (stride + 3) / 4;
            int layerHeight = heights[spriteLayer];
            if (layerWidth <= 0 || layerHeight <= 0 || stride < layerWidth ||
                (long)layerWidth * layerHeight * 1024 > 64 * 1024 * 1024)
                throw new InvalidDataException("Level collision map exceeds supported dimensions.");

            int offset = 0;
            for (int i = 0; i < spriteLayer; i++)
            {
                if (!hasTiles[i]) continue;
                int w = (props[i] & 1) != 0 ? widthReal[i] : widths[i];
                offset += 2 * (((w + 3) / 4) * heights[i]);
            }

            byte[] dictionary = streams[2];
            byte[] wordMap = streams[3];
            int wordCount = wordsPerRow * layerHeight;

            JjTileset ts = tilesetPath == null ? JjTileset.Load(dir, tileset) : JjTileset.LoadFile(tilesetPath);
            if (ts == null)
            {
                LastError = "tileset not found: '" + tileset + "'";
                return false;
            }

            TilesetFile = ts.Path;
            this.tileset = ts;
            WidthTiles = layerWidth;
            HeightTiles = layerHeight;
            solid = new byte[WidthPx * HeightPx];
            eventIds = new int[WidthTiles * HeightTiles];
            spriteTiles = new int[WidthTiles * HeightTiles];

            for (int row = 0; row < layerHeight; row++)
            {
                for (int wcol = 0; wcol < wordsPerRow; wcol++)
                {
                    int wordIndex = BitConverter.ToUInt16(wordMap, offset + 2 * (row * wordsPerRow + wcol));
                    for (int k = 0; k < 4; k++)
                    {
                        int col = wcol * 4 + k;
                        if (col >= layerWidth) continue;
                        int raw = BitConverter.ToUInt16(dictionary, wordIndex * 8 + k * 2);
                        bool hflip = (raw & maxTiles) != 0;
                        bool vflip = (raw & 0x2000) != 0;
                        int tile = ResolveTile(raw % maxTiles, staticTiles, animFrames, maxTiles, ref hflip, ref vflip);
                        ts.ValidateTile(tile);
                        spriteTiles[row * WidthTiles + col] = tile | (hflip ? 0x10000 : 0) | (vflip ? 0x20000 : 0);
                        eventIds[row * WidthTiles + col] = EventFromStream(streams[1], row * layerWidth + col);
                        ts.BlitMask(tile, hflip, vflip, solid, WidthPx, col * 32, row * 32);
                    }
                }
            }

            return true;
        }

        private static int EventFromStream(byte[] events, int index)
        {
            int p = index * 4;
            return p + 4 <= events.Length ? BitConverter.ToInt32(events, p) : 0;
        }

        private static int[] ReadAnim(Reader info)
        {
            // Record layout (137 bytes): 3 shorts, pingpong, speed, frame count, 64 shorts of frames.
            info.Skip(2 + 2 + 2 + 1 + 1);
            int frameCount = info.U8();
            int[] frames = new int[64];
            for (int i = 0; i < 64; i++) frames[i] = info.U16();
            int[] result = new int[Math.Min(frameCount, 64)];
            Array.Copy(frames, result, result.Length);
            return result;
        }

        private static int ResolveTile(int tile, int staticTiles, int[][] anims, int maxTiles, ref bool hflip, ref bool vflip)
        {
            int guard = 0;
            while (tile >= staticTiles && guard++ < 8)
            {
                int[] frames = tile - staticTiles < anims.Length ? anims[tile - staticTiles] : null;
                if (frames == null || frames.Length == 0)
                {
                    return 0;
                }

                int raw = frames[0];
                if ((raw & maxTiles) != 0) hflip = !hflip;
                if ((raw & 0x2000) != 0) vflip = !vflip;
                tile = raw % maxTiles;
            }

            return tile;
        }

        internal static byte[][] ReadStreams(byte[] data)
        {
            if (data.Length < HeaderSize) throw new InvalidDataException("Truncated asset header.");
            byte[][] result = new byte[4][];
            int pos = HeaderSize;
            long total = 0;
            for (int i = 0; i < 4; i++)
            {
                int comp = BitConverter.ToInt32(data, 230 + i * 8);
                int uncomp = BitConverter.ToInt32(data, 234 + i * 8);
                total += uncomp;
                if (comp < 2 || comp > data.Length - pos || uncomp < 0 || total > 64 * 1024 * 1024)
                    throw new InvalidDataException("Invalid or oversized compressed asset stream.");
                result[i] = Inflate(data, pos, comp, uncomp);
                pos += comp;
            }

            return result;
        }

        private static byte[] Inflate(byte[] data, int offset, int compressedLength, int uncompressedLength)
        {
            // zlib stream: skip the 2-byte header and feed the raw deflate data
            using (MemoryStream input = new MemoryStream(data, offset + 2, compressedLength - 2))
            using (DeflateStream ds = new DeflateStream(input, CompressionMode.Decompress))
            {
                byte[] output = new byte[uncompressedLength];
                int read = 0;
                while (read < uncompressedLength)
                {
                    int n = ds.Read(output, read, uncompressedLength - read);
                    if (n <= 0) throw new InvalidDataException("Truncated compressed asset stream.");
                    read += n;
                }

                if (ds.ReadByte() != -1) throw new InvalidDataException("Asset stream size mismatch.");
                return output;
            }
        }

        internal class Reader
        {
            private readonly byte[] b;
            private int p;

            public Reader(byte[] buffer) { b = buffer; }

            private void Require(int n)
            {
                if (n < 0 || n > b.Length - p) throw new InvalidDataException("Truncated level metadata.");
            }

            public void Skip(int n) { Require(n); p += n; }
            public int U8() { Require(1); return b[p++]; }
            public int U16() { Require(2); int v = BitConverter.ToUInt16(b, p); p += 2; return v; }

            public int[] UIntArray(int n)
            {
                if (n < 0 || n > (b.Length - p) / 4) throw new InvalidDataException("Truncated level array.");
                int[] r = new int[n];
                for (int i = 0; i < n; i++) { r[i] = BitConverter.ToInt32(b, p); p += 4; }
                return r;
            }

            public string FixedString(int n)
            {
                Require(n);
                string s = Encoding.ASCII.GetString(b, p, n);
                p += n;
                int z = s.IndexOf('\0');
                return z >= 0 ? s.Substring(0, z) : s;
            }
        }
    }

    /// <summary>
    /// Tileset (.j2t) graphics, palette, transparency and collision masks.
    /// </summary>
    class JjTileset
    {
        private byte[] masks;
        private int[] maskOffsets;
        private int[] imageOffsets, transparencyOffsets;
        private int[] palette;
        private byte[] images, transparency, opaque;
        private int tileCount;
        public string Path { get; private set; }

        public static JjTileset Load(string dir, string tilesetName)
        {
            string path = JjLevelMap.FindFile(dir, tilesetName);
            if (path == null)
            {
                return null;
            }

            return LoadFile(path);
        }

        internal static JjTileset LoadFile(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            int version = BitConverter.ToUInt16(data, 220);
            int maxTiles = version == 512 ? 1024 : 4096;
            if (version != 512 && version != 513 && version != 768)
                throw new InvalidDataException("Unsupported tileset version: " + version);
            byte[][] streams = JjLevelMap.ReadStreams(data);

            // Data1: 256 palette entries (4 bytes), tile count, opaque[max], unknown[max], then 6 long arrays
            // (image offset, unknown, transparency offset, unknown, mask offset, flipped mask offset).
            byte[] s1 = streams[0];
            if (s1.Length < 1028 + maxTiles * 26)
                throw new InvalidDataException("Truncated tileset metadata.");
            int p = 256 * 4 + 4 + maxTiles + maxTiles;
            p += maxTiles * 4 * 4; // skip image offset, unknown2, transparency offset, unknown3
            JjTileset ts = new JjTileset();
            ts.Path = path;
            ts.tileCount = BitConverter.ToInt32(s1, 1024);
            if (ts.tileCount < 0 || ts.tileCount > maxTiles)
                throw new InvalidDataException("Invalid tileset tile count.");
            ts.palette = new int[256];
            for (int i = 0; i < 256; i++)
                ts.palette[i] = unchecked((int)0xFF000000) | (s1[i * 4] << 16) |
                    (s1[i * 4 + 1] << 8) | s1[i * 4 + 2];
            ts.images = streams[1];
            ts.transparency = streams[2];
            ts.opaque = new byte[maxTiles];
            Array.Copy(s1, 1028, ts.opaque, 0, maxTiles);
            ts.imageOffsets = new int[maxTiles];
            ts.transparencyOffsets = new int[maxTiles];
            int addressBase = 1028 + 2 * maxTiles;
            for (int i = 0; i < ts.tileCount; i++)
            {
                int address = BitConverter.ToInt32(s1, addressBase + i * 4);
                ts.imageOffsets[i] = address;
                int imageOffset = address & int.MaxValue;
                int imageSize = address < 0 ? 4096 : 1024;
                ts.transparencyOffsets[i] = BitConverter.ToInt32(s1, addressBase + maxTiles * 8 + i * 4);
                if (imageOffset > ts.images.Length - imageSize ||
                    (ts.opaque[i] == 0 && (ts.transparencyOffsets[i] < 0 ||
                    ts.transparencyOffsets[i] > ts.transparency.Length - 128)))
                    throw new InvalidDataException("Invalid tileset image/transparency offset.");
            }
            ts.maskOffsets = new int[maxTiles];
            for (int i = 0; i < maxTiles; i++)
            {
                ts.maskOffsets[i] = BitConverter.ToInt32(s1, p + i * 4);
            }

            ts.masks = streams[3];
            return ts;
        }

        internal int Pixel(int tile, int x, int y)
        {
            if (tile == 0) return 0;
            int index = y * 32 + x;
            int address = imageOffsets[tile];
            if (address < 0)
            {
                int offset = (address & int.MaxValue) + index * 4;
                return (images[offset + 3] << 24) | (images[offset] << 16) |
                    (images[offset + 1] << 8) | images[offset + 2];
            }
            if (opaque[tile] == 0 && (transparency[transparencyOffsets[tile] + (index >> 3)] &
                (1 << (index & 7))) == 0) return 0;
            return palette[images[address + index]];
        }

        internal void ValidateTile(int tile)
        {
            if (tile < 0 || tile >= tileCount)
                throw new InvalidDataException("Level references a tile outside the tileset.");
        }

        public void BlitMask(int tile, bool hflip, bool vflip, byte[] dest, int destStride, int dx, int dy)
        {
            if (tile < 0 || tile >= maskOffsets.Length)
            {
                return;
            }

            int offset = maskOffsets[tile];
            if (offset == 0 || offset + 128 > masks.Length)
            {
                return;
            }

            for (int i = 0; i < 1024; i++)
            {
                if ((masks[offset + (i >> 3)] & (1 << (i & 7))) == 0) continue;
                int x = i & 31, y = i >> 5;
                if (hflip) x = 31 - x;
                if (vflip) y = 31 - y;
                dest[(dy + y) * destStride + dx + x] = 1;
            }
        }
    }
}
