/*  BrickDigiSetup.cs

    Setup - General - DIGI MODES (after Navigation): a tab for the digital modes with a page each for FT8 / FT4, RTTY and FreeDV.
    The FT8 / FT4 page has the Network Services group with PSK Reporter spotting (BrickPskReporter.cs).
    Built at run time like the other Brick additions to Setup; the choices are kept in digital.txt, not in the Setup database.
*/

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace BrickSDR
{
    public partial class Setup
    {
        private TabPage tpBrickDigi;
        private CheckBox brickChkPsk;
        private Label brickPskStatus;
        private Timer brickDigiTimer;

        // Online logs: the QSOs of every mode go to QRZ.com, eQSL, Club Log and LoTW (BrickOnlineLogs.cs)
        private void BrickBuildOnlineLogsPage(TabControl tc)
        {
            console.OnlineLogsInit();
            OnlineLogsConfig c = OnlineLogs.Config;
            TabPage pg = new TabPage("Online logs") { BackColor = SystemColors.Control, AutoScroll = true };
            tc.TabPages.Add(pg);
            Func<string, int, int, int, GroupBox> grp = (t, y, h, x) => { GroupBox g = new GroupBox { Text = t, Location = new Point(10, y), Size = new Size(680, h) }; pg.Controls.Add(g); return g; };
            Func<GroupBox, string, int, int, int, string, bool, TextBox> field = (g, cap, x, y, w, val, pass) =>
            {
                g.Controls.Add(new Label { Text = cap, AutoSize = true, Location = new Point(x, y + 3) });
                TextBox t = new TextBox { Location = new Point(x + 90, y), Width = w, Text = val, UseSystemPasswordChar = pass };
                g.Controls.Add(t); return t;
            };
            Label hint = new Label { AutoSize = false, Location = new Point(12, 6), Size = new Size(660, 44), ForeColor = SystemColors.GrayText,
                Text = "Every QSO logged in any mode (FT8 / FT4 automatically, the others with Log QSO in the DIGI menu or the RTTY / CW window) is sent to the services switched on here. Keys and passwords are stored encrypted for this Windows user." };
            pg.Controls.Add(hint);

            GroupBox gq = grp("QRZ.com Logbook", 54, 100, 0);
            CheckBox cq = new CheckBox { Text = "Upload", AutoSize = true, Location = new Point(16, 24), Checked = c.Qrz };
            TextBox tq = field(gq, "API key", 90, 22, 190, c.QrzKey, true);
            Button bq = new Button { Text = "Check key", Location = new Point(400, 20), Size = new Size(90, 25) };
            Label lq = new Label { AutoSize = false, Location = new Point(16, 50), Size = new Size(640, 46) };
            gq.Controls.AddRange(new Control[] { cq, bq, lq });
            cq.CheckedChanged += (s, e) => { c.Qrz = cq.Checked; OnlineLogs.Save(); };
            tq.TextChanged += (s, e) => { c.QrzKey = tq.Text.Trim(); OnlineLogs.Save(); };
            bq.Click += (s, e) =>
            {
                bq.Enabled = false; lq.Text = "Checking...";
                string key = tq.Text.Trim();
                System.Threading.Tasks.Task.Run(() => { string r = OnlineLogs.QrzCheck(key); try { BeginInvoke(new Action(() => { lq.Text = r; bq.Enabled = true; })); } catch { } });
            };

            GroupBox ge = grp("eQSL.cc", 162, 80, 0);
            CheckBox ce = new CheckBox { Text = "Upload", AutoSize = true, Location = new Point(16, 24), Checked = c.Eqsl };
            TextBox teu = field(ge, "User", 90, 22, 110, c.EqslUser, false);
            Label lp = new Label { Text = "Password", AutoSize = true, Location = new Point(310, 25) };
            TextBox tep = new TextBox { Location = new Point(380, 22), Width = 110, Text = c.EqslPass, UseSystemPasswordChar = true };
            Label ln = new Label { Text = "QTH nickname (optional)", AutoSize = true, Location = new Point(16, 53) };
            TextBox ten = new TextBox { Location = new Point(160, 50), Width = 120, Text = c.EqslNick };
            ge.Controls.AddRange(new Control[] { ce, lp, tep, ln, ten });
            ce.CheckedChanged += (s, e) => { c.Eqsl = ce.Checked; OnlineLogs.Save(); };
            teu.TextChanged += (s, e) => { c.EqslUser = teu.Text.Trim(); OnlineLogs.Save(); };
            tep.TextChanged += (s, e) => { c.EqslPass = tep.Text; OnlineLogs.Save(); };
            ten.TextChanged += (s, e) => { c.EqslNick = ten.Text.Trim(); OnlineLogs.Save(); };

            GroupBox gc = grp("Club Log", 250, 130, 0);
            CheckBox cc = new CheckBox { Text = "Upload", AutoSize = true, Location = new Point(16, 24), Checked = c.Club };
            TextBox tct = field(gc, "API token", 90, 22, 300, c.ClubToken, true);
            Label lcc = new Label { Text = "Call sign", AutoSize = true, Location = new Point(90, 53) };
            TextBox tcc = new TextBox { Location = new Point(180, 50), Width = 110, Text = c.ClubCall };
            Label lcn = new Label { AutoSize = false, Location = new Point(16, 78), Size = new Size(640, 44), ForeColor = SystemColors.GrayText,
                Text = "The token is made in Club Log: Settings - API keys. The call sign is the one of your Club Log logbook (empty = the call sign of the FT8 / RTTY window)." };
            gc.Controls.AddRange(new Control[] { cc, lcc, tcc, lcn });
            cc.CheckedChanged += (s, e) => { c.Club = cc.Checked; OnlineLogs.Save(); };
            tct.TextChanged += (s, e) => { c.ClubToken = tct.Text.Trim(); OnlineLogs.Save(); };
            tcc.TextChanged += (s, e) => { c.ClubCall = tcc.Text.Trim().ToUpperInvariant(); OnlineLogs.Save(); };

            GroupBox gl = grp("LoTW (through TQSL)", 390, 128, 0);
            CheckBox cl = new CheckBox { Text = "Upload", AutoSize = true, Location = new Point(16, 24), Checked = c.Lotw };
            TextBox tlp = field(gl, "TQSL", 90, 22, 300, string.IsNullOrEmpty(c.TqslPath) ? OnlineLogs.FindTqsl() : c.TqslPath, false);
            TextBox tll = field(gl, "Station location", 90, 50, 190, c.TqslLocation, false);
            Button bl = new Button { Text = "Station locations...", Location = new Point(500, 48), Size = new Size(130, 25) };
            Label ll = new Label { AutoSize = false, Location = new Point(16, 78), Size = new Size(640, 44), ForeColor = SystemColors.GrayText,
                Text = "Needs TQSL with your certificate and a Station Location (name it above). TQSL signs and uploads; a password-protected certificate can't be used here." };
            gl.Controls.AddRange(new Control[] { cl, bl, ll });
            cl.CheckedChanged += (s, e) => { c.Lotw = cl.Checked; OnlineLogs.Save(); };
            tlp.TextChanged += (s, e) => { c.TqslPath = tlp.Text.Trim(); OnlineLogs.Save(); };
            tll.TextChanged += (s, e) => { c.TqslLocation = tll.Text.Trim(); OnlineLogs.Save(); };
            bl.Click += (s, e) =>
            {
                string exe = tlp.Text.Trim();
                try { if (System.IO.File.Exists(exe)) System.Diagnostics.Process.Start(exe, "-s"); } catch { }
            };

            Label st = new Label { AutoSize = false, Location = new Point(12, 526), Size = new Size(640, 70) };
            pg.Controls.Add(st);
            Timer tm = new Timer { Interval = 1000 };
            tm.Tick += (s, e) =>
            {
                if (!pg.Visible) return;
                st.Text = "Waiting in the queue: " + OnlineLogs.QueueLength + "\r\n" +
                          string.Join("\r\n", OnlineLogs.Services.Select(x => x + ": " + OnlineLogs.Status(x)).Where(x => !x.EndsWith(": ")));
            };
            tm.Start();
        }

        public void BrickAddDigiModesTab()
        {
            if (tpBrickDigi != null) return;
            tpBrickDigi = new TabPage("DIGI MODES") { Name = "tpBrickDigiModes", BackColor = SystemColors.Control };
            TabControl tc = new TabControl { Name = "brickTcDigiModes", Dock = DockStyle.Fill };
            TabPage ft = new TabPage("FT8/FT4") { BackColor = SystemColors.Control };
            TabPage rtty = new TabPage("RTTY") { BackColor = SystemColors.Control };
            TabPage fdv = new TabPage("FreeDV") { BackColor = SystemColors.Control };
            tc.TabPages.AddRange(new[] { ft, rtty, fdv });
            tpBrickDigi.Controls.Add(tc);

            // FT8 / FT4: Network Services
            GroupBox grp = new GroupBox { Text = "Network Services", Location = new Point(10, 10), Size = new Size(680, 128) };
            brickChkPsk = new CheckBox { Name = "brickChkFtPsk", Text = "Enable PSK Reporter Spotting", AutoSize = true, Location = new Point(16, 26) };
            brickChkPsk.Checked = console.FtPskEnabled;
            brickChkPsk.CheckedChanged += (s, e) => { console.FtSetPsk(brickChkPsk.Checked); brickPskStatus.Text = "Status: " + console.FtPskStatus; };
            Label info = new Label
            {
                AutoSize = false, Location = new Point(34, 50), Size = new Size(630, 44),
                Text = "Sends the stations decoded in FT8 / FT4 (call sign, locator, SNR, frequency, mode and time of the signal), with your call sign and " +
                       "locator from the FT8 / FT4 window, to pskreporter.info. A report goes out about every five minutes while FT8 / FT4 is running."
            };
            brickPskStatus = new Label { AutoSize = false, Location = new Point(34, 98), Size = new Size(630, 20), Text = "Status: " + console.FtPskStatus };
            grp.Controls.AddRange(new Control[] { brickChkPsk, info, brickPskStatus });
            ft.Controls.Add(grp);

            // FT8 / FT4: stations that are not answered
            GroupBox gb = new GroupBox { Text = "Stations that are not answered", Location = new Point(10, 146), Size = new Size(680, 256) };
            ComboBox cb = new ComboBox { Location = new Point(16, 54), Width = 250, DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
            try { cb.Items.AddRange(CountryData.AllCountries().ToArray()); } catch { }
            Button add = new Button { Text = "Add", Location = new Point(274, 52), Size = new Size(70, 25) };
            ListBox lb = new ListBox { Location = new Point(16, 88), Size = new Size(328, 132), IntegralHeight = false };
            Button rem = new Button { Text = "Remove", Location = new Point(354, 88), Size = new Size(80, 25) };
            Label ghelp = new Label
            {
                AutoSize = false, Location = new Point(16, 20), Size = new Size(650, 30),
                Text = "Choose a country (or type a call sign): the stations of these countries are shown dimmed in the lists, can't be clicked and are never answered by the auto-sequence."
            };
            Label gnote = new Label
            {
                AutoSize = false, Location = new Point(354, 120), Size = new Size(312, 100), ForeColor = SystemColors.GrayText,
                Text = "A call sign entry works too: \"3Y0K\" is that call only, \"UA*\" is every call starting with UA. Use it for expedition calls and new prefixes: the country table inside the program is from March 2026 and doesn't know them. A call the table doesn't know is never blocked."
            };
            CheckBox unk = new CheckBox { Text = "Do not answer stations whose country is unknown to the table (hashed calls, new prefixes, expeditions)", AutoSize = true, Location = new Point(16, 226) };
            unk.Checked = console.FtBlockUnknown;
            unk.CheckedChanged += (s, e) => console.FtBlockUnknown = unk.Checked;
            Button upd = new Button { Text = "Update prefix table", Location = new Point(354, 52), Size = new Size(140, 25) };
            Label updInfo = new Label { AutoSize = false, Location = new Point(500, 52), Size = new Size(176, 28), ForeColor = SystemColors.GrayText, Text = CountryData.TableInfo };
            upd.Click += (s, e) =>
            {
                upd.Enabled = false; updInfo.Text = "Downloading...";
                System.Threading.Tasks.Task.Run(() =>
                {
                    string msg = CountryData.UpdateFromWeb();
                    try { BeginInvoke(new Action(() => { updInfo.Text = msg; upd.Enabled = true; })); } catch { }
                });
            };
            Action fill = () => { lb.Items.Clear(); foreach (string s in console.FtBlockedCountries) lb.Items.Add(s); };
            add.Click += (s, e) =>
            {
                string t = cb.Text.Trim();
                if (t.Length == 0) return;
                // "Russia" is several entries of the table (European, Asiatic, Kaliningrad...): a name that is part of country names adds all of them
                if (t.Length >= 4 && t.IndexOf(' ') < 0 && !t.Any(char.IsDigit) && !t.EndsWith("*"))
                {
                    List<string> part = new List<string>();
                    foreach (object o in cb.Items) if (((string)o).IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) part.Add((string)o);
                    if (part.Count > 0 && !part.Exists(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase)))
                    {
                        foreach (string x in part) console.FtBlockAdd(x);
                        cb.Text = ""; fill(); updInfo.Text = "Added: " + string.Join(", ", part);
                        return;
                    }
                }
                foreach (object o in cb.Items) if (string.Equals((string)o, t, StringComparison.OrdinalIgnoreCase)) { t = (string)o; break; }
                if (t.IndexOf(' ') < 0 && !cb.Items.Contains(t)) t = t.ToUpperInvariant();            // a call sign or prefix
                console.FtBlockAdd(t); cb.Text = ""; fill();
            };
            rem.Click += (s, e) => { if (lb.SelectedItem != null) { console.FtBlockRemove((string)lb.SelectedItem); fill(); } };
            fill();
            gb.Controls.AddRange(new Control[] { ghelp, cb, add, lb, rem, gnote, unk, upd, updInfo });
            ft.Controls.Add(gb);

            rtty.Controls.Add(new Label { AutoSize = false, Location = new Point(12, 14), Size = new Size(680, 40), Text = "The RTTY settings (shift, baud, reverse, AFC, squelch, TX level) are in the RTTY window: DIGI menu - RTTY." });
            fdv.Controls.Add(new Label { AutoSize = false, Location = new Point(12, 14), Size = new Size(680, 40), Text = "The FreeDV settings (call sign, version, noise reduction, AGC, equalizer, Reporter) are in the FreeDV window: the Settings button of the FreeDV strip." });

            BrickBuildOnlineLogsPage(tc);

            tcGeneral.TabPages.Add(tpBrickDigi);       // the last page, after Navigation

            // the status line follows the reporter while the tab is shown
            brickDigiTimer = new Timer { Interval = 1000 };
            brickDigiTimer.Tick += (s, e) =>
            {
                if (!tpBrickDigi.Visible) return;
                string t = "Status: " + console.FtPskStatus;
                if (brickPskStatus.Text != t) brickPskStatus.Text = t;
                bool on = console.FtPskEnabled;
                if (brickChkPsk.Checked != on) brickChkPsk.Checked = on;
            };
            brickDigiTimer.Start();
        }
    }
}
