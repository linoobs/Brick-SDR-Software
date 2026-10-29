/*  BrickDigital.cs

    RTTY and CW terminals for Brick-SDR (DIGI menu). The engines (receive taps and the transmit feed in ChannelMaster's
    kdigi.c, the RTTY modem and CW decoder in BrickRttyCodec.cs / BrickCwCodec.cs) come from Kainos by Justin Cron K7JUS
    (https://github.com/PNWHam-K7JUS/Kainos), GPL v3; the terminal window here is Brick-SDR's own.
*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace BrickSDR
{
    public partial class Console
    {
        private BrickDigiForm _digiForm;
        private string _digiMode = "";                  // "RTTY" or "CW" while a terminal is open

        // settings (digital.txt in the data folder)
        private string _digiMyCall = "";
        private int _digiRx;
        private double _rttyShift = 170, _rttyBaud = 45.45;
        private bool _rttyReverse, _rttyAfc = true;
        private int _rttySql = 15, _rttyLevel = -10, _cwSql = 20;

        // engine state
        private Thread _digiThread;
        private volatile bool _digiRun;
        private int _digiTapRx = -1;
        private RttyDemod _rttyDemod;
        private RttyMod _rttyMod;
        private CwDecoder _cwDecoder;
        private readonly ConcurrentQueue<KeyValuePair<char, bool>> _digiOut = new ConcurrentQueue<KeyValuePair<char, bool>>();     // char, sent
        private readonly ConcurrentQueue<char> _rttyTxQueue = new ConcurrentQueue<char>();
        private readonly ConcurrentQueue<char> _cwEcho = new ConcurrentQueue<char>();
        private volatile bool _rttyTx, _rttyTxBegun, _rttyTxEnding, _rttyTxDrained;
        private bool _cwEchoHooked, _cwWasSending;

        private string DigiFile { get { return Path.Combine(AppDataPath, "digital.txt"); } }

        private void digiLoadSettings()
        {
            try
            {
                if (!File.Exists(DigiFile)) return;
                foreach (string line in File.ReadAllLines(DigiFile))
                {
                    int e = line.IndexOf('=');
                    if (e < 1) continue;
                    string k = line.Substring(0, e), v = line.Substring(e + 1);
                    double d;
                    bool num = double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
                    switch (k)
                    {
                        case "mycall": _digiMyCall = v.Trim().ToUpperInvariant(); break;
                        case "rx": if (num) _digiRx = d >= 1 ? 1 : 0; break;
                        case "shift": if (num && d > 20 && d < 2000) _rttyShift = d; break;
                        case "baud": if (num && d > 20 && d < 300) _rttyBaud = d; break;
                        case "rev": _rttyReverse = v == "1"; break;
                        case "afc": _rttyAfc = v != "0"; break;
                        case "rsql": if (num) _rttySql = (int)Math.Max(0, Math.Min(60, d)); break;
                        case "rlevel": if (num) _rttyLevel = (int)Math.Max(-40, Math.Min(0, d)); break;
                        case "csql": if (num) _cwSql = (int)Math.Max(0, Math.Min(100, d)); break;
                    }
                }
            }
            catch { }
        }

        private void digiSaveSettings()
        {
            try
            {
                File.WriteAllLines(DigiFile, new[]
                {
                    "mycall=" + _digiMyCall, "rx=" + _digiRx,
                    "shift=" + _rttyShift.ToString(CultureInfo.InvariantCulture), "baud=" + _rttyBaud.ToString(CultureInfo.InvariantCulture),
                    "rev=" + (_rttyReverse ? 1 : 0), "afc=" + (_rttyAfc ? 1 : 0), "rsql=" + _rttySql, "rlevel=" + _rttyLevel, "csql=" + _cwSql
                });
            }
            catch { }
        }

        // ---- opening and closing ----

        private void brickDigitalOpen(string mode)
        {
            if (_digiForm == null || _digiForm.IsDisposed)
            {
                digiLoadSettings();
                _digiForm = new BrickDigiForm(this);
                _digiForm.FormClosed += (s, e) => digiStop();
            }
            if (_digiMode != mode)
            {
                digiStopEngine();
                digiTransmitAbort();
                _digiMode = mode;
            }
            if (mode == "CW" && !_cwEchoHooked)
            {
                _cwEchoHooked = true;
                // CWX starts a character from its remote queue: echo the next one we gave it
                CWXRemoteCharacterStartedHandlers += (remaining, pending) =>
                {
                    char c;
                    if (_cwEcho.TryDequeue(out c)) _digiOut.Enqueue(new KeyValuePair<char, bool>(c, true));
                };
            }
            _digiForm.SetMode(mode);
            if (!_digiForm.Visible) _digiForm.Show(this);
            _digiForm.BringToFront();
            digiStartEngine();
        }

        private void digiStop()
        {
            digiTransmitAbort();
            digiStopEngine();
            _digiMode = "";
        }

        private DSPMode digiRxMode { get { return _digiRx == 0 ? _rx1_dsp_mode : _rx2_dsp_mode; } }

        // ---- engine ----

        private void digiStartEngine()
        {
            if (_digiRun) return;
            _digiRun = true;
            _digiThread = new Thread(digiWork) { IsBackground = true, Name = "Brick digital", Priority = ThreadPriority.AboveNormal };
            _digiThread.Start();
        }

        private void digiStopEngine()
        {
            _digiRun = false;
            if (_digiThread != null) _digiThread.Join(500);
            _digiThread = null;
            try { if (_digiTapRx >= 0) KDigi.KDigiRxTap(_digiTapRx, 0); } catch { }
            _digiTapRx = -1;
            _rttyDemod = null; _cwDecoder = null;
        }

        // USB / DIGU put mark above space in the audio; LSB / DIGL (RTTY's usual) below
        private bool rttyUpperSideband { get { DSPMode m = digiRxMode; return m == DSPMode.USB || m == DSPMode.DIGU; } }

        internal bool RttyModeOk { get { DSPMode m = digiRxMode; return m == DSPMode.LSB || m == DSPMode.USB || m == DSPMode.DIGL || m == DSPMode.DIGU; } }
        internal bool CwModeOk { get { DSPMode m = digiRxMode; return m == DSPMode.CWL || m == DSPMode.CWU; } }

        private void rttyApplyParams()
        {
            bool rev = _rttyReverse ^ rttyUpperSideband;
            if (_rttyDemod != null)
            {
                _rttyDemod.Nominal = 2210; _rttyDemod.Afc = _rttyAfc; _rttyDemod.Shift = _rttyShift; _rttyDemod.Baud = _rttyBaud;
                _rttyDemod.Reverse = rev; _rttyDemod.UnshiftOnSpace = true;
                _rttyDemod.Squelch = _rttySql / 100.0;
                _rttyDemod.MinConfidence = 0.3 + _rttySql / 200.0;
            }
            if (_rttyMod != null)
            {
                _rttyMod.Center = _rttyDemod != null ? _rttyDemod.Center : 2210; _rttyMod.Shift = _rttyShift; _rttyMod.Baud = _rttyBaud; _rttyMod.Reverse = rev;
                _rttyMod.Amplitude = (float)Math.Pow(10, _rttyLevel / 20.0);
            }
        }

        private void digiWork()
        {
            float[] buf = new float[16384];
            List<float> tx = new List<float>(8192);
            int rate0 = 0;
            string mode0 = "";
            while (_digiRun)
            {
                try
                {
                    string mode = _digiMode;
                    int rx = _digiRx;
                    if (rx != _digiTapRx)
                    {
                        if (_digiTapRx >= 0) KDigi.KDigiRxTap(_digiTapRx, 0);
                        KDigi.KDigiRxTap(rx, 1);
                        _digiTapRx = rx;
                    }
                    int rate = KDigi.KDigiRxRate(rx);
                    if (rate > 0 && (rate != rate0 || mode != mode0))
                    {
                        rate0 = rate; mode0 = mode;
                        if (mode == "RTTY")
                        {
                            _rttyDemod = new RttyDemod(rate);
                            _rttyDemod.Decoded += c => _digiOut.Enqueue(new KeyValuePair<char, bool>(c, false));
                        }
                        else if (mode == "CW")
                        {
                            _cwDecoder = new CwDecoder(rate);
                            _cwDecoder.Decoded += c => _digiOut.Enqueue(new KeyValuePair<char, bool>(c, false));
                        }
                    }
                    if (mode == "RTTY") rttyApplyParams();
                    else if (mode == "CW" && _cwDecoder != null) { _cwDecoder.Pitch = CWPitch; _cwDecoder.Squelch = _cwSql / 100.0; }

                    int n;
                    while ((n = KDigi.KDigiRxRead(rx, buf, buf.Length)) > 0)
                    {
                        if (_mox) continue;
                        if (mode == "RTTY" && _rttyDemod != null) _rttyDemod.Process(buf, n);
                        else if (mode == "CW" && _cwDecoder != null) _cwDecoder.Process(buf, n);
                    }

                    // RTTY transmit: keep ~0.2 s of audio queued ahead of the radio
                    if (mode == "RTTY" && _rttyTx && !_rttyTxDrained)
                    {
                        int txRate = KDigi.KDigiTxRate();
                        if (txRate > 0)
                        {
                            if (_rttyMod == null) { _rttyMod = new RttyMod(txRate); rttyApplyParams(); }
                            while (KDigi.KDigiTxQueued() < txRate / 5 && !_rttyTxDrained)
                            {
                                tx.Clear();
                                char c;
                                if (!_rttyTxBegun) { _rttyMod.Begin(tx); _rttyTxBegun = true; }
                                else if (_rttyTxQueue.TryDequeue(out c)) { _rttyMod.Char(tx, c); _digiOut.Enqueue(new KeyValuePair<char, bool>(c, true)); }
                                else if (_rttyTxEnding) { _rttyMod.End(tx); _rttyTxDrained = true; }
                                else _rttyMod.Idle(tx);
                                float[] a = tx.ToArray();
                                int off = 0;
                                while (off < a.Length && _digiRun)
                                {
                                    float[] part = off == 0 ? a : a.Skip(off).ToArray();
                                    int w = KDigi.KDigiTxWrite(part, part.Length);
                                    off += w;
                                    if (w == 0) Thread.Sleep(5);
                                }
                            }
                        }
                    }
                }
                catch (Exception) { }
                Thread.Sleep(20);
            }
        }

        // ---- RTTY sending ----

        internal bool RttyTransmitting { get { return _rttyTx; } }

        internal string RttyStartProblem
        {
            get
            {
                if (!PowerOn) return "The radio is off";
                if (!RttyModeOk) return "Use DIGL or LSB (or DIGU / USB) for RTTY";
                if (_digiRx == 1 && !chkVFOBTX.Checked) return "RX2 is selected but VFO A transmits";
                return null;
            }
        }

        internal void RttySend(string text, bool thenReceive)
        {
            foreach (char c in text) _rttyTxQueue.Enqueue(c);
            if (!_rttyTx)
            {
                if (RttyStartProblem != null) { while (_rttyTxQueue.TryDequeue(out char _)) { } return; }
                KDigi.KDigiTxClear();
                _rttyMod = null;
                _rttyTxBegun = false;
                _rttyTxEnding = false;
                _rttyTxDrained = false;
                KDigi.KDigiTxEnable(1);
                _rttyTx = true;
                MOX = true;
            }
            if (thenReceive) _rttyTxEnding = true;
        }

        internal void RttyType(char c) { if (_rttyTx) _rttyTxQueue.Enqueue(c); }
        internal void RttyReceive() { if (_rttyTx) _rttyTxEnding = true; }

        internal void RttyAbort()
        {
            while (_rttyTxQueue.TryDequeue(out char _)) { }
            rttyStopTx();
        }

        private void rttyStopTx()
        {
            if (!_rttyTx) return;
            _rttyTx = false;
            KDigi.KDigiTxClear();
            if (_mox) MOX = false;
            KDigi.KDigiTxEnable(0);
            _digiOut.Enqueue(new KeyValuePair<char, bool>('\n', true));
        }

        // ---- CW sending (through CWX) ----

        internal bool CwSending
        {
            get
            {
                if (m_frmCWXForm == null || m_frmCWXForm.IsDisposed) return false;
                return m_frmCWXForm.PendingRemoteCharacters > 0 || m_frmCWXForm.Characters2Send > 0;
            }
        }

        internal string CwStartProblem
        {
            get
            {
                if (!PowerOn) return "The radio is off";
                if (!CwModeOk) return "Use CWL or CWU to send CW";
                return null;
            }
        }

        internal void CwSend(string text)
        {
            if (CwStartProblem != null || string.IsNullOrEmpty(text)) return;
            text = text.ToUpperInvariant();
            foreach (char c in text) _cwEcho.Enqueue(c);
            CWXForm.RemoteMessage(Encoding.ASCII.GetBytes(text));
        }

        internal void CwAbort()
        {
            try { CWXForm.AbortSending(); } catch { }
            while (_cwEcho.TryDequeue(out char _)) { }
            _digiOut.Enqueue(new KeyValuePair<char, bool>('\n', true));
        }

        private void digiTransmitAbort()
        {
            if (_rttyTx) RttyAbort();
            if (_cwEchoHooked && CwSending) CwAbort();
        }

        // called by the terminal's timer
        internal void DigiUiTick(BrickDigiForm f)
        {
            KeyValuePair<char, bool> kv;
            int guard = 0;
            while (guard++ < 2000 && _digiOut.TryDequeue(out kv)) f.Append(kv.Key, kv.Value);
            if (_digiMode == "RTTY" && _rttyTx)
            {
                if (!_mox) { RttyAbort(); return; }                          // un-keyed elsewhere (the MOX button, a foot switch...)
                if (_rttyTxDrained && KDigi.KDigiTxQueued() == 0)
                    BeginInvoke(new Action(() => { if (_rttyTx && _rttyTxDrained) rttyStopTx(); }));
            }
            if (_digiMode == "CW")
            {
                bool sending = CwSending;
                if (_cwWasSending && !sending) f.Append('\n', true);
                _cwWasSending = sending;
            }
        }

        // ---- the terminal window ----

        internal sealed class BrickDigiForm : Form
        {
            private readonly Console c;
            private readonly RichTextBox rx;
            private readonly TextBox type, my, their;
            private readonly Label status, title, hint;
            private readonly Panel settings;
            private readonly Button bRx1, bRx2, bTx, bEnd, bAbort, bClear;
            private readonly List<Button> macroButtons = new List<Button>();
            private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 80 };
            private readonly MeterBar meter = new MeterBar();
            private string mode = "";
            private bool lastSent;
            private int lines;

            // RTTY settings
            private ComboBox shift, baud;
            private CheckBox rev, afc;
            private NumericUpDown rsql, rlevel;
            // CW settings
            private NumericUpDown csql, wpm;
            private Label lblShift, lblBaud, lblSql, lblLevel, lblWpm;

            private static readonly string[][] Macros =
            {
                new[] { "CQ", "CQ CQ CQ DE {MY} {MY} {MY} PSE K\r\n" },
                new[] { "Answer", "{CALL} DE {MY} {MY} KN\r\n" },
                new[] { "Report", "{CALL} DE {MY} UR RST 599 599 NAME OP QTH HR = HW? {CALL} DE {MY} KN\r\n" },
                new[] { "73", "{CALL} DE {MY} TNX FER QSO 73 SK\r\n" },
            };

            public BrickDigiForm(Console console)
            {
                c = console;
                Text = "Brick-SDR - Digital";
                BackColor = cBg;
                ForeColor = cText;
                Font = new Font("Segoe UI", 9f);
                StartPosition = FormStartPosition.Manual;
                MinimumSize = new Size(640, 420);
                ClientSize = new Size(760, 500);
                ShowInTaskbar = false;
                try { Icon = c.Icon; } catch { }
                Rectangle wa = c.Bounds;
                Location = new Point(Math.Max(0, wa.Left + 60), Math.Max(0, wa.Top + 90));

                title = new Label { AutoSize = false, Left = 12, Top = 8, Width = 200, Height = 26, Font = new Font("Segoe UI Semibold", 12f), ForeColor = cAccent, TextAlign = ContentAlignment.MiddleLeft };
                bRx1 = mk("RX1", 218, 8, 54, 26, () => { c._digiRx = 0; c.digiSaveSettings(); sync(); });
                bRx2 = mk("RX2", 276, 8, 54, 26, () => { c._digiRx = 1; c.digiSaveSettings(); sync(); });
                my = field(354, 9, 90); their = field(500, 9, 90);
                Label lm = lbl("My call", 354 - 0, -1), lt = lbl("Their call", 500, -1);
                my.Text = c._digiMyCall;
                my.TextChanged += (s, e) => { c._digiMyCall = my.Text.Trim().ToUpperInvariant(); c.digiSaveSettings(); };
                my.CharacterCasing = CharacterCasing.Upper; their.CharacterCasing = CharacterCasing.Upper;
                bClear = mk("Clear", 0, 8, 60, 26, () => { rx.Clear(); lines = 0; });

                settings = new Panel { Left = 12, Top = 40, Height = 30, BackColor = cBg };
                shift = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 62, FlatStyle = FlatStyle.Flat, BackColor = cBtn, ForeColor = cText };
                shift.Items.AddRange(new object[] { "170", "200", "425", "850" });
                baud = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 62, FlatStyle = FlatStyle.Flat, BackColor = cBtn, ForeColor = cText };
                baud.Items.AddRange(new object[] { "45.45", "50", "75", "100" });
                rev = new CheckBox { Text = "Reverse", AutoSize = true, ForeColor = cText };
                afc = new CheckBox { Text = "AFC", AutoSize = true, ForeColor = cText };
                rsql = nud(0, 60, 5, c._rttySql); rlevel = nud(-40, 0, 1, c._rttyLevel);
                csql = nud(0, 100, 5, c._cwSql); wpm = nud(5, 60, 1, 20);
                lblShift = lbl("Shift", 0, 0); lblBaud = lbl("Baud", 0, 0); lblSql = lbl("SQL", 0, 0); lblLevel = lbl("TX dB", 0, 0); lblWpm = lbl("TX WPM", 0, 0);
                shift.Text = c._rttyShift.ToString("0", CultureInfo.InvariantCulture);
                baud.Text = c._rttyBaud.ToString("0.##", CultureInfo.InvariantCulture);
                rev.Checked = c._rttyReverse; afc.Checked = c._rttyAfc;
                shift.SelectedIndexChanged += (s, e) => { c._rttyShift = double.Parse((string)shift.SelectedItem, CultureInfo.InvariantCulture); c.digiSaveSettings(); };
                baud.SelectedIndexChanged += (s, e) => { c._rttyBaud = double.Parse((string)baud.SelectedItem, CultureInfo.InvariantCulture); c.digiSaveSettings(); };
                rev.CheckedChanged += (s, e) => { c._rttyReverse = rev.Checked; c.digiSaveSettings(); };
                afc.CheckedChanged += (s, e) => { c._rttyAfc = afc.Checked; c.digiSaveSettings(); };
                rsql.ValueChanged += (s, e) => { c._rttySql = (int)rsql.Value; c.digiSaveSettings(); };
                rlevel.ValueChanged += (s, e) => { c._rttyLevel = (int)rlevel.Value; c.digiSaveSettings(); };
                csql.ValueChanged += (s, e) => { c._cwSql = (int)csql.Value; c.digiSaveSettings(); };
                wpm.ValueChanged += (s, e) => { try { if (c.CWXForm.WPM != (int)wpm.Value) c.CWXForm.WPM = (int)wpm.Value; } catch { } };

                rx = new RichTextBox
                {
                    ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = cField, ForeColor = cText, DetectUrls = false,
                    Font = new Font("Consolas", 12f), ScrollBars = RichTextBoxScrollBars.Vertical, HideSelection = false, TabStop = false
                };
                meter.Height = 14;
                status = new Label { AutoSize = false, Height = 20, ForeColor = cMuted, TextAlign = ContentAlignment.MiddleLeft };
                hint = new Label { AutoSize = false, Height = 20, ForeColor = cMuted, TextAlign = ContentAlignment.MiddleRight };
                type = new TextBox { BackColor = cField, ForeColor = cText, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 12f), CharacterCasing = CharacterCasing.Upper };
                type.KeyPress += typeKey;
                bTx = mk("TX", 0, 0, 70, 30, () => { if (mode == "RTTY") c.RttySend("", false); });
                bEnd = mk("RX", 0, 0, 70, 30, () => { if (mode == "RTTY") c.RttyReceive(); else c.CwStopQueueSafe(); });
                bAbort = mk("ABORT", 0, 0, 80, 30, () => { if (mode == "RTTY") c.RttyAbort(); else c.CwAbort(); });
                bAbort.FlatAppearance.BorderColor = cTx;
                foreach (string[] m in Macros)
                {
                    string text = m[1];
                    macroButtons.Add(mk(m[0], 0, 0, 80, 28, () => macro(text)));
                }

                Controls.AddRange(new Control[] { title, bRx1, bRx2, lm, lt, my, their, bClear, settings, rx, meter, status, hint, type, bTx, bEnd, bAbort });
                foreach (Button b in macroButtons) Controls.Add(b);
                settings.Controls.AddRange(new Control[] { lblShift, shift, lblBaud, baud, rev, afc, lblSql, rsql, lblLevel, rlevel, csql, lblWpm, wpm });
                Resize += (s, e) => layout();
                timer.Tick += (s, e) => tick();
                FormClosing += (s, e) => timer.Stop();
                Shown += (s, e) => { layout(); timer.Start(); type.Focus(); };
                layout();
            }

            private Label lbl(string t, int x, int y)
            {
                Label l = new Label { Text = t, AutoSize = true, ForeColor = cMuted, Left = x, Top = y < 0 ? -1 : y };
                if (y == -1) { l.Top = 0; l.Left = x; l.Tag = "hdr"; }
                return l;
            }

            private TextBox field(int x, int y, int w)
            {
                return new TextBox { Left = x, Top = y + 0, Width = w, BackColor = cField, ForeColor = cText, BorderStyle = BorderStyle.FixedSingle };
            }

            private NumericUpDown nud(int min, int max, int inc, int v)
            {
                return new NumericUpDown { Minimum = min, Maximum = max, Increment = inc, Value = Math.Max(min, Math.Min(max, v)), Width = 56, BackColor = cField, ForeColor = cText, BorderStyle = BorderStyle.FixedSingle };
            }

            private Button mk(string text, int x, int y, int w, int h, Action act)
            {
                Button b = new Button
                {
                    Text = text, Left = x, Top = y, Width = w, Height = h, FlatStyle = FlatStyle.Flat, BackColor = cBtn, ForeColor = cText,
                    UseVisualStyleBackColor = false, TabStop = false, Cursor = Cursors.Hand
                };
                b.FlatAppearance.BorderColor = cLine;
                b.FlatAppearance.MouseOverBackColor = cBtnHover;
                b.Click += (s, e) => { act(); type.Focus(); };
                return b;
            }

            public void SetMode(string m)
            {
                mode = m;
                title.Text = m == "RTTY" ? "RTTY" : "CW";
                bool r = m == "RTTY";
                foreach (Control k in new Control[] { lblShift, shift, lblBaud, baud, rev, afc, lblLevel, rlevel }) k.Visible = r;
                foreach (Control k in new Control[] { lblWpm, wpm }) k.Visible = !r;
                csql.Visible = !r; rsql.Visible = r; lblSql.Visible = true;
                bTx.Visible = r;
                bEnd.Text = r ? "RX" : "STOP";
                if (!r) { try { wpm.Value = Math.Max(wpm.Minimum, Math.Min(wpm.Maximum, c.CWXForm.WPM)); } catch { } }
                int si = shift.Items.IndexOf(c._rttyShift.ToString("0", CultureInfo.InvariantCulture)); if (si >= 0) shift.SelectedIndex = si;
                int bi = baud.Items.IndexOf(c._rttyBaud.ToString("0.##", CultureInfo.InvariantCulture)); if (bi >= 0) baud.SelectedIndex = bi;
                layoutSettings();
                sync();
            }

            private void sync()
            {
                foreach (Button b in new[] { bRx1, bRx2 })
                {
                    bool on = (b == bRx1) == (c._digiRx == 0);
                    b.BackColor = on ? cAccent : cBtn; b.ForeColor = on ? Color.FromArgb(20, 20, 24) : cText;
                }
            }

            private void layoutSettings()
            {
                int x = 0;
                Action<Control, int> put = (ctl, gap) => { if (!ctl.Visible) return; ctl.Left = x; ctl.Top = ctl.Height < 24 ? 5 : 3; x += ctl.Width + gap; };
                // size labels first
                foreach (Control k in settings.Controls) if (k is Label) { k.Top = 5; }
                if (mode == "RTTY")
                {
                    put(lblShift, 4); put(shift, 14); put(lblBaud, 4); put(baud, 14); put(rev, 14); put(afc, 14); put(lblSql, 4); put(rsql, 14); put(lblLevel, 4); put(rlevel, 4);
                }
                else
                {
                    put(lblSql, 4); put(csql, 14); put(lblWpm, 4); put(wpm, 4);
                }
            }

            private void layout()
            {
                int w = ClientSize.Width, h = ClientSize.Height;
                bClear.Left = w - 12 - bClear.Width;
                title.Width = 200;
                foreach (Control k in Controls)
                {
                    Label l = k as Label;
                    if (l != null && (string)l.Tag == "hdr") l.Top = 12;
                }
                // call boxes: labels sit left of the boxes
                int cx = 354;
                foreach (Control k in Controls)
                {
                    Label l = k as Label;
                    if (l != null && (string)l.Tag == "hdr") { l.Left = l.Text == "My call" ? 346 : 506; }
                }
                my.Left = 396; my.Width = 100; their.Left = 574; their.Width = 100;
                my.Top = their.Top = 10;
                layoutSettings();
                settings.Width = w - 24;
                int bottom = h - 12;
                int mY = bottom - 28;
                int bx = 12;
                foreach (Button b in macroButtons) { b.Left = bx; b.Top = mY; bx += b.Width + 8; }
                int tY = mY - 38;
                bAbort.Left = w - 12 - bAbort.Width; bAbort.Top = tY;
                bEnd.Left = bAbort.Left - 8 - bEnd.Width; bEnd.Top = tY;
                bTx.Left = bEnd.Left - 8 - bTx.Width; bTx.Top = tY;
                int tx2 = bTx.Visible ? bTx.Left : bEnd.Left;
                type.Left = 12; type.Top = tY + 1; type.Width = tx2 - 20; type.Height = 28;
                status.Left = 12; status.Top = tY - 26; status.Width = (w - 24) / 2;
                hint.Left = status.Right; hint.Top = status.Top; hint.Width = w - 12 - hint.Left;
                meter.Left = 12; meter.Width = w - 24; meter.Top = status.Top - 18;
                rx.Left = 12; rx.Top = 76; rx.Width = w - 24; rx.Height = Math.Max(40, meter.Top - 6 - rx.Top);
            }

            public void Append(char ch, bool sent)
            {
                if (ch == '\r') return;
                if (rx.TextLength > 60000) { rx.Select(0, 20000); rx.SelectedText = ""; }
                if (sent != lastSent && ch != '\n')
                {
                    if (rx.TextLength > 0 && !rx.Text.EndsWith("\n")) rx.AppendText("\n");
                    lastSent = sent;
                }
                if (ch == '\n' && rx.TextLength > 0 && rx.Text.EndsWith("\n")) return;
                rx.SelectionStart = rx.TextLength;
                rx.SelectionLength = 0;
                rx.SelectionColor = sent ? cAccent : cText;
                rx.AppendText(ch.ToString());
            }

            private void macro(string text)
            {
                string t = text.Replace("{MY}", c._digiMyCall).Replace("{CALL}", their.Text.Trim().ToUpperInvariant());
                if (c._digiMyCall.Length == 0) { status.Text = "Enter your call sign first"; return; }
                if (mode == "RTTY") c.RttySend(t.Replace("\r\n", "\r\n"), true);
                else c.CwSend(t.Replace("\r\n", " "));
            }

            private void typeKey(object s, KeyPressEventArgs e)
            {
                if (e.KeyChar == (char)Keys.Enter)
                {
                    e.Handled = true;
                    string t = type.Text;
                    type.Clear();
                    if (mode == "RTTY")
                    {
                        if (c.RttyTransmitting) { foreach (char ch in "\r\n") c.RttyType(ch); }
                        else if (t.Length > 0) c.RttySend(t + "\r\n", false);
                    }
                    else if (t.Length > 0) c.CwSend(t);
                    return;
                }
                // while RTTY is on the air, characters go out as they are typed
                if (mode == "RTTY" && c.RttyTransmitting && e.KeyChar >= ' ')
                {
                    e.Handled = true;
                    c.RttyType(char.ToUpperInvariant(e.KeyChar));
                }
            }

            private void tick()
            {
                c.DigiUiTick(this);
                string problem = mode == "RTTY" ? c.RttyStartProblem : c.CwStartProblem;
                bool ok = mode == "RTTY" ? c.RttyModeOk : c.CwModeOk;
                if (mode == "RTTY")
                {
                    RttyDemod d = c._rttyDemod;
                    if (d != null)
                    {
                        meter.Value = d.Quality;
                        status.Text = string.Format(CultureInfo.InvariantCulture, "Quality {0:0}%   centre {1:0} Hz", d.Quality * 100, d.Center);
                    }
                    hint.Text = c.RttyTransmitting ? "TRANSMITTING - type to send, RX to finish" : (ok ? "Enter sends a line; TX keys the radio, then type" : "Use DIGL / LSB");
                    hint.ForeColor = c.RttyTransmitting ? cTx : cMuted;
                    bTx.BackColor = c.RttyTransmitting ? cTx : cBtn;
                }
                else
                {
                    CwDecoder d = c._cwDecoder;
                    if (d != null)
                    {
                        meter.Value = d.Level;
                        status.Text = string.Format(CultureInfo.InvariantCulture, "{0:0} WPM   pitch {1} Hz   SNR {2:0.0}   {3}", d.Wpm, c.CWPitch, d.Snr, d.KeyDown ? "key down" : "");
                    }
                    hint.Text = c.CwSending ? "SENDING" : (ok ? "Enter sends the line through CWX" : "Use CWL / CWU");
                    hint.ForeColor = c.CwSending ? cTx : cMuted;
                }
                if (problem != null && !c.PowerOn) status.Text = problem;
            }
        }

        internal void CwStopQueueSafe()
        {
            try { CWXForm.CWXStop(); } catch { }
            while (_cwEcho.TryDequeue(out char _)) { }
        }

        private sealed class MeterBar : Control
        {
            public double Value
            {
                get { return v; }
                set { value = Math.Max(0, Math.Min(1, value)); if (Math.Abs(value - v) > 0.004) { v = value; Invalidate(); } }
            }
            private double v;
            public MeterBar() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true); }
            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(cField);
                int w = (int)((Width - 2) * v);
                if (w > 0)
                    using (SolidBrush b = new SolidBrush(cAccent)) g.FillRectangle(b, 1, 1, w, Height - 2);
                using (Pen p = new Pen(cLine)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            }
        }
    }
}
