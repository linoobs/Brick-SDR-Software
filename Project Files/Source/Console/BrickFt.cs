/*  BrickFt.cs

    FT8 and FT4 in the DIGI menu: the engine. Audio comes from the receiver tap (kdigi.c, the same one the RTTY and CW
    terminals use), is cut into the mode's UTC slots (15 s for FT8, 7.5 s for FT4) and decoded by mfsk-core (BrickMfsk.cs)
    when the slot ends. A transmission starts at the beginning of a slot of the chosen parity, through the digital transmit
    feed, with the audio starting half a second into the slot. The window is BrickFtPane.cs.
*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BrickSDR
{
    public partial class Console
    {
        internal sealed class FtRow
        {
            public DateTime Utc;
            public float Snr, Dt, Hz;
            public string Text = "";
            public bool Even, Sent, ToMe, Cq;
            public string Sender = "", Country = "";            // who sent it and from which country (from the call sign)
        }

        private BrickFtPane _ftPane;
        private bool _ftBlockUnknown;                       // also do not answer a station whose country the table does not know (digital.txt "blockunknown")
        internal bool FtBlockUnknown { get { if (!_digiLoaded) digiLoadSettings(); return _ftBlockUnknown; } set { _ftBlockUnknown = value; digiSaveSettings(); if (_ftPane != null) _ftPane.Refresh2(); } }
        private readonly HashSet<string> _ftBlocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);     // countries that are not answered, digital.txt "blocked"
        private string _ftMode = "FT8";                 // "FT8" or "FT4", the one the window is set to
        private string _ftGrid = "";
        private int _ftRxHz = 1500, _ftTxHz = 1500;
        private bool _ftPsk;                              // PSK Reporter spotting (Setup - General - DIGI MODES - FT8/FT4), digital.txt "psk"
        private PskReporter _psk;
        // The transmit audio level has no control in the window: it is full scale (0 dB), enough for the TX leveler to saturate, so the main Drive slider sets the power as it does in CW.
        private double _ftLevel = 0;            // dB, digital.txt "ftlevel"
        private bool _ftAuto = true;
        private volatile bool _ftRun;
        private Thread _ftThread;
        private readonly ConcurrentQueue<FtRow> _ftRows = new ConcurrentQueue<FtRow>();
        private readonly object _ftDecLock = new object();
        private Mfsk.Decoder _ftDecoder;
        private readonly HashSet<string> _ftSeen = new HashSet<string>();
        private long _ftSeenPeriod = long.MinValue;
        private long _ftDecoderKey = -1;
        private string _ftStatus = "";
        private volatile float _ftInDb = -120;           // receive audio level (dB re full scale) for the meter
        private volatile bool _ftClip;
        private volatile bool _ftClockOk = true;
        private volatile int _ftDecodeMs;                     // how long the last decoding pass took, and when it ended (seconds on the UTC clock)
        private double _ftDecodeAt;
        internal int FtDecodeMs { get { return _ftDecodeMs; } }
        private double _ftClock;                          // seconds the PC clock is ahead of the stations heard (their median DT), taken off the slot timing
        internal double FtClock { get { return _ftClock; } }
        private double _ftSlotPos;                       // seconds into the current slot, for the progress bar
        internal double FtSlotLength { get { return _ftMode == "FT4" ? 7.5 : 15.0; } }
        internal double FtSlotPos { get { return _ftSlotPos; } }

        // the QSO in progress
        private string _ftDx = "", _ftDxGrid = "", _ftRptSent = "", _ftRptRcvd = "";
        private int _ftMsgNo = 6;                        // the Tx1 .. Tx6 message that goes out next (6 = CQ)
        private string _ftCustom = "";                   // a message typed by hand, sent instead of the numbered one
        private volatile bool _ftTxEnable;
        private volatile bool _ftTxEven = true;
        private int _ftTries;
        private bool _ftLogged;
        private DateTime _ftQsoStart;

        // transmit state (engine thread)
        private int _ftTxState;                          // 0 idle, 1 on the air
        private float[] _ftTxAudio;
        private int _ftTxPos;
        private double _ftTxAudioStart, _ftTxBegan;
        private long _ftTxSlot = -1, _ftSkipSlot = -1;
        private string _ftTxSent = "";
        private volatile bool _ftTxActive;
        internal bool FtTransmitting { get { return _ftTxActive; } }

        private static readonly DateTime FtEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static double ftUtcSeconds() { return (DateTime.UtcNow - FtEpoch).TotalSeconds; }

        // ---- opening and closing ----

        // FreeDV is switched off when a terminal or FT8 / FT4 takes the receiver (the mode of FreeDV and of RTTY is the same, so no mode change does it); the DIGI menu calls it before it sets the mode, because switching FreeDV off puts the receiver back in the mode it had
        internal void ftRadeOffForTerminal()
        {
            try { for (int i = 0; i < 2; i++) if (RadeEnabledOn(i)) SetRadeEnabled(i, false); } catch { }
        }

        internal void brickFtOpen(string mode)
        {
            if (!Mfsk.Available()) { MessageBox.Show(this, "mfsk.dll is missing next to the program.", "FT8 / FT4", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!_digiLoaded) digiLoadSettings();
            if (mode != _ftMode) { ftDropDecoder(); _ftMode = mode; }
            if (_ftPane == null || _ftPane.IsDisposed) _ftPane = new BrickFtPane(this);
            _ftPane.SetMode(_ftMode);
            brickDockRelease("ft");
            brickDockShow("ft", _ftPane);
            _digiOpenKey = _ftMode; digiSaveSettings();
            ftStartEngine();
        }

        internal void brickFtSetMode(string mode)
        {
            if (mode == _ftMode) return;
            ftAbortTx();
            _ftTxEnable = false;
            ftDropDecoder();
            _ftMode = mode;
            ftApplyDial();
            _digiOpenKey = mode;
            digiSaveSettings();
        }

        internal void brickFtClose()
        {
            _ftTxEnable = false;
            ftAbortTx();
            ftStopEngine();
            brickDockHide("ft");
            _digiOpenKey = ""; digiSaveSettings();
        }

        // the window is gone for another one (or the mode no longer fits)
        private void ftRelease()
        {
            _ftTxEnable = false;
            ftAbortTx();
            ftStopEngine();
        }

        private void ftStartEngine()
        {
            if (_ftRun) return;
            _ftRun = true;
            _ftThread = new Thread(ftWork) { IsBackground = true, Name = "Brick FT8", Priority = ThreadPriority.AboveNormal };
            _ftThread.Start();
        }

        private void ftStopEngine()
        {
            try { if (_psk != null) { _psk.FlushIfDue(); _psk.Stop(); } } catch { }
            _ftRun = false;
            if (_ftThread != null) _ftThread.Join(800);
            _ftThread = null;
            try { if (_digiTapRx >= 0) digiTap(_digiTapRx, 0); } catch { }
            _digiTapRx = -1;
            ftDropDecoder();
        }

        private void ftDropDecoder()
        {
            lock (_ftDecLock) { if (_ftDecoder != null) { _ftDecoder.Dispose(); _ftDecoder = null; } _ftDecoderKey = -1; }
        }

        internal bool FtModeOk { get { return digiRxMode == DSPMode.DIGU; } }

        internal string FtStartProblem
        {
            get
            {
                if (!PowerOn) return "The radio is off";
                if (!FtModeOk) return "Use DIGU";
                if (_digiRx == 1 && !chkVFOBTX.Checked) return "RX2 is selected but VFO A transmits";
                if (_digiMyCall.Length < 3) return "Enter your call sign first";
                if (_ftGrid.Length < 4) return "Enter your grid square first";
                return null;
            }
        }

        // put the receiver on the dial frequency of the mode, on the band it is on
        internal void ftApplyDial()
        {
            try
            {
                bool rx2 = _digiRx == 1;
                Band b = rx2 ? RX2Band : RX1Band;
                double[] f;
                if (brickDigiFreq.TryGetValue(b, out f))
                {
                    brickDigiSet(rx2, DSPMode.DIGU, f[_ftMode == "FT4" ? 1 : 0]);
                    if (!rx2) brickDigiChoice = _ftMode;
                }
                else brickDigiSet(rx2, DSPMode.DIGU, null);
            }
            catch { }
        }

        // ---- the engine thread: receive, slots, decode, transmit ----

        private void ftWork()
        {
            float[] buf = new float[16384];
            float[] ring = null;
            long total = 0, startIdx = 0;
            int rate = 0;
            long lastSlot = long.MinValue, prefixSlot = long.MinValue, fullSlot = long.MinValue;
            long lastSpecAt = 0;
            int lastRateWarn = 0;
            double smooth = -120;
            while (_ftRun)
            {
                try
                {
                    string mode = _ftMode;
                    double T = mode == "FT4" ? 7.5 : 15.0;
                    int rx = _digiRx;
                    if (rx != _digiTapRx)
                    {
                        if (_digiTapRx >= 0) digiTap(_digiTapRx, 0);
                        digiTap(rx, 1);
                        _digiTapRx = rx;
                        lastSlot = long.MinValue;
                    }
                    int r = KDigi.KDigiRxRate(rx);
                    if (r > 0 && r != rate)
                    {
                        rate = r;
                        ring = new float[rate * 40];
                        total = 0; startIdx = 0; lastSlot = long.MinValue;
                    }
                    if (rate > 0)
                    {
                        int n;
                        double sq = 0, pk = 0; int cnt = 0;
                        while ((n = KDigi.KDigiRxRead(rx, buf, buf.Length)) > 0)
                        {
                            for (int i = 0; i < n; i++)
                            {
                                float v = buf[i];
                                ring[(int)((total + i) % ring.Length)] = v;
                                sq += v * v; float a = Math.Abs(v); if (a > pk) pk = a;
                            }
                            cnt += n;
                            total += n;
                        }
                        if (cnt > 0)
                        {
                            double db = 10 * Math.Log10(Math.Max(1e-12, sq / cnt));
                            smooth = smooth < -100 ? db : smooth + 0.3 * (db - smooth);
                            _ftInDb = (float)smooth;
                            _ftClip = pk >= 0.98;
                        }
                        if (total - lastSpecAt >= rate * 3 / 10 && total >= SpecN) { lastSpecAt = total; ftSpectrum(ring, total, rate); }       // a line of the close-up waterfall about every 0.3 s
                        double now = ftUtcSeconds() - _ftClock;
                        long slot = (long)Math.Floor(now / T);
                        double into = now - slot * T;
                        _ftSlotPos = into;
                        if (lastSlot == long.MinValue) { lastSlot = slot; startIdx = total - (long)(into * rate); }        // started inside a slot: its first part is missing (zeros)
                        if (slot != lastSlot)
                        {
                            // the slot that just ended: a last pass if the one after its signal was missed (it ran at the start, in a half slot...)
                            if (fullSlot != lastSlot && lastSlot != _ftSkipSlot && !_ftTxActive)
                                ftStartPass(ring, total, rate, startIdx, T, lastSlot, mode, false);
                            lastSlot = slot;
                            startIdx = total - (long)(into * rate);
                        }
                        // The messages are on the screen as soon as the signals are over, not at the end of the slot, so a station can be chosen and answered at once:
                        // FT8 has a first pass at 11.8 s (what the decoder of WSJT-X does too: the strong stations are found there), and a pass of the whole slot
                        // right after the last symbol (0.5 s + the length of the signal + a little). In a slot that was entered in the middle the missing start is zeros.
                        double sigLen = mode == "FT4" ? 5.04 : 12.64, fullAt = 0.5 + sigLen + 0.15;
                        if (slot == lastSlot && slot != _ftSkipSlot && !_ftTxActive)
                        {
                            if (mode == "FT8" && prefixSlot != slot && into >= 11.85 && into < fullAt) { prefixSlot = slot; ftStartPass(ring, total, rate, startIdx, T, slot, mode, true); }
                            if (fullSlot != slot && into >= fullAt) { fullSlot = slot; ftStartPass(ring, total, rate, startIdx, T, slot, mode, false); }
                        }
                        ftTxStep(now, T, slot, into);
                    }
                    else
                    {
                        if (lastRateWarn++ % 100 == 0) _ftStatus = "Waiting for the receiver audio";
                    }
                }
                catch (Exception) { }
                Thread.Sleep(15);
            }
        }

        // the audio of a slot for the decoder: 16 bit at 12 kHz when the receiver rate is a multiple of it (mfsk-core's own rate, and its early pass needs it), else at
        // the receiver rate. Before the first sample (the program entered the slot late) there are zeros. The level is fitted to the peak of what there is (the AGC changes it).
        private short[] ftGrab(float[] ring, long total, int rate, long start, double slotSeconds, out int outRate, out int real)
        {
            int f = rate % 12000 == 0 ? rate / 12000 : 1;
            outRate = rate / f;
            int len = (int)Math.Round(slotSeconds * outRate);
            float[] tmp = new float[len];
            float peak = 0;
            real = 0;
            for (int i = 0; i < len; i++)
            {
                long a = start + (long)i * f;
                double sum = 0; bool any = false;
                for (int k = 0; k < f; k++)
                {
                    long ix = a + k;
                    if (ix >= 0 && ix < total && total - ix < ring.Length) { sum += ring[(int)(ix % ring.Length)]; any = true; }
                }
                if (any) { tmp[i] = (float)(sum / f); real = i + 1; float ab = Math.Abs(tmp[i]); if (ab > peak) peak = ab; }
            }
            float scale = peak > 1e-6f ? Math.Max(0.02f, Math.Min(50f, 0.9f / peak)) * 32767f : 0f;
            short[] s = new short[len];
            for (int i = 0; i < len; i++) { float v = tmp[i] * scale; s[i] = (short)(v > 32767 ? 32767 : v < -32768 ? -32768 : v); }
            return s;
        }

        // one decoding pass over the slot that began at sample startIdx; prefix: the early pass of FT8 (the first 141 696 samples at 12 kHz = 11.8 s)
        private void ftStartPass(float[] ring, long total, int rate, long startIdx, double T, long period, string mode, bool prefix)
        {
            int outRate, real;
            short[] s = ftGrab(ring, total, rate, startIdx, T, out outRate, out real);
            if (real < outRate * 2) return;                                        // not even two seconds of this slot
            if (prefix)
            {
                if (outRate != 12000) return;
                short[] p = new short[141696];
                Array.Copy(s, p, Math.Min(p.Length, s.Length));
                s = p;
            }
            string md = mode; bool pf = prefix;
            Task.Run(() => ftDecode(s, outRate, period, md, pf));
        }

        private void ftDecode(short[] samples, int rate, long period, string mode, bool prefix = false)
        {
            try
            {
                List<Mfsk.Decoder.Row> rows;
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                lock (_ftDecLock)
                {
                    if (!_ftRun || mode != _ftMode) return;
                    if (_ftDecoder == null) _ftDecoder = new Mfsk.Decoder(mode == "FT4" ? Mfsk.FT4 : Mfsk.FT8);
                    _ftDecoder.Context(_digiMyCall, _ftGrid, _ftDx, _ftDxGrid, _ftRxHz);
                    rows = _ftDecoder.DecodePeriod(samples, rate, period, prefix);
                    // the early pass and the pass at the end of a slot see the same signals: report each message once
                    if (_ftSeenPeriod != period) { _ftSeen.Clear(); _ftSeenPeriod = period; }
                    rows = rows.FindAll(x => _ftSeen.Add(x.Text));
                }
                _ftDecodeMs = (int)sw.ElapsedMilliseconds;
                _ftDecodeAt = ftUtcSeconds() - _ftClock;
                double T = mode == "FT4" ? 7.5 : 15.0;
                DateTime start = FtEpoch.AddSeconds(period * T);
                foreach (var r in rows)
                {
                    FtRow row = new FtRow { Utc = start, Snr = r.Snr, Dt = r.Dt, Hz = r.Hz, Text = r.Text, Even = period % 2 == 0 };
                    ftClassify(row);
                    _ftRows.Enqueue(row);
                }
                ftTrackClock(rows);
                if (rows.Count > 0) BeginInvoke(new Action(() => { foreach (var r in rows) ftAutoSequence(r.Text, r.Snr, period % 2 == 0, r.Hz); ftPskAdd(rows, period, mode); }));
            }
            catch (Exception ex) { _ftStatus = "Decoder: " + ex.Message; }
        }

        // most stations keep good time: their median DT is the error of this PC clock
        private void ftTrackClock(List<Mfsk.Decoder.Row> rows)
        {
            List<float> d = new List<float>();
            foreach (var r in rows) if (r.Snr > -16) d.Add(r.Dt);
            if (d.Count < 4) return;
            d.Sort();
            float med = d[d.Count / 2];
            if (Math.Abs(med) < 0.12f) return;
            _ftClock = Math.Max(-3, Math.Min(3, _ftClock + 0.7 * med));
        }

        private void ftClassify(FtRow row)
        {
            string[] t = row.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            row.Cq = t.Length > 0 && t[0] == "CQ";
            row.ToMe = _digiMyCall.Length > 0 && t.Length > 0 && ftBase(t[0]) == _digiMyCall;
            row.Sender = FtSender(row);
            row.Country = FtCountryOf(row.Sender);
        }

        private static string ftBase(string call) { return call.Trim('<', '>'); }

        // ---- transmit ----

        private string ftCurrentMessage()
        {
            if (_ftCustom.Length > 0) return _ftCustom;
            string my = _digiMyCall, dx = _ftDx, g = _ftGrid.Length >= 4 ? _ftGrid.Substring(0, 4) : _ftGrid;
            string rs = _ftRptSent.Length > 0 ? _ftRptSent : "-10";
            switch (_ftMsgNo)
            {
                case 1: return dx.Length > 0 ? dx + " " + my + " " + g : "";
                case 2: return dx.Length > 0 ? dx + " " + my + " " + rs : "";
                case 3: return dx.Length > 0 ? dx + " " + my + " R" + rs : "";
                case 4: return dx.Length > 0 ? dx + " " + my + " RR73" : "";
                case 5: return dx.Length > 0 ? dx + " " + my + " 73" : "";
                default: return "CQ " + my + " " + g;
            }
        }

        internal string FtNextMessage { get { return ftCurrentMessage(); } }

        // how late into its slot a transmission may still begin: the first part of the signal is left out (the receiver of the other side needs most of it, so not more than this)
        // FT8: 2.5 s (a fifth of the 12.6 s signal), FT4: 1.2 s. Pressing Enable TX a little after the start of the slot is no loss of the whole round.
        internal static double FtLateLimit(double T) { return T > 10 ? 2.5 : 1.2; }

        private void ftTxStep(double now, double T, long slot, double into)
        {
            if (_ftTxState == 0)
            {
                if (!_ftTxEnable) return;
                string msg = ftCurrentMessage();
                if (msg.Length == 0 || FtStartProblem != null) return;
                if ((slot % 2 == 0) != _ftTxEven) return;
                if (slot == _ftTxSlot || into > FtLateLimit(T)) return;
                int txRate = KDigi.KDigiTxRate();
                if (txRate <= 0) return;
                string err;
                float[] a = Mfsk.Encode(_ftMode == "FT4" ? Mfsk.FT4 : Mfsk.FT8, msg, txRate, _ftTxHz, out err);
                if (a == null) { _ftStatus = "Can't send \"" + msg + "\": " + err; _ftTxEnable = false; return; }
                float amp = (float)Math.Pow(10, _ftLevel / 20.0);
                for (int i = 0; i < a.Length; i++) a[i] *= amp;
                _ftTxAudio = a;
                _ftTxPos = 0;
                double t0 = slot * T + 0.5;                      // the frame starts half a second into the slot
                if (now > t0) _ftTxPos = (int)((now - t0) * txRate);
                _ftTxAudioStart = t0;
                _ftTxBegan = now;
                _ftTxSlot = slot; _ftSkipSlot = slot;
                _ftTxSent = msg;
                _ftTxActive = true;
                KDigi.KDigiTxClear();
                KDigi.KDigiTxEnable(1);
                BeginInvoke(new Action(() => { if (_ftTxActive) MOX = true; }));
                _ftTxState = 1;
                _ftRows.Enqueue(new FtRow { Utc = FtEpoch.AddSeconds(slot * T), Snr = 0, Dt = 0, Hz = _ftTxHz, Text = msg, Even = slot % 2 == 0, Sent = true });
                return;
            }
            // on the air
            if (!_ftTxEnable) { ftEndTx(true); return; }
            if (now - _ftTxBegan > 1.2 && !_mox) { _ftTxEnable = false; ftEndTx(true); return; }          // un-keyed elsewhere
            int rate = KDigi.KDigiTxRate();
            if (rate > 0 && now >= _ftTxAudioStart - 0.02 || _ftTxPos > 0)
            {
                float[] a = _ftTxAudio;
                while (_ftTxPos < a.Length && KDigi.KDigiTxQueued() < rate / 4)
                {
                    int n = Math.Min(rate / 10, a.Length - _ftTxPos);
                    float[] part = new float[n];
                    Array.Copy(a, _ftTxPos, part, 0, n);
                    int w = KDigi.KDigiTxWrite(part, n);
                    _ftTxPos += w;
                    if (w == 0) break;
                }
                if (_ftTxPos >= a.Length && KDigi.KDigiTxQueued() == 0) { Thread.Sleep(120); ftEndTx(false); }
            }
        }

        private void ftEndTx(bool aborted)
        {
            string sent = _ftTxSent;
            _ftTxState = 0;
            KDigi.KDigiTxClear();
            _ftTxActive = false;
            BeginInvoke(new Action(() =>
            {
                if (_mox) MOX = false;
                KDigi.KDigiTxEnable(0);
                if (!aborted) ftAfterTx(sent);
            }));
        }

        private void ftAbortTx()
        {
            if (_ftTxState == 0 && !_ftTxActive) return;
            _ftTxState = 0;
            _ftTxActive = false;
            try { KDigi.KDigiTxClear(); } catch { }
            if (_mox) MOX = false;
            try { KDigi.KDigiTxEnable(0); } catch { }
        }

        internal void FtHalt() { _ftTxEnable = false; ftAbortTx(); if (_ftPane != null) _ftPane.Refresh2(); }

        internal void FtEnableTx(bool on)
        {
            if (on)
            {
                string p = FtStartProblem;
                if (p != null) { _ftStatus = p; return; }
                if (ftCurrentMessage().Length == 0) { _ftStatus = "Choose a station or press CQ first"; return; }
                if (_ftDx.Length > 0 && FtIsBlockedCall(_ftDx)) { _ftStatus = _ftDx + " is not answered (" + FtBlockReason(_ftDx, FtCountryOf(_ftDx)) + ")"; return; }
                _ftTries = 0;
                if (_ftDx.Length == 0 && !_ftTxEnable) ftTxInSoonestSlot();                      // CQ: no station to keep step with
                _ftTxEnable = true;
            }
            else FtHalt();
        }

        // the parity of the slot a transmission can still start in: this one when it has only just begun, else the next
        private void ftTxInSoonestSlot()
        {
            double T = FtSlotLength, now = ftUtcSeconds() - _ftClock;
            long slot = (long)Math.Floor(now / T);
            double into = now - slot * T;
            long use = into <= FtLateLimit(T) ? slot : slot + 1;
            _ftTxEven = use % 2 == 0;
        }

        internal void FtSetMessage(int no)
        {
            _ftCustom = "";
            _ftMsgNo = no;
        }

        internal void FtSetCustom(string text) { _ftCustom = text.Trim().ToUpperInvariant(); }

        internal void FtCallCq()
        {
            _ftDx = ""; _ftDxGrid = ""; _ftRptSent = ""; _ftRptRcvd = ""; _ftCustom = ""; _ftMsgNo = 6; _ftLogged = false;
            _ftQsoStart = default(DateTime);
            if (_ftPane != null) _ftPane.Refresh2();
            ftTxInSoonestSlot();
            FtEnableTx(true);
        }

        // the message just went out: where the QSO goes next
        private void ftAfterTx(string sent)
        {
            if (!_ftAuto) return;
            if (_ftCustom.Length > 0) { _ftCustom = ""; _ftTxEnable = false; return; }
            if (_ftMsgNo == 5) { _ftTxEnable = false; ftLogQso(); return; }                  // 73 sent: done
            if (_ftMsgNo == 4) { ftLogQso(); }                                              // RR73 sent: logged, wait for 73 once
            if (++_ftTries > (_ftMsgNo == 6 ? 40 : 6)) { _ftTxEnable = false; _ftStatus = "No answer, transmit stopped"; }
        }

        // a decoded message: moves a QSO on when it is addressed to us (or answers our CQ)
        private static readonly Regex GridRe = new Regex("^[A-R]{2}[0-9]{2}$", RegexOptions.Compiled);
        private static readonly Regex RptRe = new Regex("^[+-][0-9]{2}$", RegexOptions.Compiled);
        private static readonly Regex RRptRe = new Regex("^R[+-][0-9]{2}$", RegexOptions.Compiled);

        private static string ftReport(float snr)
        {
            int s = (int)Math.Round(Math.Max(-30, Math.Min(32, snr)));
            return (s < 0 ? "-" : "+") + Math.Abs(s).ToString("00");
        }

        private void ftAutoSequence(string text, float snr, bool even, float hz, bool force = false)
        {
            if (!_ftAuto || _digiMyCall.Length == 0 || !_ftRun) return;
            string[] t = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 3 || ftBase(t[0]) != _digiMyCall) return;
            string from = ftBase(t[1]), what = t[2];
            if (FtIsBlockedCall(from)) return;                                              // a country that is not answered
            if (_ftDx.Length > 0 && from != _ftDx) return;                                   // someone else while we're busy
            if (_ftDx.Length == 0 && !_ftTxEnable && !force) return;
            if (_ftDx.Length == 0 || _ftLogged) { _ftQsoStart = DateTime.UtcNow; _ftLogged = false; }
            bool changed = true;
            if (GridRe.IsMatch(what) && what != "RR73")
            {
                _ftDx = from; _ftDxGrid = what;
                if (_ftRptSent.Length == 0) _ftRptSent = ftReport(snr);
                _ftMsgNo = 2;
            }
            else if (RptRe.IsMatch(what))
            {
                _ftDx = from; _ftRptRcvd = what;
                if (_ftRptSent.Length == 0) _ftRptSent = ftReport(snr);
                _ftMsgNo = 3;
            }
            else if (RRptRe.IsMatch(what))
            {
                _ftDx = from; _ftRptRcvd = what.Substring(1);
                if (_ftRptSent.Length == 0) _ftRptSent = ftReport(snr);
                _ftMsgNo = 4;
            }
            else if (what == "RR73" || what == "RRR")
            {
                _ftDx = from;
                ftLogQso();
                _ftMsgNo = 5;
            }
            else if (what == "73")
            {
                ftLogQso();
                _ftTxEnable = false;
                _ftStatus = "QSO with " + from + " complete";
                changed = false;
            }
            else changed = false;
            if (changed)
            {
                _ftTries = 0;
                _ftCustom = "";
                _ftTxEven = !even;                                                           // answer in the other slot
                _ftTxHz = Math.Max(200, Math.Min(3000, (int)Math.Round(hz)));
                if (_ftPane != null) _ftPane.Refresh2();
            }
        }

        // who sent a decoded message: the call after CQ (and its DX / region modifier), else the second call
        internal string FtSender(FtRow row)
        {
            string[] t = row.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 2) return "";
            if (row.Cq) return ftBase(t.Length > 3 && t[1].Length <= 4 && !t[1].Any(char.IsDigit) ? t[2] : t[1]);
            return ftBase(t[1]);
        }

        // a double click on a decoded message
        internal void FtPick(FtRow row, bool enable)
        {
            if (row.Sent) return;
            if (FtIsBlocked(row)) { _ftStatus = row.Sender + " is not answered (" + FtBlockReason(row) + ")"; return; }
            string[] t = row.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 2) return;
            string who = row.ToMe ? ftBase(t[1]) : FtSender(row);
            if (who.Length == 0) return;
            if (!enable && who == _ftDx) return;                                              // the same station again: leave the QSO as it is
            _ftCustom = "";
            _ftLogged = false;
            _ftQsoStart = DateTime.UtcNow;
            _ftTxEven = !row.Even;                                                            // answer in the other slot
            _ftRxHz = (int)Math.Round(row.Hz);
            _ftTries = 0;
            if (row.ToMe)
            {
                _ftDx = "";
                ftAutoSequence(row.Text, row.Snr, row.Even, row.Hz, true);
                if (_ftDx.Length == 0) { _ftDx = who; _ftMsgNo = 1; }
            }
            else
            {
                _ftDx = who;
                int i = row.Cq && t.Length > 3 && t[1].Length <= 4 && !t[1].Any(char.IsDigit) ? 3 : 2;       // CQ DX K1ABC FN42
                _ftDxGrid = row.Cq && t.Length > i && GridRe.IsMatch(t[i]) ? t[i] : "";
                _ftRptSent = ""; _ftRptRcvd = "";
                _ftMsgNo = 1;
                _ftTxHz = Math.Max(200, Math.Min(3000, _ftRxHz));
            }
            if (enable) FtEnableTx(true);
            if (_ftPane != null) _ftPane.Refresh2();
        }

        // the folder of the log, with the file selected
        internal void FtShowLog()
        {
            try
            {
                if (File.Exists(FtLogFile)) System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + FtLogFile + "\"");
                else { _ftStatus = "No QSO logged yet. The log is " + FtLogFile; System.Diagnostics.Process.Start("explorer.exe", "\"" + DigiModesFolder + "\""); }
            }
            catch { }
        }



        // ---- countries that are not answered ----

        // the country of a call sign from the prefix table; a /P, /M, /QRP or a number after the slash doesn't count, a short prefix before the slash does
        internal static string FtCountryOf(string call)
        {
            if (string.IsNullOrEmpty(call)) return "";
            try
            {
                string c = call.Trim().Trim('<', '>').ToUpperInvariant();
                if (c.Contains("/"))
                {
                    string[] p = c.Split('/');
                    string best = p[0];
                    foreach (string s in p) if (s.Length > best.Length) best = s;
                    if (p.Length == 2 && p[0].Length >= 1 && p[0].Length <= 3 && p[0].Any(char.IsLetter) && p[0].Any(char.IsDigit) || p.Length == 2 && p[0].Length >= 1 && p[0].Length <= 2 && p[0].Any(char.IsLetter)) best = p[0];
                    c = best;
                }
                CountryData.PrefixData d = CountryData.GetCallsignData(c);
                return d != null && d.Country != null ? d.Country : "";
            }
            catch { return ""; }
        }

        // why a station is not answered: the entry of the list that matches it, or null. An entry is a country name of the table, or a call sign pattern:
        // "3Y0K" is that call only, "UA*" is every call starting with UA (for prefixes the table doesn't know yet, and for expedition calls)
        // the parts of a call sign that can name a country: "UA/DL1ABC" has two, "W1AW/P" one (P, M, QRP and the like are no prefixes)
        private static readonly HashSet<string> NotAPrefix = new HashSet<string> { "P", "M", "MM", "AM", "QRP", "A", "B", "R", "T", "LH", "LGT", "MOBILE", "QRPP" };
        private static List<string> ftSegments(string c)
        {
            List<string> l = new List<string>();
            foreach (string s in c.Split('/')) if (s.Length >= 2 && !NotAPrefix.Contains(s) && s.Any(char.IsLetter)) l.Add(s);
            if (l.Count == 0 && c.Length > 0) l.Add(c);
            return l;
        }

        internal string FtBlockReason(string call, string country)
        {
            string c = (call ?? "").Trim().Trim('<', '>').ToUpperInvariant();
            bool unknownCall = c.Length == 0 || c == "..." || c.Contains("...");              // a hashed call the decoder could not resolve
            if (_ftBlocked.Count == 0 && !_ftBlockUnknown) return null;
            if (unknownCall) return _ftBlockUnknown ? "unknown call sign" : null;
            List<string> segs = ftSegments(c);
            bool anyCountry = country.Length > 0;
            if (country.Length > 0 && _ftBlocked.Contains(country)) return country;
            // every part is looked up. All parts in blocked countries: blocked. Only some of them (UR/DL1ABC): not clear, so not blocked, only marked (FtWarnReason)
            if (segs.Count > 1)
            {
                List<string> ks = new List<string>();
                foreach (string s in segs) { string k = FtCountryOf(s); if (k.Length > 0 && !ks.Contains(k)) ks.Add(k); }
                if (ks.Count > 0) anyCountry = true;
                if (ks.Count > 0 && ks.TrueForAll(k => _ftBlocked.Contains(k))) return ks[0];
            }
            foreach (string e in _ftBlocked)
            {
                if (e.IndexOf(' ') >= 0) continue;                                            // a country name
                string p = e.ToUpperInvariant();
                bool star = p.EndsWith("*");
                string q = star ? p.Substring(0, p.Length - 1) : p;
                // the whole call only: "UR*" blocks UR1ABC, not UR/DL1ABC (that one is only marked); "3Y0K" is also 3Y0K/P
                if (star ? c.StartsWith(q, StringComparison.Ordinal) : (c == q || c.StartsWith(q + "/", StringComparison.Ordinal) || c.EndsWith("/" + q, StringComparison.Ordinal))) return e;
            }
            if (_ftBlockUnknown && !anyCountry) return "unknown country";
            return null;
        }

        // a call sign with a prefix that is a blocked country and a base call of another one (UR/DL1ABC): not blocked, but marked in another colour
        internal string FtWarnReason(FtRow r)
        {
            if (r == null || r.Sent || _ftBlocked.Count == 0 || r.Sender.Length == 0 || FtBlockReason(r) != null) return null;
            string c = r.Sender.Trim('<', '>').ToUpperInvariant();
            if (!c.Contains("/")) return null;
            List<string> segs = ftSegments(c);
            if (segs.Count < 2) return null;
            foreach (string s in segs) { string k = FtCountryOf(s); if (k.Length > 0 && _ftBlocked.Contains(k)) return "check: " + k + " in " + c; }
            return null;
        }

        internal string FtBlockReason(FtRow r) { return r == null || r.Sent ? null : FtBlockReason(r.Sender, r.Country); }
        internal bool FtIsBlocked(FtRow r) { return FtBlockReason(r) != null; }
        internal bool FtIsBlockedCall(string call) { return FtBlockReason(call, FtCountryOf(call)) != null; }
        internal List<string> FtBlockedCountries { get { if (!_digiLoaded) digiLoadSettings(); List<string> l = new List<string>(_ftBlocked); l.Sort(StringComparer.OrdinalIgnoreCase); return l; } }

        internal void FtBlockAdd(string country)
        {
            if (string.IsNullOrWhiteSpace(country)) return;
            if (!_digiLoaded) digiLoadSettings();
            _ftBlocked.Add(country.Trim());
            digiSaveSettings();
            if (_ftPane != null) _ftPane.Refresh2();
        }

        internal void FtBlockRemove(string country)
        {
            if (!_digiLoaded) digiLoadSettings();
            _ftBlocked.Remove(country);
            digiSaveSettings();
            if (_ftPane != null) _ftPane.Refresh2();
        }

        // ---- PSK Reporter ----

        internal bool FtPskEnabled { get { if (!_digiLoaded) digiLoadSettings(); return _ftPsk; } }

        internal string FtPskStatus
        {
            get
            {
                if (!FtPskEnabled) return "Off";
                if (_digiMyCall.Length < 3 || _ftGrid.Length < 4) return "Enter your call sign and grid in the FT8 / FT4 window";
                return _psk != null && _psk.Running ? _psk.Status : "Waiting for FT8 / FT4 decodes";
            }
        }

        internal void FtSetPsk(bool on)
        {
            _ftPsk = on;
            digiSaveSettings();
            if (!on && _psk != null) _psk.Stop();
        }

        // the stations of one decoded period go to the reporter (UI thread): call sign and locator from the message, the dial frequency plus the audio frequency, the SNR
        private void ftPskAdd(List<Mfsk.Decoder.Row> rows, long period, string mode)
        {
            try
            {
                if (!_ftPsk || _digiMyCall.Length < 3 || _ftGrid.Length < 4) return;
                if (_psk == null) _psk = new PskReporter();
                if (!_psk.Running) _psk.Start(_digiMyCall, _ftGrid, "Brick-SDR " + BrickBranding.Version);
                if (!_psk.Running) return;
                double dial = (_digiRx == 1 ? VFOBFreq : VFOAFreq) * 1e6;
                double T = mode == "FT4" ? 7.5 : 15.0;
                DateTime start = FtEpoch.AddSeconds(period * T);
                foreach (var r in rows)
                {
                    FtRow tmp = new FtRow { Text = r.Text };
                    ftClassify(tmp);
                    string call = FtSender(tmp);
                    string[] t = r.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    int at = Array.IndexOf(t, call);
                    string grid = at >= 0 && at + 1 < t.Length && GridRe.IsMatch(t[at + 1]) && t[at + 1] != "RR73" ? t[at + 1] : "";
                    _psk.Add(call, grid, (long)Math.Round(dial + r.Hz), (int)Math.Round(r.Snr), mode, start);
                }
            }
            catch { }
        }

        // ---- the log (ADIF) ----

        // the QSO logs (ADIF) of the digital modes are kept in the digi_modes folder of the data folder
        internal string DigiModesFolder
        {
            get
            {
                string d = Path.Combine(AppDataPath, "digi_modes");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }

        private string FtLogFile
        {
            get
            {
                string f = Path.Combine(DigiModesFolder, "ft_log.adi");
                try
                {
                    string old = Path.Combine(AppDataPath, "ft_log.adi");      // where it was before: move it over once
                    if (File.Exists(old) && !File.Exists(f)) File.Move(old, f);
                }
                catch { }
                return f;
            }
        }

        private void ftLogQso()
        {
            if (_ftLogged || _ftDx.Length == 0) return;
            _ftLogged = true;
            try
            {
                double mhz = _digiRx == 1 ? VFOBFreq : VFOAFreq;
                DateTime on = _ftQsoStart == default(DateTime) ? DateTime.UtcNow : _ftQsoStart, off = DateTime.UtcNow;
                StringBuilder sb = new StringBuilder();
                Action<string, string> f = (k, v) => { if (!string.IsNullOrEmpty(v)) sb.Append("<" + k + ":" + v.Length + ">" + v + " "); };
                f("CALL", _ftDx); f("GRIDSQUARE", _ftDxGrid);
                f("MODE", _ftMode == "FT4" ? "MFSK" : "FT8");
                if (_ftMode == "FT4") f("SUBMODE", "FT4");
                f("RST_SENT", _ftRptSent); f("RST_RCVD", _ftRptRcvd);
                f("FREQ", mhz.ToString("0.000000", CultureInfo.InvariantCulture));
                f("QSO_DATE", off.ToString("yyyyMMdd")); f("TIME_ON", on.ToString("HHmmss")); f("TIME_OFF", off.ToString("HHmmss"));
                f("STATION_CALLSIGN", _digiMyCall); f("MY_GRIDSQUARE", _ftGrid);
                sb.Append("<EOR>\r\n");
                bool fresh = !File.Exists(FtLogFile);
                File.AppendAllText(FtLogFile, (fresh ? "Brick-SDR FT8 / FT4 log\r\n<ADIF_VER:5>3.1.4 <PROGRAMID:9>Brick-SDR <EOH>\r\n" : "") + sb);
                _ftStatus = "Logged " + _ftDx;
            }
            catch { }
        }

        // drained by the window's timer
        internal bool FtTryRow(out FtRow row) { return _ftRows.TryDequeue(out row); }
        internal string FtStatus { get { return _ftStatus; } set { _ftStatus = value; } }
        internal float FtInDb { get { return _ftInDb; } }
        internal bool FtClip { get { return _ftClip; } }
    }
}
