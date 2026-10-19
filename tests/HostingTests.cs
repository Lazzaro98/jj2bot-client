using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Windows.Forms;
using Jj2;

class HostingTests
{
    private static int checks;

    private static void Assert(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }

    private static void Invalid(Jj2HostOptions options, Action<Jj2HostOptions> change)
    {
        string executable = options.ExecutablePath, level = options.LevelPath, name = options.ServerName, mode = options.Mode;
        int port = options.Port, players = options.MaxPlayers;
        change(options);
        bool rejected = false;
        try { Jj2HostLauncher.CreateStartInfo(options); }
        catch (ArgumentException) { rejected = true; }
        finally
        {
            options.ExecutablePath = executable; options.LevelPath = level; options.ServerName = name;
            options.Mode = mode; options.Port = port; options.MaxPlayers = players;
        }
        Assert(rejected, "Invalid host configuration was accepted.");
    }

    [STAThread] static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "jj2-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string executable = Path.Combine(root, "Jazz 2.exe"), level = Path.Combine(root, "SwingMap.j2l");
        string spacedLevel = Path.Combine(root, "Swing Map.j2l");
        File.WriteAllBytes(executable, new byte[0]);
        File.WriteAllBytes(level, new byte[0]);
        File.WriteAllBytes(spacedLevel, new byte[0]);
        try
        {
            Jj2HostOptions options = new Jj2HostOptions { ExecutablePath = executable, LevelPath = level,
                ServerName = "Friends & Jazz", Mode = "capture", Port = 10053, MaxPlayers = 8 };
            ProcessStartInfo start = Jj2HostLauncher.CreateStartInfo(options);
            Assert(start.FileName == executable && start.WorkingDirectory == root, "Incorrect game path or working directory.");
            Assert(!start.UseShellExecute, "Game launch must not run via a shell.");
            Assert(start.Arguments == "SwingMap.j2l -server -capture -port=10053 -maxPlayers 8 " +
                "-serverName=\"Friends & Jazz\" -windowed", "Incorrect launch arguments.");
            Assert(!start.Arguments.Contains(" -list"), "Server was published without opting in.");
            options.ListPublicly = true;
            Assert(Jj2HostLauncher.CreateStartInfo(options).Arguments.EndsWith(" -list"), "Public listing option missing.");
            foreach (string mode in new[] { "battle", "capture", "coop", "race", "treasure" })
            {
                options.Mode = mode;
                Assert(Jj2HostLauncher.CreateStartInfo(options).Arguments.Contains(" -" + mode + " "), "Unsupported mode mapping.");
            }
            Invalid(options, delegate(Jj2HostOptions o) { o.ExecutablePath = ""; });
            Invalid(options, delegate(Jj2HostOptions o) { o.ExecutablePath = level; });
            Invalid(options, delegate(Jj2HostOptions o) { o.ExecutablePath = "Jazz2.exe"; });
            Invalid(options, delegate(Jj2HostOptions o) { o.LevelPath = "missing.j2l"; });
            Invalid(options, delegate(Jj2HostOptions o) { o.LevelPath = executable; });
            Invalid(options, delegate(Jj2HostOptions o) { o.LevelPath = spacedLevel; });
            Invalid(options, delegate(Jj2HostOptions o) { o.LevelPath = Path.Combine(root, "..", "outside.j2l"); });
            Invalid(options, delegate(Jj2HostOptions o) { o.ServerName = " "; });
            Invalid(options, delegate(Jj2HostOptions o) { o.ServerName = new string('a', 31); });
            Invalid(options, delegate(Jj2HostOptions o) { o.ServerName = "name\" -list"; });
            Invalid(options, delegate(Jj2HostOptions o) { o.ServerName = "name\\"; });
            Invalid(options, delegate(Jj2HostOptions o) { o.ServerName = "name\n"; });
            Invalid(options, delegate(Jj2HostOptions o) { o.ServerName = "\u0107"; });
            Invalid(options, delegate(Jj2HostOptions o) { o.Mode = "battle -list"; });
            Invalid(options, delegate(Jj2HostOptions o) { o.Port = 0; });
            Invalid(options, delegate(Jj2HostOptions o) { o.Port = 65536; });
            Invalid(options, delegate(Jj2HostOptions o) { o.MaxPlayers = 1; });
            Invalid(options, delegate(Jj2HostOptions o) { o.MaxPlayers = 33; });
            options.Port = 65535; options.MaxPlayers = 32;
            Assert(Jj2HostLauncher.CreateStartInfo(options).Arguments.Contains("-port=65535 -maxPlayers 32"), "Upper bounds rejected.");
            options.Port = 1; options.MaxPlayers = 2;
            Assert(Jj2HostLauncher.CreateStartInfo(options).Arguments.Contains("-port=1 -maxPlayers 2"), "Lower bounds rejected.");

            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            try
            {
                Assert(!Jj2HostLauncher.IsListening(port), "TCP alone was reported as a ready server.");
                bool blocked = false;
                try { Jj2HostLauncher.EnsurePortAvailable(port); }
                catch (InvalidOperationException) { blocked = true; }
                Assert(blocked, "Occupied TCP port accepted.");
                using (UdpClient udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port)))
                    Assert(Jj2HostLauncher.IsListening(port), "TCP+UDP listener not detected.");
            }
            finally { listener.Stop(); }
            using (UdpClient udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            {
                int udpPort = ((IPEndPoint)udp.Client.LocalEndPoint).Port;
                bool blocked = false;
                try { Jj2HostLauncher.EnsurePortAvailable(udpPort); }
                catch (InvalidOperationException) { blocked = true; }
                Assert(blocked, "Occupied UDP port accepted.");
            }

            using (HostServerForm dialog = new HostServerForm(executable))
            {
                Assert(dialog.AcceptButton != null && dialog.CancelButton != null, "Hosting dialog keyboard actions missing.");
                Assert(dialog.Options == null, "Dialog accepted options without confirmation.");
            }
            using (Form1 browser = new Form1())
            using (Process process = Process.GetCurrentProcess())
            {
                Control[] buttons = browser.Controls.Find("hostServerButton", true);
                Assert(buttons.Length == 1 && buttons[0].Text == "Host a server", "Browser hosting button missing.");
                Assert(browser.Controls.Contains(buttons[0]), "Host button is not visible in the browser.");
                Assert(buttons[0].Bounds.Right <= browser.ClientSize.Width, "Host button lies outside the browser.");
                BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(Form1).GetField("hostProcess", fields).SetValue(browser, process);
                typeof(Form1).GetField("hostStartedAt", fields).SetValue(browser, DateTime.UtcNow);
                typeof(Form1).GetField("hostTimeoutReported", fields).SetValue(browser, true);
                MethodInfo tick = typeof(Form1).GetMethod("HostTimer_Tick", fields);
                TcpListener localServer = new TcpListener(IPAddress.Loopback, 0);
                localServer.Start();
                try
                {
                    int localPort = ((IPEndPoint)localServer.LocalEndpoint).Port;
                    typeof(Form1).GetField("hostPort", fields).SetValue(browser, localPort);
                    using (UdpClient udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, localPort)))
                    {
                        tick.Invoke(browser, new object[] { null, EventArgs.Empty });
                        Assert(buttons[0].Enabled && buttons[0].Text == "Join hosted server",
                            "Readiness did not recover after an earlier startup timeout.");
                        Assert((bool)typeof(Form1).GetField("hostReady", fields).GetValue(browser),
                            "Browser did not mark the server ready.");
                    }
                    tick.Invoke(browser, new object[] { null, EventArgs.Empty });
                    Assert(!buttons[0].Enabled, "Join remained enabled after the server stopped listening.");
                    Assert(!(bool)typeof(Form1).GetField("hostReady", fields).GetValue(browser),
                        "Server still shown as ready after losing its UDP listener.");
                }
                finally { localServer.Stop(); }
            }
            Console.WriteLine("PASS: " + checks + " hosting checks");
        }
        finally
        {
            File.Delete(executable);
            File.Delete(level);
            File.Delete(spacedLevel);
            Directory.Delete(root);
        }
    }
}
