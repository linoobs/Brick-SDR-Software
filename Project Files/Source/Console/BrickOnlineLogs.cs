/*  BrickOnlineLogs.cs

    Every logged QSO (any mode) can be sent to online logbooks: QRZ.com Logbook, eQSL.cc, Club Log and Logbook of The World (LoTW, through the TQSL program).
    The settings are in digi_modes\online_logs.cfg; keys and passwords are stored encrypted for this Windows user (DPAPI), not as plain text. A QSO that can't be
    sent now (no network, the service is down) stays in digi_modes\upload_queue.txt and is tried again; one that the service refuses is moved to upload_failed.txt.

    The protocols: QRZ Logbook API (logbook.qrz.com/api, ACTION=INSERT), eQSL ImportADIF.cfm, Club Log REST API (clublog.org/api/v1/qso, a token of the user),
    TQSL command line (-d -u -a compliant -x -l location file).
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace BrickSDR
{
    internal sealed class OnlineLogsConfig
    {
        public bool Qrz; public string QrzKey = "";
        public bool Eqsl; public string EqslUser = "", EqslPass = "", EqslNick = "";
        public bool Club; public string ClubToken = "", ClubCall = "";
        public bool Lotw; public string TqslPath = "", TqslLocation = "";
    }

    internal static class OnlineLogs
    {
        public static readonly string[] Services = { "QRZ", "eQSL", "ClubLog", "LoTW" };
        private static readonly object gate = new object();
        private static OnlineLogsConfig cfg;
        private static Thread worker;
        private static volatile bool running;
        private static string folder = "", myCall = "";
        private static readonly Dictionary<string, string> status = new Dictionary<string, string>();
        private static DateTime nextTry = DateTime.MinValue;

        private static string CfgFile { get { return Path.Combine(folder, "online_logs.cfg"); } }
        private static string QueueFile { get { return Path.Combine(folder, "upload_queue.txt"); } }
        private static string FailedFile { get { return Path.Combine(folder, "upload_failed.txt"); } }

        // folder: the digi_modes folder of the data folder
        public static void Init(string digiFolder, string call)
        {
            folder = digiFolder; myCall = call ?? "";
            if (cfg == null) cfg = Load();
            if (worker == null)
            {
                running = true;
                worker = new Thread(Work) { IsBackground = true, Name = "Online logs" };
                worker.Start();
            }
        }

        public static void SetCall(string call) { myCall = call ?? ""; }

        public static OnlineLogsConfig Config { get { if (cfg == null) cfg = Load(); return cfg; } }

        public static string Status(string service) { lock (gate) { string s; return status.TryGetValue(service, out s) ? s : ""; } }
        private static void SetStatus(string service, string text) { lock (gate) status[service] = text; }

        public static int QueueLength { get { try { return File.Exists(QueueFile) ? File.ReadAllLines(QueueFile).Length : 0; } catch { return 0; } } }

        // ---- settings (secrets encrypted with DPAPI) ----

        private static string Protect(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            try { return "enc:" + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s), null, DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }

        private static string Unprotect(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (!s.StartsWith("enc:")) return s;                 // typed in the file by hand
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s.Substring(4)), null, DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }

        private static OnlineLogsConfig Load()
        {
            OnlineLogsConfig c = new OnlineLogsConfig();
            try
            {
                if (!File.Exists(CfgFile)) return c;
                foreach (string line in File.ReadAllLines(CfgFile))
                {
                    int e = line.IndexOf('=');
                    if (e < 1) continue;
                    string k = line.Substring(0, e), v = line.Substring(e + 1);
                    switch (k)
                    {
                        case "qrz": c.Qrz = v == "1"; break;
                        case "qrzkey": c.QrzKey = Unprotect(v); break;
                        case "eqsl": c.Eqsl = v == "1"; break;
                        case "equser": c.EqslUser = v; break;
                        case "eqpass": c.EqslPass = Unprotect(v); break;
                        case "eqnick": c.EqslNick = v; break;
                        case "club": c.Club = v == "1"; break;
                        case "clubtoken": c.ClubToken = Unprotect(v); break;
                        case "clubcall": c.ClubCall = v; break;
                        case "lotw": c.Lotw = v == "1"; break;
                        case "tqsl": c.TqslPath = v; break;
                        case "tqslloc": c.TqslLocation = v; break;
                    }
                }
            }
            catch { }
            return c;
        }

        public static void Save()
        {
            try
            {
                OnlineLogsConfig c = Config;
                Directory.CreateDirectory(folder);
                File.WriteAllLines(CfgFile, new[]
                {
                    "qrz=" + (c.Qrz ? 1 : 0), "qrzkey=" + Protect(c.QrzKey),
                    "eqsl=" + (c.Eqsl ? 1 : 0), "equser=" + c.EqslUser, "eqpass=" + Protect(c.EqslPass), "eqnick=" + c.EqslNick,
                    "club=" + (c.Club ? 1 : 0), "clubtoken=" + Protect(c.ClubToken), "clubcall=" + c.ClubCall,
                    "lotw=" + (c.Lotw ? 1 : 0), "tqsl=" + c.TqslPath, "tqslloc=" + c.TqslLocation
                });
            }
            catch { }
        }

        public static string FindTqsl()
        {
            foreach (string p in new[] { @"C:\Program Files (x86)\TrustedQSL\tqsl.exe", @"C:\Program Files\TrustedQSL\tqsl.exe" })
                if (File.Exists(p)) return p;
            return "";
        }

        // ---- the queue ----

        public static bool AnyEnabled { get { OnlineLogsConfig c = Config; return c.Qrz || c.Eqsl || c.Club || c.Lotw; } }

        // a QSO was logged: it goes to every service that is switched on
        public static void Enqueue(string adifRecord)
        {
            try
            {
                OnlineLogsConfig c = Config;
                List<string> lines = new List<string>();
                string b = Convert.ToBase64String(Encoding.UTF8.GetBytes(adifRecord));
                if (c.Qrz && c.QrzKey.Length > 0) lines.Add("QRZ\t" + b);
                if (c.Eqsl && c.EqslUser.Length > 0 && c.EqslPass.Length > 0) lines.Add("eQSL\t" + b);
                if (c.Club && c.ClubToken.Length > 0) lines.Add("ClubLog\t" + b);
                if (c.Lotw && c.TqslLocation.Length > 0) lines.Add("LoTW\t" + b);
                if (lines.Count == 0) return;
                lock (gate) File.AppendAllLines(QueueFile, lines);
                nextTry = DateTime.MinValue;
            }
            catch { }
        }

        private enum R { Ok, Duplicate, Retry, Fail, Manual }

        private static void Work()
        {
            while (running)
            {
                try
                {
                    if (DateTime.UtcNow >= nextTry) ProcessQueue();
                }
                catch { }
                Thread.Sleep(2000);
            }
        }

        private static void ProcessQueue()
        {
            List<string> lines;
            lock (gate) { if (!File.Exists(QueueFile)) return; lines = new List<string>(File.ReadAllLines(QueueFile)); }
            if (lines.Count == 0) return;
            List<string> keep = new List<string>();
            bool retry = false;
            foreach (string line in lines)
            {
                int t = line.IndexOf('\t');
                if (t < 1) continue;
                string svc = line.Substring(0, t), adif;
                try { adif = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(t + 1))); } catch { continue; }
                if (retry) { keep.Add(line); continue; }                       // the network was down a moment ago: wait
                string msg;
                R r = Send(svc, adif, out msg);
                string call = Regex.Match(adif, @"<CALL:\d+>([^<\s]+)", RegexOptions.IgnoreCase).Groups[1].Value;
                switch (r)
                {
                    case R.Ok: SetStatus(svc, DateTime.Now.ToString("HH:mm") + "  " + call + " sent"); break;
                    case R.Duplicate: SetStatus(svc, DateTime.Now.ToString("HH:mm") + "  " + call + " already there"); break;
                    case R.Retry: SetStatus(svc, DateTime.Now.ToString("HH:mm") + "  " + call + " will be sent later (" + msg + ")"); keep.Add(line); retry = true; break;
                    case R.Manual: SetStatus(svc, DateTime.Now.ToString("HH:mm") + "  " + call + " put into qrz_manual.adi (" + ManualCount + " QSOs waiting for a manual import)"); break;
                    default:
                        SetStatus(svc, DateTime.Now.ToString("HH:mm") + "  " + call + " refused: " + msg);
                        try { File.AppendAllText(FailedFile, svc + "\t" + DateTime.Now.ToString("s") + "\t" + msg + "\t" + adif + "\r\n"); } catch { }
                        break;
                }
            }
            lock (gate)
            {
                // what came into the queue meanwhile stays
                List<string> now = File.Exists(QueueFile) ? new List<string>(File.ReadAllLines(QueueFile)) : new List<string>();
                for (int i = lines.Count; i < now.Count; i++) keep.Add(now[i]);
                if (keep.Count == 0) { try { File.Delete(QueueFile); } catch { } } else File.WriteAllLines(QueueFile, keep);
            }
            if (retry) nextTry = DateTime.UtcNow.AddMinutes(2);
        }

        private static R Send(string svc, string adif, out string msg)
        {
            msg = "";
            try
            {
                switch (svc)
                {
                    case "QRZ": return SendQrz(adif, out msg);
                    case "eQSL": return SendEqsl(adif, out msg);
                    case "ClubLog": return SendClub(adif, out msg);
                    case "LoTW": return SendLotw(adif, out msg);
                }
                msg = "unknown service";
                return R.Fail;
            }
            catch (WebException ex)
            {
                HttpWebResponse hr = ex.Response as HttpWebResponse;
                msg = hr != null ? "HTTP " + (int)hr.StatusCode : ex.Message;
                return hr == null || (int)hr.StatusCode >= 500 ? R.Retry : R.Fail;
            }
            catch (Exception ex) { msg = ex.Message; return R.Retry; }
        }

        private static string UserAgent { get { return "Brick-SDR/" + BrickBranding.Version + (myCall.Length > 0 ? " (" + myCall + ")" : ""); } }

        private static string PostForm(string url, string body, string contentType, string bearer, out int code)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;                    // TLS 1.2
            HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
            rq.Method = "POST"; rq.UserAgent = UserAgent; rq.ContentType = contentType; rq.Timeout = 30000;
            if (bearer != null) rq.Headers["Authorization"] = "Bearer " + bearer;
            byte[] data = Encoding.UTF8.GetBytes(body);
            rq.ContentLength = data.Length;
            using (Stream s = rq.GetRequestStream()) s.Write(data, 0, data.Length);
            try
            {
                using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
                using (StreamReader sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8))
                { code = (int)rs.StatusCode; return sr.ReadToEnd(); }
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse)
            {
                HttpWebResponse hr = (HttpWebResponse)ex.Response;
                using (StreamReader sr = new StreamReader(hr.GetResponseStream(), Encoding.UTF8)) { code = (int)hr.StatusCode; return sr.ReadToEnd(); }
            }
        }

        private static Dictionary<string, string> Pairs(string text)
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string p in text.Trim().Split('&'))
            {
                int e = p.IndexOf('=');
                if (e > 0) d[p.Substring(0, e)] = WebUtility.UrlDecode(p.Substring(e + 1));
            }
            return d;
        }

        // ---- QRZ.com Logbook ----

        private static R SendQrz(string adif, out string msg)
        {
            int code;
            string body = "KEY=" + Uri.EscapeDataString(Config.QrzKey) + "&ACTION=INSERT&ADIF=" + Uri.EscapeDataString(adif);
            string resp = PostForm("https://logbook.qrz.com/api", body, "application/x-www-form-urlencoded", null, out code);
            Dictionary<string, string> d = Pairs(resp);
            string res; d.TryGetValue("RESULT", out res);
            string reason; d.TryGetValue("REASON", out reason);
            msg = reason ?? res ?? ("HTTP " + code);
            if (res == "OK") return R.Ok;
            if (res == "REPLACE") return R.Duplicate;
            // no paid QRZ subscription: the API can't be used, the QSO goes to a file that is imported by hand on the QRZ website
            if (res == "AUTH" && reason != null && reason.IndexOf("subscription", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AddManual(adif);
                msg = "no QRZ subscription";
                return R.Manual;
            }
            if (res == "FAIL" && reason != null && reason.IndexOf("duplicate", StringComparison.OrdinalIgnoreCase) >= 0) return R.Duplicate;
            if (code >= 500) return R.Retry;
            return R.Fail;
        }

        // ---- QRZ without a subscription: a file for the manual import (QRZ logbook - Import ADIF is free for every user) ----

        public static string ManualFile { get { return Path.Combine(folder, "qrz_manual.adi"); } }

        public static int ManualCount
        {
            get { try { return File.Exists(ManualFile) ? Regex.Matches(File.ReadAllText(ManualFile), "<EOR>", RegexOptions.IgnoreCase).Count : 0; } catch { return 0; } }
        }

        private static void AddManual(string adif)
        {
            lock (gate)
            {
                bool fresh = !File.Exists(ManualFile);
                File.AppendAllText(ManualFile, (fresh ? "Brick-SDR QSOs for the QRZ import\r\n<ADIF_VER:5>3.1.4 <PROGRAMID:9>Brick-SDR <EOH>\r\n" : "") + adif + "\r\n", Encoding.ASCII);
            }
        }

        // after the import on the website: the file is kept under another name, the next QSOs start a new one
        public static void ManualDone()
        {
            lock (gate)
            {
                try { if (File.Exists(ManualFile)) File.Move(ManualFile, Path.Combine(folder, "qrz_imported_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".adi")); } catch { }
            }
        }

        // the check of the key in Setup: reads the numbers of the logbook, writes nothing
        public static string QrzCheck(string key)
        {
            try
            {
                int code;
                string resp = PostForm("https://logbook.qrz.com/api", "KEY=" + Uri.EscapeDataString(key) + "&ACTION=STATUS", "application/x-www-form-urlencoded", null, out code);
                Dictionary<string, string> d = Pairs(resp);
                string res; d.TryGetValue("RESULT", out res);
                if (res == "OK")
                {
                    string data; d.TryGetValue("DATA", out data);
                    Dictionary<string, string> n = Pairs(data ?? "");
                    string cnt, owner, book;
                    n.TryGetValue("COUNT", out cnt); n.TryGetValue("CALLSIGN", out owner); n.TryGetValue("BOOK_ID", out book);
                    return "Key accepted" + (owner != null ? ", logbook of " + owner : "") + (cnt != null ? ", " + cnt + " QSOs" : "");
                }
                string reason; d.TryGetValue("REASON", out reason);
                return "Not accepted: " + (reason ?? res ?? resp.Trim());
            }
            catch (Exception ex) { return "No answer: " + ex.Message; }
        }

        // ---- eQSL.cc ----

        private static R SendEqsl(string adif, out string msg)
        {
            OnlineLogsConfig c = Config;
            string rec = adif;
            if (c.EqslNick.Length > 0 && rec.IndexOf("APP_EQSL_QTH_NICKNAME", StringComparison.OrdinalIgnoreCase) < 0)
                rec = "<APP_EQSL_QTH_NICKNAME:" + c.EqslNick.Length + ">" + c.EqslNick + " " + rec;
            string body = "EQSL_USER=" + Uri.EscapeDataString(c.EqslUser) + "&EQSL_PSWD=" + Uri.EscapeDataString(c.EqslPass) + "&ADIFData=" + Uri.EscapeDataString("Brick-SDR upload <EOH> " + rec);
            int code;
            string resp = PostForm("https://www.eQSL.cc/qslcard/ImportADIF.cfm", body, "application/x-www-form-urlencoded", null, out code);
            string plain = Regex.Replace(resp, "<[^>]+>", " ");
            plain = Regex.Replace(plain, @"\s+", " ");
            Match m = Regex.Match(plain, @"Result:\s*(\d+)\s+out of\s+(\d+)\s+records added", RegexOptions.IgnoreCase);
            if (m.Success && m.Groups[1].Value == m.Groups[2].Value && m.Groups[1].Value != "0") { msg = ""; return R.Ok; }
            if (plain.IndexOf("Duplicate", StringComparison.OrdinalIgnoreCase) >= 0) { msg = "duplicate"; return R.Duplicate; }
            Match er = Regex.Match(plain, @"(Error|Warning):[^<]{0,120}", RegexOptions.IgnoreCase);
            msg = er.Success ? er.Value.Trim() : "HTTP " + code;
            if (code != 200 || plain.IndexOf("down", StringComparison.OrdinalIgnoreCase) >= 0 && plain.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0) return R.Retry;
            return R.Fail;
        }

        // ---- Club Log (REST API: a token of the user, Settings - API Keys of the Club Log account) ----

        private static string JsonEsc(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char ch in s)
            {
                if (ch == '"') sb.Append("\\\""); else if (ch == '\\') sb.Append("\\\\");
                else if (ch == '\r') sb.Append("\\r"); else if (ch == '\n') sb.Append("\\n"); else if (ch < ' ') sb.Append(' '); else sb.Append(ch);
            }
            return sb.ToString();
        }

        private static R SendClub(string adif, out string msg)
        {
            OnlineLogsConfig c = Config;
            string call = c.ClubCall.Length > 0 ? c.ClubCall : myCall;
            int code;
            string json = "{\"call\":\"" + JsonEsc(call) + "\",\"adif\":\"" + JsonEsc(adif) + "\"}";
            string resp = PostForm("https://clublog.org/api/v1/qso", json, "application/json", c.ClubToken, out code);
            string message = Regex.Match(resp, "\"message\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value;
            msg = message.Length > 0 ? message : "HTTP " + code;
            if (code == 201) return R.Ok;
            if (code == 200) return R.Duplicate;
            if (code >= 500) return R.Retry;
            return R.Fail;                                                                // 403: the token is refused; 422: the QSO is refused
        }

        // ---- LoTW: TQSL signs and uploads ----

        private static R SendLotw(string adif, out string msg)
        {
            OnlineLogsConfig c = Config;
            string exe = c.TqslPath.Length > 0 ? c.TqslPath : FindTqsl();
            if (exe.Length == 0 || !File.Exists(exe)) { msg = "TQSL not found"; return R.Fail; }
            string tmp = Path.Combine(Path.GetTempPath(), "BrickSDR_lotw_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".adi");
            File.WriteAllText(tmp, "Brick-SDR\r\n<ADIF_VER:5>3.1.4 <PROGRAMID:9>Brick-SDR <EOH>\r\n" + adif + "\r\n", Encoding.ASCII);
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, "-d -u -a compliant -x -l \"" + c.TqslLocation.Replace("\"", "") + "\" \"" + tmp + "\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                using (Process p = Process.Start(psi))
                {
                    string err = p.StandardError.ReadToEndAsync().Result;
                    if (!p.WaitForExit(90000)) { try { p.Kill(); } catch { } msg = "TQSL did not finish (a password question?)"; return R.Fail; }
                    Match fm = Regex.Match(err, @"Final Status:\s*(.*?)\s*\((\d+)\)\s*$", RegexOptions.Multiline);
                    msg = fm.Success ? fm.Groups[1].Value : "exit code " + p.ExitCode;
                    int rc = fm.Success ? int.Parse(fm.Groups[2].Value) : p.ExitCode;
                    if (rc == 0 || rc == 9) return R.Ok;
                    if (rc == 8) return R.Duplicate;
                    if (rc == 11) return R.Retry;
                    return R.Fail;
                }
            }
            finally { try { File.Delete(tmp); } catch { } }
        }
    }
}
