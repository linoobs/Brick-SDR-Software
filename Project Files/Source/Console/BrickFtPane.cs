/*  BrickFtPane.cs

    The FT8 / FT4 window docked under the panadapter: band activity and the QSO list, the call signs, the frequencies,
    the transmit message and Enable TX. The engine is in BrickFt.cs.
*/

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace BrickSDR
{
    public partial class Console
    {
        // a list of decoded messages, drawn by hand: newest at the bottom, follows them unless scrolled back
        internal sealed class FtList : Control
        {
            public readonly List<FtRow> Items = new List<FtRow>();
            public string Heading = "";
            public bool Compact;                    // no DT column: the QSO list is the narrower one
            public Func<FtRow, bool> Accept = r => true;
            public event Action<FtRow> Picked;                 // a click: the station is selected
            public event Action<FtRow> Activated;              // a double click: select and enable the transmitter
            public Func<FtRow, bool> Mark = r => false;        // rows of the station selected as DX
            private int top;                            // first row shown
            private bool follow = true;
            private const int RowH = 18, HeadH = 20, Max = 600;
            private readonly Font font = new Font("Consolas", 10.5f);
            private readonly Font bold = new Font("Consolas", 10.5f, FontStyle.Bold);

            public FtList()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = cField;
                Cursor = Cursors.Hand;
            }

            private int visibleRows { get { return Math.Max(1, (Height - HeadH - 2) / RowH); } }

            public void Add(FtRow r)
            {
                if (!Accept(r)) return;
                Items.Add(r);
                if (Items.Count > Max) { Items.RemoveRange(0, 100); top = Math.Max(0, top - 100); }
                if (follow) top = Math.Max(0, Items.Count - visibleRows);
                Invalidate();
            }

            public void Clear() { Items.Clear(); top = 0; follow = true; Invalidate(); }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                int max = Math.Max(0, Items.Count - visibleRows);
                top = Math.Max(0, Math.Min(max, top - Math.Sign(e.Delta) * 3));
                follow = top >= max;
                Invalidate();
                base.OnMouseWheel(e);
            }

            private FtRow rowAt(int y)
            {
                int i = top + (y - HeadH) / RowH;
                return y >= HeadH && i >= 0 && i < Items.Count ? Items[i] : null;
            }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                base.OnMouseClick(e);
                FtRow r = rowAt(e.Y);
                if (r != null && Picked != null) { Picked(r); Invalidate(); }
            }

            protected override void OnMouseDoubleClick(MouseEventArgs e)
            {
                base.OnMouseDoubleClick(e);
                FtRow r = rowAt(e.Y);
                if (r != null && Activated != null) { Activated(r); Invalidate(); }
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                if (follow) top = Math.Max(0, Items.Count - visibleRows);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(cField);
                TextFormatFlags fl = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.Left;
                using (Brush b = new SolidBrush(Color.FromArgb(34, 38, 52))) g.FillRectangle(b, 0, 0, Width, HeadH);
                TextRenderer.DrawText(g, Heading, new Font("Segoe UI Semibold", 9f), new Rectangle(6, 0, Width, HeadH), cMuted, fl);
                int y = HeadH + 1;
                for (int i = top; i < Items.Count && y < Height; i++, y += RowH)
                {
                    FtRow r = Items[i];
                    bool newSlot = i > 0 && Items[i - 1].Utc != r.Utc;
                    Color fore = r.Sent ? cTx : r.ToMe ? Color.FromArgb(255, 120, 90) : r.Cq ? kGreen : cText;
                    if (r.ToMe) using (Brush b = new SolidBrush(Color.FromArgb(60, 255, 90, 60))) g.FillRectangle(b, 0, y, Width, RowH);
                    else if (r.Sent) using (Brush b = new SolidBrush(Color.FromArgb(40, 255, 60, 60))) g.FillRectangle(b, 0, y, Width, RowH);
                    if (!r.Sent && Mark(r)) using (Brush b = new SolidBrush(Color.FromArgb(70, cAccent))) g.FillRectangle(b, 0, y, Width, RowH);
                    if (newSlot) using (Pen p = new Pen(cLine)) g.DrawLine(p, 0, y, Width, y);
                    Rectangle c0 = new Rectangle(6, y, 52, RowH);
                    TextRenderer.DrawText(g, r.Utc.ToString("HHmmss"), font, c0, kTextDim, fl);
                    if (!r.Sent)
                    {
                        TextRenderer.DrawText(g, r.Snr.ToString("0", CultureInfo.InvariantCulture).PadLeft(3), font, new Rectangle(66, y, 40, RowH), fore, fl);
                        if (!Compact) TextRenderer.DrawText(g, r.Dt.ToString("0.0", CultureInfo.InvariantCulture).PadLeft(4), font, new Rectangle(108, y, 44, RowH), kTextDim, fl);
                    }
                    else TextRenderer.DrawText(g, "Tx", bold, new Rectangle(66, y, 40, RowH), fore, fl);
                    TextRenderer.DrawText(g, ((int)Math.Round(r.Hz)).ToString(CultureInfo.InvariantCulture).PadLeft(4), font, new Rectangle(Compact ? 106 : 158, y, 44, RowH), kTextDim, fl);
                    TextRenderer.DrawText(g, r.Text, r.ToMe || r.Sent || r.Cq ? bold : font, new Rectangle(Compact ? 158 : 212, y, Math.Max(10, Width - (Compact ? 158 : 212)), RowH), fore, fl | TextFormatFlags.EndEllipsis);
                }
                using (Pen p = new Pen(cLine)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            }
        }

        internal sealed class BrickFtPane : Panel
        {
            private readonly Console c;
            private readonly BrickBtn bFt8, bFt4, bAuto, bTx, bHalt, bCq, bClear, bLog;
            private readonly BrickBtn[] bMsg = new BrickBtn[5];
            private readonly BrickField my, grid, dx, msg;
            private readonly Label lblMy, lblGrid, lblDx, lblRxHz, lblTxHz, status, clock;
            private readonly BrickNum rxHz, txHz;
            private readonly FtList activity, qso;
            private readonly MeterBar slotBar = new MeterBar(), inBar = new MeterBar();
            private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 100 };
            private bool setting;
            private string lastNote = "";
            private DateTime noteAt;

            public BrickFtPane(Console console)
            {
                c = console;
                BackColor = cCard;
                ForeColor = cText;
                Font = new Font("Segoe UI", 9f);
                Size = new Size(1000, 270);

                bFt8 = mk("FT8", 50, 26, () => { c.brickFtSetMode("FT8"); activity.Clear(); qso.Clear(); sync(); });
                bFt4 = mk("FT4", 50, 26, () => { c.brickFtSetMode("FT4"); activity.Clear(); qso.Clear(); sync(); });
                bClear = mk("Clear", 60, 26, () => { activity.Clear(); qso.Clear(); });
                bLog = mk("Log", 50, 26, () => c.FtShowLog());
                lblMy = lbl("My call"); lblGrid = lbl("Grid"); lblDx = lbl("DX call");
                my = new BrickField { CharacterCasing = CharacterCasing.Upper, Width = 90 };
                grid = new BrickField { CharacterCasing = CharacterCasing.Upper, Width = 64, MaxLength = 6 };
                dx = new BrickField { CharacterCasing = CharacterCasing.Upper, Width = 100 };
                my.Text = c._digiMyCall; grid.Text = c._ftGrid; dx.Text = c._ftDx;
                my.TextChangedX += (s, e) => { if (setting) return; c._digiMyCall = my.Text.Trim().ToUpperInvariant(); c.digiSaveSettings(); };
                grid.TextChangedX += (s, e) => { if (setting) return; c._ftGrid = grid.Text.Trim().ToUpperInvariant(); c.digiSaveSettings(); };
                dx.TextChangedX += (s, e) => { if (setting) return; c._ftDx = dx.Text.Trim().ToUpperInvariant(); c._ftLogged = false; };
                // a call sign and Enter: answer the last message that station sent, as a double click on it would
                dx.KeyPressX += (s, e) =>
                {
                    if (e.KeyChar != (char)Keys.Enter) return;
                    e.Handled = true;
                    string call = dx.Text.Trim().ToUpperInvariant();
                    for (int i = activity.Items.Count - 1; call.Length > 0 && i >= 0; i--)
                    {
                        FtRow r = activity.Items[i];
                        if (r.Sent) continue;
                        if (c.FtSender(r) == call) { c.FtPick(r, true); return; }
                    }
                    c.FtStatus = call.Length > 0 ? call + " not in the list" : "";
                };

                lblRxHz = lbl("Rx"); lblTxHz = lbl("Tx");
                rxHz = new BrickNum { Minimum = 200, Maximum = 3000, Increment = 10, Suffix = " Hz", Width = 104, Height = 24, Value = c._ftRxHz };
                txHz = new BrickNum { Minimum = 200, Maximum = 3000, Increment = 10, Suffix = " Hz", Width = 104, Height = 24, Value = c._ftTxHz };
                rxHz.ValueChanged += (s, e) => { if (setting) return; c._ftRxHz = (int)rxHz.Value; c.digiSaveSettings(); };
                txHz.ValueChanged += (s, e) => { if (setting) return; c._ftTxHz = (int)txHz.Value; c.digiSaveSettings(); };
                bAuto = mk("Auto-sequence", 112, 24, null); bAuto.Toggle = true; bAuto.Checked = c._ftAuto;
                bAuto.CheckedChanged += (s, e) => { c._ftAuto = bAuto.Checked; c.digiSaveSettings(); };

                activity = new FtList { Heading = "Band activity      dB    DT   Hz   message" };
                qso = new FtList { Heading = "Your QSO        dB   Hz   message", Compact = true };
                qso.Accept = r => r.Sent || r.ToMe || (c._ftDx.Length > 0 && r.Text.Contains(c._ftDx));
                activity.Picked += r => c.FtPick(r, false);
                qso.Picked += r => c.FtPick(r, false);
                activity.Activated += r => c.FtPick(r, true);
                qso.Activated += r => c.FtPick(r, true);
                activity.Mark = qso.Mark = r => c._ftDx.Length > 0 && c.FtSender(r) == c._ftDx;

                slotBar.Height = 6; inBar.Height = 6;
                status = new Label { AutoSize = false, Height = 18, ForeColor = cMuted, BackColor = cCard, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
                clock = new Label { AutoSize = false, Height = 18, Width = 250, ForeColor = cMuted, BackColor = cCard, TextAlign = ContentAlignment.MiddleRight };

                bCq = mk("CQ", 52, 28, () => { c.FtSetMessage(6); c._ftDx = ""; c.FtCallCq(); sync(); });
                string[] names = { "Tx1", "Tx2", "Tx3", "Tx4", "Tx5" };
                for (int i = 0; i < 5; i++)
                {
                    int no = i + 1;
                    bMsg[i] = mk(names[i], 44, 28, () => { c.FtSetMessage(no); sync(); });
                }
                msg = new BrickField(new Font("Consolas", 11.5f)) { CharacterCasing = CharacterCasing.Upper };
                msg.KeyPressX += (s, e) =>
                {
                    if (e.KeyChar == (char)Keys.Enter) { e.Handled = true; c.FtSetCustom(msg.Text); sync(); }
                };
                bTx = mk("Enable TX", 96, 28, () => { c.FtEnableTx(!c._ftTxEnable); sync(); });
                bTx.OnColor = cTx;
                bHalt = mk("Halt", 64, 28, () => { c.FtHalt(); sync(); });
                bHalt.OnColor = cTx; bHalt.ForeColor = cTx;

                Controls.AddRange(new Control[] { bFt8, bFt4, lblMy, my, lblGrid, grid, lblDx, dx, bLog, bClear,
                    lblRxHz, rxHz, lblTxHz, txHz, bAuto, activity, qso, slotBar, inBar, status, clock, bCq, msg, bTx, bHalt });
                foreach (BrickBtn b in bMsg) Controls.Add(b);
                Resize += (s, e) => layout();
                timer.Tick += (s, e) => tick();
                VisibleChanged += (s, e) => { if (Visible) { layout(); sync(); timer.Start(); } else timer.Stop(); };
                layout();
            }

            private Label lbl(string t)
            {
                return new Label { Text = t, AutoSize = true, ForeColor = cMuted, BackColor = cCard, Font = new Font("Segoe UI", 9f) };
            }

            private BrickBtn mk(string text, int w, int h, Action act)
            {
                BrickBtn b = new BrickBtn { Text = text, Width = w, Height = h };
                if (act != null) b.Click += (s, e) => act();
                return b;
            }

            public void SetMode(string mode)
            {
                setting = true;
                bFt8.On = mode == "FT8"; bFt4.On = mode == "FT4";
                my.Text = c._digiMyCall; grid.Text = c._ftGrid;
                rxHz.Value = c._ftRxHz; txHz.Value = c._ftTxHz;
                setting = false;
                activity.Clear(); qso.Clear();
                sync();
            }

            // refresh from the engine's QSO state (called from it, on the UI thread)
            public void Refresh2()
            {
                setting = true;
                if (dx.Text != c._ftDx) dx.Text = c._ftDx;
                if (txHz.Value != c._ftTxHz) txHz.Value = c._ftTxHz;
                if (rxHz.Value != c._ftRxHz) rxHz.Value = c._ftRxHz;
                setting = false;
                sync();
            }

            private void sync()
            {
                bFt8.On = c._ftMode == "FT8"; bFt4.On = c._ftMode == "FT4";
                bTx.On = c._ftTxEnable;
                bTx.Text = c._ftTxEnable ? "TX enabled" : "Enable TX";
                bCq.On = c._ftMsgNo == 6 && c._ftCustom.Length == 0;
                for (int i = 0; i < 5; i++) bMsg[i].On = c._ftMsgNo == i + 1 && c._ftCustom.Length == 0;
                if (!msg.Box.Focused) msg.Text = c.FtNextMessage;
            }

            private void layout()
            {
                int w = Width, h = Height;
                if (w < 300 || h < 120) return;
                // row 1: mode, calls
                bClear.SetBounds(w - 8 - 60, 6, 60, 26);
                bLog.SetBounds(bClear.Left - 6 - 50, 6, 50, 26);
                bFt8.SetBounds(8, 6, 50, 26); bFt4.SetBounds(62, 6, 50, 26);
                int x = 124;
                lblMy.Location = new Point(x, 11); x += lblMy.PreferredWidth + 4; my.SetBounds(x, 6, 90, 26); x += 98;
                lblGrid.Location = new Point(x, 11); x += lblGrid.PreferredWidth + 4; grid.SetBounds(x, 6, 64, 26); x += 72;
                lblDx.Location = new Point(x, 11); x += lblDx.PreferredWidth + 4; dx.SetBounds(x, 6, 100, 26);
                // row 2: frequencies, auto
                x = 8;
                lblRxHz.Location = new Point(x, 40); x += lblRxHz.PreferredWidth + 4; rxHz.Location = new Point(x, 37); x += rxHz.Width + 12;
                lblTxHz.Location = new Point(x, 40); x += lblTxHz.PreferredWidth + 4; txHz.Location = new Point(x, 37); x += txHz.Width + 12;
                bAuto.Location = new Point(x, 37);
                // bottom: messages and TX
                int rowY = h - 34;
                bCq.Location = new Point(8, rowY);
                int bx = 8 + bCq.Width + 6;
                foreach (BrickBtn b in bMsg) { b.Location = new Point(bx, rowY); bx += b.Width + 4; }
                bHalt.Location = new Point(w - 8 - bHalt.Width, rowY);
                bTx.Location = new Point(bHalt.Left - 6 - bTx.Width, rowY);
                msg.SetBounds(bx + 6, rowY, Math.Max(80, bTx.Left - 6 - (bx + 6)), 28);
                // status line and the two bars
                status.SetBounds(8, rowY - 22, w - 16 - clock.Width, 18);
                clock.SetBounds(w - 8 - clock.Width, rowY - 22, clock.Width, 18);
                slotBar.SetBounds(8, rowY - 30, (w - 24) / 2, 6);
                inBar.SetBounds(slotBar.Right + 8, rowY - 30, w - 8 - (slotBar.Right + 8), 6);
                // the lists
                int top = 70, bot = slotBar.Top - 4;
                int lw = (int)((w - 24) * 0.5);
                activity.SetBounds(8, top, lw, Math.Max(40, bot - top));
                qso.SetBounds(8 + lw + 8, top, w - 24 - lw, Math.Max(40, bot - top));
            }

            private void tick()
            {
                sync();
                FtRow r;
                int guard = 0;
                while (guard++ < 200 && c.FtTryRow(out r)) { activity.Add(r); qso.Add(r); }
                double T = c.FtSlotLength;
                slotBar.Value = Math.Max(0, Math.Min(1, c.FtSlotPos / T));
                double lvl = Math.Max(0, Math.Min(1, (c.FtInDb + 60) / 60.0));
                inBar.Value = lvl;
                bool tx = c.FtTransmitting;
                string text = c.FtStatus;
                if (text != lastNote) { lastNote = text; noteAt = DateTime.UtcNow; }
                else if (text.Length > 0 && (DateTime.UtcNow - noteAt).TotalSeconds > 7) { c.FtStatus = ""; text = ""; }
                string problem = c.FtStartProblem;
                if (tx) { text = "TRANSMITTING   " + c.FtNextMessage; status.ForeColor = cTx; }
                else
                {
                    status.ForeColor = cMuted;
                    if (text.Length == 0 || problem != null && !c._ftTxEnable) text = problem ?? "";
                    if (text.Length == 0) text = c._ftTxEnable ? "Waiting for the " + (c._ftTxEven ? "even" : "odd") + " slot: " + c.FtNextMessage : "Listening";
                }
                if (status.Text != text) status.Text = text;
                DateTime u = DateTime.UtcNow;
                u = u.AddSeconds(-c.FtClock);
                clock.Text = u.ToString("HH:mm:ss") + " UTC" + (c._digiRx == 1 ? "  RX2" : "") + (Math.Abs(c.FtClock) >= 0.1 ? "  (PC clock " + (c.FtClock > 0 ? "+" : "") + c.FtClock.ToString("0.0", CultureInfo.InvariantCulture) + " s)" : "");
                bTx.On = c._ftTxEnable || tx;
                if (!msg.Box.Focused && msg.Text != c.FtNextMessage) msg.Text = c.FtNextMessage;
                if (tx) { slotBar.Invalidate(); }
            }
        }
    }
}
