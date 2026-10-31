/*  BrickCallLookup.cs

    Fills in name, QTH and locator for a call sign typed into the log bar (BrickLogBar.cs). All sources are asked at the same time and
    what each of them knows is merged as the answers come in:
      HamQTH (free account, set in Setup > DIGI MODES > Online logs), QRZ.com XML (needs the paid XML subscription),
      callook.info (US), HamDB.org (US), RadioID.net (DMR users, worldwide), the program's own logs, and the country table (cty.csv).
    The first source that knows a field wins, in that order. Web pages of QRZ.com and Google can be opened by the buttons of the bar.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using Newtonsoft.Json.Linq;

namespace BrickSDR
{
    internal sealed class CallInfo
    {
        public string Name = "", Qth = "", Grid = "", Country = "";
        public readonly List<string> Sources = new List<string>();

        public bool Empty { get { return Name.Length == 0 && Qth.Length == 0 && Grid.Length == 0 && Country.Length == 0; } }

        // takes over the fields that are still empty
        public bool Merge(CallInfo o, string source)
        {
            bool got = false;
            if (Name.Length == 0 && o.Name.Length > 0) { Name = o.Name; got = true; }
            if (Qth.Length == 0 && o.Qth.Length > 0) { Qth = o.Qth; got = true; }
            if (Grid.Length == 0 && o.Grid.Length > 0) { Grid = o.Grid; got = true; }
            if (Country.Length == 0 && o.Country.Length > 0) { Country = o.Country; got = true; }
            if (got && !Sources.Contains(source)) Sources.Add(source);
            return got;
        }
    }

    internal static class CallLookup
    {
        // the call that the data bases know: DL1ABC/P and DL1ABC/QRP are DL1ABC, UR/DL1ABC is DL1ABC
        public static string BaseCall(string call)
        {
            call = (call ?? "").Trim().ToUpperInvariant();
            string best = "";
            foreach (string p in call.Split('/'))
                if (p.Length >= 3 && Regex.IsMatch(p, "[0-9]") && Regex.IsMatch(p, "[A-Z]") && p.Length >= best.Length) best = p;
            return best;
        }

        private static string Cap(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) return "";
            StringBuilder sb = new StringBuilder();
            bool up = true;
            foreach (char c in s.ToLowerInvariant())
            {
                sb.Append(up ? char.ToUpperInvariant(c) : c);
                up = c == ' ' || c == '-' || c == '\'';
            }
            return sb.ToString();
        }

        private static string Get(string url, string accept = null)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
            rq.UserAgent = "Brick-SDR/" + BrickBranding.Version;
            rq.Timeout = 8000; rq.ReadWriteTimeout = 8000;
            if (accept != null) rq.Accept = accept;
            using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
            using (StreamReader sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        // update(info, source) is called from worker threads each time a source added something; the returned task ends when all sources are done
        public static Task Run(string call, string digiFolder, Action<CallInfo, string> update)
        {
            string bc = BaseCall(call);
            if (bc.Length == 0) return Task.FromResult(0);
            CallInfo merged = new CallInfo();
            object gate = new object();
            OnlineLogsConfig cfg = OnlineLogs.Config;
            List<Task> tasks = new List<Task>();

            Action<string, Func<CallInfo>> add = (name, fn) => tasks.Add(Task.Run(() =>
            {
                try
                {
                    CallInfo r = fn();
                    if (r == null || r.Empty) return;
                    CallInfo snap = null;
                    lock (gate)
                    {
                        // sources are merged in the order of their answers; the order of the list above only decides when several answer together
                        if (merged.Merge(r, name)) snap = Copy(merged);
                    }
                    if (snap != null) update(snap, name);
                }
                catch { }
            }));

            if (cfg.HamQthUser.Length > 0 && cfg.HamQthPass.Length > 0) add("HamQTH", () => HamQth(bc, cfg.HamQthUser, cfg.HamQthPass));
            if (cfg.QrzXmlUser.Length > 0 && cfg.QrzXmlPass.Length > 0) add("QRZ XML", () => QrzXml(bc, cfg.QrzXmlUser, cfg.QrzXmlPass));
            add("callook.info", () => Callook(bc));
            add("HamDB", () => HamDb(bc));
            add("RadioID", () => RadioId(bc));
            add("own log", () => OwnLog(bc, digiFolder));
            add("cty", () => Cty(call));
            return Task.WhenAll(tasks);
        }

        private static CallInfo Copy(CallInfo i)
        {
            CallInfo c = new CallInfo { Name = i.Name, Qth = i.Qth, Grid = i.Grid, Country = i.Country };
            c.Sources.AddRange(i.Sources);
            return c;
        }

        // ---- the sources ----

        private static string HamQthSession, HamQthSessionUser;

        private static string Tag(XmlDocument d, XmlNamespaceManager ns, string name)
        {
            XmlNode n = d.SelectSingleNode("//h:" + name, ns);
            return n == null ? "" : n.InnerText.Trim();
        }

        private static CallInfo HamQth(string call, string user, string pass)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (HamQthSession == null || HamQthSessionUser != user)
                {
                    XmlDocument s = new XmlDocument();
                    s.LoadXml(Get("https://www.hamqth.com/xml.php?u=" + Uri.EscapeDataString(user) + "&p=" + Uri.EscapeDataString(pass)));
                    XmlNamespaceManager sn = new XmlNamespaceManager(s.NameTable); sn.AddNamespace("h", "https://www.hamqth.com");
                    string id = Tag(s, sn, "session_id");
                    if (id.Length == 0) return null;
                    HamQthSession = id; HamQthSessionUser = user;
                }
                XmlDocument d = new XmlDocument();
                d.LoadXml(Get("https://www.hamqth.com/xml.php?id=" + HamQthSession + "&callsign=" + Uri.EscapeDataString(call) + "&prg=Brick-SDR"));
                XmlNamespaceManager ns = new XmlNamespaceManager(d.NameTable); ns.AddNamespace("h", "https://www.hamqth.com");
                string err = Tag(d, ns, "error");
                if (err.Length > 0)
                {
                    if (err.IndexOf("Session", StringComparison.OrdinalIgnoreCase) >= 0) { HamQthSession = null; continue; }   // expired: log in again once
                    return null;
                }
                string nick = Tag(d, ns, "nick"), name = Tag(d, ns, "adr_name");
                string first = nick.Length > 0 ? nick : (name.Split(' ').Length > 0 ? name.Split(' ')[0] : "");
                return new CallInfo { Name = Cap(first), Qth = Cap(Tag(d, ns, "qth")), Grid = Tag(d, ns, "grid").ToUpperInvariant(), Country = Tag(d, ns, "country") };
            }
            return null;
        }

        private static string QrzXmlKey, QrzXmlKeyUser;

        private static CallInfo QrzXml(string call, string user, string pass)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (QrzXmlKey == null || QrzXmlKeyUser != user)
                {
                    XmlDocument s = new XmlDocument();
                    s.LoadXml(Get("https://xmldata.qrz.com/xml/current/?username=" + Uri.EscapeDataString(user) + ";password=" + Uri.EscapeDataString(pass) + ";agent=Brick-SDR"));
                    XmlNamespaceManager sn = new XmlNamespaceManager(s.NameTable); sn.AddNamespace("h", s.DocumentElement.NamespaceURI);
                    string key = Tag(s, sn, "Key");
                    if (key.Length == 0) return null;
                    QrzXmlKey = key; QrzXmlKeyUser = user;
                }
                XmlDocument d = new XmlDocument();
                d.LoadXml(Get("https://xmldata.qrz.com/xml/current/?s=" + QrzXmlKey + ";callsign=" + Uri.EscapeDataString(call)));
                XmlNamespaceManager ns = new XmlNamespaceManager(d.NameTable); ns.AddNamespace("h", d.DocumentElement.NamespaceURI);
                string err = Tag(d, ns, "Error");
                if (err.Length > 0)
                {
                    if (err.IndexOf("Session", StringComparison.OrdinalIgnoreCase) >= 0) { QrzXmlKey = null; continue; }
                    return null;
                }
                string city = Tag(d, ns, "addr2"), st = Tag(d, ns, "state");
                return new CallInfo { Name = Cap(Tag(d, ns, "fname").Split(' ')[0]), Qth = Cap(city) + (st.Length > 0 && Tag(d, ns, "country") == "United States" ? ", " + st : ""),
                                      Grid = Tag(d, ns, "grid").ToUpperInvariant(), Country = Tag(d, ns, "country") };
            }
            return null;
        }

        private static CallInfo Callook(string call)
        {
            JObject j = JObject.Parse(Get("https://callook.info/" + Uri.EscapeDataString(call) + "/json"));
            if ((string)j["status"] != "VALID") return null;
            string name = (string)j["name"] ?? "", line2 = (string)j["address"]?["line2"] ?? "";
            // "Joseph H Taylor Jr" is first name, initial, last name: the first word is the first name for a person (clubs have "Name" in capitals only)
            string[] w = name.Split(' ');
            string first = (string)j["type"] == "CLUB" ? "" : (w.Length > 1 ? w[0] : "");
            string city = line2.Contains(",") ? line2.Substring(0, line2.IndexOf(',')) : line2;
            string st = Regex.Match(line2, @",\s*([A-Z]{2})\b").Groups[1].Value;
            return new CallInfo { Name = Cap(first), Qth = Cap(city) + (st.Length > 0 ? ", " + st : ""), Grid = ((string)j["location"]?["gridsquare"] ?? "").ToUpperInvariant(), Country = "United States" };
        }

        private static CallInfo HamDb(string call)
        {
            JObject j = JObject.Parse(Get("https://api.hamdb.org/" + Uri.EscapeDataString(call) + "/json/Brick-SDR"));
            JToken c = j["hamdb"]?["callsign"];
            if (c == null || (string)c["call"] == "NOT_FOUND" || (string)c["fname"] == "NOT_FOUND") return null;
            string city = (string)c["addr2"] ?? "", st = (string)c["state"] ?? "";
            return new CallInfo { Name = Cap(((string)c["fname"] ?? "").Split(' ')[0]), Qth = Cap(city) + (st.Length > 0 ? ", " + st : ""), Grid = ((string)c["grid"] ?? "").ToUpperInvariant(), Country = "United States" };
        }

        private static CallInfo RadioId(string call)
        {
            JObject j = JObject.Parse(Get("https://radioid.net/api/dmr/user/?callsign=" + Uri.EscapeDataString(call)));
            JArray r = j["results"] as JArray;
            if (r == null || r.Count == 0) return null;
            JToken u = r[0];
            string st = (string)u["state"] ?? "";
            return new CallInfo { Name = Cap(((string)u["fname"] ?? "").Split(' ')[0]), Qth = Cap((string)u["city"] ?? "") + (st.Length > 0 ? ", " + Cap(st) : ""), Country = (string)u["country"] ?? "" };
        }

        // what the program logged before: the last name / QTH / locator given for this call
        private static CallInfo OwnLog(string call, string folder)
        {
            CallInfo r = new CallInfo();
            foreach (string file in new[] { "ft_log.adi", "qso_log.adi" })
            {
                string path = Path.Combine(folder, file);
                if (!File.Exists(path)) continue;
                foreach (string rec in Regex.Split(File.ReadAllText(path), "<EOR>", RegexOptions.IgnoreCase))
                {
                    Match m = Regex.Match(rec, @"<CALL:\d+>([^<\s]+)", RegexOptions.IgnoreCase);
                    if (!m.Success || !string.Equals(BaseCall(m.Groups[1].Value), call, StringComparison.OrdinalIgnoreCase)) continue;
                    string n = Field(rec, "NAME"), q = Field(rec, "QTH"), g = Field(rec, "GRIDSQUARE");
                    if (n.Length > 0) r.Name = n;
                    if (q.Length > 0) r.Qth = q;
                    if (g.Length > 0) r.Grid = g.ToUpperInvariant();
                }
            }
            return r;
        }

        private static string Field(string rec, string tag)
        {
            Match m = Regex.Match(rec, "<" + tag + @":(\d+)(?::[A-Z])?>", RegexOptions.IgnoreCase);
            if (!m.Success) return "";
            int len = int.Parse(m.Groups[1].Value), at = m.Index + m.Length;
            return at + len <= rec.Length ? rec.Substring(at, len).Trim() : "";
        }

        private static CallInfo Cty(string call)
        {
            CountryData.PrefixData d = CountryData.GetCallsignData(call.Trim().ToUpperInvariant());
            return d == null ? null : new CallInfo { Country = d.Country };
        }
    }
}
