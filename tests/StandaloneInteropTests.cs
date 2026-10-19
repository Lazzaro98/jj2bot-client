using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
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
        string temporary = Path.Combine(Path.GetTempPath(), "jj2-interop-" + Guid.NewGuid().ToString("N"));
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
                        "-connect 127.0.0.1:" + server.Port + " -windowed") {
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
                                if (name != null && name != "Bot Alpha" && name != "Bot Bravo" &&
                                    (bool)type.GetField("Ready").GetValue(peer) && type.GetField("Position").GetValue(peer) != null)
                                    return true;
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
    }
}
