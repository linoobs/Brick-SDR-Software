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
        private BrickDigiPane _digiPane;
        private Form _digiHost;                         // the terminal's own window while it is popped out
        private Panel _pnlDigi;                         // under the panadapter: where a terminal (or the FreeDV strip) docks
        private bool _digiPopped;
        private string _digiOpenKey = "";               // what was open under the panadapter ("RTTY", "CW", "RADE"), reopened at the next start
        private int _digiDockHeight = 250;
        private string _digiMode = "";                  // "RTTY" or "CW" while a terminal is open

        // settings (digital.txt in the data folder)
        private string _digiMyCall = "";
        private int _digiRx;
        private double _rttyShift = 170, _rttyBaud = 45.45;
        private bool _rttyReverse, _rttyAfc = true;
        private int _rttySql = 15, _rttyLevel = 0, _cwSql = 20;

        // engine state
        private Thread _digiThread;
        private volatile bool _digiRun;
        private int _digiTapRx = -1;
        private RttyDemod _rttyDemod;
        private RttyMod _rttyMod;
        private CwDecoder _cwDecoder;
        private DeepCwDecoder _cwDeep;                  // the CW decoder in use when DeepCW (neural network) loads; _cwDecoder is the basic one kept as the fallback
        private GgMorseDecoder _cwGg;                  // the third CW decoder (Goertzel and a cost function, after ggmorse)
        internal volatile int _cwKind = 1;              // which one is in use: 0 basic, 1 DeepCW, 2 Goertzel (ggmorse); switched in the CW window, digital.txt "cwdec"
        private int _cwKindAsked = -1;
        private static DeepCwEngine _deepEngine;
        private static string _deepError;
        private static bool _deepTried;
        private readonly ConcurrentQueue<KeyValuePair<char, bool>> _digiOut = new ConcurrentQueue<KeyValuePair<char, bool>>();     // char, sent
        private readonly ConcurrentQueue<char> _rttyTxQueue = new ConcurrentQueue<char>();
        private readonly ConcurrentQueue<char> _cwEcho = new ConcurrentQueue<char>();
        private volatile bool _rttyTx, _rttyTxBegun, _rttyTxEnding, _rttyTxDrained;
        private bool _cwEchoHooked, _cwWasSending;

        private string DigiFile { get { return Path.Combine(AppDataPath, "digital.txt"); } }

        private bool _digiLoaded;

        private void digiLoadSettings()
        {
            _digiLoaded = true;
            try
            {
                if (!File.Exists(DigiFile)) { foreach (string cn in new[] { "European Russia", "Asiatic Russia", "Kaliningrad" }) _ftBlocked.Add(cn); return; }
                bool lvl2 = false;
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
                        case "pop": _digiPopped = v == "1"; break;
                        case "open": _digiOpenKey = v.Trim(); break;
                        case "lvl": lvl2 = v == "3"; break;
                        case "grid": _ftGrid = v.Trim().ToUpperInvariant(); break;
                        case "ftmode": if (v == "FT4") _ftMode = "FT4"; break;
                        case "ftrx": if (num) _ftRxHz = (int)Math.Max(200, Math.Min(3000, d)); break;
                        case "fttx": if (num) _ftTxHz = (int)Math.Max(200, Math.Min(3000, d)); break;
                        case "ftauto": _ftAuto = v != "0"; break;
                        case "psk": _ftPsk = v == "1"; break;
                        case "blockunknown": _ftBlockUnknown = v == "1"; break;
                        case "blocked": _ftBlockedSeen = true; _ftBlocked.Clear(); foreach (string cn in v.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)) _ftBlocked.Add(cn.Trim()); break;
                        case "cwdec": if (num) _cwKind = (int)Math.Max(0, Math.Min(2, d)); break;
                        case "ftlevel": if (num) _ftLevel = Math.Max(-40, Math.Min(0, d)); break;
                    }
                }
                // before level scheme 3 the TX audio of RTTY and FT8 was set low (-10 dB): the Drive slider did not give what it gives in CW
                if (!lvl2) { _rttyLevel = 0; _ftLevel = 0; }
                // the first start (no list saved yet): Russia is not answered, the user can take it out of the list
                if (!_ftBlockedSeen) { _ftBlocked.Clear(); foreach (string cn in new[] { "European Russia", "Asiatic Russia", "Kaliningrad" }) _ftBlocked.Add(cn); }
            }
            catch { }
        }

        private void digiSaveSettings()
        {
            if (!_digiLoaded) digiLoadSettings();
            try
            {
                File.WriteAllLines(DigiFile, new[]
                {
                    "mycall=" + _digiMyCall, "rx=" + _digiRx,
                    "shift=" + _rttyShift.ToString(CultureInfo.InvariantCulture), "baud=" + _rttyBaud.ToString(CultureInfo.InvariantCulture),
                    "rev=" + (_rttyReverse ? 1 : 0), "afc=" + (_rttyAfc ? 1 : 0), "rsql=" + _rttySql, "rlevel=" + _rttyLevel, "csql=" + _cwSql, "pop=" + (_digiPopped ? 1 : 0), "open=" + _digiOpenKey,
                    "grid=" + _ftGrid, "ftmode=" + _ftMode, "ftrx=" + _ftRxHz, "fttx=" + _ftTxHz, "ftauto=" + (_ftAuto ? 1 : 0), "psk=" + (_ftPsk ? 1 : 0), "blocked=" + string.Join(";", _ftBlocked), "blockunknown=" + (_ftBlockUnknown ? 1 : 0), "cwdec=" + _cwKind, "ftlevel=" + _ftLevel.ToString(CultureInfo.InvariantCulture), "lvl=3"
                });
            }
            catch { }
        }

        // ---- opening and closing ----

        private void brickDigitalOpen(string mode)
        {
            if (_digiPane == null || _digiPane.IsDisposed)
            {
                digiLoadSettings();
                _digiPane = new BrickDigiPane(this);
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
            _digiOpenKey = mode; digiSaveSettings();
            _digiPane.SetMode(mode);
            brickDockRelease("digi");        // the FreeDV strip (one thing under the panadapter at a time)
            digiPlace();
            digiStartEngine();
        }

        // at the start: the terminal or strip that was open when the program was closed is back
        private void brickDigitalRestore()
        {
            try
            {
                string logFile = FtLogFile;       // makes the digi_modes folder (and moves an old ft_log.adi into it)
                digiLoadSettings();
                OnlineLogsInit();                 // retries what could not be uploaded last time
                brickEnsureSsbLeveler();
                // the TX profile and the saved state may still be loading: look again
                System.Windows.Forms.Timer lv = new System.Windows.Forms.Timer { Interval = 4000 };
                lv.Tick += (s, e) => { lv.Stop(); lv.Dispose(); brickEnsureSsbLeveler(); };
                lv.Start();
                // a window comes back only when the receiver is still in a mode that suits it (the program may have been closed in SSB with the window open)
                DSPMode rm = digiRxMode;
                bool fits = _digiOpenKey == "RTTY" ? (rm == DSPMode.DIGU || rm == DSPMode.DIGL)
                          : _digiOpenKey == "CW" ? (rm == DSPMode.CWL || rm == DSPMode.CWU)
                          : (_digiOpenKey == "FT8" || _digiOpenKey == "FT4") ? rm == DSPMode.DIGU : true;
                if (!fits && _digiOpenKey.Length > 0) { _digiOpenKey = ""; digiSaveSettings(); }
                if (_digiOpenKey == "RTTY" || _digiOpenKey == "CW") brickDigitalOpen(_digiOpenKey);
                else if (_digiOpenKey == "RADE") brickRadeOpen(RadeEnabledOn(1) && !RadeEnabledOn(0) ? 1 : 0);
                else if (_digiOpenKey == "FT8" || _digiOpenKey == "FT4") brickFtOpen(_digiOpenKey);
            }
            catch { }
        }

        private void digiStop()
        {
            digiTransmitAbort();
            digiStopEngine();
            _digiMode = "";
        }

        // ---- where the terminal lives: docked under the panadapter, or in its own window (Pop out) ----

        private void digiPlace()
        {
            if (_digiPane == null) return;
            if (_digiPopped)
            {
                if (_digiHost == null || _digiHost.IsDisposed)
                {
                    _digiHost = new Form
                    {
                        Owner = this,
                        ShowInTaskbar = false,
                        StartPosition = FormStartPosition.Manual,
                        BackColor = cBg,
                        Size = new Size(820, 340),
                        MinimumSize = new Size(700, 280),
                        Location = new Point(Left + 80, Top + Height / 2),
                        Icon = Icon
                    };
                    _digiHost.FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; digiClose(); } };
                }
                _digiHost.Text = "Brick-SDR - " + _digiMode;
                brickDockHide("digi");
                if (_digiPane.Parent != _digiHost) { _digiPane.Dock = DockStyle.Fill; _digiHost.Controls.Add(_digiPane); }
                _digiPane.SetPopped(true);
                _digiPane.Visible = true;
                if (!_digiHost.Visible) _digiHost.Show(this);
            }
            else
            {
                if (_digiHost != null) _digiHost.Hide();
                _digiPane.SetPopped(false);
                brickDockShow("digi", _digiPane);
            }
        }

        internal void digiTogglePop()
        {
            _digiPopped = !_digiPopped;
            digiSaveSettings();
            digiPlace();
        }

        internal void digiClose()
        {
            _digiOpenKey = ""; digiSaveSettings();
            if (_digiPane != null) _digiPane.Visible = false;
            if (_digiHost != null) _digiHost.Hide();
            brickDockHide("digi");
            digiStop();
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

        // The decoders' tap on a receiver on or off. While it is on, WDSP's output gain of that receiver stays at unity and the
        // RX AF / MUTE level is applied after the tap (radio.cs RXOutputGain, kdigi.c), so what is decoded doesn't depend on them.
        private void digiTap(int rx, int on)
        {
            KDigi.KDigiRxTap(rx, on);
            if (rx < 0 || rx > 1) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    try { var d = radio.GetDSPRX(rx, 0); d.RXOutputGain = d.RXOutputGain; } catch { }
                }));
            }
            catch { }
        }

        // the DeepCW network, loaded once; null (and the reason in _deepError) when the model or ONNX Runtime can't be loaded
        private static DeepCwEngine deepCwEngine()
        {
            if (!_deepTried)
            {
                _deepTried = true;
                try { _deepEngine = DeepCwEngine.Create(Path.Combine(System.Windows.Forms.Application.StartupPath, "deepcw", "model.onnx"), out _deepError); }
                catch (Exception ex) { _deepError = ex.Message; }
            }
            return _deepEngine;
        }

        // the CW decoder chosen in the window (_cwKind); DeepCW falls back to the basic one when it can't be loaded
        private void cwBuild(int rate)
        {
            if (_cwDeep != null) { _cwDeep.Dispose(); _cwDeep = null; }
            _cwDecoder = null; _cwGg = null;
            int asked = _cwKind, kind = asked;
            _cwKindAsked = asked;
            if (kind == 1)
            {
                DeepCwEngine de = deepCwEngine();
                if (de != null)
                {
                    _cwDeep = new DeepCwDecoder(de, rate);
                    _cwDeep.Decoded += c => _digiOut.Enqueue(new KeyValuePair<char, bool>(c, false));
                    return;
                }
                kind = 0;
            }
            if (kind == 2)
            {
                _cwGg = new GgMorseDecoder(rate);
                _cwGg.Decoded += c => _digiOut.Enqueue(new KeyValuePair<char, bool>(c, false));
                return;
            }
            _cwDecoder = new CwDecoder(rate);
            _cwDecoder.Decoded += c => _digiOut.Enqueue(new KeyValuePair<char, bool>(c, false));
        }

        internal bool DeepCwAvailable { get { return deepCwEngine() != null; } }
        internal string DeepCwError { get { return _deepError; } }

        private void digiStopEngine()
        {
            if (_cwDeep != null) { _cwDeep.Dispose(); _cwDeep = null; }
            _digiRun = false;
            if (_digiThread != null) _digiThread.Join(500);
            _digiThread = null;
            try { if (_digiTapRx >= 0) digiTap(_digiTapRx, 0); } catch { }
            _digiTapRx = -1;
            _rttyDemod = null; _cwDecoder = null; _cwGg = null; _cwKindAsked = -1;
        }

        // USB / DIGU put mark above space in the audio; LSB / DIGL (RTTY's usual) below
        private bool rttyUpperSideband { get { DSPMode m = digiRxMode; return m == DSPMode.USB || m == DSPMode.DIGU; } }

        internal bool RttyModeOk { get { DSPMode m = digiRxMode; return m == DSPMode.LSB || m == DSPMode.USB || m == DSPMode.DIGL || m == DSPMode.DIGU; } }
        internal bool CwModeOk { get { DSPMode m = digiRxMode; return m == DSPMode.CWL || m == DSPMode.CWU; } }

        // The RTTY tones as offsets from the receiver's VFO in Hz (mark first; negative below the VFO on LSB / DIGL), for the
        // markers on that receiver's panadapter (BrickDigiMarkers.cs); null when the terminal isn't open on it
        internal double[] BrickDigiMarkers(int rx)
        {
            if (_digiMode != "RTTY" || _digiPane == null || !_digiPane.Visible || _digiRx != rx - 1 || !RttyModeOk) return null;
            RttyDemod d = _rttyDemod;
            double c = d != null ? d.Center : 2210, h = _rttyShift / 2;
            bool upper = rttyUpperSideband;
            double mark = _rttyReverse ^ upper ? c + h : c - h, space = _rttyReverse ^ upper ? c - h : c + h;
            return upper ? new[] { mark, space } : new[] { -mark, -space };
        }

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
                        if (_digiTapRx >= 0) digiTap(_digiTapRx, 0);
                        digiTap(rx, 1);
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
                        else if (mode == "CW") cwBuild(rate);
                    }
                    // the decoder was switched in the CW window
                    if (mode == "CW" && rate > 0 && _cwKind != _cwKindAsked) { cwBuild(rate); _digiOut.Enqueue(new KeyValuePair<char, bool>('\n', false)); }
                    if (mode == "RTTY") rttyApplyParams();
                    else if (mode == "CW")
                    {
                        if (_cwDeep != null) _cwDeep.Pitch = CWPitch;
                        else if (_cwGg != null) { }
                        else if (_cwDecoder != null) { _cwDecoder.Pitch = CWPitch; _cwDecoder.Squelch = _cwSql / 100.0; }
                    }

                    int n;
                    while ((n = KDigi.KDigiRxRead(rx, buf, buf.Length)) > 0)
                    {
                        if (_mox) continue;
                        if (mode == "RTTY" && _rttyDemod != null) _rttyDemod.Process(buf, n);
                        else if (mode == "CW") { if (_cwDeep != null) _cwDeep.Process(buf, n); else if (_cwGg != null) _cwGg.Process(buf, n); else if (_cwDecoder != null) _cwDecoder.Process(buf, n); }
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
        internal void DigiUiTick(BrickDigiPane f)
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
