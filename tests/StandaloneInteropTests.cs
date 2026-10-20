using System;
using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Jj2;

static class StandaloneInteropTests
{
    static void Wait(Func<bool> condition, string message, int seconds)
    {
        Stopwatch clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            Application.DoEvents();
            if (condition()) return;
            Thread.Sleep(15);
        }
        throw new Exception(message);
    }

    static int FreePort()
    {
        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static void Run(string assets, string executable)
    {
        string temporary = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "jj2-interop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        string originalDirectory = jj2udpClient.GameDirectory;
        Process native = null;
        jj2udpClient a = null, b = null, c = null;
        using (Form controls = new Form())
        using (RichTextBox aChat = new RichTextBox())
        using (RichTextBox aPlayers = new RichTextBox())
        using (RichTextBox aInput = new RichTextBox())
        using (RichTextBox bChat = new RichTextBox())
        using (RichTextBox bPlayers = new RichTextBox())
        using (RichTextBox bInput = new RichTextBox())
        using (RichTextBox cChat = new RichTextBox())
        using (RichTextBox cPlayers = new RichTextBox())
        using (RichTextBox cInput = new RichTextBox())
        using (Jj2StandaloneServer server = new Jj2StandaloneServer(new Jj2ServerOptions {
            AssetDirectory = assets, LevelPath = Path.Combine(assets, "bbswing.j2l"), Port = FreePort(),
            MaxPlayers = 4, ServerName = "Standalone interoperability" }, Console.WriteLine))
        {
            controls.Controls.AddRange(new Control[] { aChat, aPlayers, aInput, bChat, bPlayers, bInput, cChat, cPlayers, cInput });
            foreach (Control control in controls.Controls) { IntPtr handle = control.Handle; }
            try
            {
                server.Start();
                jj2udpClient.GameDirectory = temporary;
                a = new jj2udpClient(IPAddress.Loopback, server.Port, 0, "Bot Alpha",
                    aChat, aPlayers, aInput, 0, 0, null);
                Wait(delegate { return a.IsReadyForChat() && a.Body != null; }, "First bot did not download and load the map.", 20);
                if (a.Level == null || a.Tcp.PreparedLevel == null)
                    throw new Exception("The downloaded map was not parsed by the actual bot client.");
                b = new jj2udpClient(IPAddress.Loopback, server.Port, 0, "Bot Bravo",
                    bChat, bPlayers, bInput, 1, 0, null);
                Wait(delegate { return b.IsReadyForChat() && b.Body != null; }, "Second bot did not become ready.", 20);
                Wait(delegate { return a.GetRemotePlayers().Length > 0 && b.GetRemotePlayers().Length > 0; },
                    "Actual bot clients did not exchange movement.", 5);
                aInput.Text = "standalone-chat-check";
                a.SendMessage();
                Wait(delegate { return bChat.Text.Contains("standalone-chat-check"); }, "Bot-to-bot chat failed.", 5);
                Console.WriteLine("PASS: two actual bot clients downloaded/loaded assets and exchanged movement and chat.");

                if (!String.IsNullOrEmpty(executable))
                {
                    StringBuilder nativeOutput = new StringBuilder();
                    native = Process.Start(new ProcessStartInfo(executable,
                        "-connect 127.0.0.1:" + server.Port + " -windowed -nochatlogger") {
                            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable)), UseShellExecute = false,
                            RedirectStandardOutput = true });
                    if (native == null) throw new Exception("Native JJ2+ client did not start.");
                    native.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) lock (nativeOutput) nativeOutput.AppendLine(e.Data);
                    };
                    native.BeginOutputReadLine();
                    try { Wait(delegate
                    {
                        if (native.HasExited) throw new Exception("Native JJ2+ client exited before becoming ready.");
                        lock (typeof(Jj2StandaloneServer).GetField("sync", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(server))
                        {
                            IDictionary peers = (IDictionary)typeof(Jj2StandaloneServer)
                                .GetField("peers", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(server);
                            foreach (object peer in peers.Values)
                            {
                                Type type = peer.GetType();
                                string name = (string)type.GetField("Name").GetValue(peer);
                                byte[] position = (byte[])type.GetField("Position").GetValue(peer);
                                if (name != null && name != "Bot Alpha" && name != "Bot Bravo" &&
                                    (bool)type.GetField("Ready").GetValue(peer) && position != null &&
                                    ((BitConverter.ToUInt16(position, 10) & 0x7FFF) != 0 ||
                                    (BitConverter.ToUInt16(position, 12) & 0x7FFF) != 0)) return true;
                            }
                        }
                        return false;
                    }, "Native JJ2+ client did not load the map and send gameplay.", 20);
                    Wait(delegate { return a.GetRemotePlayers().Length >= 2 && b.GetRemotePlayers().Length >= 2; },
                        "Native player movement was not visible to both actual bots.", 5);
                    c = new jj2udpClient(IPAddress.Loopback, server.Port, 0, "Late Bot",
                        cChat, cPlayers, cInput, 2, 0, null);
                    Wait(delegate { return c.IsReadyForChat() && c.Body != null; }, "Late bot did not become ready.", 10);
                    cInput.Text = "late-join-chat-check";
                    c.SendMessage();
                    Wait(delegate
                    {
                        lock (nativeOutput)
                        {
                            string text = nativeOutput.ToString();
                            return text.Contains("Late Bot") && text.Contains("late-join-chat-check");
                        }
                    }, "Native client did not recognize a late-joining player's roster and chat.", 5); }
                    catch
                    {
                        lock (nativeOutput) Console.WriteLine("Native output: " + nativeOutput);
                        throw;
                    }
                    Stopwatch stable = Stopwatch.StartNew();
                    while (stable.Elapsed.TotalSeconds < 16)
                    {
                        Application.DoEvents(); Thread.Sleep(15);
                        if (native.HasExited || server.PlayerCount != 4)
                            throw new Exception("A client was disconnected during the keepalive/liveness check.");
                    }
                    Console.WriteLine("PASS: native JJ2+ loaded bbswing, relayed movement, recognized a late player's chat, and stayed connected.");
                }
                server.Stop();
                Wait(delegate { return !a.IsConnected() && !b.IsConnected() && (c == null || !c.IsConnected()); },
                    "Server Stop did not disconnect the bot clients.", 5);
                Console.WriteLine("PASS: standalone server stopped and released both client sessions.");
            }
            finally
            {
                if (native != null)
                {
                    if (!native.HasExited) { native.Kill(); native.WaitForExit(); }
                    native.Dispose();
                }
                if (a != null) a.Disconnect();
                if (b != null) b.Disconnect();
                if (c != null) c.Disconnect();
                jj2udpClient.GameDirectory = originalDirectory;
                Directory.Delete(temporary, true);
            }
        }
    if (!String.IsNullOrEmpty(executable)) RunHotPotato(assets, executable);
    }

    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out WindowRect rectangle);
    [StructLayout(LayoutKind.Sequential)]
    struct WindowRect { public int Left, Top, Right, Bottom; }

    static void CaptureNative(Process native, string name)
    {
    native.Refresh();
    WindowRect rectangle;
    if (native.MainWindowHandle == IntPtr.Zero || !GetWindowRect(native.MainWindowHandle, out rectangle))
        throw new Exception("Native game window unavailable for visual verification.");
    using (Bitmap image = new Bitmap(rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top))
    {
        using (Graphics graphics = Graphics.FromImage(image))
        {
            IntPtr dc = graphics.GetHdc();
            try { if (!PrintWindow(native.MainWindowHandle, dc, 0)) throw new Exception("Native window capture failed."); }
            finally { graphics.ReleaseHdc(dc); }
        }
        image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name + ".png"));
    }
    }

    static object NativePeer(Jj2StandaloneServer server)
    {
    object sync = typeof(Jj2StandaloneServer).GetField("sync", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(server);
    lock (sync)
    {
        IDictionary peers = (IDictionary)typeof(Jj2StandaloneServer).GetField("peers", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(server);
        foreach (object peer in peers.Values)
        {
            string name = (string)peer.GetType().GetField("Name").GetValue(peer);
            if (name != null && name != "Potato Alpha" && name != "Potato Bravo") return peer;
        }
    }
    return null;
    }

    static byte[] NativePosition(object peer)
    {
    return peer == null ? null : (byte[])peer.GetType().GetField("Position").GetValue(peer);
    }

    static void RunHotPotato(string assets, string executable)
    {
    Process native = null;
    jj2udpClient alpha = null, bravo = null;
    string original = jj2udpClient.GameDirectory;
    using (Form controls = new Form())
    using (RichTextBox chatA = new RichTextBox())
    using (RichTextBox playersA = new RichTextBox())
    using (RichTextBox inputA = new RichTextBox())
    using (RichTextBox chatB = new RichTextBox())
    using (RichTextBox playersB = new RichTextBox())
    using (RichTextBox inputB = new RichTextBox())
    using (Jj2StandaloneServer server = new Jj2StandaloneServer(new Jj2ServerOptions {
        AssetDirectory = assets, LevelPath = Path.Combine(assets, "bbswing.j2l"), Port = FreePort(),
        MaxPlayers = 4, ServerName = "Hot Potato native verification", HotPotato = true },
        Console.WriteLine, delegate(int count) { return count - 1; }))
    {
        controls.Controls.AddRange(new Control[] { chatA, playersA, inputA, chatB, playersB, inputB });
        foreach (Control control in controls.Controls) { IntPtr handle = control.Handle; }
        try
        {
            server.Start();
            jj2udpClient.GameDirectory = assets;
            alpha = new jj2udpClient(IPAddress.Loopback, server.Port, 0, "Potato Alpha", chatA, playersA, inputA, 1, 0, null);
            Wait(delegate { return alpha.IsReadyForChat() && alpha.Body != null; }, "Hot Potato Alpha not ready.", 15);
            bravo = new jj2udpClient(IPAddress.Loopback, server.Port, 0, "Potato Bravo", chatB, playersB, inputB, 1, 0, null);
            Wait(delegate { return bravo.IsReadyForChat() && bravo.Body != null; }, "Hot Potato Bravo not ready.", 15);
            alpha.SetPosition(20000, 20000); bravo.SetPosition(30000, 30000);
            native = Process.Start(new ProcessStartInfo(executable,
                "-connect 127.0.0.1:" + server.Port + " -windowed -nochatlogger") {
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable)), UseShellExecute = false,
                RedirectStandardOutput = true });
            if (native == null) throw new Exception("Native Hot Potato client failed to start.");
            native.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
            { if (e.Data != null) Console.WriteLine("NATIVE: " + e.Data); };
            native.BeginOutputReadLine();
            object nativePeer = null;
            Wait(delegate {
                if (native.HasExited) throw new Exception("Native Hot Potato client exited.");
                nativePeer = NativePeer(server);
                byte[] position = NativePosition(nativePeer);
                return position != null && (BitConverter.ToUInt16(position, 10) & 0x7FFF) > 0;
            }, "Native Hot Potato client did not spawn.", 20);
            byte nativeId = (byte)nativePeer.GetType().GetField("Id").GetValue(nativePeer);
            Wait(delegate { return (bool)nativePeer.GetType().GetField("HudReady").GetValue(nativePeer); },
                "Native Hot Potato HUD module did not initialize.", 8);
            HotPotatoRound round = (HotPotatoRound)typeof(Jj2StandaloneServer)
                .GetField("hotPotato", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(server);
            server.ExecuteAdmin("/start");
            if (round.Holder != nativeId) throw new Exception("Injected random selector did not select the native player.");
            double firstDeadline = round.Deadline;
            Stopwatch frames = Stopwatch.StartNew();
            while (frames.ElapsedMilliseconds < 2800) { Application.DoEvents(); Thread.Sleep(15); }
            CaptureNative(native, "native-hotpotato-local-holder");
            Wait(delegate {
                byte[] position = NativePosition(nativePeer);
                if (position != null)
                    alpha.SetPosition((BitConverter.ToUInt16(position, 10) & 0x7FFF) + 8,
                        BitConverter.ToUInt16(position, 12) & 0x7FFF);
                return round.Holder == alpha.Tcp.MyPlayerId;
            }, "Native-to-bot proximity pass failed.", 5);
            if (round.Deadline != firstDeadline) throw new Exception("Native transfer reset the fuse.");
            frames.Restart();
            while (frames.ElapsedMilliseconds < 650)
            {
                Application.DoEvents();
                byte[] position = NativePosition(nativePeer);
                if (position != null)
                    alpha.SetPosition((BitConverter.ToUInt16(position, 10) & 0x7FFF) + 60,
                        BitConverter.ToUInt16(position, 12) & 0x7FFF);
                Thread.Sleep(15);
            }
            CaptureNative(native, "native-hotpotato-transferred");
            alpha.SetPosition(20000, 20000);
            Wait(delegate { return alpha.Tcp.IsSpectating; }, "Expired holder was not authoritatively spectated.", 18);
            if (!round.Running || round.Holder != nativeId) throw new Exception("New holder was not selected after elimination.");
            Wait(delegate { return round.Finished; }, "Second native fuse did not finish.", 18);
            if (round.Winner != bravo.Tcp.MyPlayerId || !(bool)nativePeer.GetType().GetField("Spectator").GetValue(nativePeer))
                throw new Exception("Native elimination/winner state incorrect.");
            frames.Restart();
            while (frames.ElapsedMilliseconds < 600) { Application.DoEvents(); Thread.Sleep(15); }
            CaptureNative(native, "native-hotpotato-eliminated");
            server.ExecuteAdmin("/start");
            try { Wait(delegate {
                byte[] position = NativePosition(nativePeer);
                return !alpha.Tcp.IsSpectating && position != null &&
                    ((BitConverter.ToUInt16(position, 10) & 0x7FFF) > 0 ||
                    (BitConverter.ToUInt16(position, 12) & 0x7FFF) > 0);
            },
                "Fresh round did not restore eliminated clients.", 8); }
            catch
            {
                byte[] position = NativePosition(nativePeer);
                Console.WriteLine("Restart state: " + (position == null ? "no position" : BitConverter.ToString(position)) +
                    ", Alpha spectating: " + alpha.Tcp.IsSpectating);
                CaptureNative(native, "native-hotpotato-restart-failure");
                throw;
            }
            if (!round.Running || round.RemainingPlayers != 3) throw new Exception("Fresh native round did not restart.");
            frames.Restart();
            while (frames.ElapsedMilliseconds < 600) { Application.DoEvents(); Thread.Sleep(15); }
            CaptureNative(native, "native-hotpotato-restarted");
            nativePeer.GetType().GetField("HudReady").SetValue(nativePeer, false);
            frames.Restart();
            while (frames.ElapsedMilliseconds < 3200) { Application.DoEvents(); Thread.Sleep(15); }
            CaptureNative(native, "native-hotpotato-stale-hud");
            if (!round.Running || round.Holder != nativeId || round.RemainingPlayers != 3)
                throw new Exception("Losing HUD updates changed authoritative round state.");
            nativePeer.GetType().GetField("HudReady").SetValue(nativePeer, true);
            server.ExecuteAdmin("/reset");
            frames.Restart();
            while (frames.ElapsedMilliseconds < 400) { Application.DoEvents(); Thread.Sleep(15); }
            CaptureNative(native, "native-hotpotato-reset-hud");
            if (round.Running || round.Holder.HasValue) throw new Exception("Native round reset failed.");
            if (native.HasExited) throw new Exception("Native Hot Potato client disconnected unexpectedly.");
            foreach (RemotePlayer player in alpha.GetRemotePlayers())
                if (player.Key == alpha.Tcp.MyPlayerId) throw new Exception("Self marker created a duplicate bot avatar.");
            Console.WriteLine("PASS: native JJ2+ Hot Potato HUD initialization, holder announcements, proximity transfer without fuse reset, two expiries, forced spectator, winner, restart and HUD loss without client-side elimination. Visual captures saved beside test runner for local/remote stars, native countdown and stale/reset HUD inspection.");
        }
        finally
        {
            if (alpha != null) alpha.Disconnect();
            if (bravo != null) bravo.Disconnect();
            if (native != null)
            {
                if (!native.HasExited) { native.Kill(); native.WaitForExit(); }
                native.Dispose();
            }
            jj2udpClient.GameDirectory = original;
        }
    }
    }
}
