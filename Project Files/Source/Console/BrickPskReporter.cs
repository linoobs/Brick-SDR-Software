/*  BrickPskReporter.cs

    Reception reports for PSK Reporter (https://pskreporter.info/): the stations decoded in FT8 / FT4 go to
    report.pskreporter.info as IPFIX over UDP, in the format of https://pskreporter.info/pskdev.html (the same one WSJT-X uses).
    One datagram holds the receiver record (your call sign, locator, program) and up to a few dozen spots.
    Rules of the service that are kept here: a datagram at most every five minutes (with a random part, not tied to the clock)
    unless it is full, the same UDP source port for all of them, the record format descriptors in the first three datagrams and
    then once an hour, a call sign at most once in fifteen minutes on the same band, informationSource = 1.
*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace BrickSDR
{
    internal sealed class PskReporter
    {
        internal struct Spot
        {
            public string Call, Grid, Mode;
            public long FreqHz;
            public int Snr;
            public DateTime Utc;
        }

        private const int SpotsPerDatagram = 60;

        private readonly string host;
        private readonly int port;
        private readonly ConcurrentQueue<Spot> queue = new ConcurrentQueue<Spot>();
        private readonly Dictionary<string, DateTime> lastReported = new Dictionary<string, DateTime>();
        private readonly uint domain = (uint)new Random().Next(1, int.MaxValue);
        private readonly Random rnd = new Random();
        private UdpClient udp;
        private Thread thread;
        private volatile bool run;
        private uint sequence;
        private int datagrams;
        private DateTime lastTemplates = DateTime.MinValue;
        private string rxCall = "", rxGrid = "", software = "";

        public int SpotsSent { get; private set; }
        public string Status { get; private set; }
        public DateTime LastSent { get; private set; }

        public PskReporter(string host = "report.pskreporter.info", int port = 4739)
        {
            this.host = host; this.port = port;
            Status = "Off";
        }

        public static string CleanCall(string call)
        {
            if (call == null) return "";
            call = call.Trim().Trim('<', '>').ToUpperInvariant();
            if (call.Length < 3 || call.Length > 13) return "";
            if (!Regex.IsMatch(call, "^[A-Z0-9/]+$") || !Regex.IsMatch(call, "[0-9]") || !Regex.IsMatch(call, "[A-Z]")) return "";
            return call;
        }

        public void Start(string myCall, string myGrid, string softwareName)
        {
            Stop();
            rxCall = CleanCall(myCall); rxGrid = (myGrid ?? "").Trim(); software = softwareName ?? "";
            if (rxCall.Length == 0) { Status = "Enter your call sign"; return; }
            run = true;
            sequence = 0; datagrams = 0; lastTemplates = DateTime.MinValue;
            Status = "Waiting for spots";
            thread = new Thread(Loop) { IsBackground = true, Name = "PSK Reporter" };
            thread.Start();
        }

        public void Stop()
        {
            run = false;
            Thread t = thread; thread = null;
            if (t != null) t.Join(1500);
            try { if (udp != null) udp.Close(); } catch { }
            udp = null;
            Spot s; while (queue.TryDequeue(out s)) { }
            Status = "Off";
        }

        public bool Running { get { return run; } }

        public void Add(string call, string grid, long freqHz, int snr, string mode, DateTime utc)
        {
            if (!run) return;
            call = CleanCall(call);
            if (call.Length == 0 || call == rxCall || freqHz <= 0) return;
            string band = (freqHz / 100000).ToString();
            string key = call + "|" + band;
            lock (lastReported)
            {
                DateTime t;
                if (lastReported.TryGetValue(key, out t) && (DateTime.UtcNow - t).TotalMinutes < 15) return;
                lastReported[key] = DateTime.UtcNow;
            }
            queue.Enqueue(new Spot { Call = call, Grid = (grid ?? "").Trim(), FreqHz = freqHz, Snr = Math.Max(-128, Math.Min(127, snr)), Mode = mode, Utc = utc });
        }

        // send what is waiting right now (also used by the test)
        public void Flush() { sendQueued(); }

        // when the window closes: what is waiting goes out unless a datagram went less than five minutes ago
        public void FlushIfDue()
        {
            try { if (run && queue.Count > 0 && (DateTime.UtcNow - LastSent).TotalMinutes >= 5) sendQueued(); } catch { }
        }

        private void Loop()
        {
            DateTime next = DateTime.MinValue;
            while (run)
            {
                try
                {
                    if (queue.Count > 0)
                    {
                        if (next == DateTime.MinValue) next = DateTime.UtcNow.AddSeconds(300 + rnd.Next(0, 60));       // not on the clock, five minutes and a bit
                        if (DateTime.UtcNow >= next || queue.Count >= SpotsPerDatagram)
                        {
                            sendQueued();
                            next = DateTime.MinValue;
                        }
                    }
                    else next = DateTime.MinValue;
                }
                catch (Exception ex) { Status = "Error: " + ex.Message; }
                Thread.Sleep(1000);
            }
        }

        private void sendQueued()
        {
            List<Spot> batch = new List<Spot>();
            Spot s;
            while (queue.TryDequeue(out s))
            {
                batch.Add(s);
                if (batch.Count >= SpotsPerDatagram) { sendBatch(batch); batch = new List<Spot>(); }
            }
            if (batch.Count > 0) sendBatch(batch);
        }

        private void sendBatch(List<Spot> batch)
        {
            if (udp == null) udp = new UdpClient(host, port);      // one socket, so one source port for every datagram
            bool templates = datagrams < 3 || (DateTime.UtcNow - lastTemplates).TotalMinutes >= 60;
            byte[] d = Build(batch, templates, rxCall, rxGrid, software, domain, sequence, DateTime.UtcNow);
            udp.Send(d, d.Length);
            sequence += (uint)batch.Count;
            datagrams++;
            if (templates) lastTemplates = DateTime.UtcNow;
            SpotsSent += batch.Count;
            LastSent = DateTime.UtcNow;
            Status = "Sent " + SpotsSent + " spots, last at " + LastSent.ToString("HH:mm:ss") + " UTC";
        }

        // ---- the datagram ----

        private static readonly byte[] ReceiverTemplate =
        {
            0x00, 0x03, 0x00, 0x24, 0x99, 0x92, 0x00, 0x03, 0x00, 0x01,          // set 3, 36 bytes, template 0x9992, 3 fields, 1 scope field
            0x80, 0x02, 0xFF, 0xFF, 0x00, 0x00, 0x76, 0x8F,                      // receiverCallsign (30351.2), variable length
            0x80, 0x04, 0xFF, 0xFF, 0x00, 0x00, 0x76, 0x8F,                      // receiverLocator (30351.4)
            0x80, 0x08, 0xFF, 0xFF, 0x00, 0x00, 0x76, 0x8F,                      // decodingSoftware (30351.8)
            0x00, 0x00
        };

        private static readonly byte[] SenderTemplate =
        {
            0x00, 0x02, 0x00, 0x3C, 0x99, 0x93, 0x00, 0x07,                      // set 2, 60 bytes, template 0x9993, 7 fields
            0x80, 0x01, 0xFF, 0xFF, 0x00, 0x00, 0x76, 0x8F,                      // senderCallsign (30351.1), variable
            0x80, 0x05, 0x00, 0x04, 0x00, 0x00, 0x76, 0x8F,                      // frequency (30351.5), 4 bytes, Hz
            0x80, 0x06, 0x00, 0x01, 0x00, 0x00, 0x76, 0x8F,                      // sNR (30351.6), 1 byte signed
            0x80, 0x0A, 0xFF, 0xFF, 0x00, 0x00, 0x76, 0x8F,                      // mode (30351.10), variable
            0x80, 0x0B, 0x00, 0x01, 0x00, 0x00, 0x76, 0x8F,                      // informationSource (30351.11), 1 byte
            0x80, 0x03, 0xFF, 0xFF, 0x00, 0x00, 0x76, 0x8F,                      // senderLocator (30351.3), variable
            0x00, 0x96, 0x00, 0x04                                               // flowStartSeconds (150), 4 bytes
        };

        private static void u16(MemoryStream m, int v) { m.WriteByte((byte)(v >> 8)); m.WriteByte((byte)v); }
        private static void u32(MemoryStream m, uint v) { m.WriteByte((byte)(v >> 24)); m.WriteByte((byte)(v >> 16)); m.WriteByte((byte)(v >> 8)); m.WriteByte((byte)v); }

        private static void str(MemoryStream m, string s)
        {
            byte[] b = Encoding.UTF8.GetBytes(s ?? "");
            int n = Math.Min(b.Length, 254);
            m.WriteByte((byte)n);
            m.Write(b, 0, n);
        }

        private static void pad(MemoryStream m) { while (m.Length % 4 != 0) m.WriteByte(0); }

        private static void patchLength(MemoryStream m, long at, long to)
        {
            long end = m.Position;
            m.Position = at;
            u16(m, (int)(end - to));
            m.Position = end;
        }

        internal static byte[] Build(List<Spot> spots, bool templates, string rxCall, string rxGrid, string software, uint domain, uint sequence, DateTime nowUtc)
        {
            MemoryStream m = new MemoryStream();
            u16(m, 10);                                      // IPFIX version
            u16(m, 0);                                       // length, patched below
            u32(m, (uint)new DateTimeOffset(nowUtc).ToUnixTimeSeconds());
            u32(m, sequence);
            u32(m, domain);
            if (templates) { m.Write(ReceiverTemplate, 0, ReceiverTemplate.Length); m.Write(SenderTemplate, 0, SenderTemplate.Length); }

            // receiver record: your call sign, locator and the program
            long rs = m.Position;
            u16(m, 0x9992); u16(m, 0);
            str(m, rxCall); str(m, rxGrid); str(m, software);
            while ((m.Position - rs) % 4 != 0) m.WriteByte(0);
            patchLength(m, rs + 2, rs);

            // the spots
            if (spots.Count > 0)
            {
                long ss = m.Position;
                u16(m, 0x9993); u16(m, 0);
                foreach (Spot s in spots)
                {
                    str(m, s.Call);
                    u32(m, (uint)s.FreqHz);
                    m.WriteByte((byte)(sbyte)s.Snr);
                    str(m, s.Mode);
                    m.WriteByte(1);                          // informationSource: automatically extracted
                    str(m, s.Grid);
                    u32(m, (uint)new DateTimeOffset(DateTime.SpecifyKind(s.Utc, DateTimeKind.Utc)).ToUnixTimeSeconds());
                }
                while ((m.Position - ss) % 4 != 0) m.WriteByte(0);
                patchLength(m, ss + 2, ss);
            }
            patchLength(m, 2, 0);
            return m.ToArray();
        }
    }
}
