/*  BrickUi.cs

    Small owner-drawn controls in the Brick-SDR style, used by the digital windows (RTTY / CW terminal, FreeDV):
    a rounded button that also works as a toggle, a value stepper, and a framed text box.
*/

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BrickSDR
{
    public partial class Console
    {
        // rounded, slightly raised button; with Toggle it keeps a lit state (the main window's buttons look the same)
        internal sealed class BrickBtn : Control
        {
            public bool Toggle;
            public Color OnColor = cAccent;
            public event EventHandler CheckedChanged;
            private bool on, hover, down;

            public BrickBtn()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, false);
                SetStyle(ControlStyles.StandardClick, true);
                Font = new Font("Segoe UI Semibold", 9f);
                Cursor = Cursors.Hand;
                TabStop = false;
                Size = new Size(70, 28);
            }

            public bool On
            {
                get { return on; }
                set { if (on != value) { on = value; Invalidate(); } }
            }

            public bool Checked
            {
                get { return on; }
                set { if (on != value) { on = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); } }
            }

            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
            protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
            protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
            protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

            protected override void OnClick(EventArgs e)
            {
                if (Toggle) Checked = !on;
                base.OnClick(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Parent != null ? Parent.BackColor : cCard);
                Color baseC = on ? OnColor : cBtn;
                if (!Enabled) baseC = on ? brickShade(OnColor, -0.55) : Color.FromArgb(30, 33, 44);
                Color top = brickShade(baseC, hover && Enabled ? 0.26 : 0.16);
                Color bottom = brickShade(baseC, down && hover ? 0.08 : -0.18);
                Rectangle r = new Rectangle(1, 1, Width - 3, Height - 3);
                if (r.Width < 4 || r.Height < 4) return;
                using (GraphicsPath gp = brickRoundPath(r, 6))
                {
                    using (LinearGradientBrush lg = new LinearGradientBrush(r, top, bottom, 90f)) g.FillPath(lg, gp);
                    using (Pen pen = new Pen(on ? brickShade(OnColor, -0.35) : Color.FromArgb(70, 76, 98), 1f)) g.DrawPath(pen, gp);
                }
                using (Pen hi = new Pen(Color.FromArgb(on ? 90 : 45, 255, 255, 255), 1f)) g.DrawLine(hi, r.Left + 6, r.Top + 1, r.Right - 6, r.Top + 1);
                Color fore = !Enabled ? cMuted : (on ? Color.FromArgb(20, 20, 24) : cText);
                if (!on && OnColor == cTx && ForeColor == cTx) fore = cTx;
                TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height - 1), fore,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }

        // a value with minus and plus, held for repeating; the mouse wheel steps it too
        internal sealed class BrickNum : Control
        {
            public decimal Minimum = 0, Maximum = 100, Increment = 1;
            public int DecimalPlaces;
            public string Suffix = "";
            public event EventHandler ValueChanged;
            private decimal v;
            private int dir, hot;                       // hot: -1 minus, 1 plus
            private readonly Timer rep = new Timer { Interval = 380 };

            public BrickNum()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                Font = new Font("Segoe UI Semibold", 9f);
                Size = new Size(96, 24);
                Cursor = Cursors.Hand;
                rep.Tick += (s, e) => { rep.Interval = 70; step(dir); };
            }

            public decimal Value
            {
                get { return v; }
                set
                {
                    decimal n = Math.Max(Minimum, Math.Min(Maximum, Math.Round(value, DecimalPlaces)));
                    if (n == v) return;
                    v = n;
                    Invalidate();
                    if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
                }
            }

            private void step(int d) { Value = v + d * Increment; }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                dir = e.X < Height ? -1 : (e.X > Width - Height ? 1 : 0);
                if (dir == 0) return;
                step(dir);
                rep.Interval = 380;
                rep.Start();
            }

            protected override void OnMouseUp(MouseEventArgs e) { rep.Stop(); base.OnMouseUp(e); }
            protected override void OnMouseLeave(EventArgs e) { rep.Stop(); hot = 0; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseMove(MouseEventArgs e) { int h = e.X < Height ? -1 : (e.X > Width - Height ? 1 : 0); if (h != hot) { hot = h; Invalidate(); } base.OnMouseMove(e); }
            protected override void OnMouseWheel(MouseEventArgs e) { step(e.Delta > 0 ? 1 : -1); base.OnMouseWheel(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Parent != null ? Parent.BackColor : cCard);
                Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
                using (GraphicsPath gp = brickRoundPath(r, 5))
                {
                    using (Brush b = new SolidBrush(cField)) g.FillPath(b, gp);
                    using (Pen p = new Pen(cLine)) g.DrawPath(p, gp);
                }
                int bw = Height;
                foreach (int side in new[] { -1, 1 })
                {
                    Rectangle br = side < 0 ? new Rectangle(1, 1, bw - 2, Height - 3) : new Rectangle(Width - bw, 1, bw - 2, Height - 3);
                    if (hot == side) using (Brush b = new SolidBrush(Color.FromArgb(60, 255, 255, 255))) g.FillRectangle(b, br);
                    TextRenderer.DrawText(g, side < 0 ? "−" : "+", Font, br, cAccent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                string t = v.ToString("F" + DecimalPlaces, System.Globalization.CultureInfo.InvariantCulture) + Suffix;
                TextRenderer.DrawText(g, t, Font, new Rectangle(bw, 0, Width - 2 * bw, Height - 1), cText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        // a text box (or any control) in a thin frame that turns orange while it has the focus
        internal class BrickFrame : Panel
        {
            private readonly Control inner;

            public BrickFrame(Control child)
            {
                inner = child;
                BackColor = cField;
                Padding = new Padding(5, 3, 5, 3);
                child.BackColor = cField;
                child.ForeColor = cText;
                child.Dock = DockStyle.Fill;
                Controls.Add(child);
                child.Enter += (s, e) => Invalidate();
                child.Leave += (s, e) => Invalidate();
                SetStyle(ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath gp = brickRoundPath(new Rectangle(0, 0, Width - 1, Height - 1), 5))
                using (Pen p = new Pen(inner.Focused ? cAccent : cLine)) g.DrawPath(p, gp);
            }
        }

        internal sealed class BrickField : BrickFrame
        {
            public readonly TextBox Box;

            public BrickField(Font font = null) : this(new TextBox { BorderStyle = BorderStyle.None, Font = font ?? new Font("Segoe UI", 9f) }) { }

            private BrickField(TextBox tb) : base(tb)
            {
                Box = tb;
                Height = tb.PreferredHeight + 8;
            }

            public override string Text { get { return Box.Text; } set { Box.Text = value; } }
            public event EventHandler TextChangedX { add { Box.TextChanged += value; } remove { Box.TextChanged -= value; } }
            public event KeyPressEventHandler KeyPressX { add { Box.KeyPress += value; } remove { Box.KeyPress -= value; } }
            public CharacterCasing CharacterCasing { get { return Box.CharacterCasing; } set { Box.CharacterCasing = value; } }
            public int MaxLength { get { return Box.MaxLength; } set { Box.MaxLength = value; } }
            public void Clear() { Box.Clear(); }
            public void FocusBox() { Box.Focus(); }
        }
    }
}
