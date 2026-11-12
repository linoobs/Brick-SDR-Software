/*  BrickUpdate.cs

    Update check: the program asks the GitHub releases of the project (latest release) a short time after the start and then every 6 hours.
    A newer version shows "Update x.y.z available" in the status bar; a click opens a window with a "What's new" button (the change log
    of the release), Download and install (downloads the .msi and starts the installer, the program closes and starts again) and
    Skip this version. The check can be switched off in Setup - General - Updates (update.txt: auto=, skip=). The check is a plain
    GET of the public release data, nothing about the user or the program is sent.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace BrickSDR
{
    public partial class Console
    {
        private const string UpdateApi = "https://api.github.com/repos/linoobs/Brick-SDR-Software/releases/latest";

        private bool _updAuto = true, _updLoaded;
        private string _updSkip = "";
        private DateTime _updNext = DateTime.MinValue, _updStart = DateTime.UtcNow;
        private volatile bool _updBusy;
        private ToolStripStatusLabel _updLabel;
        private Form _updForm;

        // result of the last check (null = nothing newer / not checked)
        private string _updVersion, _updNotes, _updPage, _updMsi;
        internal string UpdateStatus = "";
        internal bool UpdateBusy { get { return _updBusy; } }
        internal bool UpdateAvailable { get { return _updVersion != null; } }

        // the window of the update (also from the About window: it is TopMost, so this one has to be too)
        internal void UpdateShow() { updShowWindow(); if (_updForm != null) _updForm.TopMost = true; }

        private string UpdateFile { get { return Path.Combine(AppDataPath, "update.txt"); } }

        private void updLoad()
        {
            _updLoaded = true;
            try
            {
                if (!File.Exists(UpdateFile)) return;
                foreach (string line in File.ReadAllLines(UpdateFile))
                {
                    int e = line.IndexOf('=');
                    if (e < 1) continue;
                    string k = line.Substring(0, e), v = line.Substring(e + 1).Trim();
                    if (k == "auto") _updAuto = v != "0";
                    else if (k == "skip") _updSkip = v;
                }
            }
            catch { }
        }

        private void updSave()
        {
            try { File.WriteAllLines(UpdateFile, new[] { "auto=" + (_updAuto ? 1 : 0), "skip=" + _updSkip }); } catch { }
        }

        internal bool UpdateAuto
        {
            get { if (!_updLoaded) updLoad(); return _updAuto; }
            set { if (!_updLoaded) updLoad(); _updAuto = value; updSave(); }
        }

        private static bool updParse(string s, out Version v)
        {
            v = null;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim().TrimStart('v', 'V');
            int i = 0;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
            s = s.Substring(0, i).TrimEnd('.');
            if (s.IndexOf('.') < 0) s += ".0";
            return Version.TryParse(s, out v);
        }

        // called from brickTick
        private void brickUpdateTick()
        {
            try
            {
                if (initializing) return;
                if (!_updLoaded) updLoad();
                if (_updLabel == null && statusStripMain != null)
                {
                    _updLabel = new ToolStripStatusLabel
                    {
                        Name = "brickUpdateLabel", ForeColor = cAccent, BackColor = Color.Transparent, Overflow = ToolStripItemOverflow.Never,
                        Margin = new Padding(0, 0, 10, 0), IsLink = false, Visible = false, Font = new Font("Segoe UI Semibold", 9f)
                    };
                    _updLabel.Click += (s, e) => updShowWindow();
                    _updLabel.MouseEnter += (s, e) => { if (statusStripMain != null) statusStripMain.Cursor = Cursors.Hand; };
                    _updLabel.MouseLeave += (s, e) => { if (statusStripMain != null) statusStripMain.Cursor = Cursors.Default; };
                    int at = statusStripMain.Items.IndexOf(brickSpeedLabel);
                    statusStripMain.Items.Insert(at < 0 ? statusStripMain.Items.Count : at, _updLabel);
                }
                if (_updAuto && !_updBusy && DateTime.UtcNow >= _updNext && (DateTime.UtcNow - _updStart).TotalSeconds > 25)
                    updCheck(false);
                bool show = _updVersion != null;
                if (_updLabel != null)
                {
                    string t = show ? "Update " + _updVersion + " available" : "";
                    if (_updLabel.Text != t) _updLabel.Text = t;
                    if (_updLabel.Visible != show) _updLabel.Visible = show;
                }
            }
            catch { }
        }

        // asks GitHub in the background; manual = from the Setup button (shows the skipped version too and says "up to date")
        internal void updCheck(bool manual)
        {
            if (_updBusy) return;
            _updBusy = true;
            _updNext = DateTime.UtcNow.AddHours(6);
            UpdateStatus = "Checking...";
            Task.Run(() =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(UpdateApi);
                    rq.UserAgent = "Brick-SDR-update-check";
                    rq.Accept = "application/vnd.github+json";
                    rq.Timeout = 15000;
                    string json;
                    using (WebResponse rs = rq.GetResponse())
                    using (StreamReader sr = new StreamReader(rs.GetResponseStream()))
                        json = sr.ReadToEnd();
                    JObject j = JObject.Parse(json);
                    string tag = (string)j["tag_name"];
                    Version nv, cv;
                    if (!updParse(tag, out nv) || !updParse(BrickBranding.Version, out cv)) { UpdateStatus = "Could not read the version of the latest release"; return; }
                    if (nv <= cv) { _updVersion = null; UpdateStatus = "Brick-SDR " + BrickBranding.Version + " is up to date"; return; }
                    string ver = tag.Trim().TrimStart('v', 'V');
                    string msi = null;
                    JArray assets = j["assets"] as JArray;
                    if (assets != null)
                        foreach (JToken a in assets)
                            if (((string)a["name"] ?? "").EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) { msi = (string)a["browser_download_url"]; break; }
                    _updNotes = (string)j["body"] ?? "";
                    _updPage = (string)j["html_url"] ?? "";
                    _updMsi = msi;
                    UpdateStatus = "Version " + ver + " is available";
                    if (!manual && ver == _updSkip) { _updVersion = null; return; }
                    _updVersion = ver;
                }
                catch (Exception ex) { UpdateStatus = "Check failed: " + ex.Message; }
                finally { _updBusy = false; }
            });
        }

        private void updShowWindow()
        {
            if (_updVersion == null) return;
            if (_updForm != null && !_updForm.IsDisposed) { _updForm.Activate(); return; }

            Form f = new Form
            {
                Text = "Brick-SDR update", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, BackColor = cCard, ForeColor = cText, Font = new Font("Segoe UI", 9.5f),
                ClientSize = new Size(560, 150)
            };
            _updForm = f;
            Label head = new Label { AutoSize = false, Left = 16, Top = 14, Width = 528, Height = 26, Font = new Font("Segoe UI Semibold", 13f), ForeColor = cAccent, Text = "Brick-SDR " + _updVersion + " is available" };
            Label sub = new Label { AutoSize = false, Left = 16, Top = 44, Width = 528, Height = 22, ForeColor = cMuted, Text = "You have " + BrickBranding.Version + "." };
            Label state = new Label { AutoSize = false, Left = 16, Top = 70, Width = 528, Height = 20, ForeColor = cMuted, Text = "" };
            ProgressBar pb = new ProgressBar { Left = 16, Top = 92, Width = 528, Height = 8, Visible = false, Minimum = 0, Maximum = 100 };

            RichTextBox notes = new RichTextBox
            {
                Left = 16, Top = 160, Width = 528, Height = 230, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = cField, ForeColor = cText,
                Font = new Font("Consolas", 10f), DetectUrls = false, Visible = false, Text = string.IsNullOrWhiteSpace(_updNotes) ? "No change log in this release." : _updNotes.Replace("\r\n", "\n").Replace("\n", "\r\n")
            };

            BrickBtn bNews = new BrickBtn { Text = "What's new  ▼", Left = 16, Top = 108, Width = 120, Height = 30 };
            BrickBtn bUpd = new BrickBtn { Text = "Download and install", Left = 146, Top = 108, Width = 190, Height = 30, OnColor = cAccent, ForeColor = cAccent };
            BrickBtn bDown = bUpd;      // one button: download the installer and run it
            BrickBtn bSkip = new BrickBtn { Text = "Skip this version", Left = 346, Top = 108, Width = 120, Height = 30 };
            BrickBtn bClose = new BrickBtn { Text = "Later", Left = 476, Top = 108, Width = 68, Height = 30 };

            bNews.Click += (s, e) =>
            {
                bool open = !notes.Visible;
                notes.Visible = open;
                bNews.Text = open ? "What's new  ▲" : "What's new  ▼";
                f.ClientSize = new Size(560, open ? 404 : 150);
            };
            bClose.Click += (s, e) => f.Close();
            bSkip.Click += (s, e) => { _updSkip = _updVersion; updSave(); _updVersion = null; f.Close(); };

            Func<string> target = () => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "BrickSDR-Setup_" + _updVersion + ".msi");
            bool working = false;
            Action<bool> run = install =>
            {
                if (working) return;
                if (string.IsNullOrEmpty(_updMsi))
                {
                    try { if (!string.IsNullOrEmpty(_updPage)) Process.Start(_updPage); } catch { }
                    state.Text = "This release has no installer file; its page is opened in the browser.";
                    return;
                }
                if (install && _mox) { state.Text = "The radio is transmitting. Update after the transmission."; return; }
                working = true; bDown.Enabled = bUpd.Enabled = false; pb.Visible = true; pb.Value = 0;
                state.Text = "Downloading...";
                string url = _updMsi;
                string file = install ? Path.Combine(Path.GetTempPath(), "BrickSDR-Setup_" + _updVersion + ".msi") : target();
                Task.Run(() =>
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(file));
                        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                        HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
                        rq.UserAgent = "Brick-SDR-update-check";
                        rq.Timeout = 30000; rq.ReadWriteTimeout = 30000;
                        using (WebResponse rs = rq.GetResponse())
                        using (Stream src = rs.GetResponseStream())
                        using (FileStream dst = new FileStream(file + ".part", FileMode.Create))
                        {
                            long total = rs.ContentLength, got = 0;
                            byte[] buf = new byte[81920];
                            int n, last = -1;
                            while ((n = src.Read(buf, 0, buf.Length)) > 0)
                            {
                                dst.Write(buf, 0, n); got += n;
                                int pc = total > 0 ? (int)(got * 100 / total) : 0;
                                if (pc != last) { last = pc; try { f.BeginInvoke(new Action(() => { pb.Value = Math.Min(100, pc); state.Text = "Downloading... " + pc + " %  (" + (got >> 20) + " MB)"; })); } catch { } }
                            }
                        }
                        if (File.Exists(file)) File.Delete(file);
                        File.Move(file + ".part", file);
                        f.BeginInvoke(new Action(() =>
                        {
                            working = false; bDown.Enabled = bUpd.Enabled = true; pb.Visible = false;
                            if (!install)
                            {
                                state.Text = "Saved: " + file;
                                try { Process.Start("explorer.exe", "/select,\"" + file + "\""); } catch { }
                                return;
                            }
                            try
                            {
                                // one UAC question, then no clicks: the helper waits for this program to exit, installs with a progress bar only (/passive)
                                // and starts the new program again (through explorer, so it does not run elevated)
                                string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Brick-SDR", "BrickSDR.exe");
                                string cmd = "Wait-Process -Id " + Process.GetCurrentProcess().Id + " -ErrorAction SilentlyContinue; " +
                                             "Start-Process msiexec.exe -ArgumentList '/i','\"" + file + "\"','/passive','/norestart' -Wait; " +
                                             "if (Test-Path '" + exe + "') { Start-Process explorer.exe -ArgumentList '\"" + exe + "\"' }";
                                ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -Command \"" + cmd.Replace("\"", "\\\"") + "\"")
                                    { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                                Process.Start(psi);
                            }
                            catch (Exception ex) { state.Text = "The installer was not started: " + ex.Message; return; }
                            f.Close();
                            BeginInvoke(new Action(() => Close()));       // the installer replaces the program files: the program closes now
                        }));
                    }
                    catch (Exception ex)
                    {
                        try { File.Delete(file + ".part"); } catch { }
                        try { f.BeginInvoke(new Action(() => { working = false; bDown.Enabled = bUpd.Enabled = true; pb.Visible = false; state.Text = "Download failed: " + ex.Message; })); } catch { }
                    }
                });
            };
            bUpd.Click += (s, e) => run(true);

            f.Controls.AddRange(new Control[] { head, sub, state, pb, bNews, bUpd, bSkip, bClose, notes });
            f.FormClosed += (s, e) => { if (_updForm == f) _updForm = null; };
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(Math.Max(0, Left + (Width - f.Width) / 2), Math.Max(0, Top + (Height - f.Height) / 3));
            f.Show(this);
        }
    }
}
