using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

/// <summary>
/// Transparent TCP relay/sniffer: listens on listenPort, forwards everything to targetIp:targetPort
/// (and back), logging every chunk of bytes (hex + ascii) with direction and timestamp to console and
/// to a log file. Used to capture the real JJ2 level/tileset download conversation without needing
/// admin rights for a packet sniffer.
/// </summary>
class Jj2Proxy
{
    static StreamWriter logFile;
    static readonly object logLock = new object();

    static void Main(string[] args)
    {
        int listenPort = args.Length > 0 ? int.Parse(args[0]) : 15052;
        string targetIp = args.Length > 1 ? args[1] : "127.0.0.1";
        int targetPort = args.Length > 2 ? int.Parse(args[2]) : 10052;
        string logPath = args.Length > 3 ? args[3] : "jj2proxy.log";

        logFile = new StreamWriter(logPath, false) { AutoFlush = true };
        Log("=== Jj2Proxy listening on 0.0.0.0:" + listenPort + " -> " + targetIp + ":" + targetPort + " ===");
        Console.WriteLine("Connect your JJ2 client to this machine's IP on port " + listenPort + " (e.g. 127.0.0.1:" + listenPort + ")");
        Console.WriteLine("Logging to: " + Path.GetFullPath(logPath));
        Console.WriteLine("Press Ctrl+C to stop.");

        TcpListener listener = new TcpListener(IPAddress.Any, listenPort);
        listener.Start();

        while (true)
        {
            TcpClient incoming = listener.AcceptTcpClient();
            Log("--- New client connected from " + incoming.Client.RemoteEndPoint + " ---");
            ThreadPool.QueueUserWorkItem(delegate { HandleClient(incoming, targetIp, targetPort); });
        }
    }

    static void HandleClient(TcpClient incoming, string targetIp, int targetPort)
    {
        try
        {
            using (incoming)
            using (TcpClient outgoing = new TcpClient())
            {
                incoming.NoDelay = true;
                outgoing.NoDelay = true;
                outgoing.Connect(targetIp, targetPort);
                Log("--- Connected to real server " + targetIp + ":" + targetPort + " ---");

                NetworkStream a = incoming.GetStream();
                NetworkStream b = outgoing.GetStream();

                Thread t1 = new Thread(delegate () { Pump(a, b, "C->S"); });
                Thread t2 = new Thread(delegate () { Pump(b, a, "S->C"); });
                t1.Start();
                t2.Start();
                t1.Join();
                t2.Join();
            }
        }
        catch (Exception ex)
        {
            Log("!!! Proxy error: " + ex.Message);
        }

        Log("--- Connection closed ---");
    }

    static void Pump(NetworkStream from, NetworkStream to, string direction)
    {
        byte[] buf = new byte[65536];
        try
        {
            while (true)
            {
                int n = from.Read(buf, 0, buf.Length);
                if (n <= 0) break;
                byte[] chunk = new byte[n];
                Array.Copy(buf, chunk, n);
                LogChunk(direction, chunk);
                to.Write(chunk, 0, chunk.Length);
                to.Flush();
            }
        }
        catch
        {
            // connection closed/reset - normal at end of session
        }
    }

    static void LogChunk(string direction, byte[] data)
    {
        StringBuilder hex = new StringBuilder();
        StringBuilder ascii = new StringBuilder();
        for (int i = 0; i < data.Length; i++)
        {
            hex.Append(data[i].ToString("X2")).Append(' ');
            char c = (char)data[i];
            ascii.Append(c >= 32 && c < 127 ? c : '.');
        }

        Log("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + direction + " (" + data.Length + "B)\n  HEX: " + hex + "\n  ASC: " + ascii);
    }

    static void Log(string line)
    {
        lock (logLock)
        {
            Console.WriteLine(line);
            logFile.WriteLine(line);
        }
    }
}
