/*  BrickCtyUpdate.cs

    A newer prefix table for CountryData (Setup - General - DIGI MODES - FT8/FT4 - Update prefix table): the "Big CTY" file of
    https://www.country-files.com/ (cty.csv) is downloaded into the data folder and read at every start, instead of the table that is
    built into the program. Prefixes and the calls of expeditions change often, and the countries that are not answered in FT8 / FT4 depend on it.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace BrickSDR
{
    internal static partial class CountryData
    {
        private const string CtyUrl = "https://www.country-files.com/bigcty/cty.csv";
        private static string _tableInfo = "No table loaded (use Update prefix table)";

        private static string UpdatedFile
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenHPSDR", "Brick-SDR-x64", "digi_modes", "cty.csv"); }
        }

        public static string TableInfo { get { return _tableInfo; } }

        // called from the static constructor
        private static void LoadUpdatedTable()
        {
            try
            {
                string f = UpdatedFile;
                int n;
                if (File.Exists(f) && ReplaceFromCsv(File.ReadAllText(f, Encoding.UTF8), out n)) { _tableInfo = "Updated table: " + n + " countries, " + File.GetLastWriteTime(f).ToString("yyyy-MM-dd"); return; }
                string b = Path.Combine(System.Windows.Forms.Application.StartupPath, "cty.csv");               // the one that comes with the program
                if (File.Exists(b) && ReplaceFromCsv(File.ReadAllText(b, Encoding.UTF8), out n)) _tableInfo = "Table of the program: " + n + " countries, " + File.GetLastWriteTime(b).ToString("yyyy-MM-dd");
            }
            catch { }
        }

        // downloads and uses the new table; the text says what happened
        public static string UpdateFromWeb()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;                    // TLS 1.2
                string text;
                using (WebClient wc = new WebClient { Encoding = Encoding.UTF8 })
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = "Brick-SDR";
                    text = wc.DownloadString(CtyUrl);
                }
                int n;
                if (!ReplaceFromCsv(text, out n)) return "The file did not look like a prefix table, the old one is kept";
                string f = UpdatedFile;
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllText(f, text, new UTF8Encoding(false));
                _tableInfo = "Updated table: " + n + " countries, " + DateTime.Now.ToString("yyyy-MM-dd");
                return _tableInfo;
            }
            catch (Exception ex) { return "Update failed: " + ex.Message; }
        }

        // cty.csv: primary prefix, country, ADIF, continent, CQ zone, ITU zone, latitude, longitude, GMT offset, prefixes and calls (space separated, ends with ;)
        // a prefix may carry (CQ zone) [ITU zone] <lat/lon> {continent} ~offset~ and a leading = means exactly this call
        internal static bool ReplaceFromCsv(string text, out int count)
        {
            count = 0;
            List<PrefixData> list = new List<PrefixData>();
            Dictionary<string, PrefixData> byName = new Dictionary<string, PrefixData>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length < 20) continue;
                string[] f = line.Split(new[] { ',' }, 10);
                if (f.Length < 10) continue;
                int adif, cq, itu; double lat, lon, gmt;
                if (!int.TryParse(f[2], out adif) || !int.TryParse(f[4], out cq) || !int.TryParse(f[5], out itu)) continue;
                double.TryParse(f[6], NumberStyles.Float, CultureInfo.InvariantCulture, out lat);
                double.TryParse(f[7], NumberStyles.Float, CultureInfo.InvariantCulture, out lon);
                double.TryParse(f[8], NumberStyles.Float, CultureInfo.InvariantCulture, out gmt);
                string country = f[1].Trim();
                if (country.Length == 0) continue;
                List<string> prefixes = new List<string> { f[0].Trim().TrimStart('*') };
                foreach (string tok in f[9].TrimEnd(';', ' ', '\r').Split(' '))
                {
                    string p = tok.Trim();
                    if (p.Length == 0) continue;
                    p = p.TrimStart('=');
                    int cut = p.IndexOfAny(new[] { '(', '[', '<', '{', '~' });
                    if (cut >= 0) p = p.Substring(0, cut);
                    if (p.Length > 0) prefixes.Add(p);
                }
                PrefixData d;
                if (!byName.TryGetValue(country, out d))
                {
                    string code = getCountryCode(country, adif);
                    d = new PrefixData { Country = country, CountryCode = code, AssetCode = getAssetCode(country, adif, code), ADIF = adif, CQZone = cq, ITUZone = itu, Continent = f[3].Trim(), Latitude = lat, Longitude = lon, GMTOffset = gmt };
                    byName[country] = d; list.Add(d);
                }
                d.Prefixes.AddRange(prefixes);
            }
            foreach (PrefixData d in list) d.Prefixes = d.Prefixes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (list.Count < 300 || !list.Any(x => x.Prefixes.Contains("DL"))) return false;            // not a real table: keep the old one
            _prefixDataList = list;
            count = list.Count;
            return true;
        }
    }
}
