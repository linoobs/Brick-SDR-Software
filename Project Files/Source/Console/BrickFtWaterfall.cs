/*  BrickFtWaterfall.cs

    A close-up waterfall for the FT8 / FT4 window: the audio band of the receiver filter (Filters in the main window, e.g. 100 - 3000 Hz) spread over the
    whole width, so the signals of the stations are wide enough to see and click. The spectrum comes from the same audio the decoder reads (BrickFt.cs);
    the Rx and Tx frequencies are marked, and the stations decoded in the last periods are written above their signals (dimmed when their country is not answered).
    Left click sets the Rx frequency, right click the Tx frequency; a click on a call sign selects that station.
*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BrickSDR
{
    public partial class Console
    {
        internal sealed class FtWaterfall : Control
        {
            public event Action<double, MouseButtons> FreqClicked;
            public event Action<FtRow> StationClicked;
            public Func<double> RxHz = () => 0, TxHz = () => 0;
            public Func<FtRow, bool> Dim = r => false;
            public Func<FtRow, string> Warn = r => null;
            public List<FtRow> Stations = new List<FtRow>();

            private const int ScaleH = 16, LabelH = 42;
            private Bitmap bmp;                                  // the waterfall itself, newest line at the top
            private double lo = 100, hi = 3000;
            private static readonly Color[] pal = makePalette();
            private readonly Font small = new Font("Segoe UI", 8f), call = new Font("Consolas", 9f, FontStyle.Bold);
            private readonly List<KeyValuePair<RectangleF, FtRow>> hot = new List<KeyValuePair<RectangleF, FtRow>>();

            public FtWaterfall()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = cField;
                Cursor = Cursors.Hand;
            }

            private static Color[] makePalette()
            {
                Color[] p = new Color[256];
                for (int i = 0; i < 256; i++)
                {
                    double t = i / 255.0;
                    int r, g, b;
                    if (t < 0.25) { r = 0; g = 0; b = (int)(30 + t / 0.25 * 120); }
                    else if (t < 0.5) { double u = (t - 0.25) / 0.25; r = 0; g = (int)(u * 220); b = (int)(150 + u * 80); }
                    else if (t < 0.75) { double u = (t - 0.5) / 0.25; r = (int)(u * 255); g = 220 + (int)(u * 35); b = (int)(230 * (1 - u)); }
                    else { double u = (t - 0.75) / 0.25; r = 255; g = (int)(255 * (1 - u)); b = 0; }
                    p[i] = Color.FromArgb(r, g, b);
                }
                return p;
            }

            private int imgH { get { return Math.Max(4, Height - ScaleH - LabelH); } }

            // one line of the spectrum: values in dB over the noise, from lo to hi Hz (any number of points)
            public void Push(float[] line, double fLo, double fHi)
            {
                if (Width < 10 || Height < ScaleH + LabelH + 8) return;
                if (bmp == null || bmp.Width != Width || bmp.Height != imgH || Math.Abs(fLo - lo) > 1 || Math.Abs(fHi - hi) > 1)
                {
                    if (bmp != null) bmp.Dispose();
                    bmp = new Bitmap(Width, imgH, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                    using (Graphics g = Graphics.FromImage(bmp)) g.Clear(Color.Black);
                    lo = fLo; hi = fHi;
                }
                // scroll down by one line
                using (Graphics g = Graphics.FromImage(bmp))
                    g.DrawImage(bmp, new Rectangle(0, 1, bmp.Width, bmp.Height - 1), new Rectangle(0, 0, bmp.Width, bmp.Height - 1), GraphicsUnit.Pixel);
                int w = bmp.Width, n = line.Length;
                for (int x = 0; x < w; x++)
                {
                    int a = (int)((long)x * n / w), b = Math.Max(a + 1, (int)((long)(x + 1) * n / w));
                    float m = float.MinValue;
                    for (int k = a; k < b && k < n; k++) if (line[k] > m) m = line[k];
                    int idx = (int)Math.Max(0, Math.Min(255, (m + 2) / 30.0 * 255));          // -2 dB .. 28 dB above the noise
                    bmp.SetPixel(x, 0, pal[idx]);
                }
                Invalidate();
            }

            public void Clear() { if (bmp != null) { using (Graphics g = Graphics.FromImage(bmp)) g.Clear(Color.Black); } Invalidate(); }

            private float xOf(double hz) { return (float)((hz - lo) / (hi - lo) * Width); }
            private double hzOf(int x) { return lo + (double)x / Math.Max(1, Width) * (hi - lo); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(cField);
                hot.Clear();
                // scale
                using (Brush b = new SolidBrush(Color.FromArgb(34, 38, 52))) g.FillRectangle(b, 0, 0, Width, ScaleH);
                double step = (hi - lo) > 2000 ? 500 : (hi - lo) > 900 ? 200 : 100;
                for (double f = Math.Ceiling(lo / step) * step; f <= hi; f += step)
                {
                    float x = xOf(f);
                    using (Pen p = new Pen(Color.FromArgb(120, 125, 140))) g.DrawLine(p, x, ScaleH - 4, x, ScaleH);
                    TextRenderer.DrawText(g, ((int)f).ToString(), small, new Point((int)x - 12, 0), kTextDim, TextFormatFlags.NoPadding);
                }
                // waterfall
                if (bmp != null) g.DrawImage(bmp, 0, ScaleH + LabelH);
                // stations above it: three rows of labels, a label that would write over another is left out (its tick stays); CQ and calls to you come first
                List<FtRow> st = new List<FtRow>(Stations);
                st.Sort((a, b) => ((b.ToMe ? 2 : 0) + (b.Cq ? 1 : 0)).CompareTo((a.ToMe ? 2 : 0) + (a.Cq ? 1 : 0)));
                List<RectangleF> placed = new List<RectangleF>();
                foreach (FtRow r in st)
                {
                    if (r.Sender == null || r.Sender.Length == 0 || r.Hz < lo || r.Hz > hi) continue;
                    float x = xOf(r.Hz);
                    bool dim = Dim(r);
                    Color col = dim ? Color.FromArgb(95, 100, 115) : Warn(r) != null ? Color.FromArgb(255, 190, 60) : r.ToMe ? Color.FromArgb(255, 120, 90) : r.Cq ? kGreen : cText;
                    using (Pen p = new Pen(col)) g.DrawLine(p, x, ScaleH + LabelH - 6, x, ScaleH + LabelH + 6);
                    SizeF sz = g.MeasureString(r.Sender, call);
                    float lx = Math.Max(0, Math.Min(Width - sz.Width, x - sz.Width / 2));
                    for (int lane = 0; lane < 3; lane++)
                    {
                        RectangleF rc = new RectangleF(lx, ScaleH + lane * 13, sz.Width, 13);
                        bool free = true;
                        foreach (RectangleF o in placed) if (o.IntersectsWith(RectangleF.Inflate(rc, 2, 0))) { free = false; break; }
                        if (!free) continue;
                        placed.Add(rc);
                        using (Pen p = new Pen(col)) g.DrawLine(p, x, rc.Bottom, x, ScaleH + LabelH - 6);
                        TextRenderer.DrawText(g, r.Sender, call, new Point((int)lx, (int)rc.Top - 1), col, TextFormatFlags.NoPadding);
                        hot.Add(new KeyValuePair<RectangleF, FtRow>(rc, r));
                        break;
                    }
                }
                // Rx and Tx
                float xr = xOf(RxHz()), xt = xOf(TxHz());
                using (Pen p = new Pen(Color.FromArgb(220, 60, 214, 96), 2f)) if (xr >= 0 && xr <= Width) g.DrawLine(p, xr, ScaleH + LabelH, xr, Height);
                using (Pen p = new Pen(Color.FromArgb(220, 255, 70, 70), 2f)) if (xt >= 0 && xt <= Width) g.DrawLine(p, xt, ScaleH + LabelH, xt, Height);
                TextRenderer.DrawText(g, "Rx", small, new Point((int)xr + 3, Height - 13), Color.FromArgb(60, 214, 96), TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, "Tx", small, new Point((int)xt + 3, Height - 26), Color.FromArgb(255, 90, 90), TextFormatFlags.NoPadding);
                using (Pen p = new Pen(cLine)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                base.OnMouseClick(e);
                foreach (var h in hot)
                    if (h.Key.Contains(e.Location) && e.Button == MouseButtons.Left) { if (StationClicked != null) StationClicked(h.Value); return; }
                if (e.Y >= ScaleH + LabelH && FreqClicked != null) FreqClicked(hzOf(e.X), e.Button);
            }
        }

        // ---- the spectrum of the audio the decoder reads, for the close-up waterfall ----

        private readonly ConcurrentQueue<float[]> _ftSpec = new ConcurrentQueue<float[]>();
        private volatile int _ftSpecLo = 100, _ftSpecHi = 3000;
        private const int SpecN = 8192;
        private float[] _specWin, _specCos, _specSin, _specRe, _specIm;

        internal bool FtTrySpec(out float[] line) { return _ftSpec.TryDequeue(out line); }
        internal int FtSpecLo { get { return _ftSpecLo; } }
        internal int FtSpecHi { get { return _ftSpecHi; } }

        // the band the receiver filter lets through, as audio frequencies (DIGU: the filter limits are positive)
        private void ftFilterBand(out int lo, out int hi)
        {
            int l = _digiRx == 1 ? Display.RX2FilterLow : Display.RX1FilterLow;
            int h = _digiRx == 1 ? Display.RX2FilterHigh : Display.RX1FilterHigh;
            if (l > h) { int t = l; l = h; h = t; }
            if (h <= 0) { int t = -l; l = -h; h = t; }                       // a lower sideband filter, mirrored
            lo = Math.Max(0, l); hi = Math.Max(lo + 200, h);
        }

        // one line of the spectrum from the last SpecN samples of the ring (engine thread)
        private void ftSpectrum(float[] ring, long total, int rate)
        {
            if (_specWin == null)
            {
                _specWin = new float[SpecN]; _specCos = new float[SpecN / 2]; _specSin = new float[SpecN / 2]; _specRe = new float[SpecN]; _specIm = new float[SpecN];
                for (int i = 0; i < SpecN; i++) _specWin[i] = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * i / SpecN)));
                for (int i = 0; i < SpecN / 2; i++) { _specCos[i] = (float)Math.Cos(2 * Math.PI * i / SpecN); _specSin[i] = (float)-Math.Sin(2 * Math.PI * i / SpecN); }
            }
            int lo, hi;
            ftFilterBand(out lo, out hi);
            _ftSpecLo = lo; _ftSpecHi = hi;
            long from = total - SpecN;
            if (from < 0) return;
            for (int i = 0; i < SpecN; i++) { _specRe[i] = ring[(int)((from + i) % ring.Length)] * _specWin[i]; _specIm[i] = 0; }
            // radix-2 FFT
            for (int i = 1, j = 0; i < SpecN; i++)
            {
                int bit = SpecN >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { float t = _specRe[i]; _specRe[i] = _specRe[j]; _specRe[j] = t; t = _specIm[i]; _specIm[i] = _specIm[j]; _specIm[j] = t; }
            }
            for (int len = 2; len <= SpecN; len <<= 1)
            {
                int half = len >> 1, step = SpecN / len;
                for (int i = 0; i < SpecN; i += len)
                    for (int k = 0; k < half; k++)
                    {
                        float wr = _specCos[k * step], wi = _specSin[k * step];
                        int a = i + k, b = a + half;
                        float xr = _specRe[b] * wr - _specIm[b] * wi, xi = _specRe[b] * wi + _specIm[b] * wr;
                        _specRe[b] = _specRe[a] - xr; _specIm[b] = _specIm[a] - xi;
                        _specRe[a] += xr; _specIm[a] += xi;
                    }
            }
            double df = rate / (double)SpecN;
            int k0 = (int)Math.Ceiling(lo / df), k1 = Math.Min(SpecN / 2 - 1, (int)Math.Floor(hi / df));
            if (k1 <= k0 + 4) return;
            float[] db = new float[k1 - k0 + 1];
            for (int k = k0; k <= k1; k++) db[k - k0] = (float)(10 * Math.Log10(_specRe[k] * _specRe[k] + _specIm[k] * _specIm[k] + 1e-12));
            // over the noise: the median of the line is the noise floor
            float[] sorted = (float[])db.Clone(); Array.Sort(sorted);
            float noise = sorted[sorted.Length / 2];
            for (int i = 0; i < db.Length; i++) db[i] -= noise;
            _ftSpec.Enqueue(db);
            while (_ftSpec.Count > 60) { float[] drop; _ftSpec.TryDequeue(out drop); }
        }
    }
}
