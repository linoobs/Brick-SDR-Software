/*  BrickDigiMarkers.cs

    The RTTY terminal's mark and space tones on its receiver's panadapter (from Kainos by Justin Cron K7JUS, GPL v3).
*/

using System;
using System.Drawing;
using SharpDX.Direct2D1;

namespace BrickSDR
{
    partial class Display
    {
        // a line for the mark tone and one for the space tone, labelled near the bottom, where the decoder (with AFC, where
        // it has moved them) is listening
        private static void drawKainosDigiMarkersDX2D(int rx, int W, int H, int nVerticalShift)
        {
            if (console == null || W <= 0 || _d2dRenderTarget == null) return;
            double[] tones;
            try { tones = console.BrickDigiMarkers(rx); } catch { return; }
            if (tones == null || console.MOX) return;

            int low = rx == 1 ? rx_display_low : rx2_display_low;
            int high = rx == 1 ? rx_display_high : rx2_display_high;
            int fDiff = rx == 1 ? freq_diff : rx2_freq_diff;
            float width = high - low;
            if (width <= 0) return;

            SharpDX.Direct2D1.Brush[] brushes =
            {
                getDXBrushForColour(Color.FromArgb(255, 138, 31), 230),     // mark: the Brick orange
                getDXBrushForColour(Color.FromArgb(90, 200, 255), 230),     // space: cyan
            };
            string[] labels = { "M", "S" };
            float top = nVerticalShift + 18, bottom = nVerticalShift + H;
            float[] xs = new float[2];
            for (int i = 0; i < 2 && i < tones.Length; i++) xs[i] = (float)((tones[i] - low - fDiff) / width * W);
            for (int i = 0; i < tones.Length && i < 2; i++)
            {
                if (brushes[i] == null) continue;
                float x = xs[i];
                if (x < 0 || x > W) continue;
                drawLineDX2D(brushes[i], x, top, x, bottom, 2f);
                // each label on the outer side of its line, so the two don't run together
                bool leftSide = x < xs[1 - i];
                float ly = bottom - 40, lx = leftSide ? x - 13 : x + 3;
                _d2dRenderTarget.DrawText(labels[i], fontDX2d_callout, new SharpDX.Mathematics.Interop.RawRectangleF(lx, ly, lx + 16, ly + 16), brushes[i], DrawTextOptions.None);
            }
        }
    }
}
