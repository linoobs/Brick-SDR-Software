/*  BrickClusterDraw.cs

    DX cluster spots over the panadapter: a label with the call sign for every spot in the visible band (BrickCluster.cs),
    in rows so that labels don't cover each other, a thin line down to the spectrum at the spot frequency, colour by mode,
    fading with the age of the spot. The label rectangles are kept for the mouse (a click tunes to the spot).
*/

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace BrickSDR
{
    partial class Display
    {
        internal struct DxHit { public float X, Y, W, H; public double Hz; }

        private static DxHit[] _dxHits1 = new DxHit[0], _dxHits2 = new DxHit[0];

        // is there a label under the pixel (display coordinates)? gives the spot frequency and the receiver
        internal static bool DxHitAt(int x, int y, out double hz, out int rx)
        {
            hz = 0; rx = 1;
            for (int r = 1; r <= 2; r++)
            {
                DxHit[] a = r == 1 ? _dxHits1 : _dxHits2;
                for (int i = a.Length - 1; i >= 0; i--)
                    if (x >= a[i].X && x <= a[i].X + a[i].W && y >= a[i].Y && y <= a[i].Y + a[i].H) { hz = a[i].Hz; rx = r; return true; }
            }
            return false;
        }

        private static void dxSetHits(int rx, DxHit[] h) { if (rx == 2) _dxHits2 = h; else _dxHits1 = h; }

        private static void drawDxSpotsDX2D(int rx, int W, int H, int nVerticalShift)
        {
            if (!DxCluster.Enabled || console == null || _d2dRenderTarget == null || W <= 40 || H <= 40 || localMox(rx)) { dxSetHits(rx, new DxHit[0]); return; }

            int low = rx == 1 ? rx_display_low : rx2_display_low;
            int high = rx == 1 ? rx_display_high : rx2_display_high;
            double width = high - low;
            if (width <= 0) return;
            long vfo = rx == 1 ? vfoa_hz : vfob_hz;
            int shift = getCWSideToneShift(rx);
            int rit = rx == 1 ? (_rx1ClickDisplayCTUN ? 0 : rit_hz) : 0;
            double fLow = vfo + low + shift + rit, fHigh = vfo + high + shift + rit;

            DxSpot[] all = DxCluster.Snapshot();
            if (all.Length == 0) { dxSetHits(rx, new DxHit[0]); return; }
            DateTime now = DateTime.UtcNow;
            double maxAge = Math.Max(1, DxCluster.MaxAgeMin) * 60.0;

            // newest first, so a crowded band keeps the fresh spots
            List<DxSpot> vis = new List<DxSpot>();
            foreach (DxSpot s in all) if (s.Hz >= fLow && s.Hz <= fHigh) vis.Add(s);
            if (vis.Count == 0) { dxSetHits(rx, new DxHit[0]); return; }
            vis = vis.OrderByDescending(s => s.Time).Take(400).ToList();

            int rows = Math.Max(1, Math.Min(8, DxCluster.Rows));
            const float lh = 14f, gap = 3f;
            List<float[]>[] occ = new List<float[]>[rows];
            for (int i = 0; i < rows; i++) occ[i] = new List<float[]>();
            List<DxHit> hits = new List<DxHit>();

            SharpDX.Direct2D1.Brush back = getDXBrushForColour(Color.FromArgb(10, 12, 18), 150);
            float top0 = nVerticalShift + 22;

            foreach (DxSpot s in vis)
            {
                float x = (float)((s.Hz - fLow) / width * W);
                string text = DxCluster.ShowFreq ? s.Call + " " + (s.Hz / 1e6).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) : s.Call;
                float w = 6.6f * text.Length + 8f;
                float l = x - w / 2f, r = x + w / 2f;
                if (l < 0) { l = 0; r = w; }
                if (r > W) { r = W; l = W - w; }
                int row = -1;
                for (int i = 0; i < rows && row < 0; i++)
                {
                    bool free = true;
                    foreach (float[] o in occ[i]) if (l < o[1] + gap && r > o[0] - gap) { free = false; break; }
                    if (free) row = i;
                }
                if (row < 0) continue;
                occ[row].Add(new[] { l, r });

                double age = (now - s.Time).TotalSeconds;
                int alpha = (int)(255 - 135 * Math.Min(1.0, age / maxAge));
                alpha = 255 - (int)((255 - alpha) / 32.0 + 0.5) * 32;          // a few steps only: every alpha is a cached brush
                Color c = s.Mode == 1 ? Color.FromArgb(255, 205, 90) : s.Mode == 2 ? Color.FromArgb(120, 225, 140) : s.Mode == 3 ? Color.FromArgb(205, 150, 255) : Color.FromArgb(225, 228, 235);
                SharpDX.Direct2D1.Brush tb = getDXBrushForColour(c, alpha);
                SharpDX.Direct2D1.Brush lineB = getDXBrushForColour(c, 70);
                float y = top0 + row * (lh + 2f);

                if (lineB != null) drawLineDX2D(lineB, x, y + lh, x, nVerticalShift + H, 1f);
                if (back != null) drawFillRectangleDX2D(back, l, y, r - l, lh);
                if (tb != null)
                {
                    drawLineDX2D(tb, x, y + lh - 3, x, y + lh + 2, 1.5f);
                    drawStringDX2D(text, fontDX2d_font9, tb, l + 4f, y);
                }
                hits.Add(new DxHit { X = l, Y = y, W = r - l, H = lh, Hz = s.Hz });
            }
            dxSetHits(rx, hits.ToArray());
        }
    }
}
