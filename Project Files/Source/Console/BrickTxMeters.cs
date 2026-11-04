/*  BrickTxMeters.cs

    TX levels: a strip along the bottom edge of the display card with the meters of the transmit chain (microphone, equalizer, leveler, CFC, compressor,
    ALC), each as a bar in dB below full scale with the peak held (and the gain of the leveler / CFC / ALC). Used to set the microphone gain, CFC, the
    processor and the equalizer so that nothing is overdriven (every bar should peak at about 0 dB or below, never in the red).
    Opened and closed with a right click on the S-meter / power meter; the choice is remembered (tx_meters.txt in the data folder).
*/

using System;
using System.Drawing;
using System.Windows.Forms;

namespace BrickSDR
{
    internal sealed class BrickTxMetersStrip : Control
    {
        private struct Row
        {
            public string Name; public WDSP.txaMeterType Avg, Pk; public WDSP.txaMeterType? Gain;
            public double A, P, G; public DateTime PTime;
        }

        private static readonly Color cCard = Color.FromArgb(24, 27, 37), cLine = Color.FromArgb(48, 53, 70);
        private static readonly Color cText = Color.FromArgb(225, 228, 235), cMuted = Color.FromArgb(140, 146, 160), cAccent = Color.FromArgb(255, 138, 31), cTx = Color.FromArgb(214, 48, 48);

        private readonly Row[] rows =
        {
            new Row { Name = "MIC",   Avg = WDSP.txaMeterType.TXA_MIC_AV,  Pk = WDSP.txaMeterType.TXA_MIC_PK },
            new Row { Name = "EQ",    Avg = WDSP.txaMeterType.TXA_EQ_AV,   Pk = WDSP.txaMeterType.TXA_EQ_PK },
            new Row { Name = "LEVELER", Avg = WDSP.txaMeterType.TXA_LVLR_AV, Pk = WDSP.txaMeterType.TXA_LVLR_PK, Gain = WDSP.txaMeterType.TXA_LVLR_GAIN },
            new Row { Name = "CFC",   Avg = WDSP.txaMeterType.TXA_CFC_AV,  Pk = WDSP.txaMeterType.TXA_CFC_PK,  Gain = WDSP.txaMeterType.TXA_CFC_GAIN },
            new Row { Name = "COMP",  Avg = WDSP.txaMeterType.TXA_COMP_AV, Pk = WDSP.txaMeterType.TXA_COMP_PK },
            new Row { Name = "ALC",   Avg = WDSP.txaMeterType.TXA_ALC_AV,  Pk = WDSP.txaMeterType.TXA_ALC_PK,  Gain = WDSP.txaMeterType.TXA_ALC_GAIN },
        };

        private readonly Timer timer = new Timer { Interval = 50 };
        private const double Lo = -40.0, Hi = 6.0;           // scale of the bars, dB

        public BrickTxMetersStrip()
        {
            Dock = DockStyle.Bottom;
            Height = 44;
            BackColor = cCard;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            for (int i = 0; i < rows.Length; i++) { rows[i].A = rows[i].P = Lo; rows[i].PTime = DateTime.MinValue; }
            timer.Tick += (s, e) => { if (Visible) { poll(); Invalidate(); } };
            timer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }

        private static double clampDb(double v) { return double.IsNaN(v) ? Lo : Math.Max(Lo, Math.Min(Hi, v)); }

        private void poll()
        {
            int channel = cmaster.CMsubrcvr * cmaster.CMrcvr;     // the transmit channel
            for (int i = 0; i < rows.Length; i++)
            {
                try
                {
                    double a = clampDb(WDSP.GetTXAMeter(channel, rows[i].Avg));
                    double p = clampDb(WDSP.GetTXAMeter(channel, rows[i].Pk));
                    rows[i].A = a > rows[i].A ? a * 0.7 + rows[i].A * 0.3 : a * 0.15 + rows[i].A * 0.85;
                    if (p >= rows[i].P || (DateTime.Now - rows[i].PTime).TotalMilliseconds > 1500) { rows[i].P = p; rows[i].PTime = DateTime.Now; }
                    if (rows[i].Gain.HasValue) rows[i].G = WDSP.GetTXAMeter(channel, rows[i].Gain.Value);
                }
                catch { }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(cCard);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (Font f = new Font("Segoe UI", 8f, FontStyle.Bold))
            using (Font fs = new Font("Segoe UI", 7.5f))
            using (Brush bt = new SolidBrush(cText))
            using (Brush bm = new SolidBrush(cMuted))
            using (SolidBrush track = new SolidBrush(Color.FromArgb(10, 12, 18)))
            using (Pen topLine = new Pen(cLine, 1f))
            {
                g.DrawLine(topLine, 0, 0, Width, 0);
                int n = rows.Length, gap = 12, pad = 8;
                int cw = (Width - 2 * pad - (n - 1) * gap) / n;
                for (int i = 0; i < n; i++)
                {
                    int x = pad + i * (cw + gap);
                    g.DrawString(rows[i].Name, f, bm, x, 3);
                    string val = rows[i].P.ToString("0.0") + " dB" + (rows[i].Gain.HasValue ? "   G " + rows[i].G.ToString("+0.0;-0.0;0.0") : "");
                    SizeF sv = g.MeasureString(val, fs);
                    g.DrawString(val, fs, bt, x + cw - sv.Width, 4);
                    int by = 23, bh = 12;
                    g.FillRectangle(track, x, by, cw, bh);
                    int w = (int)Math.Round(cw * (rows[i].A - Lo) / (Hi - Lo));
                    Color c = rows[i].A > -0.5 ? cTx : (rows[i].A > -6 ? Color.FromArgb(255, 214, 60) : cAccent);
                    using (Brush bb = new SolidBrush(c)) g.FillRectangle(bb, x, by, Math.Max(0, w), bh);
                    int px = x + (int)Math.Round(cw * (rows[i].P - Lo) / (Hi - Lo));
                    using (Pen pk = new Pen(rows[i].P > -0.5 ? cTx : cText, 2f)) g.DrawLine(pk, px, by - 2, px, by + bh + 2);
                    int zero = x + (int)Math.Round(cw * (0 - Lo) / (Hi - Lo));
                    using (Pen p0 = new Pen(cMuted, 1f)) g.DrawLine(p0, zero, by - 3, zero, by + bh + 3);
                }
            }
        }
    }

    public partial class Console
    {
        private BrickTxMetersStrip brickTxMeters;
        private string BrickTxMetersFile { get { return System.IO.Path.Combine(AppDataPath, "tx_meters.txt"); } }

        // right click on the meter: the TX levels strip opens or closes
        private void brickToggleTxMeters()
        {
            if (brickDispCard == null) return;
            if (brickTxMeters == null)
            {
                brickTxMeters = new BrickTxMetersStrip { Visible = false };
                brickDispCard.Controls.Add(brickTxMeters);
                brickDispCard.Controls.SetChildIndex(brickTxMeters, brickDispCard.Controls.Count - 1);     // the bottom edge: docked first
            }
            brickTxMeters.Visible = !brickTxMeters.Visible;
            try { System.IO.File.WriteAllText(BrickTxMetersFile, brickTxMeters.Visible ? "1" : "0"); } catch { }
            brickDispCard.PerformLayout();
        }

        // reopened at start when it was open at the end of the last session
        private void brickRestoreTxMeters()
        {
            try
            {
                if (System.IO.File.Exists(BrickTxMetersFile) && System.IO.File.ReadAllText(BrickTxMetersFile).Trim() == "1" && (brickTxMeters == null || !brickTxMeters.Visible))
                    brickToggleTxMeters();
            }
            catch { }
        }
    }
}
