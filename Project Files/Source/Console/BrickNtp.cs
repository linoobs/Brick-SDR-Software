/*  BrickNtp.cs

    Time from an NTP server for the FT8 / FT4 slot timing. The PC clock itself is not changed (that needs administrator rights): the
    offset to the server is measured (SNTP, UDP port 123, the answer with the shortest round trip of a few tries) and added to the clock
    the FT8 / FT4 engine uses. Setup - General - DIGI MODES - FT8/FT4 - Time. Settings in ntp.txt of the data folder.

    This file is part of Brick-SDR, based on Thetis (GPL v2).
*/

using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace BrickSDR
{
    internal static class BrickNtp
    {
        public static string Server = "pool.ntp.org";
        public static bool Auto = true;                                  // measure at start and every 30 minutes

        private static string dir = "";
        private static volatile bool synced;
        private static double offset;                                    // seconds to add to the PC clock to get the true time
        private static DateTime syncedAt;
        private static string status = "Not measured yet";
        private static Timer timer;
        private static int busy;

        public static event Action Changed;

        public static double Offset { get { return synced ? offset : 0.0; } }
        public static bool Synced { get { return synced; } }
        public static string Status { get { return status; } }
        public static DateTime SyncedAt { get { return syncedAt; } }

        private static string File_ { get { return Path.Combine(dir, "ntp.txt"); } }

        public static void Load(string dataDir)
        {
            dir = dataDir ?? "";
            try
            {
                if (File.Exists(File_))
                    foreach (string line in File.ReadAllLines(File_))
                    {
                        int i = line.IndexOf('=');
                        if (i <= 0) continue;
                        string k = line.Substring(0, i).Trim().ToLowerInvariant(), v = line.Substring(i + 1).Trim();
                        if (k == "server" && v.Length > 0) Server = v;
                        else if (k == "auto") Auto = v != "0";
                    }
            }
            catch { }
            timer = new Timer(_ => { if (Auto) SyncAsync(null); }, null, 20000, 30 * 60 * 1000);
        }

        public static void Save()
        {
            try { if (dir.Length > 0) File.WriteAllText(File_, "server=" + Server + "\r\nauto=" + (Auto ? 1 : 0) + "\r\n"); } catch { }
        }

        // measures in the background; done gets the status text (not on the UI thread)
        public static void SyncAsync(Action<string> done)
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) { if (done != null) done("Already running"); return; }
            string server = Server;
            Task.Run(() =>
            {
                string msg;
                try
                {
                    double off, delay;
                    if (Query(server, out off, out delay))
                    {
                        offset = off; syncedAt = DateTime.Now; synced = true;
                        msg = "The PC clock is " + (off > 0 ? "behind" : "ahead") + " by " + Math.Abs(off).ToString("0.00", CultureInfo.InvariantCulture) + " s (" + server + ", " + (int)Math.Round(delay * 1000) + " ms), measured " + syncedAt.ToString("HH:mm:ss");
                    }
                    else msg = "No answer from " + server + " (UDP port 123 blocked or wrong name)";
                }
                catch (Exception ex) { msg = "Failed: " + ex.Message; }
                status = msg;
                Interlocked.Exchange(ref busy, 0);
                if (done != null) done(msg);
                try { if (Changed != null) Changed(); } catch { }
            });
        }

        private static DateTime FromNtp(byte[] b, int at)
        {
            ulong secs = ((ulong)b[at] << 24) | ((ulong)b[at + 1] << 16) | ((ulong)b[at + 2] << 8) | b[at + 3];
            ulong frac = ((ulong)b[at + 4] << 24) | ((ulong)b[at + 5] << 16) | ((ulong)b[at + 6] << 8) | b[at + 7];
            DateTime epoch = secs >= 0x80000000UL ? new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc) : new DateTime(2036, 2, 7, 6, 28, 16, DateTimeKind.Utc);
            return epoch.AddTicks((long)(secs * 10000000UL) + (long)(frac * 10000000UL / 0x100000000UL));
        }

        private static bool Query(string host, out double bestOffset, out double bestDelay)
        {
            bestOffset = 0; bestDelay = double.MaxValue;
            IPAddress ip = null;
            foreach (IPAddress a in Dns.GetHostAddresses(host)) if (a.AddressFamily == AddressFamily.InterNetwork) { ip = a; break; }
            if (ip == null) return false;
            bool any = false;
            for (int n = 0; n < 5; n++)
            {
                try
                {
                    using (UdpClient u = new UdpClient())
                    {
                        u.Client.ReceiveTimeout = 1500;
                        u.Connect(ip, 123);
                        byte[] req = new byte[48];
                        req[0] = 0x1B;                                           // no warning, version 3, client
                        DateTime t0 = DateTime.UtcNow;
                        u.Send(req, req.Length);
                        IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                        byte[] ans = u.Receive(ref ep);
                        DateTime t3 = DateTime.UtcNow;
                        if (ans.Length < 48 || (ans[0] & 7) != 4) continue;      // a server answer
                        DateTime t1 = FromNtp(ans, 32), t2 = FromNtp(ans, 40);
                        double off = ((t1 - t0).TotalSeconds + (t2 - t3).TotalSeconds) / 2.0;
                        double delay = (t3 - t0).TotalSeconds - (t2 - t1).TotalSeconds;
                        if (delay < bestDelay) { bestDelay = delay; bestOffset = off; any = true; }
                    }
                }
                catch (SocketException) { }
                Thread.Sleep(150);
            }
            return any;
        }
    }
}
