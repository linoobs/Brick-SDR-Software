/*  BrickCluster.cs

    DX cluster spots on the panadapter. A telnet client (DX Spider / CC Cluster / AR-Cluster servers and the Reverse Beacon Network
    speak the same "DX de CALL: freq DXCALL comment time" lines) keeps the spots of the last minutes; Display draws them as call sign
    labels over the spectrum (Display.drawDxSpotsDX2D, BrickClusterDraw.cs) and a click on a label tunes the receiver to the spot.
    Settings are in dxcluster.txt of the data folder (Setup - DX Cluster); the DXC button next to 3D switches the labels
    (and the connection) on and off.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace BrickSDR
{
    internal sealed class DxSpot
    {
        public string Call, Spotter, Comment;
        public int Mode;                // 0 unknown, 1 CW, 2 phone, 3 digital
        public double Hz;
        public DateTime Time;           // UTC, when the spot arrived
    }

    internal static class DxCluster
    {
        public static readonly string[][] Presets =
        {
            new[] { "NC7J (DX cluster)", "dxc.nc7j.com", "7373" },
            new[] { "VE7CC (DX cluster)", "dxc.ve7cc.net", "23" },
            new[] { "DXFun (DX cluster)", "dxfun.com", "8000" },
            new[] { "HamQTH (DX cluster)", "hamqth.com", "7300" },
            new[] { "Reverse Beacon Network, CW / RTTY", "telnet.reversebeacon.net", "7000" },
            new[] { "Reverse Beacon Network, FT8 / FT4", "telnet.reversebeacon.net", "7001" },
        };

        // settings (dxcluster.txt)
        public static bool Enabled;
        public static string Host = "dxc.nc7j.com";
        public static int Port = 7373;
        public static string Login = "";            // empty = the call sign of the digital modes
        public static int MaxAgeMin = 30;
        public static int Rows = 3;
        public static bool ShowCw = true, ShowPhone = true, ShowDigital = true, ShowUnknown = true;
        public static bool ShowFreq;                // "14.025" after the call sign

        private static string _status = "Off";
        // BRICKSDR_DXLOG=<file> in the environment: every status change and the spot count go to that file (for tests)
        public static string Status
        {
            get { return _status; }
            set
            {
                _status = value;
                try { string f = Environment.GetEnvironmentVariable("BRICKSDR_DXLOG"); if (!string.IsNullOrEmpty(f)) File.AppendAllText(f, DateTime.Now.ToString("HH:mm:ss") + " " + value + "\r\n"); } catch { }
            }
        }
        public static string FileName;              // set by Console

        private static readonly object Lock = new object();
        private static readonly Dictionary<string, DxSpot> Spots = new Dictionary<string, DxSpot>();
        private static Thread _th;
        private static volatile bool _stop;
        private static TcpClient _cl;
        private static volatile int _version;
        private static string _myCall = "";

        public static void SetMyCall(string c) { _myCall = (c ?? "").Trim().ToUpperInvariant(); }

        // ---- settings
        public static void Load()
        {
            try
            {
                if (FileName == null || !File.Exists(FileName)) return;
                foreach (string line in File.ReadAllLines(FileName))
                {
                    int e = line.IndexOf('=');
                    if (e < 1) continue;
                    string k = line.Substring(0, e), v = line.Substring(e + 1).Trim();
                    int n;
                    switch (k)
                    {
                        case "on": Enabled = v == "1"; break;
                        case "host": if (v.Length > 0) Host = v; break;
                        case "port": if (int.TryParse(v, out n) && n > 0 && n < 65536) Port = n; break;
                        case "login": Login = v.ToUpperInvariant(); break;
                        case "age": if (int.TryParse(v, out n)) MaxAgeMin = Math.Max(1, Math.Min(720, n)); break;
                        case "rows": if (int.TryParse(v, out n)) Rows = Math.Max(1, Math.Min(8, n)); break;
                        case "cw": ShowCw = v != "0"; break;
                        case "phone": ShowPhone = v != "0"; break;
                        case "digital": ShowDigital = v != "0"; break;
                        case "unknown": ShowUnknown = v != "0"; break;
                        case "freq": ShowFreq = v == "1"; break;
                    }
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                if (FileName == null) return;
                File.WriteAllLines(FileName, new[]
                {
                    "on=" + (Enabled ? 1 : 0), "host=" + Host, "port=" + Port, "login=" + Login, "age=" + MaxAgeMin, "rows=" + Rows,
                    "cw=" + (ShowCw ? 1 : 0), "phone=" + (ShowPhone ? 1 : 0), "digital=" + (ShowDigital ? 1 : 0), "unknown=" + (ShowUnknown ? 1 : 0), "freq=" + (ShowFreq ? 1 : 0)
                });
            }
            catch { }
        }

        // ---- connection
        public static void Apply()
        {
            Stop();
            if (!Enabled) { Status = "Off"; return; }
            _stop = false;
            _th = new Thread(Run) { IsBackground = true, Name = "dxcluster" };
            _th.Start();
        }

        public static void Stop()
        {
            _stop = true;
            try { if (_cl != null) _cl.Close(); } catch { }
            Thread t = _th; _th = null;
            if (t != null && t != Thread.CurrentThread) t.Join(1500);
        }

        private static string CallToUse()
        {
            string c = Login.Length > 0 ? Login : _myCall;
            return c.Length >= 3 ? c : "";
        }

        private static void Run()
        {
            int wait = 10;
            while (!_stop)
            {
                string call = CallToUse();
                if (call.Length == 0) { Status = "Enter your call sign in Setup - DX Cluster"; Sleep(5); continue; }
                try
                {
                    Status = "Connecting to " + Host + ":" + Port + "...";
                    using (TcpClient cl = new TcpClient())
                    {
                        _cl = cl;
                        IAsyncResult ar = cl.BeginConnect(Host, Port, null, null);
                        if (!ar.AsyncWaitHandle.WaitOne(12000)) throw new Exception("no answer");
                        cl.EndConnect(ar);
                        NetworkStream ns = cl.GetStream();
                        ns.ReadTimeout = 600000;
                        Status = "Connected to " + Host + ", logging in...";
                        wait = 10;
                        Read(ns, call);
                    }
                }
                catch (Exception ex) { if (!_stop) Status = "Connection failed: " + ex.Message; }
                finally { _cl = null; }
                if (_stop) break;
                Status += "  (retry in " + wait + " s)";
                Sleep(wait);
                wait = Math.Min(300, wait * 2);
            }
        }

        private static void Sleep(int s) { for (int i = 0; i < s * 5 && !_stop; i++) Thread.Sleep(200); }

        private static void Read(NetworkStream ns, string call)
        {
            StringBuilder sb = new StringBuilder();
            bool sent = false;
            byte[] buf = new byte[2048];
            int spots = 0;
            while (!_stop)
            {
                int n = ns.Read(buf, 0, buf.Length);
                if (n <= 0) throw new Exception("closed by the server");
                for (int i = 0; i < n; i++)
                {
                    byte b = buf[i];
                    if (b == 255) { i += 2; continue; }             // telnet negotiation (IAC cmd opt)
                    if (b == '\n')
                    {
                        string line = sb.ToString().Trim(); sb.Length = 0;
                        if (line.Length > 0 && Parse(line)) { spots++; if (spots % 20 == 1 || spots < 3) Status = "Connected to " + Host + ":" + Port + ", " + Count() + " spots"; }
                    }
                    else if (b >= 32 || b == '\t') sb.Append((char)b);
                }
                if (!sent)
                {
                    string p = sb.ToString().Trim().ToLowerInvariant();
                    if ((p.EndsWith(":") || p.EndsWith(">")) && (p.Contains("call") || p.Contains("login") || p.Contains("name")))
                    {
                        byte[] o = Encoding.ASCII.GetBytes(call + "\r\n");
                        ns.Write(o, 0, o.Length);
                        sent = true; sb.Length = 0;
                        Status = "Logged in as " + call + ", waiting for spots...";
                    }
                }
            }
        }

        private static readonly Regex SpotRe = new Regex(@"^DX de\s+(\S+?):\s+([0-9]+(?:\.[0-9]+)?)\s+(\S+)\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TimeRe = new Regex(@"\s+\d{4}Z(\s+[A-Z]{2}\d{2})?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        internal static bool Parse(string line)
        {
            Match m = SpotRe.Match(line);
            if (!m.Success) return false;
            double khz;
            if (!double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out khz) || khz < 100 || khz > 300000) return false;
            string call = m.Groups[3].Value.ToUpperInvariant();
            if (call.Length < 3 || !call.Any(char.IsDigit)) return false;
            string comment = TimeRe.Replace(m.Groups[4].Value, "").Trim();
            DxSpot s = new DxSpot
            {
                Call = call, Spotter = m.Groups[1].Value.ToUpperInvariant(), Comment = comment, Hz = khz * 1000.0,
                Mode = ModeOf(comment), Time = DateTime.UtcNow
            };
            lock (Lock)
            {
                // one label per call and band: a new spot replaces the older one
                Spots[call + "|" + (int)(s.Hz / 100000)] = s;
                if (Spots.Count > 6000) Prune(true);
                _version++;
            }
            return true;
        }

        private static readonly Regex CwRe = new Regex(@"\b(CW|WPM|BEACON)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PhRe = new Regex(@"\b(SSB|USB|LSB|AM|FM|PHONE)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DgRe = new Regex(@"\b(FT8|FT4|RTTY|PSK\d*|JT\d+|MSK\d*|DIGI|DATA|OLIVIA|SSTV|WSPR|Q65)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static int ModeOf(string c)
        {
            if (DgRe.IsMatch(c)) return 3;
            if (CwRe.IsMatch(c)) return 1;
            if (PhRe.IsMatch(c)) return 2;
            return 0;
        }

        private static void Prune(bool hard)
        {
            DateTime lim = DateTime.UtcNow.AddMinutes(-MaxAgeMin);
            foreach (string k in Spots.Where(x => x.Value.Time < lim).Select(x => x.Key).ToList()) Spots.Remove(k);
            if (hard && Spots.Count > 4000)
                foreach (string k in Spots.OrderBy(x => x.Value.Time).Take(Spots.Count - 3000).Select(x => x.Key).ToList()) Spots.Remove(k);
        }

        public static int Count() { lock (Lock) return Spots.Count; }

        public static void Clear() { lock (Lock) { Spots.Clear(); _version++; } }

        private static DxSpot[] _snap = new DxSpot[0];
        private static int _snapVersion = -1;
        private static DateTime _snapAt = DateTime.MinValue;

        // the spots that are young enough and pass the mode filter, as a snapshot shared by the display threads (refreshed once a second or when a spot arrives)
        public static DxSpot[] Snapshot()
        {
            DateTime now = DateTime.UtcNow;
            if (_snapVersion == _version && (now - _snapAt).TotalSeconds < 1) return _snap;
            lock (Lock)
            {
                Prune(false);
                _snap = Spots.Values.Where(s => s.Mode == 1 ? ShowCw : s.Mode == 2 ? ShowPhone : s.Mode == 3 ? ShowDigital : ShowUnknown).OrderBy(s => s.Hz).ToArray();
                _snapVersion = _version; _snapAt = now;
            }
            return _snap;
        }
    }

    public partial class Console
    {
        private System.Windows.Forms.CheckBox chkDxc;

        private string DxClusterFile { get { return Path.Combine(AppDataPath, "dxcluster.txt"); } }

        // called once the layout exists: loads the settings and starts the connection if the DXC button was on
        private void brickDxClusterInit()
        {
            DxCluster.FileName = DxClusterFile;
            DxCluster.Load();
            DxCluster.SetMyCall(_digiMyCall);
            if (!_digiLoaded) { try { digiLoadSettings(); DxCluster.SetMyCall(_digiMyCall); } catch { } }
            if (chkDxc != null) chkDxc.Checked = DxCluster.Enabled;
            DxCluster.Apply();
        }

        private void brickDxClusterToggle(bool on)
        {
            if (DxCluster.Enabled == on) return;
            DxCluster.Enabled = on;
            DxCluster.SetMyCall(_digiMyCall);
            DxCluster.Save();
            DxCluster.Apply();
        }

        internal void BrickDxClusterChanged(bool reconnect)
        {
            DxCluster.SetMyCall(_digiMyCall);
            DxCluster.Save();
            if (reconnect) DxCluster.Apply();
        }

        internal bool BrickDxClusterOn
        {
            get { return DxCluster.Enabled; }
            set { if (chkDxc != null) chkDxc.Checked = value; else brickDxClusterToggle(value); }
        }

        // a click on a call sign label: tune the receiver it belongs to to the spot
        private bool brickDxClick(int x, int y)
        {
            if (!DxCluster.Enabled) return false;
            double hz; int rx;
            if (!Display.DxHitAt(x, y, out hz, out rx)) return false;
            try
            {
                if (rx == 2 && rx2_enabled) VFOBFreq = hz / 1e6;
                else VFOAFreq = hz / 1e6;
            }
            catch { }
            return true;
        }
    }
}
