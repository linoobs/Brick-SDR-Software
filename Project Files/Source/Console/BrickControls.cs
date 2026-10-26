using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BrickSDR
{
    // owner drawn controls of the Brick-SDR main window
    public partial class Console
    {
        // Frequency display of one VFO: big MHz.kHz digits, smaller Hz digits, wheel over a digit tunes it.
        private sealed class BrickVfoBox : Control
        {
            public string Title = "VFO";
            public Func<double> GetFreq;
            public Action<double> SetFreq;
            public Func<bool> IsActive;
            public Func<bool> IsTransmitting;

            private readonly List<Rectangle> digitRects = new List<Rectangle>();
            private readonly List<double> digitStepHz = new List<double>();
            private int hover = -1;
            private double lastFreq = double.NaN;
            private bool lastActive, lastTx;
            private readonly Timer poll = new Timer();

            public BrickVfoBox()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = cCard;
                poll.Interval = 80;
                poll.Tick += (s, e) =>
                {
                    if (GetFreq == null) return;
                    double f = GetFreq();
                    bool a = IsActive != null && IsActive();
                    bool t = IsTransmitting != null && IsTransmitting();
                    if (f != lastFreq || a != lastActive || t != lastTx) Invalidate();
                };
                poll.Start();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) poll.Dispose();
                base.Dispose(disposing);
            }

            // returns true when the wheel was used on a digit
            public bool Wheel(int delta)
            {
                Point p = PointToClient(Control.MousePosition);
                int d = HitDigit(p);
                if (d < 0 || GetFreq == null || SetFreq == null) return false;
                int dir = delta > 0 ? 1 : -1;
                SetFreq(GetFreq() + dir * digitStepHz[d] * 1e-6);
                Invalidate();
                return true;
            }

            private int HitDigit(Point p)
            {
                for (int i = 0; i < digitRects.Count; i++)
                    if (digitRects[i].Contains(p.X, p.Y)) return i;
                return -1;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int h = HitDigit(e.Location);
                if (h != hover) { hover = h; Invalidate(); }
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                if (hover != -1) { hover = -1; Invalidate(); }
            }

            // left click on a digit: up, right click: down
            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                int d = HitDigit(e.Location);
                if (d < 0 || GetFreq == null || SetFreq == null) return;
                if (e.Button == MouseButtons.Left) SetFreq(GetFreq() + digitStepHz[d] * 1e-6);
                else if (e.Button == MouseButtons.Right) SetFreq(GetFreq() - digitStepHz[d] * 1e-6);
                Invalidate();
            }

            protected override void OnDoubleClick(EventArgs e)
            {
                base.OnDoubleClick(e);
                if (GetFreq == null || SetFreq == null) return;
                using (Form f = new Form())
                using (TextBox tb = new TextBox())
                using (Button ok = new Button())
                {
                    f.Text = Title + " frequency, MHz";
                    f.FormBorderStyle = FormBorderStyle.FixedToolWindow;
                    f.StartPosition = FormStartPosition.CenterParent;
                    f.ClientSize = new Size(240, 64);
                    f.BackColor = cCard;
                    tb.SetBounds(10, 10, 220, 22);
                    tb.Text = GetFreq().ToString("0.000000");
                    ok.Text = "OK"; ok.DialogResult = DialogResult.OK; ok.SetBounds(160, 36, 70, 22);
                    f.Controls.Add(tb); f.Controls.Add(ok);
                    f.AcceptButton = ok;
                    if (f.ShowDialog(FindForm()) == DialogResult.OK)
                    {
                        double v;
                        if (double.TryParse(tb.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) && v > 0)
                            SetFreq(v);
                    }
                }
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                if (Width > 8 && Height > 8)
                    using (System.Drawing.Drawing2D.GraphicsPath gp = brickRoundPath(new Rectangle(0, 0, Width, Height), 9))
                        Region = new Region(gp);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.Clear(cCard);
                using (System.Drawing.Drawing2D.GraphicsPath gp = brickRoundPath(new Rectangle(0, 0, Width - 1, Height - 1), 9))
                using (Pen pen = new Pen(cLine, 1.5f)) g.DrawPath(pen, gp);

                double f = GetFreq != null ? GetFreq() : 0;
                bool active = IsActive != null && IsActive();
                bool tx = IsTransmitting != null && IsTransmitting();
                lastFreq = f; lastActive = active; lastTx = tx;

                // title and tags
                using (Font ft = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                using (Brush bm = new SolidBrush(cMuted))
                {
                    g.DrawString(Title.ToUpperInvariant(), ft, bm, 8, 4);
                    SizeF ts = g.MeasureString(Title.ToUpperInvariant(), ft);
                    DrawTag(g, "RX", (int)ts.Width + 14, 5, Color.FromArgb(40, 190, 170), !tx);
                    DrawTag(g, "TX", (int)ts.Width + 14 + 28, 5, cTx, tx);
                }

                long hz = (long)Math.Round(f * 1e6);
                string mhz = (hz / 1000000).ToString();
                string khz = ((hz / 1000) % 1000).ToString("000");
                string small = (hz % 1000).ToString("000");

                float bigSize = 27f, smallSize = 18f;
                using (Font fbig = new Font("Consolas", bigSize, FontStyle.Bold))
                using (Font fsm = new Font("Consolas", smallSize, FontStyle.Bold))
                {
                    StringFormat sf = StringFormat.GenericTypographic;
                    float cwB = g.MeasureString("0", fbig, 100, sf).Width;
                    float dwB = g.MeasureString(".", fbig, 100, sf).Width;
                    float cwS = g.MeasureString("0", fsm, 100, sf).Width;
                    float dwS = g.MeasureString(".", fsm, 100, sf).Width;
                    float total = mhz.Length * cwB + dwB + 3 * cwB + dwS + 3 * cwS;
                    float right = Width - 38;
                    float x = right - total;
                    float yBig = 18, ySmall = 18 + (fbig.Height - fsm.Height) - 2;
                    Color col = active ? cAccent : Color.FromArgb(150, 154, 166);
                    Color colSmall = active ? Color.FromArgb(200, 110, 30) : Color.FromArgb(110, 114, 126);

                    digitRects.Clear(); digitStepHz.Clear();
                    using (Brush b = new SolidBrush(col))
                    using (Brush bs = new SolidBrush(colSmall))
                    {
                        // MHz digits
                        for (int i = 0; i < mhz.Length; i++)
                        {
                            g.DrawString(mhz[i].ToString(), fbig, b, x, yBig, sf);
                            digitRects.Add(new Rectangle((int)x, 14, (int)cwB, fbig.Height + 6));
                            digitStepHz.Add(Math.Pow(10, mhz.Length - 1 - i) * 1e6);
                            x += cwB;
                        }
                        g.DrawString(".", fbig, b, x, yBig, sf); x += dwB;
                        for (int i = 0; i < 3; i++)
                        {
                            g.DrawString(khz[i].ToString(), fbig, b, x, yBig, sf);
                            digitRects.Add(new Rectangle((int)x, 14, (int)cwB, fbig.Height + 6));
                            digitStepHz.Add(Math.Pow(10, 2 - i) * 1e3);
                            x += cwB;
                        }
                        g.DrawString(".", fsm, bs, x, ySmall, sf); x += dwS;
                        for (int i = 0; i < 3; i++)
                        {
                            g.DrawString(small[i].ToString(), fsm, bs, x, ySmall, sf);
                            digitRects.Add(new Rectangle((int)x, 14, (int)cwS, fbig.Height + 6));
                            digitStepHz.Add(Math.Pow(10, 2 - i));
                            x += cwS;
                        }
                    }

                    if (hover >= 0 && hover < digitRects.Count)
                    {
                        Rectangle r = digitRects[hover];
                        using (Pen p = new Pen(cAccent, 2f))
                            g.DrawLine(p, r.Left, r.Bottom - 2, r.Right, r.Bottom - 2);
                    }
                }
            }

            private static void DrawTag(Graphics g, string text, int x, int y, Color c, bool on)
            {
                using (Font f = new Font("Segoe UI", 6.5f, FontStyle.Bold))
                {
                    Color fill = on ? Color.FromArgb(60, c) : Color.FromArgb(30, 34, 46);
                    Color txt = on ? c : Color.FromArgb(90, 96, 112);
                    using (Brush b = new SolidBrush(fill)) g.FillRectangle(b, x, y, 24, 12);
                    using (Pen p = new Pen(on ? c : Color.FromArgb(60, 64, 80))) g.DrawRectangle(p, x, y, 24, 12);
                    using (Brush bt = new SolidBrush(txt)) g.DrawString(text, f, bt, x + 4, y - 0.5f);
                }
            }
        }

        // flat drop-down button with a dark popup list
        private sealed class BrickDropDown : Button
        {
            public readonly List<string> Items = new List<string>();
            private int selected = -1;
            public event EventHandler SelectedChanged;

            public BrickDropDown()
            {
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderColor = cLine;
                FlatAppearance.MouseOverBackColor = cBtnHover;
                BackColor = cBtn;
                ForeColor = cText;
                Font = new Font("Segoe UI", 8.5f);
                TextAlign = ContentAlignment.MiddleLeft;
                UseVisualStyleBackColor = false;
            }

            // replaces the list, nothing selected
            public void SetItems(System.Collections.Generic.IEnumerable<string> items)
            {
                Items.Clear();
                Items.AddRange(items);
                selected = -1;
                Text = "";
                Invalidate();
            }

            public int SelectedIndex
            {
                get { return selected; }
                set
                {
                    if (value == selected || value < 0 || value >= Items.Count) return;
                    selected = value;
                    Text = Items[value];
                    Invalidate();
                    if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
                }
            }

            protected override void OnClick(EventArgs e)
            {
                base.OnClick(e);
                ContextMenuStrip m = new ContextMenuStrip();
                m.Renderer = new ToolStripProfessionalRenderer(new BrickColorTable());
                m.BackColor = cCard;
                m.ForeColor = cText;
                m.ShowImageMargin = false;
                m.MaximumSize = new Size(Width + 20, 400);
                for (int i = 0; i < Items.Count; i++)
                {
                    int idx = i;
                    ToolStripMenuItem it = new ToolStripMenuItem(Items[i]);
                    it.ForeColor = i == selected ? cAccent : cText;
                    it.BackColor = cCard;
                    it.Click += (s, a) => SelectedIndex = idx;
                    m.Items.Add(it);
                }
                m.Closed += (s, a) => { Control f = FindForm(); if (f != null) { f.Invalidate(true); f.Update(); } };
                m.Show(this, new Point(0, Height));
            }

            protected override void OnPaint(PaintEventArgs pevent)
            {
                base.OnPaint(pevent);
                Graphics g = pevent.Graphics;
                int cx = Width - 10, cy = Height / 2;
                using (Brush b = new SolidBrush(cMuted))
                    g.FillPolygon(b, new Point[] { new Point(cx - 4, cy - 2), new Point(cx + 4, cy - 2), new Point(cx, cy + 3) });
            }
        }
    }
}
