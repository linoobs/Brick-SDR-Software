/*  BrickRadeStrip.cs

    The FreeDV strip docked under the panadapter: FreeDV on / off, version, sync, SNR, offset, the last callsign
    heard and the levels. Settings, mic processing and the Reporter are in the FreeDV window (Settings button).
    The status panels follow the FreeDV window of Kainos (K7JUS, GPL v3).
*/

using System;
using System.Drawing;
using System.Windows.Forms;

namespace BrickSDR
{
    public partial class Console
    {
        private BrickRadeStrip _radeStrip;

        internal sealed class BrickRadeStrip : Panel
        {
            private readonly Console c;
            private readonly BrickBtn bOn, bV1, bV2, bSet, bRep;
            private readonly Label status;
            private readonly BrickRadeForm.LevelBar rxBar, micBar;
            private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 60 };
            private int rx;
            public int Rx { get { return rx; } }
            private bool sync, active;
            private double snr;
            private float offset;

            public BrickRadeStrip(Console console)
            {
                c = console;
                c.radeLoadSettings();
                BackColor = cCard;
                DoubleBuffered = true;
                Size = new Size(760, 118);
                bOn = mk("FreeDV", 8, 8, 70, 28, () => toggle());
                bV1 = mk("V1", 84, 8, 36, 28, () => setVersion(false));
                bV2 = mk("V2", 124, 8, 36, 28, () => setVersion(true));
                bSet = mk("Settings", 8, 42, 86, 28, () => c.brickRadeSettings());
                bRep = mk("Reporter", 100, 42, 86, 28, () => FreeDVReporter.FreeDVReporterManager.ShowWindow(c));
                status = new Label { Left = 8, Top = 80, Width = 252, Height = 24, ForeColor = cMuted, Font = new Font("Segoe UI Semibold", 10.5f), TextAlign = ContentAlignment.MiddleLeft };
                rxBar = new BrickRadeForm.LevelBar { Label = "RX", Height = 22 };
                micBar = new BrickRadeForm.LevelBar { Label = "Mic", Height = 22 };
                Controls.AddRange(new Control[] { bOn, bV1, bV2, bSet, bRep, status, rxBar, micBar });
                Resize += (s, e) => place();
                VisibleChanged += (s, e) => { if (Visible) { sync_(); place(); timer.Start(); } else timer.Stop(); };
                timer.Tick += (s, e) => tick();
                rx = !c.RadeEnabledOn(0) && c.RadeEnabledOn(1) ? 1 : 0;
                place();
            }

            private BrickBtn mk(string text, int x, int y, int w, int h, Action act)
            {
                BrickBtn b = new BrickBtn { Text = text, Left = x, Top = y, Width = w, Height = h };
                b.Click += (s, e) => act();
                return b;
            }

            private static void light(BrickBtn b, bool on) { b.On = on; }

            private void place()
            {
                int w = Width;
                int bw = Math.Max(110, Math.Min(260, w - 8 - (200 + 210 + 140)));
                rxBar.SetBounds(w - 8 - bw, 14, bw, 22);
                micBar.SetBounds(w - 8 - bw, 44, bw, 22);
                Invalidate();
            }

            // the receiver comes from the DIGI menu that opened the strip
            public void SetRx(int r) { rx = r == 1 ? 1 : 0; sync_(); }

            private void toggle()
            {
                bool on = !c.RadeEnabledOn(rx);
                if (!c.SetRadeEnabled(rx, on)) status.Text = on && rx == 1 && !c.RX2Enabled ? "Turn RX2 on first" : "Can't switch while transmitting";
                sync_();
            }

            private void setVersion(bool v2)
            {
                if (rx == 0) c._rs.V2Rx1 = v2; else c._rs.V2Rx2 = v2;
                c.radeSettingsChanged();
                sync_();
            }

            private void sync_()
            {
                light(bOn, c.RadeEnabledOn(rx));
                bool v2 = rx == 0 ? c._rs.V2Rx1 : c._rs.V2Rx2;
                light(bV1, !v2); light(bV2, v2);
            }

            private void tick()
            {
                int r = rx;
                bool on = c.RadeEnabledOn(r), power = c.PowerOn;
                bool sy = false;
                int sn = 0, rxl = -120, micl = -120;
                bool rxClip = false, micClip = false;
                float off = 0;
                try
                {
                    if (on && power)
                    {
                        sy = c.RadeSyncOn(r);
                        sn = c.RadeSnrDbOn(r);
                        rxl = c.RadeRxLevelDbOn(r);
                        rxClip = c.RadeRxClipOn(r);
                        off = c.RadeFreqOffsetHzOn(r);
                        if (c.MOX && c.RadeTxReceiver == r) { micl = c.RadeMicLevelDb; micClip = c.RadeMicClip; }
                    }
                    c.radePollCalls();
                }
                catch { }
                snr = sy && active == (on && power) ? snr + 0.3 * (sn - snr) : sn;
                active = on && power; sync = sy; offset = off;
                rxBar.SetLevel(on && power && !(c.MOX && c.RadeTxReceiver == r) ? rxl : -120, rxClip);
                micBar.SetLevel(micl, micClip);
                Color col;
                string text = c.radeStatusText(r, on, power, sy, out col);
                if (status.Text != text) status.Text = text;
                status.ForeColor = col;
                if (bOn.On != on) sync_();
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                int x0 = 200;
                bool show = active && sync;
                // sync lamp
                Color lamp = !active ? kGrid : sync ? kGreen : Color.FromArgb(0x5a, 0x24, 0x20);
                using (Brush b = new SolidBrush(lamp)) g.FillEllipse(b, x0, 14, 14, 14);
                using (Font f = new Font("Segoe UI", 12f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Brush b = new SolidBrush(show ? kGreen : cMuted))
                    g.DrawString(show ? "SYNC" : "NO SYNC", f, b, x0 + 20, 13);
                // SNR and offset
                using (Font f = new Font("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Brush b = new SolidBrush(kTextDim)) { g.DrawString("SNR", f, b, x0, 40); g.DrawString("OFFSET", f, b, x0 + 96, 40); }
                using (Font f = new Font("Segoe UI", 28f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Brush b = new SolidBrush(show ? cText : kTextDim))
                    g.DrawString(show ? ((int)Math.Round(snr)).ToString() : "--", f, b, x0 - 2, 50);
                using (Font f = new Font("Segoe UI", 12f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Brush b = new SolidBrush(cMuted))
                    g.DrawString("dB", f, b, x0 + 56, 66);
                using (Font f = new Font("Segoe UI", 16f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Brush b = new SolidBrush(show ? cText : kTextDim))
                    g.DrawString(show ? (offset >= 0 ? "+" : "") + offset.ToString("0.0") + " Hz" : "--", f, b, x0 + 94, 56);
                // SNR bar, -10 .. +40 dB
                Rectangle bar = new Rectangle(x0, 96, 190, 8);
                using (Brush b = new SolidBrush(kGrid)) g.FillRectangle(b, bar);
                if (show)
                {
                    double n = Math.Max(0, Math.Min(1, (snr + 10) / 50.0));
                    Color col = snr < 0 ? cTx : snr < 6 ? kAmber : kGreen;
                    using (Brush b = new SolidBrush(col)) g.FillRectangle(b, bar.X, bar.Y, (float)(bar.Width * n), bar.Height);
                }
                // last heard
                int hx = x0 + 210;
                string call = c._radeHeardCall[rx];
                using (Font f = new Font("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Brush b = new SolidBrush(kTextDim))
                    g.DrawString("LAST HEARD", f, b, hx, 14);
                if (call.Length == 0)
                {
                    using (Font f = new Font("Segoe UI", 12f, GraphicsUnit.Pixel))
                    using (Brush b = new SolidBrush(kTextDim))
                        g.DrawString("No callsign decoded yet", f, b, hx, 40);
                }
                else
                {
                    int room = Math.Max(60, rxBar.Left - 10 - hx);
                    Font big = new Font("Arial Black", 30f, FontStyle.Regular, GraphicsUnit.Pixel);
                    SizeF size = g.MeasureString(call, big);
                    if (size.Width > room) { big.Dispose(); big = new Font("Arial Black", Math.Max(12f, 30f * room / size.Width), FontStyle.Regular, GraphicsUnit.Pixel); }
                    using (big)
                    using (Brush b = new SolidBrush(cAccent))
                        g.DrawString(call, big, b, hx, 34);
                    TimeSpan ago = DateTime.Now - c._radeHeardAt[rx];
                    string when = c._radeHeardAt[rx].ToString("HH:mm") + "  -  " + (ago.TotalSeconds < 60 ? "just now" : ago.TotalMinutes < 60 ? (int)ago.TotalMinutes + " min ago" : (int)ago.TotalHours + " h ago");
                    using (Font f = new Font("Segoe UI", 11f, GraphicsUnit.Pixel))
                    using (Brush b = new SolidBrush(cMuted))
                        g.DrawString(when, f, b, hx, 74);
                }
            }
        }

        internal void brickRadeOpen(int rx = -1)
        {
            if (_radeStrip == null || _radeStrip.IsDisposed) _radeStrip = new BrickRadeStrip(this);
            if (rx >= 0) _radeStrip.SetRx(rx);
            brickDockRelease("rade");        // a terminal under the panadapter makes room
            brickDockShow("rade", _radeStrip);
            _digiOpenKey = "RADE"; digiSaveSettings();
        }

        internal void brickRadeClose() { brickDockHide("rade"); if (_digiOpenKey == "RADE") { _digiOpenKey = ""; digiSaveSettings(); } }

        // the FreeDV window: settings, mic processing, Reporter options
        internal void brickRadeSettings()
        {
            if (_radeForm == null || _radeForm.IsDisposed) _radeForm = new BrickRadeForm(this);
            _radeForm.ShowAt();
        }
    }
}
