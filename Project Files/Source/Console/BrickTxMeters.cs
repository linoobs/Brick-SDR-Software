/*  BrickTxMeters.cs

    TX levels: a small tool window with the meters of the transmit chain (microphone, equalizer, leveler, CFC, compressor, ALC), each as a bar
    in dB below full scale with the peak held, and the gain reduction of the leveler / CFC / ALC. Used to set the microphone gain, CFC, the processor and
    the equalizer so that nothing is overdriven (all of them should peak at about 0 dB or below, never in the red).
    Opened and closed with a right click on the S-meter / power meter; the choice is remembered (tx_meters.txt in the data folder).
*/

using System;
using System.Drawing;
using System.Windows.Forms;

namespace BrickSDR
{
    internal sealed class BrickTxMetersForm : Form
    {
        private struct Row
        {
            public string Name; public WDSP.txaMeterType Avg, Pk; public WDSP.txaMeterType? Gain;
            public double A, P, G; public DateTime PTime;
        }

        private static readonly Color cBg = Color.FromArgb(18, 20, 28), cCard = Color.FromArgb(24, 27, 37), cLine = Color.FromArgb(48, 53, 70);
        private static readonly Color cText = Color.FromArgb(225, 228, 235), cMuted = Color.FromArgb(140, 146, 160), cAccent = Color.FromArgb(255, 138, 31), cTx = Color.FromArgb(214, 48, 48);

        private readonly Row[] rows =
        {
            new Row { Name = "MIC",   Avg = WDSP.txaMeterType.TXA_MIC_AV,  Pk = WDSP.txaMeterType.TXA_MIC_PK },
            new Row { Name = "EQ",    Avg = WDSP.txaMeterType.TXA_EQ_AV,   Pk = WDSP.txaMeterType.TXA_EQ_PK },
            new Row { Name = "LEVEL", Avg = WDSP.txaMeterType.TXA_LVLR_AV, Pk = WDSP.txaMeterType.TXA_LVLR_PK, Gain = WDSP.txaMeterType.TXA_LVLR_GAIN },
            new Row { Name = "CFC",   Avg = WDSP.txaMeterType.TXA_CFC_AV,  Pk = WDSP.txaMeterType.TXA_CFC_PK,  Gain = WDSP.txaMeterType.TXA_CFC_GAIN },
            new Row { Name = "COMP",  Avg = WDSP.txaMeterType.TXA_COMP_AV, Pk = WDSP.txaMeterType.TXA_COMP_PK },
            new Row { Name = "ALC",   Avg = WDSP.txaMeterType.TXA_ALC_AV,  Pk = WDSP.txaMeterType.TXA_ALC_PK,  Gain = WDSP.txaMeterType.TXA_ALC_GAIN },
        };

        private readonly Timer timer = new Timer { Interval = 50 };
        private const double Lo = -40.0, Hi = 6.0;           // scale of the bars, dB

        public BrickTxMetersForm()
        {
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            Text = "TX levels";
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = cBg;
            ClientSize = new Size(330, 8 + rows.Length * 28 + 22);
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            for (int i = 0; i < rows.Length; i++) { rows[i].A = rows[i].P = Lo; rows[i].PTime = DateTime.MinValue; }
            timer.Tick += (s, e) => { poll(); Invalidate(); };
            timer.Start();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

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
            g.Clear(cBg);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (Font f = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (Font fs = new Font("Segoe UI", 8f))
            using (Brush bt = new SolidBrush(cText))
            using (Brush bm = new SolidBrush(cMuted))
            using (SolidBrush track = new SolidBrush(Color.FromArgb(10, 12, 18)))
            {
                int bx = 50, bw = ClientSize.Width - bx - 96;
                int zero = bx + (int)Math.Round(bw * (0 - Lo) / (Hi - Lo));
                for (int i = 0; i < rows.Length; i++)
                {
                    int y = 8 + i * 28;
                    g.DrawString(rows[i].Name, f, bt, 6, y + 1);
                    g.FillRectangle(track, bx, y, bw, 16);
                    int w = (int)Math.Round(bw * (rows[i].A - Lo) / (Hi - Lo));
                    Color c = rows[i].A > -0.5 ? cTx : (rows[i].A > -6 ? Color.FromArgb(255, 214, 60) : Color.FromArgb(43, 212, 90));
                    using (Brush bb = new SolidBrush(c)) g.FillRectangle(bb, bx, y, Math.Max(0, w), 16);
                    int px = bx + (int)Math.Round(bw * (rows[i].P - Lo) / (Hi - Lo));
                    using (Pen pk = new Pen(rows[i].P > -0.5 ? cTx : cText, 2f)) g.DrawLine(pk, px, y - 2, px, y + 18);
                    using (Pen p0 = new Pen(cMuted, 1f)) g.DrawLine(p0, zero, y - 3, zero, y + 19);
                    g.DrawString(rows[i].P.ToString("0.0") + " dB", fs, bt, bx + bw + 6, y + 1);
                    if (rows[i].Gain.HasValue)
                        g.DrawString("G " + rows[i].G.ToString("+0.0;-0.0;0.0"), fs, bm, bx + bw + 6, y + 13);
                }
                // scale under the bars
                int ys = 8 + rows.Length * 28 - 6;
                foreach (int db in new[] { -40, -30, -20, -10, 0 })
                {
                    int x = bx + (int)Math.Round(bw * (db - Lo) / (Hi - Lo));
                    string t = db.ToString();
                    SizeF sz = g.MeasureString(t, fs);
                    g.DrawString(t, fs, bm, x - sz.Width / 2, ys);
                }
            }
        }
    }

    public partial class Console
    {
        private BrickTxMetersForm brickTxMeters;
        private string BrickTxMetersFile { get { return System.IO.Path.Combine(AppDataPath, "tx_meters.txt"); } }

        // right click on the meter: the TX levels window opens beside it or closes
        private void brickToggleTxMeters()
        {
            if (brickTxMeters != null && !brickTxMeters.IsDisposed)
            {
                brickTxMeters.Close(); brickTxMeters = null;
                try { System.IO.File.WriteAllText(BrickTxMetersFile, "0"); } catch { }
                return;
            }
            brickTxMeters = new BrickTxMetersForm();
            Point p = picMultiMeterDigital.PointToScreen(new Point(0, picMultiMeterDigital.Height + 4));
            brickTxMeters.Location = p;
            brickTxMeters.FormClosed += (s, e) => { brickTxMeters = null; };
            brickTxMeters.Show(this);
            try { System.IO.File.WriteAllText(BrickTxMetersFile, "1"); } catch { }
        }

        // reopened at start when it was open at the end of the last session
        private void brickRestoreTxMeters()
        {
            try { if (System.IO.File.Exists(BrickTxMetersFile) && System.IO.File.ReadAllText(BrickTxMetersFile).Trim() == "1" && brickTxMeters == null) brickToggleTxMeters(); }
            catch { }
        }
    }
}
