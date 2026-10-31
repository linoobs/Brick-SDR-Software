/*  BrickQsoLog.cs

    One place where every QSO of every mode is logged (FT8 / FT4 auto log, the Log QSO window for SSB / CW / RTTY / FreeDV / AM / FM):
    the record goes to digi_modes\qso_log.adi (ADIF 3) and, if switched on in Setup, to the online logbooks (BrickOnlineLogs.cs).
*/

using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace BrickSDR
{
    internal sealed class Qso
    {
        public string Call = "", Grid = "", Mode = "", Submode = "", RstSent = "", RstRcvd = "", Name = "", Comment = "";
        public double Mhz;
        public DateTime On, Off;
    }

    internal static class QsoAdif
    {
        private static string Ascii(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s ?? "") sb.Append(c >= ' ' && c < 127 ? c : '?');      // ADIF lengths count characters; keep it plain ASCII
            return sb.ToString();
        }

        public static string Band(double mhz)
        {
            double[][] t =
            {
                new[] { 1.8, 2.0 }, new[] { 3.5, 4.0 }, new[] { 5.06, 5.45 }, new[] { 7.0, 7.3 }, new[] { 10.1, 10.15 }, new[] { 14.0, 14.35 },
                new[] { 18.068, 18.168 }, new[] { 21.0, 21.45 }, new[] { 24.89, 24.99 }, new[] { 28.0, 29.7 }, new[] { 50.0, 54.0 }, new[] { 144.0, 148.0 }
            };
            string[] n = { "160m", "80m", "60m", "40m", "30m", "20m", "17m", "15m", "12m", "10m", "6m", "2m" };
            for (int i = 0; i < t.Length; i++) if (mhz >= t[i][0] && mhz <= t[i][1]) return n[i];
            return "";
        }

        public static string Record(Qso q, string myCall, string myGrid)
        {
            StringBuilder sb = new StringBuilder();
            Action<string, string> f = (k, v) => { v = Ascii(v); if (v.Length > 0) sb.Append("<" + k + ":" + v.Length + ">" + v + " "); };
            f("CALL", q.Call.ToUpperInvariant()); f("GRIDSQUARE", q.Grid);
            f("MODE", q.Mode); f("SUBMODE", q.Submode);
            f("BAND", Band(q.Mhz));
            f("FREQ", q.Mhz.ToString("0.000000", CultureInfo.InvariantCulture));
            f("RST_SENT", q.RstSent); f("RST_RCVD", q.RstRcvd);
            f("NAME", q.Name); f("COMMENT", q.Comment);
            f("QSO_DATE", q.On.ToString("yyyyMMdd")); f("TIME_ON", q.On.ToString("HHmmss"));
            f("QSO_DATE_OFF", q.Off.ToString("yyyyMMdd")); f("TIME_OFF", q.Off.ToString("HHmmss"));
            f("STATION_CALLSIGN", myCall); f("OPERATOR", myCall); f("MY_GRIDSQUARE", myGrid);
            sb.Append("<EOR>");
            return sb.ToString();
        }
    }

    public partial class Console
    {
        private string QsoLogFile { get { return Path.Combine(DigiModesFolder, "qso_log.adi"); } }

        internal void OnlineLogsInit() { OnlineLogs.Init(DigiModesFolder, _digiMyCall); }

        // writes the record to the local ADIF log and hands it to the online logbooks; returns the text for the status line
        internal string QsoLogAdd(Qso q)
        {
            string rec = QsoAdif.Record(q, _digiMyCall, _ftGrid);
            try
            {
                bool fresh = !File.Exists(QsoLogFile);
                File.AppendAllText(QsoLogFile, (fresh ? "Brick-SDR QSO log\r\n<ADIF_VER:5>3.1.4 <PROGRAMID:9>Brick-SDR <EOH>\r\n" : "") + rec + "\r\n", Encoding.ASCII);
            }
            catch { }
            OnlineLogs.Init(DigiModesFolder, _digiMyCall);
            OnlineLogs.SetCall(_digiMyCall);
            OnlineLogs.Enqueue(rec);
            return "Logged " + q.Call + (OnlineLogs.AnyEnabled ? " (online upload queued)" : "");
        }

        private static void qsoModeOf(DSPMode m, string digiKey, out string mode, out string sub)
        {
            sub = "";
            switch (m)
            {
                case DSPMode.LSB: mode = "SSB"; sub = "LSB"; break;
                case DSPMode.USB: mode = "SSB"; sub = "USB"; break;
                case DSPMode.CWL: case DSPMode.CWU: mode = "CW"; break;
                case DSPMode.AM: case DSPMode.SAM: mode = "AM"; break;
                case DSPMode.FM: mode = "FM"; break;
                case DSPMode.DIGU: case DSPMode.DIGL:
                    if (digiKey == "RADE") { mode = "DIGITALVOICE"; sub = "FREEDV"; }
                    else if (digiKey == "FT4") { mode = "MFSK"; sub = "FT4"; }
                    else if (digiKey == "FT8") mode = "FT8";
                    else mode = "RTTY";
                    break;
                default: mode = m.ToString(); break;
            }
        }

        // the window: call, RST, name, comment; frequency, mode and time come from the radio
        internal void brickLogQsoDialog(string call, int rx)
        {
            double mhz = rx == 1 ? VFOBFreq : VFOAFreq;
            DSPMode dm = rx == 1 ? _rx2_dsp_mode : _rx1_dsp_mode;
            string mode, sub;
            qsoModeOf(dm, _digiOpenKey, out mode, out sub);
            bool cw = mode == "CW", voice = mode == "SSB" || mode == "AM" || mode == "FM";
            DateTime start = DateTime.UtcNow;

            Form f = new Form
            {
                Text = "Log QSO", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false, ClientSize = new Size(340, 252), BackColor = Color.FromArgb(30, 32, 36), ForeColor = Color.Gainsboro,
                ShowInTaskbar = false
            };
            Label info = new Label { Left = 12, Top = 10, Width = 316, Height = 20, Text = mhz.ToString("0.000000", CultureInfo.InvariantCulture) + " MHz   " + mode + (sub.Length > 0 && mode != "SSB" ? " " + sub : sub.Length > 0 ? " " + sub : "") };
            f.Controls.Add(info);
            int y = 40;
            Func<string, string, TextBox> row = (cap, val) =>
            {
                f.Controls.Add(new Label { Left = 12, Top = y + 3, Width = 80, Height = 20, Text = cap });
                TextBox t = new TextBox { Left = 100, Top = y, Width = 228, Text = val, BackColor = Color.FromArgb(45, 48, 54), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
                f.Controls.Add(t); y += 32; return t;
            };
            TextBox tCall = row("Call", call ?? "");
            TextBox tSent = row("RST sent", cw ? "599" : voice ? "59" : "599");
            TextBox tRcvd = row("RST rcvd", cw ? "599" : voice ? "59" : "599");
            TextBox tName = row("Name", "");
            TextBox tCom = row("Comment", "");
            Button ok = new Button { Text = "Log", Left = 172, Top = y + 4, Width = 75, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 60), ForeColor = Color.White, DialogResult = DialogResult.None };
            Button cancel = new Button { Text = "Cancel", Left = 253, Top = y + 4, Width = 75, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 64, 70), ForeColor = Color.White, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) =>
            {
                if (tCall.Text.Trim().Length < 3) { tCall.Focus(); return; }
                Qso q = new Qso
                {
                    Call = tCall.Text.Trim(), Mhz = mhz, Mode = mode, Submode = sub, RstSent = tSent.Text.Trim(), RstRcvd = tRcvd.Text.Trim(),
                    Name = tName.Text.Trim(), Comment = tCom.Text.Trim(), On = start, Off = DateTime.UtcNow
                };
                string msg = QsoLogAdd(q);
                f.DialogResult = DialogResult.OK;
                try { MessageBox.Show(f.Owner, msg, "Log QSO", MessageBoxButtons.OK, MessageBoxIcon.Information); } catch { }
            };
            f.Controls.Add(ok); f.Controls.Add(cancel);
            f.AcceptButton = ok; f.CancelButton = cancel;
            f.Shown += (s, e) => { (tCall.Text.Length > 0 ? (Control)tSent : tCall).Focus(); };
            f.ShowDialog(this);
            f.Dispose();
        }
    }
}
