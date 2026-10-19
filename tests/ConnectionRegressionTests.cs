using System;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Jj2;

class ConnectionRegressionTests
{
    static int checks;
    static void Assert(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }
    static void Set(object value, string name, object field)
    {
        value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(value, field);
    }
    static object Get(object value, string name)
    {
        return value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
    }
    static byte[] Hex(string text)
    {
        byte[] result = new byte[text.Length / 2];
        for (int i = 0; i < result.Length; i++) result[i] = Convert.ToByte(text.Substring(i * 2, 2), 16);
        return result;
    }
    static void Pump(int ms)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(5); }
    }
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr w, IntPtr l);

    [STAThread] static void Main()
    {
        using (var log = new RichTextBox())
        using (var list = new RichTextBox())
        using (var input = new RichTextBox())
        {
            var tcp = new jj2tcpClient(IPAddress.Loopback, 10053, 0, delegate { }, "Test",
                log, list, input, 1, 0, null);
            var udp = (jj2udpClient)FormatterServices.GetUninitializedObject(typeof(jj2udpClient));
            Set(udp, "tcpClient", tcp);
            Set(udp, "gameplayLock", new object());
            Set(udp, "spawnRandom", new Random(1));
            Set(udp, "deathReceivedAt", new int[256]);
            Set(udp, "deathReceived", new bool[256]);
            var body = new JjBotBody(null, 100, 100);
            Set(udp, "body", body);
            // Real Zeal hit record followed by flag/game-state and projectile records.
            udp.HandleReceivedPacket(Hex("a32b071a040000000c548a21fb150000001e00020200000000000101000000010200010301010110000000221b1020dc00650d00fa0000f8ff230131"));
            Assert(log.Text.Length == 0, "Valid captured batch reported malformed");
            Assert(body.X == 100, "Nonlethal hit respawned");
            // An unrelated record precedes our lethal hit; another follows it.
            udp.HandleReceivedPacket(Hex("000007200100000099aa040000000c048701030100000099bb"));
            Assert(body.X == 2016 && body.Y == 998, "Lethal hit inside batch not processed");
            body.ResetPosition(100, 100);
            udp.HandleReceivedPacket(Hex("000007200100000099aa040000000c048701030100000099bb"));
            Assert(body.X == 100, "Duplicate batch respawned twice");
            udp.HandleReceivedPacket(Hex("00000721020000000f01010100000099bb"));
            Assert(body.X == 2016, "Batched explicit death not processed");
            body.ResetPosition(100, 100);
            udp.HandleReceivedPacket(Hex("00000722040000000c048701030100000099"));
            Assert(body.X == 100, "Partially invalid batch applied state");
            Assert(log.Text.Contains("malformed server event batch"), "Invalid envelope not reported");
            log.Clear();
            udp.HandleReceivedPacket(Hex("00000723010000000c01"));
            Assert(log.Text.Contains("malformed death event"), "Short death payload not reported");
            tcp.Disconnect();
        }

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var accept = listener.BeginAcceptTcpClient(null, null);
            using (var form = new ServerConnectionForm(new ListedServer {
                Name = "Disconnect Test", IpAddress = "127.0.0.1",
                Port = ((IPEndPoint)listener.LocalEndpoint).Port }, "Test", 1))
            {
                form.Show();
                Pump(100);
                Assert(accept.IsCompleted, "Test client did not connect");
                using (var peer = listener.EndAcceptTcpClient(accept))
                {
                    Pump(100);
                    var button = (Button)Get(form, "connectionToggleButton");
                    Assert(button.Text == "Disconnect", "Disconnect button not ready");
                    // Hold a real mouse press through several keyboard timer ticks.
                    int point = (button.Height / 2 << 16) | (button.Width / 2);
                    SendMessage(button.Handle, 0x201, (IntPtr)1, (IntPtr)point);
                    Pump(100);
                    SendMessage(button.Handle, 0x202, IntPtr.Zero, (IntPtr)point);
                    Pump(100);
                    Assert(button.Text == "Reconnect", "Mouse disconnect was cancelled by controls timer");
                    Assert(Get(form, "client") == null, "Client still attached after disconnect");
                    Assert(Get((MapView)Get(form, "mapView"), "client") == null, "Map kept disconnected client");
                    form.Close();
                }
            }
        }
        finally { listener.Stop(); }
        Console.WriteLine("PASS: " + checks + " event-batch and mouse-disconnect regression checks");
    }
}
