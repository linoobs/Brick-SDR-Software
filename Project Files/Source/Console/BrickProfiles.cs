using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace BrickSDR
{
    // S-meter calibration and PA settings of the Brick boards: when a Brick board is found for the first time
    // the values from the XML of its model (exports of the Thetis database) are loaded.
    internal static class BrickProfiles
    {
        private static string AppliedFile(Console c) { return Path.Combine(c.AppDataPath, "brick_profiles_applied.txt"); }

        private static string XmlFor(string boardName)
        {
            switch (boardName)
            {
                case "Brick2 15W": return "Brick2_14b_15W.xml";
                case "Brick2 50W": return "Brick2_14b_50W.xml";
                case "Brick3 15W": return "Brick3_15W.xml";
            }
            return null;
        }

        public static void ApplyOnce(Console console, string boardName, string mac)
        {
            try
            {
                string xml = XmlFor(boardName);
                if (xml == null || string.IsNullOrEmpty(mac)) return;
                string file = Path.Combine(System.Windows.Forms.Application.StartupPath, xml);
                if (!File.Exists(file)) return;

                string done = AppliedFile(console);
                string key = mac + "=" + xml;
                if (File.Exists(done) && File.ReadAllLines(done).Any(l => l.Trim() == key)) return;

                XDocument doc = XDocument.Load(file);
                Dictionary<string, string> state = Table(doc, "State"), options = Table(doc, "Options");
                console.BrickApplyCalibration(state);
                if (!console.IsSetupFormNull) console.SetupForm.BrickApplyPAProfiles(options);

                File.AppendAllText(done, key + Environment.NewLine);
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(Path.Combine(console.AppDataPath, "brick_profiles_error.txt"), DateTime.Now + " " + ex + Environment.NewLine); } catch { }
            }
        }

        private static Dictionary<string, string> Table(XDocument doc, string name)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            foreach (XElement e in doc.Root.Elements(name))
            {
                XElement k = e.Element("Key"), v = e.Element("Value");
                if (k != null && v != null) d[k.Value] = v.Value;
            }
            return d;
        }
    }

    public partial class Console
    {
        // loads the S-meter / display calibration values of a Brick board (same keys the database uses)
        public void BrickApplyCalibration(Dictionary<string, string> st)
        {
            System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.CurrentCulture;
            Func<string, float> f = s => float.Parse(s, ci);
            foreach (KeyValuePair<string, string> kv in st)
            {
                string name = kv.Key, val = kv.Value;
                if (name.StartsWith("rx1_level_table[") || name.StartsWith("rx2_level_table["))
                {
                    int index = int.Parse(name.Substring(name.IndexOf('[') + 1, name.IndexOf(']') - name.IndexOf('[') - 1));
                    string[] list = val.Split('|');
                    float[][] table = name.StartsWith("rx1") ? rx1_level_table : rx2_level_table;
                    for (int i = 0; i < 3; i++) table[index][i] = (float)Math.Round(f(list[i]), 3);
                }
                else if (name.StartsWith("rx1_preamp_offset[") || name.StartsWith("rx2_preamp_offset["))
                {
                    int index = int.Parse(name.Substring(name.IndexOf('[') + 1, name.IndexOf(']') - name.IndexOf('[') - 1));
                    float[] t = name.StartsWith("rx1") ? rx1_preamp_offset : rx2_preamp_offset;
                    t[index] = (float)Math.Round(f(val), 3);
                }
                else if (name == "rx_meter_cal_offset_by_radio" || name == "rx_display_cal_offset_by_radio")
                {
                    string[] list = val.Split('|');
                    float[] t = name == "rx_meter_cal_offset_by_radio" ? rx_meter_cal_offset_by_radio : rx_display_cal_offset_by_radio;
                    if (list.Length <= (int)HPSDRModel.LAST)
                        for (int i = 0; i < list.Length; i++) t[i] = f(list[i]);
                }
            }
            // single values last, they are the ones of the selected radio
            if (st.ContainsKey("rx1_display_cal_offset")) RX1DisplayCalOffset = f(st["rx1_display_cal_offset"]);
            if (st.ContainsKey("rx1_meter_cal_offset")) RX1MeterCalOffset = f(st["rx1_meter_cal_offset"]);
            if (st.ContainsKey("rx2_display_cal_offset")) RX2DisplayCalOffset = f(st["rx2_display_cal_offset"]);
            if (st.ContainsKey("rx2_meter_cal_offset")) RX2MeterCalOffset = f(st["rx2_meter_cal_offset"]);
        }
    }

    public partial class Setup
    {
        // loads the PA profiles (and the selected one) of an exported database
        public void BrickApplyPAProfiles(Dictionary<string, string> a)
        {
            if (_PAProfiles == null || !a.ContainsKey("PAProfileCount")) return;
            int n = int.Parse(a["PAProfileCount"]);
            for (int i = 0; i < n; i++)
            {
                string key = "PAProfile_" + i;
                if (!a.ContainsKey(key)) continue;
                PAProfile p = new PAProfile("", HPSDRModel.FIRST, false);
                p.DataFromString(a[key]);
                if (_PAProfiles.ContainsKey(p.ProfileName)) _PAProfiles.Remove(p.ProfileName);
                _PAProfiles.Add(p.ProfileName, p);
            }
            updatePAProfileCombo();
            if (a.ContainsKey("comboPAProfile"))
            {
                string name = a["comboPAProfile"];
                for (int i = 0; i < comboPAProfile.Items.Count; i++)
                    if ((string)comboPAProfile.Items[i] == name) { comboPAProfile.Text = name; break; }
            }
        }
    }
}
