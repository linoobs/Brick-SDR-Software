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
        }

        private BrickFtPane _ftPane;
        private string _ftMode = "FT8";                 // "FT8" or "FT4", the one the window is set to
        private string _ftGrid = "";
        private int _ftRxHz = 1500, _ftTxHz = 1500, _ftLevel = -10;
        private bool _ftAuto = true;
        private volatile bool _ftRun;
        private Thread _ftThread;
        private readonly ConcurrentQueue<FtRow> _ftRows = new ConcurrentQueue<FtRow>();
        private readonly object _ftDecLock = new object();
        private Mfsk.Decoder _ftDecoder;
        private long _ftDecoderKey = -1;
        private string _ftStatus = "";
        private volatile float _ftInDb = -120;           // receive audio level (dB re full scale) for the meter
        private volatile bool _ftClip;
        private volatile bool _ftClockOk = true;
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

        internal bool FtModeOk { get { DSPMode m = digiRxMode; return m == DSPMode.USB || m == DSPMode.DIGU; } }

        internal string FtStartProblem
        {
            get
            {
                if (!PowerOn) return "The radio is off";
                if (!FtModeOk) return "Use DIGU or USB";
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
            long lastSlot = long.MinValue;
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
                        double now = ftUtcSeconds() - _ftClock;
                        long slot = (long)Math.Floor(now / T);
                        double into = now - slot * T;
                        _ftSlotPos = into;
                        if (slot != lastSlot)
                        {
                            long endIdx = total - (long)(into * rate);
                            if (lastSlot != long.MinValue)
                            {
                                int len = (int)Math.Round(T * rate);
                                long from = endIdx - len;
                                if (from >= 0 && total - from < ring.Length && lastSlot != _ftSkipSlot && !_ftTxActive)
                                {
                                    short[] s = new short[len];
                                    for (int i = 0; i < len; i++)
                                    {
                                        float v = ring[(int)((from + i) % ring.Length)] * 32767f;
                                        s[i] = (short)(v > 32767 ? 32767 : v < -32768 ? -32768 : v);
                                    }
                                    long period = lastSlot;
                                    int rt = rate;
                                    string md = mode;
                                    Task.Run(() => ftDecode(s, rt, period, md));
                                }
                            }
                            lastSlot = slot;
                            startIdx = endIdx;
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

        private void ftDecode(short[] samples, int rate, long period, string mode)
        {
            try
            {
                List<Mfsk.Decoder.Row> rows;
                lock (_ftDecLock)
                {
                    if (!_ftRun || mode != _ftMode) return;
                    if (_ftDecoder == null) _ftDecoder = new Mfsk.Decoder(mode == "FT4" ? Mfsk.FT4 : Mfsk.FT8);
                    _ftDecoder.Context(_digiMyCall, _ftGrid, _ftDx, _ftDxGrid, _ftRxHz);
                    rows = _ftDecoder.DecodePeriod(samples, rate, period);
                }
                double T = mode == "FT4" ? 7.5 : 15.0;
                DateTime start = FtEpoch.AddSeconds(period * T);
                foreach (var r in rows)
                {
                    FtRow row = new FtRow { Utc = start, Snr = r.Snr, Dt = r.Dt, Hz = r.Hz, Text = r.Text, Even = period % 2 == 0 };
                    ftClassify(row);
                    _ftRows.Enqueue(row);
                }
                ftTrackClock(rows);
                if (rows.Count > 0) BeginInvoke(new Action(() => { foreach (var r in rows) ftAutoSequence(r.Text, r.Snr, period % 2 == 0, r.Hz); }));
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

        private void ftTxStep(double now, double T, long slot, double into)
        {
            if (_ftTxState == 0)
            {
                if (!_ftTxEnable) return;
                string msg = ftCurrentMessage();
                if (msg.Length == 0 || FtStartProblem != null) return;
                if ((slot % 2 == 0) != _ftTxEven) return;
                if (slot == _ftTxSlot || into > (T > 10 ? 1.4 : 0.9)) return;
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
                _ftTries = 0;
                _ftTxEnable = true;
            }
            else FtHalt();
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

        private void ftAutoSequence(string text, float snr, bool even, float hz)
        {
            if (!_ftAuto || _digiMyCall.Length == 0 || !_ftRun) return;
            string[] t = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 3 || ftBase(t[0]) != _digiMyCall) return;
            string from = ftBase(t[1]), what = t[2];
            if (_ftDx.Length > 0 && from != _ftDx) return;                                   // someone else while we're busy
            if (_ftDx.Length == 0 && !_ftTxEnable) return;
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

        // a double click on a decoded message
        internal void FtPick(FtRow row)
        {
            string[] t = row.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 2) return;
            _ftCustom = "";
            _ftLogged = false;
            _ftQsoStart = DateTime.UtcNow;
            _ftTxEven = !row.Even;
            if (row.Cq)
            {
                int i = 1;
                if (t.Length > 3 && t[1].Length <= 4 && !t[1].Any(char.IsDigit)) i = 2;       // CQ DX K1ABC FN42
                _ftDx = ftBase(t[i]);
                _ftDxGrid = t.Length > i + 1 && GridRe.IsMatch(t[i + 1]) ? t[i + 1] : "";
                _ftRptSent = ""; _ftRptRcvd = "";
                _ftMsgNo = 1;
                _ftTxHz = Math.Max(200, Math.Min(3000, (int)Math.Round(row.Hz)));
                _ftRxHz = _ftTxHz;
                _ftTries = 0;
                FtEnableTx(true);
            }
            else if (row.ToMe)
            {
                _ftDx = "";
                ftAutoSequence(row.Text, row.Snr, row.Even, row.Hz);
                _ftRxHz = (int)Math.Round(row.Hz);
                FtEnableTx(true);
            }
            else
            {
                _ftDx = ftBase(t[1]);
                _ftDxGrid = ""; _ftRptSent = ""; _ftRptRcvd = ""; _ftMsgNo = 1;
                _ftRxHz = (int)Math.Round(row.Hz);
            }
            if (_ftPane != null) _ftPane.Refresh2();
        }

        // ---- the log (ADIF) ----

        private string FtLogFile { get { return Path.Combine(AppDataPath, "ft_log.adi"); } }

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
