using System;
using System.Reflection;
using Jj2;

class GameplayTests
{
    private static int checks;

    static void Assert(bool condition, string name)
    {
        checks++;
        if (!condition) throw new Exception(name);
    }

    static JjLevelMap Map(bool wall)
    {
        var map = new JjLevelMap();
        typeof(JjLevelMap).GetProperty("WidthTiles").SetValue(map, 32, null);
        typeof(JjLevelMap).GetProperty("HeightTiles").SetValue(map, 20, null);
        byte[] solid = new byte[1024 * 640];
        for (int y = 0; y < 640; y++)
            for (int x = 0; x < 1024; x++)
                if (y >= 512 || (wall && x >= 200 && y >= 256)) solid[y * 1024 + x] = 1;
        Field("solid").SetValue(map, solid);
        Field("eventIds").SetValue(map, new int[32 * 20]);
        return map;
    }

    static FieldInfo Field(string name)
    {
        return typeof(JjLevelMap).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
    }

    static JjBotBody Ground(byte character, JjLevelMap map)
    {
        return new JjBotBody(map, 128, 491, character) { OnGround = true };
    }

    static void Land(JjBotBody b)
    {
        for (int i = 0; i < 400 && !b.OnGround; i++) b.Step(0, 0, false);
        Assert(b.OnGround, "Rabbit must land");
    }

    static void Packet(string expected, byte sequence, ushort shot, int x, int y, bool right, bool up, bool crouch)
    {
        byte[] packet = Jj2UdpPackets.CreateBlasterShot(sequence, shot, 1, x, y, right, up, crouch);
        Assert(BitConverter.ToString(packet).Replace("-", "").ToLowerInvariant() == expected,
            "Blaster packet must match the real-client capture byte for byte");
    }

    static void Main()
    {
        Rendering();
        var map = Map(false);
        var spaz = Ground(1, map);
        spaz.Step(0, 0, true);
        Assert(spaz.VY == -8 && spaz.Animation == 39, "Ground jump");
        for (int i = 0; i < 8; i++) spaz.Step(0, 0, true);
        Assert(!spaz.DoubleJumpUsed, "Held jump must not consume air jump");
        spaz.Step(0, 0, false);
        spaz.Step(0, 0, true);
        Assert(spaz.DoubleJumpUsed && spaz.VY == -8 && spaz.Animation == 60, "Spaz double jump");
        spaz.Step(0, 0, false);
        spaz.Step(0, 0, true);
        Assert(spaz.VY > -8, "No third jump");
        Land(spaz);
        Assert(!spaz.DoubleJumpUsed, "Landing restores double jump");

        foreach (byte character in new byte[] { 0, 2 })
        {
            var rabbit = new JjBotBody(map, 128, 100, character) { VY = 4 };
            rabbit.Step(1, 0, true);
            Assert(rabbit.Coptering && rabbit.Animation == 25 && rabbit.VY <= 0.6f, "Jazz/Lori copter");
            for (int i = 0; i < 20; i++) rabbit.Step(1, 0, true);
            Assert(rabbit.X > 128 && !rabbit.DoubleJumpUsed, "Copter can steer but is not double jump");
            for (int i = 0; i < 71; i++) rabbit.Step(0, 0, false);
            Assert(!rabbit.Coptering && rabbit.VY > 0.6f, "Copter expires after release");
            rabbit.Step(0, 0, true);
            Assert(rabbit.Coptering, "Copter can be reactivated");
            rabbit.Step(0, 1, false);
            Assert(rabbit.Stomping && !rabbit.Coptering && rabbit.Animation == 60, "Stomp cancels copter");
            for (int i = 0; i < 39; i++) rabbit.Step(0, 0, false);
            Assert(rabbit.Stomping && rabbit.Animation == 12 && rabbit.VY > 0.35f, "Stomp windup then descent");
            Land(rabbit);
            Assert(!rabbit.Stomping && !rabbit.CanFire, "Stomp landing recovery");
            for (int i = 0; i < 15; i++) rabbit.Step(0, 0, false);
            Assert(rabbit.CanFire, "Landing recovery ends");
        }

        spaz = Ground(1, Map(true));
        for (int i = 0; i < 14; i++) spaz.Step(0, 1, false);
        spaz.Step(0, 1, true);
        Assert(spaz.Sidekicking && spaz.Animation == 64 && !spaz.CanFire, "Charged Spaz kick");
        for (int i = 0; i < 45; i++) spaz.Step(0, 0, false);
        Assert(spaz.X > 128 && spaz.X < 200 && !spaz.Sidekicking, "Sidekick stops at wall");
        spaz.Step(0, 0, true);
        spaz.Step(0, 0, false);
        spaz.Step(0, 0, true);
        spaz.Step(0, 1, false);
        Assert(spaz.Stomping, "Spaz can buttstomp");
        spaz.ResetPosition(128, 491);
        Assert(!spaz.Stomping && !spaz.DoubleJumpUsed && !spaz.Sidekicking && spaz.VY == 0,
            "Teleport resets action state");

        int[] events = (int[])Field("eventIds").GetValue(map);
        events[15 * 32 + 4] = 85;
        spaz = Ground(1, map);
        spaz.Step(0, 0, false);
        Assert(!spaz.OnGround && spaz.VY < -8 && !spaz.Stomping, "Vertical spring regression");
        events[15 * 32 + 4] = 0;
        events[9 * 32 + 4] = 3;
        var hanging = new JjBotBody(map, 128, 330) { VY = 1 };
        hanging.Step(0, 0, false);
        Assert(hanging.Suspended == JjBotBody.HangType.Vine, "Vine catch regression");
        hanging.Step(0, 1, false);
        Assert(hanging.Suspended == JjBotBody.HangType.None && !hanging.Stomping, "Vine drop regression");

        Packet("ec43081110000000220e0420d9082904000600000000230131", 0x11, 14, 2265, 1066, true, false, false);
        Packet("0fa8083f1000000022130420cb08290400fa00000000230131", 0x3f, 19, 2251, 1066, false, false, false);
        Packet("1ebc08141000000022170460d4082204000000fa0000170131", 0x14, 23, 2251, 1066, false, true, false);
        Packet("7ed8089f1000000022190420cb08320400fa00000000230131", 0x9f, 25, 2251, 1066, false, false, true);
        byte[] max = Jj2UdpPackets.CreateBlasterShot(255, 1023, 31, 100, 100, true, false, false);
        Assert(max[9] == 255 && max[10] == 127, "Projectile ID includes shooter and counter");
        byte[] wrap = Jj2UdpPackets.CreateBlasterShot(0, 1024, 31, 100, 100, true, false, false);
        Assert(wrap[9] == 0 && wrap[10] == 124, "Counter wrap preserves shooter");
        bool rejected = false;
        try { Jj2UdpPackets.CreateBlasterShot(0, 0, 32, 0, 0, true, false, false); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Assert(rejected, "Invalid shooter rejected");
        Console.WriteLine("PASS: " + checks + " gameplay checks");
    }

    static void Set(object instance, string field, object value)
    {
        instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(instance, value);
    }

    static void Rendering()
    {
        var map = Map(false);
        var tileset = new JjTileset();
        byte[] images = new byte[2048 + 4096];
        for (int i = 0; i < 2048; i++) images[i] = 1;
        for (int i = 2048; i < images.Length; i += 4)
        {
            images[i + 2] = 255;
            images[i + 3] = 128;
        }
        byte[] transparency = new byte[128];
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                int pixel = y * 32 + x;
                transparency[pixel >> 3] |= (byte)(1 << (pixel & 7));
            }
        Set(tileset, "tileCount", 4);
        Set(tileset, "palette", new int[] { 0, unchecked((int)0xFFFF0000) });
        Set(tileset, "images", images);
        Set(tileset, "imageOffsets", new int[] { 0, 0, 1024, unchecked((int)0x80000800) });
        Set(tileset, "transparency", transparency);
        Set(tileset, "transparencyOffsets", new int[4]);
        Set(tileset, "opaque", new byte[] { 0, 1, 0, 2 });
        Set(map, "tileset", tileset);
        int[] tiles = new int[32 * 20];
        tiles[0] = 1;
        tiles[1] = 2;
        tiles[2] = 2 | 0x10000;
        tiles[3] = 2 | 0x20000;
        tiles[4] = 3;
        Set(map, "spriteTiles", tiles);
        using (var image = map.RenderBitmap(2))
        {
            Assert(image.Width == 512 && image.Height == 320, "Color preview dimensions");
            Assert(image.GetPixel(0, 0).ToArgb() == System.Drawing.Color.Red.ToArgb(), "Palette color");
            Assert(image.GetPixel(16, 0).R == 255, "Opaque transparency-mask pixel");
            Assert(image.GetPixel(24, 0).ToArgb() == System.Drawing.Color.FromArgb(20, 24, 31).ToArgb(),
                "Transparent pixel preserves background");
            Assert(image.GetPixel(32, 0).R == 20 && image.GetPixel(40, 0).R == 255, "Horizontal tile flip");
            Assert(image.GetPixel(48, 0).R == 20 && image.GetPixel(48, 8).R == 255, "Vertical tile flip");
            var blended = image.GetPixel(64, 0);
            Assert(blended.B > 130 && blended.R < 20, "32-bit tile alpha blending");
        }
        Assert(!map.IsSolid(0, 0) && map.IsSolid(0, 512), "Graphics preserve collision map");
        bool rejected = false;
        try { map.RenderBitmap(0); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Assert(rejected, "Invalid preview divisor rejected");
    }
}
