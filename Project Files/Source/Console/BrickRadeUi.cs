/*  BrickRadeUi.cs

    The FreeDV (RADE) window of Brick-SDR and its settings. The modem is Thetis-RADE's (Christos Nikolaou SV1EIA,
    https://github.com/sv1eia/Thetis-RADE), brought over through Kainos by Justin Cron K7JUS; the window is Brick-SDR's own,
    its status panels follow Kainos's FreeDV window.
*/

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace BrickSDR
{
    public partial class Console
    {
        // ---- settings (rade.txt in the data folder) ----

        private sealed class RadeSettings
        {
            public string Call = "";
            public bool V2Rx1, V2Rx2, VoiceModes, Noise, Agc, Eq;
            public string Grid = "", ReporterMessage = "";
            public bool Reporting, IgnoreQsy, Utc = true, OverlayRx1 = true, OverlayRx2 = true, OverlayTx = true;
            public int MicDb, Rx1Db, Rx2Db, AgcTarget = -23;
            public double BassF = 100, BassG, MidF = 1000, MidG, MidQ = 0.7, TrebleF = 5000, TrebleG, EqVol;
        }

        private readonly RadeSettings _rs = new RadeSettings();
        private bool _radeLoaded;

        private string RadeFile { get { return Path.Combine(AppDataPath, "rade.txt"); } }

        private void radeLoadSettings()
        {
            if (_radeLoaded) return;
            _radeLoaded = true;
            try
            {
                if (File.Exists(RadeFile))
                    foreach (string line in File.ReadAllLines(RadeFile))
                    {
                        int e = line.IndexOf('=');
                        if (e < 1) continue;
                        string k = line.Substring(0, e), v = line.Substring(e + 1);
                        double d;
                        bool num = double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
                        bool b = v == "1";
                        switch (k)
                        {
                            case "call": _rs.Call = v.Trim().ToUpperInvariant(); break;
                            case "v2rx1": _rs.V2Rx1 = b; break;
                            case "v2rx2": _rs.V2Rx2 = b; break;
                            case "voice": _rs.VoiceModes = b; break;
                            case "noise": _rs.Noise = b; break;
                            case "agc": _rs.Agc = b; break;
                            case "eq": _rs.Eq = b; break;
                            case "grid": _rs.Grid = v.Trim().ToUpperInvariant(); break;
                            case "repmsg": _rs.ReporterMessage = v; break;
                            case "reporting": _rs.Reporting = b; break;
                            case "ignoreqsy": _rs.IgnoreQsy = b; break;
                            case "utc": _rs.Utc = v != "0"; break;
                            case "ovrx1": _rs.OverlayRx1 = v != "0"; break;
                            case "ovrx2": _rs.OverlayRx2 = v != "0"; break;
                            case "ovtx": _rs.OverlayTx = v != "0"; break;
                            case "mic": if (num) _rs.MicDb = (int)Math.Max(-40, Math.Min(40, d)); break;
                            case "rx1": if (num) _rs.Rx1Db = (int)Math.Max(-100, Math.Min(40, d)); break;
                            case "rx2": if (num) _rs.Rx2Db = (int)Math.Max(-100, Math.Min(40, d)); break;
                            case "agct": if (num) _rs.AgcTarget = (int)Math.Max(-30, Math.Min(0, d)); break;
                            case "bassf": if (num) _rs.BassF = d; break;
                            case "bassg": if (num) _rs.BassG = d; break;
                            case "midf": if (num) _rs.MidF = d; break;
                            case "midg": if (num) _rs.MidG = d; break;
                            case "midq": if (num) _rs.MidQ = d; break;
                            case "treblef": if (num) _rs.TrebleF = d; break;
                            case "trebleg": if (num) _rs.TrebleG = d; break;
                            case "eqvol": if (num) _rs.EqVol = d; break;
                        }
                    }
            }
            catch { }
            radeApply();
        }

        private void radeSaveSettings()
        {
            Func<double, string> f = d => d.ToString(CultureInfo.InvariantCulture);
            Func<bool, string> o = b => b ? "1" : "0";
            try
            {
                File.WriteAllLines(RadeFile, new[]
                {
                    "call=" + _rs.Call, "v2rx1=" + o(_rs.V2Rx1), "v2rx2=" + o(_rs.V2Rx2), "voice=" + o(_rs.VoiceModes),
                    "noise=" + o(_rs.Noise), "agc=" + o(_rs.Agc), "eq=" + o(_rs.Eq), "mic=" + _rs.MicDb, "rx1=" + _rs.Rx1Db, "rx2=" + _rs.Rx2Db,
                    "agct=" + _rs.AgcTarget, "bassf=" + f(_rs.BassF), "bassg=" + f(_rs.BassG), "midf=" + f(_rs.MidF), "midg=" + f(_rs.MidG),
                    "midq=" + f(_rs.MidQ), "treblef=" + f(_rs.TrebleF), "trebleg=" + f(_rs.TrebleG), "eqvol=" + f(_rs.EqVol),
                    "grid=" + _rs.Grid, "repmsg=" + (_rs.ReporterMessage ?? "").Replace('\r', ' ').Replace('\n', ' '), "reporting=" + o(_rs.Reporting), "ignoreqsy=" + o(_rs.IgnoreQsy),
                    "utc=" + o(_rs.Utc), "ovrx1=" + o(_rs.OverlayRx1), "ovrx2=" + o(_rs.OverlayRx2), "ovtx=" + o(_rs.OverlayTx)
                });
            }
            catch { }
        }

        // pushes the settings into the modem
        private void radeApply()
        {
            RadeCallsign = _rs.Call;
            RadeVoiceModes = _rs.VoiceModes;
            RadeIgnoreQsyRequest = _rs.IgnoreQsy;
            RadeReporterTimesUtc = _rs.Utc;
            RadeMeasureRx1 = _rs.OverlayRx1; RadeMeasureRx2 = _rs.OverlayRx2; RadeMeasureTx = _rs.OverlayTx;
            FreeDVReporter.FreeDVReporterManager.Configure(this, _rs.Call, _rs.Grid, _rs.ReporterMessage, _rs.Reporting);
            try
            {
                Rade.SetRadaeProtocolV2(0, _rs.V2Rx1 ? 1 : 0);
                Rade.SetRadaeProtocolV2(1, _rs.V2Rx2 ? 1 : 0);
                Rade.SetRadaeMicScale(Math.Pow(10.0, _rs.MicDb / 20.0));
                Rade.SetRadaeRxDialScale(0, Math.Pow(10.0, _rs.Rx1Db / 20.0));
                Rade.SetRadaeRxDialScale(1, Math.Pow(10.0, _rs.Rx2Db / 20.0));
                Rade.SetRadaeMicRNNoiseEnabled(_rs.Noise ? 1 : 0);
                Rade.SetRadaeMicAGCTargetLufs(_rs.AgcTarget);
                Rade.SetRadaeMicAGCEnabled(_rs.Agc ? 1 : 0);
                Rade.SetRadaeMicEQBass(_rs.BassF, _rs.BassG);
                Rade.SetRadaeMicEQMid(_rs.MidF, _rs.MidG, _rs.MidQ);
                Rade.SetRadaeMicEQTreble(_rs.TrebleF, _rs.TrebleG);
                Rade.SetRadaeMicEQVol(_rs.EqVol);
                Rade.SetRadaeMicEQEnabled(_rs.Eq ? 1 : 0);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        // the colours of the status panels (the Brick palette)
        private static readonly Color kBlue = Color.FromArgb(0x40, 0xa8, 0xf0), kGreen = Color.FromArgb(0x4d, 0xd8, 0x7a), kAmber = Color.FromArgb(0xf2, 0xc1, 0x4e);
        private static readonly Color kGrid = Color.FromArgb(44, 49, 66), kTextDim = Color.FromArgb(100, 106, 122);

        // ---- the window ----

        internal sealed class BrickRadeForm : Form
        {
            private readonly Console c;
            private readonly BrickBtn bOn, bV1, bV2, bNoise, bAgc, bEq;
            private readonly Label status, footer;
            private readonly StatusPanel statusPanel;
            private readonly HeardPanel heard;
            private readonly LevelBar rxBar, micBar;
            private readonly BrickField call;
            private BrickField grid, repMsg;
            private BrickBtn chkReport, chkQsy, chkUtc, chkOv1, chkOv2, chkOvTx;
            private BrickBtn bReporter;
            private readonly BrickNum mic, rxLevel, agcTarget, bassF, bassG, midF, midG, midQ, trebleF, trebleG, eqVol;
            private readonly BrickDropDown mode;
            private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 50 };
            private int rx;
            private bool syncing;
            private int holdStatus;

            public BrickRadeForm(Console console)
            {
                c = console;
                c.radeLoadSettings();
                Text = "Brick-SDR - FreeDV";
                BackColor = cCard;
                ForeColor = cText;
                Font = new Font("Segoe UI", 9f);
                FormBorderStyle = FormBorderStyle.FixedSingle;
                MaximizeBox = false;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                ClientSize = new Size(600, 548);
                try { Icon = c.Icon; } catch { }

                bOn = mk("FreeDV", 12, 12, 84, 30, () => toggle());
                bV1 = mk("V1", 106, 12, 42, 30, () => { if (!syncing) setVersion(false); });
                bV2 = mk("V2", 152, 12, 42, 30, () => { if (!syncing) setVersion(true); });
                status = new Label { Left = 320, Top = 14, Width = 268, Height = 26, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI Semibold", 11f), ForeColor = cMuted };

                statusPanel = new StatusPanel { Left = 12, Top = 54, Width = 282, Height = 150 };
                heard = new HeardPanel { Left = 306, Top = 54, Width = 282, Height = 150 };
                rxBar = new LevelBar { Label = "RX", Left = 12, Top = 214, Width = 576, Height = 22 };
                micBar = new LevelBar { Label = "Mic", Left = 12, Top = 240, Width = 576, Height = 22 };

                // settings
                int y = 276;
                Controls.Add(lbl("Callsign", 12, y + 4));
                call = new BrickField { Left = 70, Top = y, Width = 100, MaxLength = 8, CharacterCasing = CharacterCasing.Upper };
                call.TextChangedX += (s, e) => { if (!syncing) { c._rs.Call = call.Text.Trim(); c.radeSettingsChanged(); } };
                Controls.Add(call);
                Controls.Add(lbl("Mic (dB)", 190, y + 4));
                mic = nud(246, y, -40, 40, 1, 0, v => c._rs.MicDb = (int)v);
                Controls.Add(lbl("RX level (dB)", 320, y + 4));
                rxLevel = nud(404, y, -100, 40, 1, 0, v => { if (rx == 0) c._rs.Rx1Db = (int)v; else c._rs.Rx2Db = (int)v; });
                Controls.Add(lbl("Mode", 478, y + 4));
                mode = new BrickDropDown { Left = 514, Top = y, Width = 74, Height = 24 };
                mode.Items.AddRange(new[] { "DIGU/L", "USB/LSB" });
                mode.SelectedChanged += (s, e) => { if (!syncing) { c._rs.VoiceModes = mode.SelectedIndex == 1; c.radeSettingsChanged(); } };
                Controls.Add(mode);

                y += 40;
                bNoise = mk("Noise", 12, y, 80, 28, () => { if (!syncing) { c._rs.Noise = !c._rs.Noise; c.radeSettingsChanged(); sync(); } });
                bAgc = mk("AGC", 98, y, 80, 28, () => { if (!syncing) { c._rs.Agc = !c._rs.Agc; c.radeSettingsChanged(); sync(); } });
                bEq = mk("EQ", 184, y, 80, 28, () => { if (!syncing) { c._rs.Eq = !c._rs.Eq; c.radeSettingsChanged(); sync(); } });
                Controls.Add(lbl("AGC target (LUFS)", 284, y + 6));
                agcTarget = nud(390, y + 2, -30, 0, 1, -23, v => c._rs.AgcTarget = (int)v);

                y += 40;
                Label hdr = lbl("Mic EQ", 12, y + 4); Controls.Add(hdr);
                Controls.Add(lbl("Hz", 120, y + 4)); Controls.Add(lbl("dB", 192, y + 4)); Controls.Add(lbl("Q", 264, y + 4));
                y += 26;
                Controls.Add(lbl("Bass", 12, y + 4));
                bassF = nud(100, y, 50, 500, 10, 100, v => c._rs.BassF = v); bassG = nud(172, y, -20, 20, 0.1m, 0, v => c._rs.BassG = v, 1);
                y += 28;
                Controls.Add(lbl("Mid", 12, y + 4));
                midF = nud(100, y, 200, 4000, 50, 1000, v => c._rs.MidF = v); midG = nud(172, y, -20, 20, 0.1m, 0, v => c._rs.MidG = v, 1);
                midQ = nud(244, y, 0.1m, 5, 0.1m, 0.7m, v => c._rs.MidQ = v, 1);
                y += 28;
                Controls.Add(lbl("Treble", 12, y + 4));
                trebleF = nud(100, y, 1000, 8000, 100, 5000, v => c._rs.TrebleF = v); trebleG = nud(172, y, -20, 20, 0.1m, 0, v => c._rs.TrebleG = v, 1);
                y += 28;
                Controls.Add(lbl("Volume", 12, y + 4));
                eqVol = nud(172, y, -20, 20, 1, 0, v => c._rs.EqVol = v);
                int yEq = y;

                // FreeDV Reporter (qso.freedv.org) and the panadapter overlay
                y += 40;
                bReporter = mk("Reporter", 12, y, 100, 28, () => FreeDVReporter.FreeDVReporterManager.ShowWindow(c));
                Controls.Add(bReporter);
                Controls.Add(lbl("Grid", 126, y + 6));
                grid = new BrickField { Left = 158, Top = y + 2, Width = 64, MaxLength = 6, CharacterCasing = CharacterCasing.Upper };
                grid.TextChangedX += (s, e) => { if (!syncing) { c._rs.Grid = grid.Text.Trim(); c.radeSettingsChanged(); } };
                Controls.Add(grid);
                Controls.Add(lbl("Message", 236, y + 6));
                repMsg = new BrickField { Left = 296, Top = y + 2, Width = 292, MaxLength = 64 };
                repMsg.TextChangedX += (s, e) => { if (!syncing) { c._rs.ReporterMessage = repMsg.Text; c.radeSettingsChanged(); } };
                Controls.Add(repMsg);
                y += 32;
                chkReport = chk("Report my station while FreeDV is on", 12, y, 250, v => c._rs.Reporting = v);
                chkQsy = chk("Ignore QSY requests", 270, y, 160, v => c._rs.IgnoreQsy = v);
                chkUtc = chk("Times in UTC", 440, y, 140, v => c._rs.Utc = v);
                y += 26;
                Controls.Add(lbl("Status on the panadapter:", 12, y + 3));
                chkOv1 = chk("RX1", 170, y, 56, v => c._rs.OverlayRx1 = v);
                chkOv2 = chk("RX2", 232, y, 56, v => c._rs.OverlayRx2 = v);
                chkOvTx = chk("Transmit", 294, y, 90, v => c._rs.OverlayTx = v);
                ClientSize = new Size(600, y + 40);

                footer = new Label { Left = 340, Top = yEq - 76, Width = 248, Height = 100, ForeColor = cMuted };
                Controls.AddRange(new Control[] { bOn, bV1, bV2, status, statusPanel, heard, rxBar, micBar, footer, bNoise, bAgc, bEq });

                timer.Tick += (s, e) => tick();
                showReceiver(!c.RadeEnabledOn(0) && c.RadeEnabledOn(1) ? 1 : 0);
            }

            private BrickBtn chk(string text, int x, int y, int w, Action<bool> set)
            {
                BrickBtn b = new BrickBtn { Text = text, Left = x, Top = y, Width = w, Height = 24, Toggle = true };
                b.CheckedChanged += (s, e) => { if (!syncing) { set(b.Checked); c.radeSettingsChanged(); } };
                Controls.Add(b);
                return b;
            }

            private Label lbl(string t, int x, int y)
            {
                Label l = new Label { Text = t, Left = x, Top = y, AutoSize = true, ForeColor = cMuted };
                if (!Controls.Contains(l) && t != "Mic EQ") { }
                Controls.Add(l);
                return l;
            }

            private BrickNum nud(int x, int y, decimal min, decimal max, decimal inc, decimal val, Action<double> set, int dec = 0)
            {
                BrickNum n = new BrickNum { Left = x, Top = y, Width = 74, Height = 24, Minimum = min, Maximum = max, Increment = inc, DecimalPlaces = dec, Value = Math.Max(min, Math.Min(max, val)) };
                n.ValueChanged += (s, e) => { if (!syncing) { set((double)n.Value); c.radeSettingsChanged(); } };
                Controls.Add(n);
                return n;
            }

            private BrickBtn mk(string text, int x, int y, int w, int h, Action act)
            {
                BrickBtn b = new BrickBtn { Text = text, Left = x, Top = y, Width = w, Height = h };
                b.Click += (s, e) => act();
                return b;
            }

            private static void light(BrickBtn b, bool on) { b.On = on; }

            public void ShowAt()
            {
                showReceiver(c._radeStrip != null && !c._radeStrip.IsDisposed ? c._radeStrip.Rx : (!c.RadeEnabledOn(0) && c.RadeEnabledOn(1) ? 1 : 0));      // the receiver the DIGI menu opened FreeDV on
                if (!Visible)
                {
                    Rectangle r = c.Bounds;
                    Location = new Point(Math.Max(0, r.Left + (r.Width - Width) / 2), Math.Max(0, r.Top + 40));
                    Show(c);
                }
                BringToFront();
            }

            private void setVersion(bool v2)
            {
                if (rx == 0) c._rs.V2Rx1 = v2; else c._rs.V2Rx2 = v2;
                c.radeSettingsChanged();
                sync();
            }

            private void showReceiver(int r)
            {
                rx = r;
                heard.SetHeard(c._radeHeardCall[r], c._radeHeardAt[r]);
                statusPanel.SetState(false, false, 0, 0);
                sync();
            }

            private void toggle()
            {
                bool on = !c.RadeEnabledOn(rx);
                if (!c.SetRadeEnabled(rx, on))
                {
                    status.Text = on && rx == 1 && !c.RX2Enabled ? "Turn RX2 on first" : "Can't switch while transmitting";
                    status.ForeColor = cAccent;
                    holdStatus = 40;
                }
                sync();
            }

            private void sync()
            {
                syncing = true;
                try
                {
                    light(bOn, c.RadeEnabledOn(rx));
                    bool v2 = rx == 0 ? c._rs.V2Rx1 : c._rs.V2Rx2;
                    light(bV1, !v2); light(bV2, v2);
                    light(bNoise, c._rs.Noise); light(bAgc, c._rs.Agc); light(bEq, c._rs.Eq);
                    call.Text = c._rs.Call;
                    grid.Text = c._rs.Grid; repMsg.Text = c._rs.ReporterMessage ?? "";
                    chkReport.Checked = c._rs.Reporting; chkQsy.Checked = c._rs.IgnoreQsy; chkUtc.Checked = c._rs.Utc;
                    chkOv1.Checked = c._rs.OverlayRx1; chkOv2.Checked = c._rs.OverlayRx2; chkOvTx.Checked = c._rs.OverlayTx;
                    mic.Value = Math.Max(mic.Minimum, Math.Min(mic.Maximum, c._rs.MicDb));
                    rxLevel.Value = Math.Max(rxLevel.Minimum, Math.Min(rxLevel.Maximum, rx == 0 ? c._rs.Rx1Db : c._rs.Rx2Db));
                    mode.SelectedIndex = c._rs.VoiceModes ? 1 : 0;
                    agcTarget.Value = c._rs.AgcTarget;
                    bassF.Value = clamp(bassF, c._rs.BassF); bassG.Value = clamp(bassG, c._rs.BassG);
                    midF.Value = clamp(midF, c._rs.MidF); midG.Value = clamp(midG, c._rs.MidG); midQ.Value = clamp(midQ, c._rs.MidQ);
                    trebleF.Value = clamp(trebleF, c._rs.TrebleF); trebleG.Value = clamp(trebleG, c._rs.TrebleG);
                    eqVol.Value = clamp(eqVol, c._rs.EqVol);
                    if (c._rs.Call.Length == 0)
                    {
                        footer.Text = "Your callsign isn't set, so other stations can't see who you are. Type it in the Callsign box.";
                        footer.ForeColor = kAmber;
                    }
                    else
                    {
                        footer.Text = "Sending " + c._rs.Call + " at the end of each over.\r\nFreeDV RADE (Radio Autoencoder), ported from Thetis-RADE by SV1EIA.";
                        footer.ForeColor = cMuted;
                    }
                }
                finally { syncing = false; }
            }

            private static decimal clamp(BrickNum n, double v)
            {
                return Math.Max(n.Minimum, Math.Min(n.Maximum, (decimal)v));
            }

            private void tick()
            {
                int r = rx;
                bool on = c.RadeEnabledOn(r), power = c.PowerOn;
                bool sync = false;
                int snr = 0, rxl = -120, micl = -120;
                bool rxClip = false, micClip = false;
                float offset = 0;
                try
                {
                    if (on && power)
                    {
                        sync = c.RadeSyncOn(r);
                        snr = c.RadeSnrDbOn(r);
                        rxl = c.RadeRxLevelDbOn(r);
                        rxClip = c.RadeRxClipOn(r);
                        offset = c.RadeFreqOffsetHzOn(r);
                        if (c.MOX && c.RadeTxReceiver == r)
                        {
                            micl = c.RadeMicLevelDb;
                            micClip = c.RadeMicClip;
                        }
                    }
                    c.radePollCalls();
                    heard.SetHeard(c._radeHeardCall[r], c._radeHeardAt[r]);
                }
                catch { }

                statusPanel.SetState(on && power, sync, snr, offset);
                rxBar.SetLevel(on && power && !(c.MOX && c.RadeTxReceiver == r) ? rxl : -120, rxClip);
                micBar.SetLevel(micl, micClip);
                heard.Invalidate();

                Color color;
                string text = c.radeStatusText(r, on, power, sync, out color);
                if (holdStatus > 0) holdStatus--;
                else
                {
                    if (status.Text != text) status.Text = text;
                    status.ForeColor = color;
                }
                if (bOn.On != on) light(bOn, on);
            }

            protected override void OnVisibleChanged(EventArgs e)
            {
                base.OnVisibleChanged(e);
                if (Visible) { sync(); timer.Start(); }
                else timer.Stop();
            }

            protected override void OnFormClosing(FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
                base.OnFormClosing(e);
            }

            private static GraphicsPath rounded(RectangleF r, float radius)
            {
                float dd = radius * 2f;
                GraphicsPath p = new GraphicsPath();
                p.AddArc(r.X, r.Y, dd, dd, 180, 90);
                p.AddArc(r.Right - dd, r.Y, dd, dd, 270, 90);
                p.AddArc(r.Right - dd, r.Bottom - dd, dd, dd, 0, 90);
                p.AddArc(r.X, r.Bottom - dd, dd, dd, 90, 90);
                p.CloseFigure();
                return p;
            }

            private static void drawPanel(Graphics g, Rectangle r)
            {
                using (GraphicsPath path = rounded(new RectangleF(0.5f, 0.5f, r.Width - 1.5f, r.Height - 1.5f), 5f))
                {
                    using (Brush b = new SolidBrush(cCard)) g.FillPath(b, path);
                    using (Pen p = new Pen(cLine)) g.DrawPath(p, path);
                }
            }

            // sync lamp, SNR (big figure and a -10..+40 dB bar) and frequency offset
            private sealed class StatusPanel : Control
            {
                private bool active, sync;
                private double snr;
                private float offset;

                public StatusPanel()
                {
                    SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                    BackColor = cBg;
                }

                public void SetState(bool a, bool s, int n, float off)
                {
                    snr = s && active == a ? snr + 0.3 * (n - snr) : n;
                    active = a; sync = s; offset = off;
                    Invalidate();
                }

                protected override void OnPaint(PaintEventArgs e)
                {
                    Graphics g = e.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    drawPanel(g, ClientRectangle);
                    Color lamp = !active ? kGrid : sync ? kGreen : Color.FromArgb(0x5a, 0x24, 0x20);
                    using (Brush b = new SolidBrush(lamp)) g.FillEllipse(b, 14, 14, 16, 16);
                    if (active && sync) using (Pen glow = new Pen(Color.FromArgb(90, kGreen), 3f)) g.DrawEllipse(glow, 12, 12, 20, 20);
                    using (Font f = new Font("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (Brush b = new SolidBrush(active && sync ? kGreen : cMuted))
                        g.DrawString(sync && active ? "SYNC" : "NO SYNC", f, b, 38, 13);
                    bool show = active && sync;
                    using (Font f = new Font("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (Brush b = new SolidBrush(kTextDim)) { g.DrawString("SNR", f, b, 14, 46); g.DrawString("OFFSET", f, b, 150, 46); }
                    using (Font f = new Font("Segoe UI", 34f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (Brush b = new SolidBrush(show ? cText : kTextDim))
                        g.DrawString(show ? ((int)Math.Round(snr)).ToString() : "--", f, b, 10, 58);
                    using (Font f = new Font("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (Brush b = new SolidBrush(cMuted))
                        g.DrawString("dB", f, b, 92, 78);
                    using (Font f = new Font("Segoe UI", 18f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (Brush b = new SolidBrush(show ? cText : kTextDim))
                        g.DrawString(show ? (offset >= 0 ? "+" : "") + offset.ToString("0.0") + " Hz" : "--", f, b, 148, 66);
                    Rectangle bar = new Rectangle(14, 112, Width - 28, 10);
                    using (Brush b = new SolidBrush(kGrid)) g.FillRectangle(b, bar);
                    if (show)
                    {
                        double n = Math.Max(0, Math.Min(1, (snr + 10) / 50.0));
                        Color col = snr < 0 ? cTx : snr < 6 ? kAmber : kGreen;
                        using (Brush b = new SolidBrush(col)) g.FillRectangle(b, bar.X, bar.Y, (float)(bar.Width * n), bar.Height);
                    }
                    using (Font f = new Font("Segoe UI", 9f, GraphicsUnit.Pixel))
                    using (Brush b = new SolidBrush(kTextDim))
                    using (Pen tick = new Pen(kTextDim))
                        for (int db = -10; db <= 40; db += 10)
                        {
                            float x = bar.X + bar.Width * (db + 10) / 50f;
                            g.DrawLine(tick, x, bar.Bottom, x, bar.Bottom + 3);
                            string t = db.ToString();
                            SizeF sz = g.MeasureString(t, f);
                            g.DrawString(t, f, b, Math.Max(bar.X - 2, Math.Min(bar.Right - sz.Width + 2, x - sz.Width / 2)), bar.Bottom + 4);
                        }
                }
            }

            // the callsign from the last end-of-over frame decoded, and when it was heard
            private sealed class HeardPanel : Control
            {
                private string call = "";
                private DateTime at;

                public HeardPanel()
                {
                    SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                    BackColor = cBg;
                }

                public void SetHeard(string cs, DateTime t) { call = cs; at = t; Invalidate(); }

                protected override void OnPaint(PaintEventArgs e)
                {
                    Graphics g = e.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    drawPanel(g, ClientRectangle);
                    using (Font f = new Font("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (Brush b = new SolidBrush(kTextDim))
                        g.DrawString("LAST HEARD", f, b, 14, 14);
                    using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    {
                        if (call.Length == 0)
                        {
                            using (Font f = new Font("Segoe UI", 13f, GraphicsUnit.Pixel))
                            using (Brush b = new SolidBrush(kTextDim))
                                g.DrawString("No callsign decoded yet", f, b, new RectangleF(0, 40, Width, 60), sf);
                            return;
                        }
                        Font big = new Font("Arial Black", 40f, FontStyle.Regular, GraphicsUnit.Pixel);
                        SizeF size = g.MeasureString(call, big);
                        if (size.Width > Width - 24)
                        {
                            big.Dispose();
                            big = new Font("Arial Black", Math.Max(12f, 40f * (Width - 24) / size.Width), FontStyle.Regular, GraphicsUnit.Pixel);
                        }
                        using (big)
                        using (Brush b = new SolidBrush(cAccent))
                            g.DrawString(call, big, b, new RectangleF(0, 36, Width, 66), sf);
                        TimeSpan ago = DateTime.Now - at;
                        string when = at.ToString("HH:mm") + "  -  " + (ago.TotalSeconds < 60 ? "just now" : ago.TotalMinutes < 60 ? (int)ago.TotalMinutes + " min ago" : (int)ago.TotalHours + " h ago");
                        using (Font f = new Font("Segoe UI", 12f, GraphicsUnit.Pixel))
                        using (Brush b = new SolidBrush(cMuted))
                            g.DrawString(when, f, b, new RectangleF(0, 104, Width, 24), sf);
                    }
                }
            }

            // a horizontal level meter, -60 .. 0 dBFS, with a held peak; red while the source reports clipping
            internal sealed class LevelBar : Control
            {
                public string Label = "";
                private double db = -120, peak = -120;
                private bool clip;

                public LevelBar()
                {
                    SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                    BackColor = cBg;
                }

                public void SetLevel(int d, bool cl)
                {
                    db = d;
                    peak = d >= peak ? d : Math.Max(d, peak - 0.6);
                    clip = cl;
                    Invalidate();
                }

                protected override void OnPaint(PaintEventArgs e)
                {
                    Graphics g = e.Graphics;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    using (Font f = new Font("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (Brush b = new SolidBrush(cMuted))
                        g.DrawString(Label, f, b, 0, 3);
                    Rectangle bar = new Rectangle(36, 4, Width - 36 - 64, Height - 8);
                    using (Brush b = new SolidBrush(kGrid)) g.FillRectangle(b, bar);
                    Func<double, float> X = d => bar.X + bar.Width * (float)Math.Max(0, Math.Min(1, (d + 60) / 60.0));
                    Color col = clip ? cTx : kBlue;
                    if (db > -60) using (Brush b = new SolidBrush(col)) g.FillRectangle(b, bar.X, bar.Y, X(db) - bar.X, bar.Height);
                    if (peak > -60) using (Pen p = new Pen(clip ? cTx : cText, 2f)) g.DrawLine(p, X(peak), bar.Y, X(peak), bar.Bottom);
                    using (Pen tick = new Pen(cBg))
                        for (int d = -50; d < 0; d += 10) g.DrawLine(tick, X(d), bar.Y, X(d), bar.Bottom);
                    string text = clip ? "CLIP" : db > -120 ? ((int)Math.Round(db)).ToString() + " dB" : "--";
                    using (Font f = new Font("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (Brush b = new SolidBrush(clip ? cTx : cMuted))
                    using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Far })
                        g.DrawString(text, f, b, new RectangleF(bar.Right, 3, 62, Height), sf);
                }
            }
        }

        // the callsigns heard in end-of-over frames, collected on both receivers (the FreeDV window and the strip read them)
        internal readonly string[] _radeHeardCall = { "", "" };
        internal readonly DateTime[] _radeHeardAt = new DateTime[2];
        private readonly int[] _radeCallSeq = { -1, -1 };

        internal void radePollCalls()
        {
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    int seq = RadeCallsignSeqOn(i);
                    if (seq == _radeCallSeq[i]) continue;
                    if (_radeCallSeq[i] != -1)
                    {
                        string cs = RadeRemoteCallsignOn(i);
                        if (cs.Length > 0) { _radeHeardCall[i] = cs; _radeHeardAt[i] = DateTime.Now; }
                    }
                    _radeCallSeq[i] = seq;
                }
            }
            catch { }
        }

        // what RADE is doing on a receiver, in words, and the colour for it
        internal string radeStatusText(int r, bool on, bool power, bool sync, out Color color)
        {
            if (!on) { color = kTextDim; return "Off"; }
            if (!power) { color = kTextDim; return "Radio off"; }
            if (MOX && RadeTxReceiver != r) { color = cMuted; return "Transmitting from RX" + (RadeTxReceiver + 1); }
            if (RadeSendingEoo) { color = cAccent; return "Sending end-of-over"; }
            if (MOX) { color = cTx; return RadeTransmitting ? "Transmitting FreeDV" : "Transmitting voice"; }
            if (sync) { color = kGreen; return "Receiving FreeDV"; }
            color = cMuted; return "Listening";
        }

        private void radeSettingsChanged()
        {
            radeSaveSettings();
            radeApply();
        }
    }
}
