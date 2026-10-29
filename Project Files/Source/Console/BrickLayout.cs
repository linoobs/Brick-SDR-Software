using System;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace BrickSDR
{
    // Brick-SDR main window: the existing console controls (and all of their logic) are kept,
    // they are re-parented into a new layout and painted in the Brick-SDR colours.
    public partial class Console
    {
        #region colours

        private static readonly Color cBg = Color.FromArgb(18, 20, 28);
        private static readonly Color cCard = Color.FromArgb(24, 27, 37);
        private static readonly Color cLine = Color.FromArgb(48, 53, 70);
        private static readonly Color cText = Color.FromArgb(225, 228, 235);
        private static readonly Color cMuted = Color.FromArgb(140, 146, 160);
        private static readonly Color cAccent = Color.FromArgb(255, 138, 31);
        private static readonly Color cBtn = Color.FromArgb(36, 40, 55);
        private static readonly Color cBtnHover = Color.FromArgb(52, 57, 76);
        private static readonly Color cTx = Color.FromArgb(214, 48, 48);
        private static readonly Color cField = Color.FromArgb(10, 12, 18);

        #endregion

        #region fields

        private bool brickActive = false;
        private Panel brickRoot, pnlTop, pnlLeft, pnlRight, pnlCenter, pnlBottomStrip;
        private Panel cardTxRx, cardMeter, cardOptsA, cardOptsB, cardVfoFunc;
        private ComboBox comboStepA, comboStepB;
        private bool brickSyncingStep = false;
        private System.Windows.Forms.Timer brickTimer;

        public bool BrickLayoutActive { get { return brickActive; } }

        #endregion

        #region helpers

        private static void Put(Control c, Control parent, int x, int y, int w, int h)
        {
            if (c == null) return;
            c.Dock = DockStyle.None;
            c.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            if (c.Parent != parent) parent.Controls.Add(c);
            c.SetBounds(x, y, w, h);
            c.Visible = true;
        }

        // grid of buttons, returns the y just below the last row
        private static int Grid(Control parent, int x, int y, int w, int cols, int h, int gap, params Control[] items)
        {
            int cw = (w - gap * (cols - 1)) / cols;
            int i = 0;
            foreach (Control c in items)
            {
                int r = i / cols, col = i % cols;
                if (c != null) Put(c, parent, x + col * (cw + gap), y + r * (h + gap), cw, h);
                i++;
            }
            int rows = (items.Length + cols - 1) / cols;
            return y + rows * (h + gap) - gap;
        }

        private Label brickLabel(Control parent, string text, float size, Color color, bool bold, int x, int y, int w, int h)
        {
            Label l = new Label();
            l.AutoSize = false;
            l.BackColor = Color.Transparent;
            l.ForeColor = color;
            l.Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
            l.Text = text;
            l.SetBounds(x, y, w, h);
            parent.Controls.Add(l);
            return l;
        }

        // section title with a thin line, like on vid.jpg
        private int brickSection(Control parent, string title, int y, int w)
        {
            brickLabel(parent, title.ToUpperInvariant(), 7.5f, cMuted, true, 8, y, w - 16, 14);
            return y + 17;
        }

        private Panel brickPanel(Control parent, Color back)
        {
            Panel p = new Panel();
            p.BackColor = back;
            parent.Controls.Add(p);
            return p;
        }

        private static System.Drawing.Drawing2D.GraphicsPath brickRoundPath(Rectangle r, int rad)
        {
            System.Drawing.Drawing2D.GraphicsPath gp = new System.Drawing.Drawing2D.GraphicsPath();
            int d = Math.Max(2, rad * 2);
            if (d > r.Height) d = r.Height;
            if (d > r.Width) d = r.Width;
            gp.AddArc(r.X, r.Y, d, d, 180, 90);
            gp.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            gp.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            gp.CloseFigure();
            return gp;
        }

        private static Color brickShade(Color c, double f)
        {
            int r = (int)Math.Max(0, Math.Min(255, c.R + (f >= 0 ? (255 - c.R) * f : c.R * f)));
            int g = (int)Math.Max(0, Math.Min(255, c.G + (f >= 0 ? (255 - c.G) * f : c.G * f)));
            int b = (int)Math.Max(0, Math.Min(255, c.B + (f >= 0 ? (255 - c.B) * f : c.B * f)));
            return Color.FromArgb(r, g, b);
        }

        private Panel brickCard(Control parent)
        {
            Panel p = new Panel();
            p.BackColor = cCard;
            p.SizeChanged += (s, e) =>
            {
                if (p.Width < 8 || p.Height < 8) return;
                using (System.Drawing.Drawing2D.GraphicsPath gp = brickRoundPath(new Rectangle(0, 0, p.Width, p.Height), 9))
                    p.Region = new Region(gp);
            };
            p.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (System.Drawing.Drawing2D.GraphicsPath gp = brickRoundPath(new Rectangle(0, 0, p.Width - 1, p.Height - 1), 9))
                using (Pen pen = new Pen(cLine, 1.5f))
                    e.Graphics.DrawPath(pen, gp);
            };
            parent.Controls.Add(p);
            return p;
        }

        private readonly HashSet<ButtonBase> brickHover = new HashSet<ButtonBase>();

        // rounded, slightly raised button in the Brick-SDR colours
        private void brickPaintButton(ButtonBase b, PaintEventArgs e, Color on, bool state)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Color parentBack = b.Parent != null ? b.Parent.BackColor : cCard;
            g.Clear(parentBack);
            bool hover = brickHover.Contains(b) && b.Enabled;
            bool down = hover && (Control.MouseButtons & MouseButtons.Left) != 0;
            bool teal = b.Tag is string && (string)b.Tag == "teal";
            Color baseC = state ? (teal ? Color.FromArgb(22, 50, 56) : on) : cBtn;
            if (!b.Enabled) baseC = state ? brickShade(on, -0.55) : Color.FromArgb(30, 33, 44);
            Color top = brickShade(baseC, hover ? 0.26 : 0.16);
            Color bottom = brickShade(baseC, down ? 0.08 : -0.18);
            Rectangle r = new Rectangle(1, 1, b.Width - 3, b.Height - 3);
            if (r.Width < 4 || r.Height < 4) return;
            using (System.Drawing.Drawing2D.GraphicsPath gp = brickRoundPath(r, 6))
            {
                using (System.Drawing.Drawing2D.LinearGradientBrush lg = new System.Drawing.Drawing2D.LinearGradientBrush(r, top, bottom, 90f))
                    g.FillPath(lg, gp);
                using (Pen pen = new Pen(state ? (teal ? Color.FromArgb(40, 190, 170) : brickShade(on, -0.35)) : Color.FromArgb(70, 76, 98), 1f))
                    g.DrawPath(pen, gp);
            }
            using (Pen hi = new Pen(Color.FromArgb(state ? 90 : 45, 255, 255, 255), 1f))
                g.DrawLine(hi, r.Left + 6, r.Top + 1, r.Right - 6, r.Top + 1);
            Color fore = !b.Enabled ? cMuted : (state ? (teal ? Color.FromArgb(70, 215, 195) : Color.FromArgb(20, 20, 24)) : cText);
            TextRenderer.DrawText(g, b.Text, b.Font, new Rectangle(0, 0, b.Width, b.Height - 1), fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        #endregion

        #region build

        // called once, after the console is initialised and the settings are loaded
        // ---- compact layout: all vertical positions / heights are scaled by brickK (1 = normal) when the window is low
        private const double BrickMinK = 0.72;
        private double brickK = 1.0;
        private int brickNeedH;
        private readonly Dictionary<Control, int[]> brickYOrig = new Dictionary<Control, int[]>(); // top, height, left, width
        private HashSet<Control> brickXSkip;

        private double brickKx = 1.0;
        private int brickX(int v) { return (int)Math.Round(v * brickKx); }
        private const int BrickDesignW = 1360; // client width the layout is designed for
        private const int BrickMinOuterW = 1280; // smallest window width

        private int brickY(int v) { return (int)Math.Round(v * brickK); }

        private void brickCaptureY(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c == picMultiMeterDigital || c == picRX2Meter || c.Dock != DockStyle.None) continue;
                if (!brickYOrig.ContainsKey(c)) brickYOrig[c] = new int[] { c.Top, c.Height, c.Left, c.Width };
                if (c is Panel || c is GroupBox || c is BrickVfoBox) brickCaptureY(c);
            }
        }

        // controls whose horizontal place is calculated in layoutTop
        private bool brickSkipX(Control c)
        {
            if (brickXSkip == null)
            {
                brickXSkip = new HashSet<Control> { cardMeter, boxA, boxB, cardVfoFunc, cardOptsA, cardOptsB, optsFunc, cardOptsRow3 };
            }
            return brickXSkip.Contains(c) || (c.Parent != null && (c.Parent == cardOptsRow3 || c.Parent == cardMeter || c.Parent == boxA || c.Parent == boxB || c.Parent == cardOptsA || c.Parent == cardOptsB));
        }

        private void brickApplyScale()
        {
            foreach (KeyValuePair<Control, int[]> kv in brickYOrig)
            {
                Control c = kv.Key;
                if (c.IsDisposed) continue;
                int t = kv.Value[0], h = kv.Value[1];
                bool container = c is Panel || c is GroupBox;
                int nh = container ? brickY(h) : (h <= 18 ? h : Math.Max(18, brickY(h)));
                if (!container && h > 18 && nh < h && !(c is BrickVfoBox)) nh = Math.Max(nh, Math.Min(h, c.Font.Height + 3)); // text must still fit
                int nt = brickY(t);
                int nl = c.Left, nw = c.Width;
                if (!brickSkipX(c))
                {
                    nl = brickX(kv.Value[2]); nw = brickX(kv.Value[3]);
                    // texts keep the width they need
                    if (!container && kv.Value[3] > 20 && (c is Label || c is ButtonBase) && !string.IsNullOrEmpty(c.Text))
                        nw = Math.Max(nw, Math.Min(kv.Value[3], TextRenderer.MeasureText(c.Text, c.Font).Width + 10));
                }
                if (c.Top != nt || c.Height != nh || c.Left != nl || c.Width != nw) c.SetBounds(nl, nt, nw, nh);
            }
            int topH = brickY(166);
            if (pnlTop.Height != topH) pnlTop.Height = topH;
            if (pnlLeft.Width != brickX(346)) pnlLeft.Width = brickX(346);
            if (pnlRight.Width != brickX(214)) pnlRight.Width = brickX(214);
        }

        private void brickRescale()
        {
            if (brickNeedH <= 0 || brickRoot == null) return;
            double k = Math.Min(1.0, Math.Max(BrickMinK, (double)brickRoot.ClientSize.Height / brickNeedH));
            double kx = Math.Min(1.0, Math.Max((BrickMinOuterW - (Width - ClientSize.Width)) / (double)BrickDesignW, (double)brickRoot.ClientSize.Width / BrickDesignW));
            if (Math.Abs(k - brickK) < 0.003 && Math.Abs(kx - brickKx) < 0.003) return;
            brickK = k; brickKx = kx;
            SuspendLayout();
            brickApplyScale();
            layoutTop();
            ResumeLayout(true);
            m_bResizeDX2Display = true;
        }

        // the window is never larger than the working area of the monitor it is on
        private void brickLimitToScreen(bool fit)
        {
            if (WindowState == FormWindowState.Minimized) return;
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            this.MaximumSize = new Size(Math.Max(wa.Width, MinimumSize.Width), Math.Max(wa.Height, MinimumSize.Height));
            if (!fit || WindowState != FormWindowState.Normal) return;
            int w = Math.Min(Width, MaximumSize.Width), h = Math.Min(Height, MaximumSize.Height);
            if (w != Width || h != Height) Size = new Size(w, h);
            int x = Math.Min(Math.Max(Left, wa.Left), Math.Max(wa.Left, wa.Right - Width));
            int y = Math.Min(Math.Max(Top, wa.Top), Math.Max(wa.Top, wa.Bottom - Height));
            if (x != Left || y != Top) Location = new Point(x, y);
        }

        private void brickBuildLayout()
        {
            if (brickActive) return;

            SuspendLayout();

            brickActive = true;

            // status bar: room for the texts, no size grip, items do not touch the frame
            statusStripMain.AutoSize = false;
            statusStripMain.SizingGrip = false;
            statusStripMain.Height = 30;
            statusStripMain.Padding = new Padding(8, 3, 8, 3);
            statusStripMain.GripStyle = ToolStripGripStyle.Hidden;
            foreach (ToolStripItem sit in statusStripMain.Items)
            {
                sit.Font = new Font("Segoe UI", 9f);
                sit.Margin = new Padding(2, 2, 2, 2);
                sit.AutoSize = true;
                sit.TextAlign = ContentAlignment.MiddleLeft;
            }

            // fixed pixel layout, do not let the old autoscale / resize code move things around
            this.AutoScaleMode = AutoScaleMode.None;
            this.BackColor = cBg;
            this.MinimumSize = new Size(1360, 900);
            // first start after the installation: no saved window yet
            bool brickFirstRun = true;
            try { brickFirstRun = !(File.Exists(DB.FileName) && DB.GetVarsDictionary("State").ContainsKey("console_width")); } catch { }

            // not used any more
            foreach (Control c in new Control[] {
                panelButtonBar, panelMeterLabels, panelVFOALabels, panelVFOBLabels, panelVFOLabels, panelAndromedaMisc,
                lblModeBigLabel, lblRX2ModeBigLabel, nudPwrTemp, nudPwrTemp2, chkDX, chkRX1Preamp, chkRX2Preamp,
                lblAF2, lblPWR2, lblRF2, panelOptions, panelPower, picSquelch, picRX2Squelch })
                if (c != null) c.Visible = false;

            brickRoot = new Panel();
            brickRoot.Dock = DockStyle.Fill;
            brickRoot.BackColor = cBg;
            this.Controls.Add(brickRoot);

            pnlTop = new Panel { Dock = DockStyle.Top, Height = 166, BackColor = cBg };
            pnlLeft = new Panel { Dock = DockStyle.Left, Width = 346, BackColor = cBg, AutoScroll = true };
            pnlRight = new Panel { Dock = DockStyle.Right, Width = 214, BackColor = cBg };
            pnlCenter = new Panel { Dock = DockStyle.Fill, BackColor = cBg, Padding = new Padding(8, 0, 8, 0) };
            brickRoot.Controls.Add(pnlLeft);
            brickRoot.Controls.Add(pnlRight);
            brickRoot.Controls.Add(pnlTop);
            brickRoot.Controls.Add(pnlCenter);
            pnlCenter.BringToFront();
            brickRoot.BringToFront();

            buildTop();
            buildLeft();
            buildRight();
            buildCenter();

            // the window cannot be made smaller than its content, so no scroll bars are needed
            pnlLeft.AutoScroll = false;
            int need = pnlTop.Height + Math.Max(brickLeftBottom, Math.Max(cardRx1Filter.Bottom, cardRx2Filter.Bottom)) + 10;
            int chrome = this.Height - this.ClientSize.Height;
            // the window never gets smaller than the layout needs (VFO B must not run into the S-meter)
            int chromeW = this.Width - this.ClientSize.Width;
            // small window: the vertical distances shrink (down to BrickMinK), the window may go down to about 1360 x 768
            brickNeedH = need;
            brickCaptureY(pnlTop); brickCaptureY(pnlLeft); brickCaptureY(pnlRight); brickCaptureY(pnlRx2View);
            this.MinimumSize = new Size(BrickMinOuterW, (int)Math.Ceiling(need * BrickMinK) + menuStrip1.Height + statusStripMain.Height + chrome);
            brickRoot.Resize += (s, e) => brickRescale();
            if (this.Height < this.MinimumSize.Height) this.Height = this.MinimumSize.Height;
            if (this.Width < this.MinimumSize.Width) this.Width = this.MinimumSize.Width;
            if (brickFirstRun)
            {
                // first start: the smallest size the layout allows, in the middle of the screen; afterwards the user's size and place are kept
                this.Size = this.MinimumSize;
                Rectangle wa = Screen.FromControl(this).WorkingArea;
                this.Location = new Point(wa.Left + Math.Max(0, (wa.Width - this.Width) / 2), wa.Top + Math.Max(0, (wa.Height - this.Height) / 2));
            }

            brickApplyTheme(brickRoot);
            brickRescale();
            brickLimitToScreen(true);
            this.LocationChanged += (s, e) => brickLimitToScreen(false);
            brickApplyTheme(statusStripMain);
            brickStyleMenu();
            if (!IsSetupFormNull) { SetupForm.BrickSimplifyNR(); SetupForm.BrickAddLineColouring(); SetupForm.BrickHookRadioTools(); }
            Common.UseImmersiveDarkMode(this.Handle, true);

            VFOTextLightColor = cAccent;
            VFOTextDarkColor = Color.FromArgb(120, 126, 140);
            SmallVFOColor = Color.FromArgb(200, 110, 30);
            BandTextLightColor = Color.FromArgb(70, 200, 110);
            BandTextDarkColor = Color.FromArgb(60, 120, 80);
            VFOBackgroundColor = cField;
            BandBackgroundColor = cField;

            chkSplitDisplay.Checked = RX2Enabled;
            BeginInvoke(new Action(brickDigitalRestore));      // the terminal or FreeDV strip that was open at the last exit

            // focus parking place: the old code moves the focus here after clicks, it must stay focusable (off screen)
            btnHidden.SetBounds(-1000, -1000, 1, 1);
            btnHidden.Visible = true;

            // spectrum background: a picture (default picDisplay.png) on a slightly lighter base
            Display.DisplayBackgroundColor = Color.FromArgb(Display.DisplayBackgroundColor.A, 24, 30, 42);
            brickLoadBackground();

            Display.LineColourMode = brickLineMode();

            // plain dark spectrum, no world map behind it
            DisableBackgroundImage = false;
            if (!IsSetupFormNull) SetupForm.chkDisablePicDisplayBackgroundImage.Checked = false;

            // the S-meter shows the averaged signal
            for (int i = 0; i < comboMeterRXMode.Items.Count; i++)
                if (comboMeterRXMode.Items[i].ToString().IndexOf("Avg", StringComparison.OrdinalIgnoreCase) >= 0 && comboMeterRXMode.Items[i].ToString().IndexOf("Sig", StringComparison.OrdinalIgnoreCase) >= 0)
                { comboMeterRXMode.SelectedIndex = i; break; }
            for (int i = 0; i < comboRX2MeterMode.Items.Count; i++)
                if (comboRX2MeterMode.Items[i].ToString().IndexOf("Avg", StringComparison.OrdinalIgnoreCase) >= 0 && comboRX2MeterMode.Items[i].ToString().IndexOf("Sig", StringComparison.OrdinalIgnoreCase) >= 0)
                { comboRX2MeterMode.SelectedIndex = i; break; }

            chkRX2.CheckedChanged += (s, e) => brickUpdateRx2Enabled();
            brickUpdateRx2Enabled();
            brickSyncRx2Band();

            // default tuning step 500 Hz
            int idx = TuneStepLookup("500Hz");
            if (idx >= 0) TuneStepIndex = idx;

            this.Resize += (s, e) => updateResolutionStatusBarText();
            updateResolutionStatusBarText();
            brickTimer = new System.Windows.Forms.Timer();
            brickTimer.Interval = 200;
            brickTimer.Tick += brickTick;
            brickTimer.Start();


            ResumeLayout(true);
            PerformLayout();
        }

        #endregion

        #region top

        private BrickVfoBox boxA, boxB;
        private BrickDropDown ddStepA, ddStepB;
        private Panel cardOptsRow3, optsFunc;

        private void buildTop()
        {
            // --- TX / RX
            cardTxRx = brickCard(pnlTop);
            cardTxRx.SetBounds(8, 6, 340, 154);
            brickSection(cardTxRx, "TX / RX", 6, cardTxRx.Width);
            chkPower.Image = null; chkRX2.Image = null;
            chkPower.Text = "POWER"; chkRX2.Text = "RX2"; chkMON.Text = "MON"; chkRX2SR.Text = "DUP";
            chkMOX.Text = "MOX"; chkTUN.Text = "TUNE"; chkMUT.Text = "MUTE";
            Grid(cardTxRx, 8, 26, 324, 3, 34, 6, chkPower, chkFWCATUBypass, chkTUN, chkRX2, chk2TONE, chkMOX, chkMON, chkMUT, chkRX2SR);
            chkMUT.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) brickSetMuteAll(!brickMuteAll); };
            chkMUT.CheckedChanged += (s, e) => { if (!chkMUT.Checked && brickMuteAll) { brickPrevMut = false; brickSetMuteAll(false); } };
            chkExternalPA.Visible = false;
            comboTuneMode.Visible = false; ckQuickRec.Visible = false; ckQuickPlay.Visible = false;

            // --- VFO A / VFO B, drawn by the main window itself
            grpVFOA.Visible = false; grpVFOB.Visible = false; grpVFOBetween.Visible = false;
            boxA = new BrickVfoBox();
            boxA.Title = "VFO A";
            boxA.GetFreq = () => VFOAFreq;
            boxA.SetFreq = v => { if (!_mox) VFOAFreq = v; };
            boxA.IsActive = () => true;
            boxA.IsTransmitting = () => _mox && VFOATX;
            boxA.SetBounds(356, 6, 244, 92);
            pnlTop.Controls.Add(boxA);
            Put(txtVFOABand, boxA, 8, 68, 190, 20);
            Put(chkVFOATX, boxA, 204, 68, 32, 20);
            brickBandStrip(txtVFOABand);
            chkVFOLock.Appearance = Appearance.Button; chkVFOLock.Text = "A";
            Put(chkVFOLock, boxA, 212, 30, 26, 26);

            boxB = new BrickVfoBox();
            boxB.Title = "VFO B";
            boxB.GetFreq = () => VFOBFreq;
            boxB.SetFreq = v => { if (!(_mox && VFOBTX)) VFOBFreq = v; };
            boxB.IsActive = () => chkRX2.Checked || VFOBTX;
            boxB.IsTransmitting = () => _mox && VFOBTX;
            boxB.SetBounds(842, 6, 244, 92);
            pnlTop.Controls.Add(boxB);
            Put(txtVFOBBand, boxB, 8, 68, 190, 20);
            Put(chkVFOBTX, boxB, 204, 68, 32, 20);
            brickBandStrip(txtVFOBBand);
            chkVFOBLock.Appearance = Appearance.Button; chkVFOBLock.Text = "B";
            Put(chkVFOBLock, boxB, 212, 30, 26, 26);
            lblRX2ModeLabel.Visible = false; lblRX2FilterLabel.Visible = false; lblRX2MuteVFOB.Visible = false; lblRX2APF.Visible = false;

            ddStepA = brickStepDropDown(boxA);
            ddStepB = brickStepDropDown(boxB);

            // --- VFO functions
            cardVfoFunc = brickCard(pnlTop);
            cardVfoFunc.SetBounds(608, 6, 226, 92);
            brickLabel(cardVfoFunc, "VFO", 7.5f, cMuted, true, 8, 2, 100, 14);
            chkVFOSync.Appearance = Appearance.Button; chkVFOSync.Text = "SYNC";
            chkEnableMultiRX.Appearance = Appearance.Button; chkEnableMultiRX.Text = "SUB-RX";
            chkPanSwap.Appearance = Appearance.Button; chkPanSwap.Text = "PAN";
            chkVFOSplit.Text = "SPLIT"; chkRIT.Text = "RIT"; chkXIT.Text = "XIT";
            btnVFOAtoB.Text = "A > B"; btnVFOSwap.Text = "A <> B"; btnVFOBtoA.Text = "B > A";
            Grid(cardVfoFunc, 8, 18, 210, 3, 22, 3,
                btnVFOAtoB, btnVFOSwap, btnVFOBtoA, chkVFOSplit, chkVFOSync, chkEnableMultiRX);
            Put(chkRIT, cardVfoFunc, 8, 68, 44, 22);
            Button ritArrow = new Button { Text = "\u25BE" };
            ritArrow.SetBounds(54, 68, 22, 22);
            ritArrow.Click += (s, e) => brickShowOffsetPopup(ritArrow, "RIT", chkRIT, udRIT, btnRITReset);
            cardVfoFunc.Controls.Add(ritArrow);
            Button panBtn = brickPanButton();
            panBtn.SetBounds(84, 68, 58, 22);
            cardVfoFunc.Controls.Add(panBtn);
            Put(chkXIT, cardVfoFunc, 150, 68, 44, 22);
            Button xitArrow = new Button { Text = "\u25BE" };
            xitArrow.SetBounds(196, 68, 22, 22);
            xitArrow.Click += (s, e) => brickShowOffsetPopup(xitArrow, "XIT", chkXIT, udXIT, btnXITReset);
            cardVfoFunc.Controls.Add(xitArrow);
            chkPanSwap.Visible = false;
            btnIFtoVFO.Visible = false; btnZeroBeat.Visible = false;
            panelVFO.Visible = false;

            // --- panadapter settings and RIT / XIT, second row
            cardOptsA = brickPanel(pnlTop, cBg);
            cardOptsA.SetBounds(356, 102, 244, 28);
            foreach (CheckBox cb in new CheckBox[] { chkDisplayPeak, chkDisplayAVG, chkFWCATU, chkRX2DisplayPeak, chkRX2DisplayAVG, chkX2TR }) cb.Appearance = Appearance.Button;
            chkDisplayPeak.Text = "Peak"; chkDisplayAVG.Text = "AVG"; chkFWCATU.Text = "CTUN";
            chkRX2DisplayPeak.Text = "Peak"; chkRX2DisplayAVG.Text = "AVG"; chkX2TR.Text = "CTUN";
            Put(chkDisplayPeak, cardOptsA, 0, 3, 76, 24);
            Put(chkDisplayAVG, cardOptsA, 82, 3, 76, 24);
            Put(chkFWCATU, cardOptsA, 164, 3, 76, 24);

            optsFunc = brickPanel(pnlTop, cBg);
            optsFunc.SetBounds(608, 102, 226, 28);
            foreach (Control c0 in new Control[] { udRIT, btnRITReset, udXIT, btnXITReset }) { optsFunc.Controls.Add(c0); c0.Visible = false; }

            cardOptsB = brickPanel(pnlTop, cBg);
            cardOptsB.SetBounds(842, 102, 244, 28);
            Put(chkRX2DisplayPeak, cardOptsB, 0, 3, 76, 24);
            Put(chkRX2DisplayAVG, cardOptsB, 82, 3, 76, 24);
            Put(chkX2TR, cardOptsB, 164, 3, 76, 24);
            if (!IsSetupFormNull) SetupForm.BrickAddDisplayModes(comboDisplayMode, comboRX2DisplayMode);
            panelRX2Display.Visible = false;
            panelDisplay2.Visible = false;

            // --- pan and zoom, third row
            Panel row3 = brickPanel(pnlTop, cBg);
            row3.SetBounds(356, 132, 730, 28);
            cardOptsRow3 = row3;
            Put(lblDisplayPan, row3, 0, 3, 30, 22);
            Put(ptbDisplayPan, row3, 30, 3, 150, 22);
            Put(btnDisplayPanCenter, row3, 184, 3, 58, 22);
            Put(lblDisplayZoom, row3, 252, 3, 42, 22);
            Put(ptbDisplayZoom, row3, 294, 3, 130, 22);
            Put(radDisplayZoom05, row3, 430, 3, 44, 22);
            Put(radDisplayZoom1x, row3, 477, 3, 44, 22);
            Put(radDisplayZoom2x, row3, 524, 3, 44, 22);
            Put(radDisplayZoom4x, row3, 571, 3, 44, 22);
            Put(btnDisplayZTB, row3, 618, 3, 44, 22);
            chk3D1 = new CheckBox { Name = "brick3D1", Text = "3D", Appearance = Appearance.Button };
            chk3D1.SetBounds(668, 3, 40, 22);
            row3.Controls.Add(chk3D1);
            chk3D1.CheckedChanged += (s, e) => { Display.Use3DRX1 = chk3D1.Checked; brickSave3D(); };
            // --- S-meters, the second one only while RX2 runs
            cardMeter = brickCard(pnlTop);
            cardMeter.SetBounds(1094, 6, 372, 154);
            grpMultimeter.Visible = false; grpMultimeterMenus.Visible = false;
            txtMultiText.Visible = false; pnlResizeMeter.Visible = false;
            grpRX2Meter.Visible = false;
            Put(picMultiMeterDigital, cardMeter, 6, 6, 360, 126);
            picMultiMeterDigital.BackColor = cCard;
            Put(picRX2Meter, cardMeter, 6, 72, 360, 60);
            picRX2Meter.BackColor = cCard;
            // feedback and PureSignal indicators live under the S-meter
            // the originals keep working inside a hidden host with the same coordinates
            Panel fbHost = new Panel { Visible = false, Bounds = infoBar.ClientRectangle };
            infoBar.Controls.Add(fbHost);
            fbHost.Controls.Add(infoBar.FeedbackLabel);
            fbHost.Controls.Add(infoBar.PureSignalLabel);
            lblFbMirror = brickMirror(cardMeter, 8, 135, 112, 17);
            lblPsMirror = brickMirror(cardMeter, 126, 135, 112, 17);
            layoutTop();

            pnlTop.Resize += (s, e) => layoutTop();
        }

        private Font brickBandFont = new Font("Segoe UI Semibold", 11f);
        private Label lblFbMirror, lblPsMirror;

        // ---- colouring of the spectrum line (0 standard, 1 waterfall colours, 2 noise standard + signals waterfall)
        private string BrickLineFile { get { return Path.Combine(AppDataPath, "line_colours.txt"); } }
        private int brickLineMode()
        {
            try { int m; if (File.Exists(BrickLineFile) && int.TryParse(File.ReadAllText(BrickLineFile).Trim(), out m) && m >= 0 && m <= 3) return m; } catch { }
            return 1;
        }
        public int BrickLineMode { get { return brickLineMode(); } }
        public void BrickSetLineMode(int m)
        {
            try { File.WriteAllText(BrickLineFile, m.ToString()); } catch { }
            Display.LineColourMode = m;
        }

        // ---- spectrum background picture
        private string BrickBackgroundSettingFile { get { return Path.Combine(AppDataPath, "spectrum_background.txt"); } }

        public string BrickBackgroundPath
        {
            get
            {
                try { if (File.Exists(BrickBackgroundSettingFile)) { string s = File.ReadAllText(BrickBackgroundSettingFile).Trim(); if (s.Length > 0) return s; } } catch { }
                return Path.Combine(Application.StartupPath, "picDisplay.png");
            }
        }

        public void BrickSetBackground(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) { if (File.Exists(BrickBackgroundSettingFile)) File.Delete(BrickBackgroundSettingFile); }
                else File.WriteAllText(BrickBackgroundSettingFile, path);
            }
            catch { }
            brickLoadBackground();
        }

        private void brickLoadBackground()
        {
            string path = BrickBackgroundPath;
            if (!File.Exists(path)) path = Path.Combine(Application.StartupPath, "picDisplay.png");
            if (!File.Exists(path)) return;
            try
            {
                using (Image src = Image.FromFile(path))
                {
                    // tall pictures are cut to a wide band, the display is much wider than high
                    Bitmap bmp;
                    if (src.Height > src.Width * 0.7)
                    {
                        int h = (int)(src.Width * 0.5625);
                        int y = Math.Max(0, (int)(src.Height * 0.44) - h / 2);
                        if (y + h > src.Height) y = src.Height - h;
                        bmp = new Bitmap(src.Width, h);
                        using (Graphics g = Graphics.FromImage(bmp))
                            g.DrawImage(src, new Rectangle(0, 0, bmp.Width, bmp.Height), new Rectangle(0, y, src.Width, h), GraphicsUnit.Pixel);
                    }
                    else bmp = new Bitmap(src);
                    PnlDisplayBackgroundImage = bmp;
                }
                DisableBackgroundImage = false;
            }
            catch { }
        }

        // a click on the power scale numbers lets the full scale be changed
        private void brickMeterMouseDown(object sender, MouseEventArgs e)
        {
            if (_mox && !brickPwrLabelRect.IsEmpty && brickPwrLabelRect.Contains(e.Location)) brickAskPowerMax();
        }

        private void brickBandStrip(TextBox t)
        {
            t.Font = brickBandFont;
            t.TextAlign = HorizontalAlignment.Center;
        }

        private void brickKeepBandFont(TextBox t)
        {
            if (t.Font.Size != brickBandFont.Size || t.Font.Name != brickBandFont.Name) t.Font = brickBandFont;
            if (t.TextAlign != HorizontalAlignment.Center) t.TextAlign = HorizontalAlignment.Center;
        }

        private void brickMirrorLabel(Label mine, Control orig)
        {
            if (mine == null || orig == null) return;
            if (mine.Text != orig.Text) mine.Text = orig.Text;
            if (mine.BackColor != orig.BackColor) mine.BackColor = orig.BackColor;
            if (mine.ForeColor != orig.ForeColor) mine.ForeColor = orig.ForeColor;
        }

        private Label brickMirror(Control parent, int x, int y, int w, int h)
        {
            Label l = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI Semibold", 8.5f), BackColor = Color.DimGray, ForeColor = Color.Black };
            l.SetBounds(x, y, w, h);
            l.SizeChanged += (s, e) =>
            {
                using (System.Drawing.Drawing2D.GraphicsPath gp = brickRoundPath(new Rectangle(0, 0, l.Width, l.Height), 5))
                    l.Region = new Region(gp);
            };
            l.Size = new Size(w, h + 1); l.Size = new Size(w, h);
            parent.Controls.Add(l);
            return l;
        }

        private Panel pnlRx2View;
        private CheckBox chk3D1, chk3D2;
        private string Brick3DFile { get { return Path.Combine(AppDataPath, "display3d.txt"); } }
        private void brickSave3D() { try { File.WriteAllText(Brick3DFile, (chk3D1 != null && chk3D1.Checked ? "1" : "0") + (chk3D2 != null && chk3D2.Checked ? "1" : "0")); } catch { } }
        private void brickLoad3D() { try { if (!File.Exists(Brick3DFile)) return; string t = File.ReadAllText(Brick3DFile).Trim(); if (t.Length >= 2) { chk3D1.Checked = t[0] == '1'; chk3D2.Checked = t[1] == '1'; } } catch { } }
        private readonly List<CheckBox> rx2ZoomButtons = new List<CheckBox>();
        private int rx2ZoomValue = -1;

        private void applyRx2View(PrettyTrackBar pan, PrettyTrackBar zoom)
        {
            rx2ZoomValue = zoom.Value;
            foreach (CheckBox b in rx2ZoomButtons) b.Checked = (int)b.Tag == zoom.Value;
            specRX.GetSpecRX(1).PanSlider = (double)pan.Value / 1000.0;
            specRX.GetSpecRX(1).ZoomSlider = ((double)zoom.Value - 10.0) / 230.0;
            CalcRX2DisplayFreq();
        }

        private BrickDropDown brickStepDropDown(Control parent)
        {
            BrickDropDown dd = new BrickDropDown();
            foreach (TuneStep ts in TuneStepList) dd.Items.Add(ts.Name);
            dd.SetBounds(parent.Width - 84, 2, 78, 18);
            dd.SelectedIndex = tune_step_index;
            dd.SelectedChanged += (s, e) =>
            {
                if (brickSyncingStep) return;
                if (dd.SelectedIndex >= 0) TuneStepIndex = dd.SelectedIndex;
            };
            parent.Controls.Add(dd);
            dd.BringToFront();
            TuneStepIndexChangedHandlers += (rx, o, n) => brickSyncStepCombos(n);
            return dd;
        }

        private void brickSyncStepCombos(int index)
        {
            brickSyncingStep = true;
            try
            {
                if (ddStepA != null) ddStepA.SelectedIndex = index;
                if (ddStepB != null) ddStepB.SelectedIndex = index;
            }
            finally { brickSyncingStep = false; }
        }

        // wheel over a digit of a VFO display tunes that digit
        private bool brickVfoWheel(MouseEventArgs e)
        {
            Point p = Cursor.Position;
            if (boxA != null && boxA.Visible && boxA.RectangleToScreen(boxA.ClientRectangle).Contains(p)) return boxA.Wheel(e.Delta);
            if (boxB != null && boxB.Visible && boxB.RectangleToScreen(boxB.ClientRectangle).Contains(p)) return boxB.Wheel(e.Delta);
            return false;
        }

        private void layoutTop()
        {
            if (cardMeter == null) return;
            int w = pnlTop.ClientSize.Width;
            // the S-meter keeps its size at the right edge, the two VFO blocks share what is left
            int x0 = brickX(356), fw = brickX(226);
            int meterW = Math.Max(brickX(300), Math.Min(brickX(372), w - (x0 + fw + 32) - 2 * brickX(223)));
            picMultiMeterDigital.MouseDown -= brickMeterMouseDown;
            picMultiMeterDigital.MouseDown += brickMeterMouseDown;
            cardMeter.SetBounds(Math.Max(850, w - 8 - meterW), 6, meterW, 154);
            int aw = Math.Max(200, (cardMeter.Left - 8 - x0 - fw - 16) / 2);
            boxA.SetBounds(x0, 6, aw, 92);
            cardVfoFunc.SetBounds(boxA.Right + 8, 6, fw, 92);
            boxB.SetBounds(cardVfoFunc.Right + 8, 6, aw, 92);
            foreach (BrickVfoBox bx in new BrickVfoBox[] { boxA, boxB })
            {
                bx.Controls.Cast<Control>().ToList().ForEach(c => { });
            }
            txtVFOABand.SetBounds(8, 68, aw - 60, 20); chkVFOATX.SetBounds(aw - 44, 68, 32, 20); chkVFOLock.SetBounds(aw - 32, 30, 26, 26);
            txtVFOBBand.SetBounds(8, 68, aw - 60, 20); chkVFOBTX.SetBounds(aw - 44, 68, 32, 20); chkVFOBLock.SetBounds(aw - 32, 30, 26, 26);
            ddStepA.Left = aw - 84; ddStepB.Left = aw - 84;
            cardOptsA.SetBounds(x0, 102, aw, 28);
            optsFunc.SetBounds(cardVfoFunc.Left, 102, fw, 28);
            cardOptsB.SetBounds(boxB.Left, 102, aw, 28);
            {
                int bw = (aw - 8) / 3;
                Control[] ra = { chkDisplayPeak, chkDisplayAVG, chkFWCATU }, rb = { chkRX2DisplayPeak, chkRX2DisplayAVG, chkX2TR };
                for (int k = 0; k < 3; k++) { ra[k].SetBounds(k * (bw + 4), 3, bw, 24); rb[k].SetBounds(k * (bw + 4), 3, bw, 24); }
            }
            cardOptsRow3.SetBounds(x0, 132, cardMeter.Left - 8 - x0, 28);
            {
                // pan / zoom row: when the row is short the two sliders give room
                int avail = cardOptsRow3.Width;
                int panW = brickX(150), zoomW = brickX(130), deficit = Math.Max(0, brickX(712) - avail);
                int cut = Math.Min((deficit + 1) / 2, panW - 60); panW -= cut; deficit -= cut;
                cut = Math.Min(deficit, zoomW - 60); zoomW -= cut;
                int xx = 0;
                Action<Control, int> put = (c, wd) => { c.SetBounds(xx, c.Top, wd, c.Height); xx += wd; };
                put(lblDisplayPan, brickX(30)); put(ptbDisplayPan, panW); xx += brickX(4); put(btnDisplayPanCenter, brickX(58)); xx += brickX(10);
                put(lblDisplayZoom, brickX(42)); put(ptbDisplayZoom, zoomW); xx += brickX(6);
                foreach (Control zc in new Control[] { radDisplayZoom05, radDisplayZoom1x, radDisplayZoom2x, radDisplayZoom4x, btnDisplayZTB }) { put(zc, brickX(44)); xx += brickX(3); }
                xx += brickX(3);
                put(chk3D1, brickX(40));
            }
            bool two = chkRX2 != null && chkRX2.Checked && !_mox;
            picMultiMeterDigital.SetBounds(6, brickY(4), meterW - 12, brickY(two ? 74 : 128));
            picRX2Meter.SetBounds(6, brickY(80), meterW - 12, brickY(52));
            picRX2Meter.Visible = two;
            picMultiMeterDigital.Invalidate();
            if (brickYOrig.Count > 0) brickApplyScale();
        }

        #endregion

        #region left

        // only boards with a second ADC / attenuator get an RX2 attenuator control
        private bool brickHasRx2Att()
        {
            switch (HardwareSpecific.Model)
            {
                case HPSDRModel.HPSDR:
                case HPSDRModel.HERMES:
                case HPSDRModel.ANAN10:
                case HPSDRModel.ANAN10E:
                case HPSDRModel.ANAN100:
                case HPSDRModel.ANAN100B:
                case HPSDRModel.HERMESLITE:
                case HPSDRModel.ANAN_G2E:
                    return false;
            }
            return true;
        }

        private bool brickMuteAll, brickPrevMut, brickPrevMut2, brickSlidersUnmute, brickPrevVac1, brickPrevVac2;
        private Band brickLastTxBand = Band.FIRST;
        // right click on MUTE: everything off, the button turns red
        private void brickSetMuteAll(bool on)
        {
            if (on == brickMuteAll) return;
            if (on)
            {
                brickPrevMut = chkMUT.Checked; brickPrevMut2 = chkRX2Mute.Checked;
                brickPrevVac1 = m_bMuteWillMuteVAC1; brickPrevVac2 = m_bMuteWillMuteVAC2;
                brickMuteAll = true;
                chkMUT.Checked = true;
                if (RX2Enabled) chkRX2Mute.Checked = true;
                // the VAC outputs go quiet too
                MuteWillMuteVAC1 = true; MuteWillMuteVAC2 = true;
                Audio.MonitorVolume = 0.0;
            }
            else
            {
                brickMuteAll = false;
                MuteWillMuteVAC1 = brickPrevVac1; MuteWillMuteVAC2 = brickPrevVac2;
                chkMUT.Checked = brickPrevMut;
                chkRX2Mute.Checked = brickPrevMut2;
                ptbAF_Scroll(this, EventArgs.Empty);
            }
            chkMUT.Invalidate();
        }
        private bool brickLastMox;
        private int brickLeftBottom;
        private readonly List<Control> rx2OnlyControls = new List<Control>();

        private void buildLeft()
        {
            int w = pnlLeft.Width;
            int cw = w - 14; // card width
            int y = 4;

            // ---- RECEIVE: RX1 on the left, RX2 on the right
            Panel rx = brickCard(pnlLeft);
            rx.SetBounds(8, y, cw, 244);
            brickLabel(rx, "RECEIVE 1", 7.5f, cMuted, true, 8, 6, 140, 14);
            brickLabel(rx, "RECEIVE 2", 7.5f, cMuted, true, 176, 6, 140, 14);
            int half = 156;
            int ry = 22;
            brickSliderAt(rx, lblAF, ptbAF, 8, ry, half);
            brickSliderAt(rx, lblRX1AF, ptbRX1AF, 8, ry + 38, half);
            brickSliderAt(rx, lblRF, ptbRF, 8, ry + 76, half);
            Put(chkSquelch, rx, 8, ry + 114, 72, 20);
            Put(ptbSquelch, rx, 8, ry + 134, half, 22);
            brickAgcButtons(rx, lblAGC, comboAGC, 8, ry + 160);

            brickSliderAt(rx, lblRX2AF, ptbRX2AF, 176, ry + 38, half);
            brickSliderAt(rx, lblRX2RF, ptbRX2RF, 176, ry + 76, half);
            Put(chkRX2Squelch, rx, 176, ry + 114, 72, 20);
            Put(ptbRX2Squelch, rx, 176, ry + 134, half, 22);
            List<Control> agc2 = brickAgcButtons(rx, lblRX2AGC, comboRX2AGC, 176, ry + 160);

            // pan of the receivers is no longer shown
            foreach (Control pc in new Control[] { ptbPanMainRX, ptbRX2Pan, chkPanSwap, ptbPanSubRX })
            {
                Control c0 = pc;
                c0.Visible = false;
                c0.VisibleChanged += (s, e) => { if (c0.Visible) c0.Visible = false; };
            }

            // mix of the two receivers: centre = both as set, left fades RX2, right fades RX1
            int mixY = ry;
            Label mixLbl = brickLabel(rx, "Mix  RX1 ◄► RX2", 8.5f, cText, false, 176, mixY, 106, 15);
            Label mixVal = brickLabel(rx, "Center", 8.5f, cAccent, true, 282, mixY, 50, 15);
            mixVal.TextAlign = ContentAlignment.TopRight; mixVal.Visible = false;
            Button mixCtr = new Button { Text = "CTR", Font = new Font("Segoe UI Semibold", 7f) };
            mixCtr.SetBounds(298, mixY - 2, 34, 18);
            rx.Controls.Add(mixCtr);
            PrettyTrackBar mix = new PrettyTrackBar { Minimum = 0, Maximum = 100, Value = 50, HeadImage = ptbAF.HeadImage };
            mix.SetBounds(176, mixY + 16, half, 22);
            rx.Controls.Add(mix);
            double base1 = ptbRX1AF.Value, base2 = ptbRX2AF.Value;
            bool mixing = false;
            Func<double, double> fade1 = m => Math.Min(1.0, 2.0 * (1.0 - m / 100.0));
            Func<double, double> fade2 = m => Math.Min(1.0, 2.0 * m / 100.0);
            Action applyMix = () =>
            {
                mixing = true;
                double m = mix.Value;
                ptbRX1AF.Value = Math.Max(ptbRX1AF.Minimum, Math.Min(ptbRX1AF.Maximum, (int)Math.Round(base1 * fade1(m))));
                ptbRX1AF_Scroll(this, EventArgs.Empty);
                ptbRX2AF.Value = Math.Max(ptbRX2AF.Minimum, Math.Min(ptbRX2AF.Maximum, (int)Math.Round(base2 * fade2(m))));
                ptbRX2AF_Scroll(this, EventArgs.Empty);
                mixVal.Text = mix.Value == 50 ? "Center" : (mix.Value < 50 ? "RX1 " + (100 - mix.Value) + " %" : "RX2 " + mix.Value + " %");
                mixing = false;
            };
            mix.Scroll += (s, e) => applyMix();
            mix.DoubleClick += (s, e) => { mix.Value = 50; applyMix(); };
            mixCtr.Click += (s, e) => { mix.Value = 50; applyMix(); };
            // moving an AF slider by hand changes the base the mix works from
            ptbRX1AF.Scroll += (s, e) => { if (!mixing) { double f = fade1(mix.Value); base1 = f > 0.05 ? ptbRX1AF.Value / f : ptbRX1AF.Value; } };
            ptbRX2AF.Scroll += (s, e) => { if (!mixing) { double f = fade2(mix.Value); base2 = f > 0.05 ? ptbRX2AF.Value / f : ptbRX2AF.Value; } };
            rx2OnlyControls.AddRange(new Control[] { mixLbl, mixVal, mix, mixCtr });
            rx2OnlyControls.AddRange(new Control[] { lblRX2AF, ptbRX2AF, lblRX2RF, ptbRX2RF, chkRX2Squelch, ptbRX2Squelch, lblRX2AGC });
            rx2OnlyControls.AddRange(agc2);
            lblRX2AF.Visible = true; ptbRX2AF.Visible = true;
            y = rx.Bottom + 6;

            // ---- TRANSMIT
            Panel tx = brickCard(pnlLeft);
            tx.SetBounds(8, y, cw, 64);
            int ty = brickSection(tx, "Transmit", 6, tx.Width);
            brickSliderAt(tx, lblPWR, ptbPWR, 8, ty, 156);
            brickSliderAt(tx, lblTune, ptbTune, 176, ty, 156);
            lblTune.TextChanged += (s, e) => { if (lblTune.Text.StartsWith("Tu | 2T")) lblTune.Text = "Tune" + lblTune.Text.Substring(7); };
            // the old code hides the tune slider unless it is in use: keep it on screen, a highlight shows when it is active
            foreach (Control tc in new Control[] { lblTune, ptbTune })
            {
                Control t0 = tc;
                t0.Visible = true;
                t0.VisibleChanged += (s, e) => { if (!t0.Visible) t0.Visible = true; };
            }
            lblPAProfile.Visible = false;
            foreach (Control c in new Control[] { comboTXProfile, comboAMTXProfile, comboFMTXProfile, comboDigTXProfile, lblTransmitProfile, labelTS7, lblDigTXProfile })
                c.Visible = false;
            y = tx.Bottom + 6;

            // ---- DSP
            Panel dsp = brickCard(pnlLeft);
            dsp.SetBounds(8, y, cw, 120);
            brickLabel(dsp, "DSP", 7.5f, cMuted, true, 8, 6, 140, 14);
            brickLabel(dsp, "RX2", 7.5f, cMuted, true, 176, 6, 140, 14);
            panelDSP.Visible = false;
            Grid(dsp, 8, 22, 156, 3, 26, 5, chkNR, chkNB, chkANF, chkDSPNB2, chkBIN, chkTNF, btnTNFAdd, chkVAC1);
            Grid(dsp, 176, 22, 156, 3, 26, 5, chkRX2NR, chkRX2NB, chkRX2ANF, chkRX2NB2, chkRX2BIN, chkRX2Mute, chkVAC2);
            rx2OnlyControls.AddRange(new Control[] { chkRX2NR, chkRX2NB, chkRX2ANF, chkRX2NB2, chkRX2BIN, chkRX2Mute, chkVAC2 });
            panelRX2DSP.Visible = false;
            y = dsp.Bottom + 6;

            // ---- INPUT
            Panel inp = brickCard(pnlLeft);
            inp.SetBounds(8, y, cw, 82);
            brickSection(inp, "Input", 6, inp.Width);
            int iy = 22;
            lblPreamp.Visible = false; lblRX2Preamp.Visible = false;
            chkRxAnt.Visible = false;
            pbAutoAttWarningRX1.Visible = false; pbAutoAttWarningRX2.Visible = false;
            brickAttBlock(inp, 8, iy, comboPreamp, udRX1StepAttData,
                () => !IsSetupFormNull && SetupForm.RX1EnableAtt, v => { if (!IsSetupFormNull) SetupForm.RX1EnableAtt = v; }, false);
            List<Control> att2 = brickAttBlock(inp, 115, iy, comboRX2Preamp, udRX2StepAttData,
                () => !IsSetupFormNull && SetupForm.RX2EnableAtt, v => { if (!IsSetupFormNull) SetupForm.RX2EnableAtt = v; }, true, brickHasRx2Att);
            rx2OnlyControls.AddRange(att2);
            CheckBox txMode = new CheckBox { Appearance = Appearance.Button, AutoCheck = false, Text = "TX ATT", Enabled = false };
            txMode.Tag = "teal"; txMode.SetBounds(222 + 17, iy, 70, 22);
            inp.Controls.Add(txMode);
            brickStepper(inp, udTXStepAttData, 222, iy + 26, true);
            y = inp.Bottom + 6;

            // ---- transmitter settings of the current mode
            Panel mp = brickCard(pnlLeft);
            mp.SetBounds(8, y, cw, 172);
            brickLeftBottom = mp.Bottom;
            foreach (Panel p in new Panel[] { panelModeSpecificPhone, panelModeSpecificCW, panelModeSpecificDigital, panelModeSpecificFM })
            {
                p.BackColor = cCard;
                Put(p, mp, 1, 2, 336, 168);
            }

            brickStyleModePanels();
            panelSoundControls.Visible = false;
            panelMultiRX.Visible = false;
            panelRX2RF.Visible = false; panelRX2Mixer.Visible = false; panelRX2Power.Visible = false; panelRX2Mode.Visible = false; panelRX2Filter.Visible = false;
            grpDisplaySplit.Visible = false; chkFullDuplex.Visible = false; picRX2Squelch.Visible = false;
        }

        private readonly List<Action> brickAgcSyncs = new List<Action>();

        // one receiver input: a quick-click ATT / S-ATT switch above the control,
        // ATT = drop-down (0 / -10 / -20 / -30), S-ATT = stepper 0..31
        private List<Control> brickAttBlock(Control parent, int x, int y, ComboBox combo, NumericUpDown nud, Func<bool> getStep, Action<bool> setStep, bool rx2, Func<bool> present = null)
        {
            List<Control> made = new List<Control>();
            combo.Location = new Point(-3000, 0);
            nud.Parent = parent; nud.Location = new Point(-3000, 0);

            CheckBox mode = new CheckBox { Appearance = Appearance.Button, AutoCheck = false, Text = "ATT" };
            mode.SetBounds(x + 17, y, 70, 22);
            mode.Tag = "teal";
            mode.Click += (s, e) => setStep(!getStep());
            parent.Controls.Add(mode); made.Add(mode);

            BrickDropDown dd = new BrickDropDown();
            List<int> map = new List<int>();   // drop-down entry -> combo index
            string sig = "";
            dd.SetBounds(x, y + 26, 104, 24);
            dd.SelectedChanged += (s, e) => { if (dd.SelectedIndex >= 0 && dd.SelectedIndex < map.Count && combo.SelectedIndex != map[dd.SelectedIndex]) combo.SelectedIndex = map[dd.SelectedIndex]; };
            parent.Controls.Add(dd); made.Add(dd);

            List<Control> st = brickStepper(parent, nud, x, y + 26, true);
            made.AddRange(st);

            brickAgcSyncs.Add(() =>
            {
                if (present != null && !present())
                {
                    foreach (Control c in made) if (c.Visible) c.Visible = false;
                    return;
                }
                if (!mode.Visible) mode.Visible = true;
                bool step = getStep();
                string t = step ? "S-ATT" : "ATT";
                if (mode.Text != t) mode.Text = t;
                if (mode.Checked != step) mode.Checked = step;
                if (dd.Visible == step) dd.Visible = !step;
                foreach (Control c in st) if (c.Visible != step) c.Visible = step;
                if (dd.Visible == step) dd.Visible = !step;
                // only 0, -10, -20 and -30 dB are offered
                string now = combo.Items.Count.ToString();
                for (int i = 0; i < combo.Items.Count; i++) now += "|" + combo.Items[i];
                if (now != sig)
                {
                    sig = now;
                    map.Clear();
                    List<string> names = new List<string>();
                    foreach (int want in new int[] { 0, -10, -20, -30 })
                    {
                        int found = -1;
                        for (int i = 0; i < combo.Items.Count; i++)
                        {
                            int v;
                            string tt = combo.Items[i].ToString().ToLowerInvariant().Replace("db", "").Trim();
                            if (int.TryParse(tt, out v) && v == want) { found = i; if (want == 0) break; }
                        }
                        if (found >= 0) { map.Add(found); names.Add(want + " dB"); }
                    }
                    dd.SetItems(names);
                }
                int cur = map.IndexOf(combo.SelectedIndex);
                if (cur >= 0 && dd.SelectedIndex != cur) dd.SelectedIndex = cur;
            });
            return made;
        }

        // attenuator / preamp choice as buttons; the combo box moves out of sight but keeps working
        private List<Control> brickPreampButtons(Control parent, ComboBox combo, int x, int y)
        {
            List<Control> made = new List<Control>();
            combo.Location = new Point(-3000, 0);
            List<CheckBox> btns = new List<CheckBox>();
            int n = combo.Items.Count;
            int bw = n > 0 ? Math.Min(40, 124 / n) : 30;
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                CheckBox cb = new CheckBox { Appearance = Appearance.Button, AutoCheck = false, Tag = combo.Items[i].ToString() };
                cb.Text = combo.Items[i].ToString().Replace("dB", "").Replace("db", "").Trim();
                cb.SetBounds(x + i * (bw + 2), y, bw, 22);
                cb.Click += (s, e) => { if (combo.SelectedIndex != idx) combo.SelectedIndex = idx; };
                parent.Controls.Add(cb);
                btns.Add(cb);
                made.Add(cb);
            }
            brickAgcSyncs.Add(() =>
            {
                for (int i = 0; i < btns.Count; i++)
                {
                    btns[i].Checked = (string)btns[i].Tag == combo.Text;
                    btns[i].Visible = combo.Visible;
                    if (combo.Enabled != btns[i].Enabled) btns[i].Enabled = combo.Enabled;
                }
            });
            return made;
        }

        // step attenuator as  -  value  +  buttons; the numeric box stays out of sight and does the work
        private List<Control> brickStepper(Control parent, NumericUpDown nud, int x, int y, bool always = false)
        {
            List<Control> made = new List<Control>();
            nud.Parent = parent;
            nud.Location = new Point(-3000, 0);
            Button minus = new Button { Text = "\u2212" };
            Button plus = new Button { Text = "+" };
            Label val = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI Semibold", 10f), ForeColor = cAccent };
            minus.SetBounds(x, y, 28, 24);
            val.SetBounds(x + 30, y, 44, 24);
            plus.SetBounds(x + 76, y, 28, 24);
            decimal limit = Math.Min(31, nud.Maximum);
            int dir = 0;
            System.Windows.Forms.Timer rep = new System.Windows.Forms.Timer { Interval = 350 };
            Action stepOnce = () =>
            {
                if (nud.Value > limit) nud.Value = limit;
                decimal v = nud.Value + dir;
                if (v < nud.Minimum) v = nud.Minimum;
                if (v > limit) v = limit;
                if (v != nud.Value) nud.Value = v;
            };
            rep.Tick += (s, e) => { rep.Interval = 70; stepOnce(); };
            MouseEventHandler down(int d) { return (s, e) => { dir = d; stepOnce(); rep.Interval = 350; rep.Start(); }; }
            MouseEventHandler up = (s, e) => rep.Stop();
            minus.MouseDown += down(-1); plus.MouseDown += down(1);
            minus.MouseUp += up; plus.MouseUp += up;
            minus.MouseLeave += (s, e) => rep.Stop(); plus.MouseLeave += (s, e) => rep.Stop();
            parent.Controls.Add(minus); parent.Controls.Add(val); parent.Controls.Add(plus);
            made.Add(minus); made.Add(val); made.Add(plus);
            brickAgcSyncs.Add(() =>
            {
                string t = nud.Value.ToString() + " dB";
                if (val.Text != t) val.Text = t;
                foreach (Control c in new Control[] { minus, val, plus })
                {
                    if (!always)
                    {
                        if (c.Visible != nud.Visible) c.Visible = nud.Visible;
                        if (c.Enabled != nud.Enabled) c.Enabled = nud.Enabled;
                    }
                }
            });
            return made;
        }

        // where the mouse is, for tuning with the wheel (spectrum top half or the part below it)
        private TuneLocation brickTuneHit()
        {
            Point p = Cursor.Position;
            Rectangle r = pnlDisplay.RectangleToScreen(pnlDisplay.ClientRectangle);
            if (r.Contains(p)) return p.Y > r.Top + r.Height / 2 ? TuneLocation.DisplayBottom : TuneLocation.DisplayTop;
            return TuneLocation.Other;
        }

        // RIT / XIT: on/off, offset slider and reset in a small popup
        private void brickShowOffsetPopup(Control anchor, string name, CheckBox chk, NumericUpDown nud, Button reset)
        {
            Panel p = new Panel { Size = new Size(282, 128), BackColor = cCard };
            brickLabel(p, name + " \u00B7 OFFSET", 7.5f, cMuted, true, 12, 8, 200, 14);
            Label val = brickLabel(p, "", 9f, cAccent, true, 150, 8, 118, 16);
            val.TextAlign = ContentAlignment.TopRight;
            PrettyTrackBar sl = new PrettyTrackBar { Minimum = -9999, Maximum = 9999, HeadImage = ptbAF.HeadImage };
            sl.SetBounds(12, 34, 256, 22);
            decimal cur = nud.Value;
            if (cur < -9999) cur = -9999; if (cur > 9999) cur = 9999;
            sl.Value = (int)cur;
            Action show = () => val.Text = (nud.Value > 0 ? "+" : "") + nud.Value.ToString() + " Hz";
            show();
            sl.Scroll += (s, e) => { nud.Value = sl.Value; show(); };
            sl.DoubleClick += (s, e) => { nud.Value = 0; sl.Value = 0; show(); };
            p.Controls.Add(sl);
            brickLabel(p, "-9999", 7.5f, cMuted, false, 12, 58, 50, 12);
            Label r = brickLabel(p, "+9999", 7.5f, cMuted, false, 218, 58, 50, 12);
            r.TextAlign = ContentAlignment.TopRight;
            CheckBox on = new CheckBox { Text = "ON", Appearance = Appearance.Button, AutoCheck = false, Checked = chk.Checked };
            Button rst = new Button { Text = "RESET" };
            on.SetBounds(12, 80, 80, 28); rst.SetBounds(100, 80, 80, 28);
            on.Click += (s, e) => { chk.Checked = !chk.Checked; on.Checked = chk.Checked; on.Invalidate(); };
            rst.Click += (s, e) => { nud.Value = 0; sl.Value = 0; show(); };
            p.Controls.Add(on); p.Controls.Add(rst);
            brickLabel(p, "Double-click the slider = 0", 7.5f, cMuted, false, 12, 112, 250, 14);
            foreach (Control c in p.Controls) brickStyleControl(c);
            ToolStripDropDown dd = new ToolStripDropDown { AutoClose = true, BackColor = cCard, Padding = Padding.Empty };
            ToolStripControlHost host = new ToolStripControlHost(p) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = p.Size };
            dd.Items.Add(host);
            dd.Show(anchor, new Point(0, anchor.Height));
        }

        // PAN button: a small panel with the two pan sliders
        private Button brickPanButton()
        {
            Button b = new Button { Text = "PAN \u25BE" };
            b.Click += (s, e) => brickShowPanPopup(b);
            return b;
        }

        private static string brickPanText(int v)
        {
            if (v == 50) return "Center";
            return v < 50 ? "L " + ((50 - v) * 2).ToString() : "R " + ((v - 50) * 2).ToString();
        }

        private PrettyTrackBar brickPanSlider(Control host, PrettyTrackBar src, EventHandler scrolled, string caption, int y, bool enabled, out Label valueLabel)
        {
            Label cap = brickLabel(host, caption, 8.5f, enabled ? cText : cMuted, true, 12, y, 170, 16);
            Label val = brickLabel(host, brickPanText(src.Value), 8.5f, enabled ? cAccent : cMuted, true, 150, y, 118, 16);
            val.TextAlign = ContentAlignment.TopRight;
            PrettyTrackBar p = new PrettyTrackBar { Minimum = 0, Maximum = 100, Value = src.Value, HeadImage = src.HeadImage, Enabled = enabled };
            p.SetBounds(12, y + 18, 256, 22);
            p.Scroll += (s, e) =>
            {
                src.Value = p.Value;
                scrolled(this, EventArgs.Empty);
                val.Text = brickPanText(p.Value);
            };
            p.DoubleClick += (s, e) =>
            {
                p.Value = 50; src.Value = 50;
                scrolled(this, EventArgs.Empty);
                val.Text = brickPanText(50);
            };
            host.Controls.Add(p);
            brickLabel(host, "L", 7.5f, cMuted, false, 12, y + 40, 12, 12);
            Label r = brickLabel(host, "R", 7.5f, cMuted, false, 256, y + 40, 12, 12);
            valueLabel = val;
            return p;
        }

        private void brickShowPanPopup(Control anchor)
        {
            Panel p = new Panel { Size = new Size(282, 212), BackColor = cCard };
            brickLabel(p, "PAN \u00B7 LEFT / RIGHT", 7.5f, cMuted, true, 12, 8, 200, 14);
            Label v1, v2;
            bool sub = chkEnableMultiRX.Checked;
            PrettyTrackBar a = brickPanSlider(p, ptbPanMainRX, ptbPanMainRX_Scroll, "Pan A", 28, true, out v1);
            PrettyTrackBar b = brickPanSlider(p, ptbPanSubRX, ptbPanSubRX_Scroll, sub ? "Pan SUB" : "Pan SUB  (SUB-RX Off)", 86, sub, out v2);
            Button swapAB = new Button { Text = "A \u25C4\u25BA B" };
            Button center = new Button { Text = "CENTER" };
            CheckBox swap = new CheckBox { Text = "SWAP", Appearance = Appearance.Button, AutoCheck = false, Checked = chkPanSwap.Checked };
            swapAB.SetBounds(12, 148, 82, 26); center.SetBounds(100, 148, 84, 26); swap.SetBounds(190, 148, 78, 26);
            swapAB.Click += (s, e) =>
            {
                int t = ptbPanMainRX.Value;
                ptbPanMainRX.Value = ptbPanSubRX.Value; ptbPanSubRX.Value = t;
                ptbPanMainRX_Scroll(this, EventArgs.Empty); ptbPanSubRX_Scroll(this, EventArgs.Empty);
                a.Value = ptbPanMainRX.Value; b.Value = ptbPanSubRX.Value; v1.Text = brickPanText(a.Value); v2.Text = brickPanText(b.Value);
            };
            center.Click += (s, e) =>
            {
                ptbPanMainRX.Value = 50; ptbPanSubRX.Value = 50;
                ptbPanMainRX_Scroll(this, EventArgs.Empty); ptbPanSubRX_Scroll(this, EventArgs.Empty);
                a.Value = 50; b.Value = 50; v1.Text = brickPanText(50); v2.Text = brickPanText(50);
            };
            swap.Click += (s, e) => { chkPanSwap.Checked = !chkPanSwap.Checked; swap.Checked = chkPanSwap.Checked; swap.Invalidate(); };
            p.Controls.Add(swapAB); p.Controls.Add(center); p.Controls.Add(swap);
            brickLabel(p, "Double-click a slider = center", 7.5f, cMuted, false, 12, 184, 250, 14);
            foreach (Control c in p.Controls) brickStyleControl(c);
            ToolStripDropDown dd = new ToolStripDropDown { AutoClose = true, BackColor = cCard, Padding = Padding.Empty };
            ToolStripControlHost host = new ToolStripControlHost(p) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = p.Size };
            dd.Items.Add(host);
            dd.Show(anchor, new Point(0, anchor.Height));
        }

        // AGC choice as buttons; the combo box stays hidden and does the work
        private List<Control> brickAgcButtons(Control parent, Label caption, ComboBox combo, int x, int y)
        {
            List<Control> made = new List<Control>();
            caption.Text = "AGC";
            Put(caption, parent, x, y + 3, 30, 16);
            combo.Visible = false;
            List<CheckBox> btns = new List<CheckBox>();
            int n = combo.Items.Count;
            int cols = 3, bw = 38, bh = 22;
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                CheckBox rb = new CheckBox { Appearance = Appearance.Button, AutoCheck = false, Text = combo.Items[i].ToString() };
                rb.SetBounds(x + 32 + (i % cols) * (bw + 2), y + (i / cols) * (bh + 2), bw, bh);
                rb.Click += (s, e) => { if (combo.SelectedIndex != idx) combo.SelectedIndex = idx; };
                parent.Controls.Add(rb);
                btns.Add(rb);
                made.Add(rb);
            }
            EventHandler sync = (s, e) => { for (int i = 0; i < btns.Count; i++) btns[i].Checked = btns[i].Text == combo.Text; };
            combo.SelectedIndexChanged += sync;
            brickAgcSyncs.Add(() => sync(null, null));
            sync(null, null);
            return made;
        }

        private void brickSliderAt(Control parent, Label caption, Control slider, int x, int y, int w)
        {
            Put(caption, parent, x, y, w, 15);
            caption.TextAlign = ContentAlignment.MiddleLeft;
            Put(slider, parent, x, y + 15, w, 22);
        }

        // RX2 regulators are active only while RX2 runs
        private void brickUpdateRx2Enabled()
        {
            bool on = chkRX2.Checked;
            foreach (Control c in rx2OnlyControls) c.Enabled = on;
            foreach (Control c in new Control[] { chkRX2DisplayPeak, chkRX2DisplayAVG, chkX2TR }) c.Enabled = on;
            layoutTop();
        }

        #endregion

        #region right

        private Panel cardRx1Band, cardRx1Mode, cardRx1Filter, cardRx2Band, cardRx2Mode, cardRx2Filter, pnlRxSwitch;
        private RadioButton radView1, radView2;
        private readonly List<RadioButton> rx2BandButtons = new List<RadioButton>();

        private void buildRight()
        {
            int w = pnlRight.Width;
            int y = 4;

            pnlRxSwitch = brickPanel(pnlRight, cBg);
            pnlRxSwitch.SetBounds(6, y, w - 12, 26);
            radView1 = new RadioButton { Text = "RX1", Appearance = Appearance.Button, Checked = true };
            radView2 = new RadioButton { Text = "RX2", Appearance = Appearance.Button };
            radView1.SetBounds(0, 0, (w - 12) / 2 - 2, 24);
            radView2.SetBounds((w - 12) / 2 + 2, 0, (w - 12) / 2 - 2, 24);
            pnlRxSwitch.Controls.Add(radView1);
            pnlRxSwitch.Controls.Add(radView2);
            radView1.CheckedChanged += (s, e) => brickShowRxView();
            y += 30;
            int top = y;
            int cwd = w - 12;

            // ---- RX1 band / mode / filter
            cardRx1Band = brickCard(pnlRight);
            cardRx1Band.SetBounds(6, y, cwd, 176);
            int by = brickSection(cardRx1Band, "Band", 6, cwd);
            foreach (Panel p in new Panel[] { panelBandHF, panelBandVHF, panelBandGEN })
            {
                p.BackColor = cCard;
                Put(p, cardRx1Band, 0, by, cwd, cardRx1Band.Height - by - 2);
            }
            Control[] hf = { radBand160, radBand80, radBand60, radBand40, radBand30, radBand20, radBand17, radBand15, radBand12, radBand10, radBand6, radBand2, btnBandVHF, radBandWWV, radBandGEN };
            Control[] vhf = { radBandVHF0, radBandVHF1, radBandVHF2, radBandVHF3, radBandVHF4, radBandVHF5, radBandVHF6, radBandVHF7, radBandVHF8, radBandVHF9, radBandVHF10, radBandVHF11, radBandVHF12, radBandVHF13, btnBandHF };
            Control[] gen = { radBandGEN0, radBandGEN1, radBandGEN2, radBandGEN3, radBandGEN4, radBandGEN5, radBandGEN6, radBandGEN7, radBandGEN8, radBandGEN9, radBandGEN10, radBandGEN11, radBandGEN12, radBandGEN13, btnBandHF1 };
            Grid(panelBandHF, 8, 0, cwd - 16, 3, 25, 5, hf);
            Grid(panelBandVHF, 8, 0, cwd - 16, 3, 25, 5, vhf);
            Grid(panelBandGEN, 8, 0, cwd - 16, 3, 25, 5, gen);
            y = cardRx1Band.Bottom + 6;

            cardRx1Mode = brickCard(pnlRight);
            cardRx1Mode.SetBounds(6, y, cwd, 148);
            int my = brickSection(cardRx1Mode, "Mode", 6, cwd);
            panelMode.BackColor = cCard;
            Put(panelMode, cardRx1Mode, 0, my, cwd, cardRx1Mode.Height - my);
            brickModeGroups(panelMode, cwd - 16, 0, radModeLSB, radModeUSB, radModeCWL, radModeCWU, radModeAM, radModeSAM, radModeDIGL, radModeDIGU, radModeDSB, radModeFMN, radModeDRM, radModeSPEC);
            y = cardRx1Mode.Bottom + 6;

            cardRx1Filter = brickCard(pnlRight);
            cardRx1Filter.SetBounds(6, y, cwd, 270);
            int fy = brickSection(cardRx1Filter, "Filter", 6, cwd);
            panelFilter.BackColor = cCard;
            Put(panelFilter, cardRx1Filter, 0, fy, cwd, cardRx1Filter.Height - fy);
            int fyy = Grid(panelFilter, 8, 0, cwd - 16, 3, 26, 5,
                radFilter1, radFilter2, radFilter3, radFilter4, radFilter5, radFilter6, radFilter7, radFilter8, radFilter9, radFilter10, radFilterVar1, radFilterVar2);
            int fw = cwd - 16;
            Put(lblFilterWidth, panelFilter, 8, fyy + 8, 40, 16);
            Put(ptbFilterWidth, panelFilter, 48, fyy + 6, fw - 40, 22);
            Put(lblFilterShift, panelFilter, 8, fyy + 32, 40, 16);
            Put(ptbFilterShift, panelFilter, 48, fyy + 30, fw - 90, 22);
            Put(btnFilterShiftReset, panelFilter, 8 + fw - 40, fyy + 30, 40, 22);
            lblFilterLow.Text = "Low"; lblFilterHigh.Text = "High";
            lblFilterLow.AutoSize = false; lblFilterHigh.AutoSize = false;
            Put(lblFilterLow, panelFilter, 8, fyy + 60, 40, 16);
            brickValueStepper(panelFilter, udFilterLow, 50, fyy + 56, 142);
            Put(lblFilterHigh, panelFilter, 8, fyy + 90, 40, 16);
            brickValueStepper(panelFilter, udFilterHigh, 50, fyy + 86, 142);

            // ---- RX2, the same three cards in the same place
            y = top;
            cardRx2Band = brickCard(pnlRight);
            cardRx2Band.SetBounds(6, y, cwd, 176);
            int b2 = brickSection(cardRx2Band, "Band", 6, cwd);
            comboRX2Band.Visible = false; lblRX2Band.Visible = false; lblRX2Preamp.Visible = false;
            string[] bandNames = { "160", "80", "60", "40", "30", "20", "17", "15", "12", "10", "6", "WWV", "GEN" };
            List<Control> bandBtns = new List<Control>();
            foreach (string bn in bandNames)
            {
                RadioButton rb = new RadioButton { Appearance = Appearance.Button, Text = bn, Tag = bn };
                rb.Click += (s, e) => brickPickRx2Band((string)((RadioButton)s).Tag);
                rx2BandButtons.Add(rb);
                bandBtns.Add(rb);
                cardRx2Band.Controls.Add(rb);
            }
            Grid(cardRx2Band, 8, b2, cwd - 16, 3, 25, 5, bandBtns.ToArray());
            comboRX2Band.SelectedIndexChanged += (s, e) => brickSyncRx2Band();
            y = cardRx2Band.Bottom + 6;

            cardRx2Mode = brickCard(pnlRight);
            cardRx2Mode.SetBounds(6, y, cwd, 148);
            int m2 = brickSection(cardRx2Mode, "Mode", 6, cwd);
            brickModeGroups(cardRx2Mode, cwd - 16, m2, radRX2ModeLSB, radRX2ModeUSB, radRX2ModeCWL, radRX2ModeCWU, radRX2ModeAM, radRX2ModeSAM, radRX2ModeDIGL, radRX2ModeDIGU, radRX2ModeDSB, radRX2ModeFMN, radRX2ModeDRM, radRX2ModeSPEC);
            y = cardRx2Mode.Bottom + 6;

            cardRx2Filter = brickCard(pnlRight);
            cardRx2Filter.SetBounds(6, y, cwd, 252);
            int f2 = brickSection(cardRx2Filter, "Filter", 6, cwd);
            int f2y = Grid(cardRx2Filter, 8, f2, cwd - 16, 3, 26, 5,
                radRX2Filter1, radRX2Filter2, radRX2Filter3, radRX2Filter4, radRX2Filter5, radRX2Filter6, radRX2Filter7, radRX2FilterVar1, radRX2FilterVar2);
            Put(lblRX2FilterLow, cardRx2Filter, 8, f2y + 58, 28, 16);
            Put(udRX2FilterLow, cardRx2Filter, 38, f2y + 55, 62, 22);
            Put(lblRX2FilterHigh, cardRx2Filter, 106, f2y + 58, 30, 16);
            Put(udRX2FilterHigh, cardRx2Filter, 136, f2y + 55, 62, 22);
            lblRX2FilterLow.Visible = false; udRX2FilterLow.Visible = false; lblRX2FilterHigh.Visible = false; udRX2FilterHigh.Visible = false;
            cardRx2Band.Visible = false; cardRx2Mode.Visible = false; cardRx2Filter.Visible = false;
            radRX2ModeSPEC.Visible = true;
        }

        // modes in pairs (LSB / USB, CWL / CWU, AM / SAM, DIGL / DIGU) that switch over on a click, then the single ones
        private void brickModeGroups(Control parent, int w, int y0, RadioButton lsb, RadioButton usb, RadioButton cwl, RadioButton cwu, RadioButton am, RadioButton sam,
            RadioButton digl, RadioButton digu, RadioButton dsb, RadioButton fm, RadioButton drm, RadioButton spec)
        {
            int gap = 5, cw = (w - gap) / 2, h = 26;
            RadioButton[][] pairs = { new[] { lsb, usb }, new[] { cwl, cwu }, new[] { am, sam }, new[] { digl, digu } };
            string[][] names = { new[] { "LSB", "USB" }, new[] { "CW-L", "CW-U" }, new[] { "AM", "SAM" }, new[] { "DIGL", "DIGU" } };
            for (int i = 0; i < pairs.Length; i++)
            {
                RadioButton ra = pairs[i][0], rb = pairs[i][1];
                bool rx2Card = ra.Name.StartsWith("radRX2");
                bool digi = i == 3 && !rx2Card;   // RX1: the DIGI drop-down, RX2 keeps DIGL / DIGU
                BrickModePair bp = new BrickModePair { NameA = digi ? "DIGU" : names[i][0], NameB = digi ? null : names[i][1], Font = new Font("Segoe UI", 8.5f) };
                bp.SetBounds(8 + (i % 2) * (cw + gap), y0 + (i / 2) * (h + gap), cw, h);
                bp.GetState = () => ra.Checked ? 1 : (rb.Checked ? 2 : 0);
                int pairIndex = i;
                bp.Pick = n =>
                {
                    if (digi) { brickShowDigiMenu(bp, rx2Card); return; }
                    if (n == 0) n = brickDefaultSideband(pairIndex, rx2Card);
                    if (n == 1) ra.Checked = true; else rb.Checked = true;
                };
                if (digi)
                {
                    bp.GetState = () => (ra.Checked || rb.Checked || (brickDigiChoice == "CW" && (RX1DSPMode == DSPMode.CWL || RX1DSPMode == DSPMode.CWU))) ? 1 : 0;
                    bp.TextFn = () => brickDigiLabel() + "  \u25BE";
                    brickDigiButton = bp;
                    string digiSig = null;
                    brickAgcSyncs.Add(() => { string sig = brickDigiLabel() + "|" + bp.GetState(); if (sig != digiSig) { digiSig = sig; bp.Invalidate(); } });   // follows mode changes made elsewhere (the CW buttons)
                }
                ra.CheckedChanged += (s, e) => bp.Invalidate();
                rb.CheckedChanged += (s, e) => bp.Invalidate();
                parent.Controls.Add(bp);
                foreach (RadioButton hid in pairs[i])
                {
                    RadioButton hh = hid;
                    if (hh.Parent != parent) parent.Controls.Add(hh);
                    hh.SetBounds(-3000, 0, 10, 10);
                    hh.TabStop = false;
                }
            }
            Grid(parent, 8, y0 + 2 * (h + gap), w, 2, h, gap, dsb, fm, drm, spec);
        }

        // ---- DIGI: digital modes and presets in a drop-down (the way PAN works), styled like the menu bar
        private static readonly Dictionary<Band, double[]> brickDigiFreq = new Dictionary<Band, double[]>
        {
            // band -> { FT8, FT4 } dial frequency in MHz
            { Band.B160M, new[] { 1.840, 1.840 } }, { Band.B80M, new[] { 3.573, 3.575 } }, { Band.B60M, new[] { 5.357, 5.357 } },
            { Band.B40M, new[] { 7.074, 7.0475 } }, { Band.B30M, new[] { 10.136, 10.140 } }, { Band.B20M, new[] { 14.074, 14.080 } },
            { Band.B17M, new[] { 18.100, 18.104 } }, { Band.B15M, new[] { 21.074, 21.140 } }, { Band.B12M, new[] { 24.915, 24.919 } },
            { Band.B10M, new[] { 28.074, 28.180 } }, { Band.B6M, new[] { 50.313, 50.318 } }
        };

        // what the DIGI button shows: the last choice from its menu (DIGU on a fresh install), kept in line with the real mode
        private string brickDigiChoiceValue;
        private BrickModePair brickDigiButton;
        private string BrickDigiFile { get { return Path.Combine(AppDataPath, "digi_choice.txt"); } }
        private string brickDigiChoice
        {
            get
            {
                if (brickDigiChoiceValue == null)
                {
                    brickDigiChoiceValue = "DIGU";
                    try { if (File.Exists(BrickDigiFile)) { string t = File.ReadAllText(BrickDigiFile).Trim(); if (t.Length > 0) brickDigiChoiceValue = t; } } catch { }
                }
                return brickDigiChoiceValue;
            }
            set
            {
                brickDigiChoiceValue = value;
                try { File.WriteAllText(BrickDigiFile, value); } catch { }
                if (brickDigiButton != null) brickDigiButton.Invalidate();
            }
        }

        private string brickDigiLabel()
        {
            string c = brickDigiChoice;
            DSPMode m = RX1DSPMode;
            if (RadeEnabledOn(0)) return "RADE";
            if (c == "RADE") c = m == DSPMode.DIGL ? "DIGL" : "DIGU";   // RADE was switched off: the mode it left
            // the real mode wins when it contradicts the choice (e.g. the mode was changed elsewhere)
            if (m == DSPMode.DIGL && (c == "DIGU" || c == "FT8" || c == "FT4" || c == "CW")) return "DIGL";
            if (m == DSPMode.DIGU && (c == "DIGL" || c == "RTTY" || c == "CW")) return "DIGU";
            return c;
        }

        // which of the two modes of a pair to start with: LSB below 10 MHz (not on 60 m), USB above; the other pairs use their first
        private int brickDefaultSideband(int pairIndex, bool rx2)
        {
            if (pairIndex == 0)
            {
                double f = rx2 ? VFOBFreq : VFOAFreq;
                Band b = rx2 ? RX2Band : RX1Band;
                return (f < 10.0 && b != Band.B60M) ? 1 : 2;
            }
            if (pairIndex == 3) return 2;   // DIGU
            return 1;
        }

        private void brickDigiSet(bool rx2, DSPMode mode, double? mhz)
        {
            if (mode == DSPMode.DIGU || mode == DSPMode.DIGL) brickRememberBeforeDigi(rx2);
            if (rx2) { RX2DSPMode = mode; if (mhz.HasValue) VFOBFreq = mhz.Value; }
            else { RX1DSPMode = mode; if (mhz.HasValue) VFOAFreq = mhz.Value; }
        }

        private void brickShowDigiMenu(Control anchor, bool rx2)
        {
            DSPMode cur = rx2 ? RX2DSPMode : RX1DSPMode;
            Band band = rx2 ? RX2Band : RX1Band;
            double freq = rx2 ? VFOBFreq : VFOAFreq;
            ContextMenuStrip m = new ContextMenuStrip { Renderer = new BrickMenuRenderer(), Font = new Font("Segoe UI", 10f), ShowImageMargin = false, ShowCheckMargin = false, RightToLeft = RightToLeft.No, BackColor = cCard, ForeColor = cText };
            Action<string, bool, Action, bool> add = (text, on, act, enabled) =>
            {
                ToolStripMenuItem it = new ToolStripMenuItem(text) { Enabled = enabled, ForeColor = cText, BackColor = cCard, CheckOnClick = false, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 3, 8, 3) };
                if (on) it.Text = "\u25CF  " + text; else it.Text = "    " + text;
                if (act != null) it.Click += (s, e) => act();
                m.Items.Add(it);
            };
            double[] ft;
            brickDigiFreq.TryGetValue(band, out ft);
            add("DIGL", cur == DSPMode.DIGL, () => { brickDigiSet(rx2, DSPMode.DIGL, null); brickDigiChoice = "DIGL"; }, true);
            add("DIGU", cur == DSPMode.DIGU, () => { brickDigiSet(rx2, DSPMode.DIGU, null); brickDigiChoice = "DIGU"; }, true);
            m.Items.Add(new ToolStripSeparator());
            add("FT8" + (ft != null ? "   " + ft[0].ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) : ""), _ftPane != null && brickDockKey == "ft" && _ftMode == "FT8", () => { brickDigiSet(rx2, DSPMode.DIGU, ft[0]); brickDigiChoice = "FT8"; _digiRx = rx2 ? 1 : 0; brickFtOpen("FT8"); }, ft != null);
            add("FT4" + (ft != null ? "   " + ft[1].ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) : ""), _ftPane != null && brickDockKey == "ft" && _ftMode == "FT4", () => { brickDigiSet(rx2, DSPMode.DIGU, ft[1]); brickDigiChoice = "FT4"; _digiRx = rx2 ? 1 : 0; brickFtOpen("FT4"); }, ft != null);
            add("FreeDV RADE", RadeEnabledOn(rx2 ? 1 : 0), () => { brickRadeOpen(); SetRadeEnabled(rx2 ? 1 : 0, true); if (!rx2) brickDigiChoice = "RADE"; }, true);
            add("RTTY", false, () => { brickDigiSet(rx2, DSPMode.DIGL, null); brickDigiChoice = "RTTY"; _digiRx = rx2 ? 1 : 0; brickDigitalOpen("RTTY"); }, true);
            add("CW", false, () => { brickDigiSet(rx2, freq < 10.0 ? DSPMode.CWL : DSPMode.CWU, null); brickDigiChoice = "CW"; _digiRx = rx2 ? 1 : 0; brickDigitalOpen("CW"); }, true);
            m.Items.Add(new ToolStripSeparator());
            add("DX", false, () => { try { System.Diagnostics.Process.Start("https://www.dxsummit.fi/"); } catch { } }, true);
            add("POTA", false, () => { try { System.Diagnostics.Process.Start("https://pota.app/#/"); } catch { } }, true);
            m.Show(anchor, new Point(0, anchor.Height));
        }

        private void brickPickRx2Band(string key)
        {
            for (int i = 0; i < comboRX2Band.Items.Count; i++)
            {
                string t = comboRX2Band.Items[i].ToString().Trim().ToUpperInvariant().TrimEnd('M');
                if (t == key)
                {
                    comboRX2Band.SelectedIndex = i;
                    SetupRX2Band(comboRX2Band.Items[i].ToString());
                    return;
                }
            }
        }

        private void brickSyncRx2Band()
        {
            string cur = comboRX2Band.SelectedItem == null ? "" : comboRX2Band.SelectedItem.ToString().Trim().ToUpperInvariant().TrimEnd('M');
            foreach (RadioButton rb in rx2BandButtons) rb.Checked = (string)rb.Tag == cur;
        }

        private void brickShowRxView()
        {
            bool rx1 = radView1.Checked;
            Display.BrickActiveRX = rx1 ? 1 : 2;
            cardRx1Band.Visible = rx1; cardRx1Mode.Visible = rx1; cardRx1Filter.Visible = rx1;
            cardRx2Band.Visible = !rx1; cardRx2Mode.Visible = !rx1; cardRx2Filter.Visible = !rx1;
        }

        #endregion

        #region centre

        private void buildCenter()
        {
            Panel dispCard = brickCard(pnlCenter);
            dispCard.Dock = DockStyle.Fill;
            dispCard.Padding = new Padding(4);
            brickDispCard = dispCard;

            // pan and zoom of the second receiver, below the bottom waterfall (only while RX2 runs)
            pnlRx2View = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = cCard, Visible = false };
            dispCard.Controls.Add(pnlRx2View);
            Label l2p = brickLabel(pnlRx2View, "RX2  Pan:", 9f, cText, false, 4, 6, 70, 18);
            PrettyTrackBar p2 = new PrettyTrackBar { Minimum = ptbDisplayPan.Minimum, Maximum = ptbDisplayPan.Maximum, Value = ptbDisplayPan.Value, HeadImage = ptbAF.HeadImage };
            p2.SetBounds(76, 3, 150, 22);
            pnlRx2View.Controls.Add(p2);
            Button c2 = new Button { Text = "Center" };
            c2.SetBounds(232, 3, 62, 24);
            pnlRx2View.Controls.Add(c2);
            Label l2z = brickLabel(pnlRx2View, "Zoom:", 9f, cText, false, 306, 6, 48, 18);
            PrettyTrackBar z2 = new PrettyTrackBar { Minimum = ptbDisplayZoom.Minimum, Maximum = ptbDisplayZoom.Maximum, Value = ptbDisplayZoom.Value, HeadImage = ptbAF.HeadImage };
            z2.SetBounds(356, 3, 130, 22);
            pnlRx2View.Controls.Add(z2);
            string[] zn = { "0.5x", "1x", "2x", "4x" };
            int[] zv = { 0, 0, 0, 0 };
            Action<PrettyTrackBar> noop = null;
            for (int i = 0; i < 4; i++)
            {
                double factor = i == 0 ? 0.5 : (i == 1 ? 1.0 : (i == 2 ? 2.0 : 4.0));
                CheckBox zb = new CheckBox { Text = zn[i], Appearance = Appearance.Button, AutoCheck = false };
                zb.SetBounds(494 + i * 48, 3, 44, 24);
                // zoom factor = 1 / ((max + min - value) * 0.01)  ->  value = max + min - 100 / factor
                int target = (int)Math.Round(z2.Maximum + z2.Minimum - 100.0 / factor);
                zb.Click += (s, e) => { z2.Value = Math.Max(z2.Minimum, Math.Min(z2.Maximum, target)); applyRx2View(p2, z2); };
                zb.Tag = target;
                pnlRx2View.Controls.Add(zb);
                rx2ZoomButtons.Add(zb);
            }
            chk3D2 = new CheckBox { Name = "brick3D2", Text = "3D", Appearance = Appearance.Button };
            chk3D2.SetBounds(690, 3, 40, 24);
            pnlRx2View.Controls.Add(chk3D2);
            chk3D2.CheckedChanged += (s, e) => { Display.Use3DRX2 = chk3D2.Checked; brickSave3D(); };
            brickLoad3D();
            rx2ZoomValue = z2.Value;
            foreach (CheckBox zb3 in rx2ZoomButtons) zb3.Checked = (int)zb3.Tag == z2.Value;
            p2.Scroll += (s, e) => applyRx2View(p2, z2);
            z2.Scroll += (s, e) => applyRx2View(p2, z2);
            c2.Click += (s, e) => { p2.Value = (p2.Maximum - p2.Minimum) / 2; applyRx2View(p2, z2); };
            chkRX2.CheckedChanged += (s, e) => { pnlRx2View.Visible = chkRX2.Checked; if (chkRX2.Checked) applyRx2View(p2, z2); };
            // RX2 was already on when the window is built (restored state): show its Pan / Zoom row now
            pnlRx2View.Visible = chkRX2.Checked;
            if (chkRX2.Checked) applyRx2View(p2, z2);
            Put(panelDisplay, dispCard, 0, 0, 100, 100);
            panelDisplay.Dock = DockStyle.Fill;
            panelDisplay.BringToFront();
            EventHandler keepFill = (s, e) => { if (panelDisplay.Dock != DockStyle.Fill) { panelDisplay.Dock = DockStyle.Fill; panelDisplay.BringToFront(); } };
            panelDisplay.DockChanged += keepFill;
            panelDisplay.LocationChanged += keepFill;
            panelDisplay.SizeChanged += keepFill;
            panelDisplay.SizeChanged += (s, e) => m_bResizeDX2Display = true;
        }

        #endregion


        #region theme

        private void brickStyleMenu()
        {
            menuStrip1.BackColor = cBg;
            menuStrip1.ForeColor = cText;
            menuStrip1.Renderer = new BrickMenuRenderer();
            statusStripMain.Renderer = new BrickMenuRenderer();
            statusStripMain.BackColor = cCard; statusStripMain.ForeColor = cText;
            foreach (ToolStripItem si in statusStripMain.Items) { si.ForeColor = cText; if (!(si is ToolStripSeparator)) si.BackColor = Color.Transparent; if (si is ToolStripDropDownItem) brickMenuColours(si); }
            menuStrip1.ForeColor = cText;
            foreach (ToolStripItem it0 in menuStrip1.Items) brickMenuColours(it0);
            foreach (ToolStripItem it in menuStrip1.Items) { it.ForeColor = cText; it.BackColor = cBg; it.AutoSize = true; }
            // the console can no longer be collapsed
            foreach (ToolStripItem it in menuStrip1.Items)
                if (it.Text != null && it.Text.StartsWith("Collapse", StringComparison.OrdinalIgnoreCase)) it.Visible = false;
        }

        private void brickMenuColours(ToolStripItem it)
        {
            it.ForeColor = cText; it.BackColor = cCard;
            it.Font = new Font("Segoe UI", 10.5f);
            ToolStripDropDownItem dd = it as ToolStripDropDownItem;
            if (dd != null)
            {
                dd.DropDown.BackColor = cCard; dd.DropDown.ForeColor = cText;
                dd.DropDown.Font = new Font("Segoe UI", 10.5f);
                foreach (ToolStripItem sub in dd.DropDownItems) brickMenuColours(sub);
            }
        }

        // menu bar, drop-down menus and the status bar in the Brick-SDR style: flat dark, rounded hover with an accent line
        private class BrickMenuRenderer : ToolStripProfessionalRenderer
        {
            public BrickMenuRenderer() : base(new BrickColorTable()) { RoundedEdges = false; }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                bool status = e.ToolStrip is StatusStrip;
                using (SolidBrush b = new SolidBrush(status || e.ToolStrip is ToolStripDropDown ? cCard : cBg)) e.Graphics.FillRectangle(b, e.AffectedBounds);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                if (e.ToolStrip is StatusStrip)
                {
                    using (Pen p = new Pen(cLine)) e.Graphics.DrawLine(p, 0, 0, e.ToolStrip.Width, 0);
                }
                else if (e.ToolStrip is ToolStripDropDown)
                {
                    using (Pen p = new Pen(Color.FromArgb(70, 76, 98))) e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
                }
                else
                {
                    using (Pen p = new Pen(cLine)) e.Graphics.DrawLine(p, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
                }
            }

            private void hover(Graphics g, Rectangle r, bool top, bool pressed)
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Rectangle rr = new Rectangle(r.X + 2, r.Y + 2, r.Width - 5, r.Height - 5);
                using (System.Drawing.Drawing2D.GraphicsPath gp = brickRoundPath(rr, 6))
                {
                    using (System.Drawing.Drawing2D.LinearGradientBrush lg = new System.Drawing.Drawing2D.LinearGradientBrush(rr, brickShade(cBtn, pressed ? 0.05 : 0.22), brickShade(cBtn, pressed ? -0.1 : -0.05), 90f))
                        g.FillPath(lg, gp);
                    using (Pen p = new Pen(Color.FromArgb(top ? 200 : 120, cAccent), 1f)) g.DrawPath(p, gp);
                }
                if (top) using (Pen u = new Pen(cAccent, 2f)) g.DrawLine(u, rr.Left + 8, rr.Bottom - 1, rr.Right - 8, rr.Bottom - 1);
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                ToolStripItem it = e.Item;
                if (!it.IsOnDropDown)
                {
                    // like the band buttons: rounded, slightly raised, orange while hovered / open
                    Graphics g = e.Graphics;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    bool on = it.Selected || it.Pressed;
                    using (SolidBrush clear = new SolidBrush(cBg)) g.FillRectangle(clear, new Rectangle(Point.Empty, it.Size));
                    Rectangle r = new Rectangle(1, 2, it.Width - 3, it.Height - 5);
                    // flat and quiet at rest (these are not radio controls), orange only while hovered / open
                    using (System.Drawing.Drawing2D.GraphicsPath gp = brickRoundPath(r, 6))
                    {
                        if (on)
                        {
                            using (System.Drawing.Drawing2D.LinearGradientBrush lg = new System.Drawing.Drawing2D.LinearGradientBrush(r, brickShade(cAccent, 0.12), brickShade(cAccent, it.Pressed ? 0.02 : -0.18), 90f))
                                g.FillPath(lg, gp);
                            using (Pen pen = new Pen(brickShade(cAccent, -0.35), 1f)) g.DrawPath(pen, gp);
                        }
                        else
                        {
                            using (Pen pen = new Pen(Color.FromArgb(52, 57, 74), 1f)) g.DrawPath(pen, gp);
                        }
                    }
                    return;
                }
                if (!it.Selected && !it.Pressed) return;
                hover(e.Graphics, new Rectangle(Point.Empty, it.Size), false, it.Pressed);
            }

            protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected && !e.Item.Pressed) return;
                hover(e.Graphics, new Rectangle(Point.Empty, e.Item.Size), false, e.Item.Pressed);
            }

            protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected && !e.Item.Pressed) return;
                hover(e.Graphics, new Rectangle(Point.Empty, e.Item.Size), false, e.Item.Pressed);
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                bool topOn = !e.Item.IsOnDropDown && (e.Item.Selected || e.Item.Pressed);
                e.TextColor = !e.Item.Enabled ? cMuted : (topOn ? Color.FromArgb(20, 20, 24) : ((e.Item.Selected || e.Item.Pressed) ? Color.White : cText));
                base.OnRenderItemText(e);
            }

            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = e.Item.Selected ? cAccent : cMuted;
                base.OnRenderArrow(e);
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                using (Pen p = new Pen(cLine))
                {
                    if (e.Vertical) e.Graphics.DrawLine(p, e.Item.Width / 2, 4, e.Item.Width / 2, e.Item.Height - 4);
                    else e.Graphics.DrawLine(p, 8, e.Item.Height / 2, e.Item.Width - 8, e.Item.Height / 2);
                }
            }

            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                Rectangle r = new Rectangle(e.ImageRectangle.X - 1, e.ImageRectangle.Y - 1, e.ImageRectangle.Width + 2, e.ImageRectangle.Height + 2);
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(70, cAccent))) e.Graphics.FillRectangle(b, r);
                using (Pen p = new Pen(cAccent, 2f)) { e.Graphics.DrawLine(p, r.Left + 3, r.Top + r.Height / 2, r.Left + r.Width / 2 - 1, r.Bottom - 4); e.Graphics.DrawLine(p, r.Left + r.Width / 2 - 1, r.Bottom - 4, r.Right - 3, r.Top + 4); }
            }
        }

        private class BrickColorTable : ProfessionalColorTable
        {
            public override Color MenuItemSelected { get { return Color.FromArgb(52, 57, 76); } }
            public override Color MenuItemBorder { get { return Color.FromArgb(70, 76, 98); } }
            public override Color MenuBorder { get { return Color.FromArgb(48, 53, 70); } }
            public override Color ToolStripDropDownBackground { get { return Color.FromArgb(24, 27, 37); } }
            public override Color ImageMarginGradientBegin { get { return Color.FromArgb(24, 27, 37); } }
            public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(24, 27, 37); } }
            public override Color ImageMarginGradientEnd { get { return Color.FromArgb(24, 27, 37); } }
            public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(52, 57, 76); } }
            public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(52, 57, 76); } }
            public override Color MenuItemPressedGradientBegin { get { return Color.FromArgb(52, 57, 76); } }
            public override Color MenuItemPressedGradientEnd { get { return Color.FromArgb(52, 57, 76); } }
        }

        private void brickApplyTheme(Control root)
        {
            foreach (Control c in root.Controls) brickStyleControl(c);
        }

        private void brickStyleControl(Control c)
        {
            if (c is BrickDropDown) return;
            if (c == infoBar.FeedbackLabel || c == infoBar.PureSignalLabel) return;
            if (c is PrettyTrackBar)
            {
                c.BackColor = c.Parent != null ? c.Parent.BackColor : cCard;
                return;
            }

            if (c is GroupBox)
            {
                c.BackColor = cCard;
                c.ForeColor = cMuted;
            }
            else if (c is Label)
            {
                c.BackColor = Color.Transparent;
                if (c.Font.Size <= 9.5f && c.Font.Name == "Microsoft Sans Serif") c.Font = new Font("Segoe UI Semibold", 9f);
                if (c.ForeColor == SystemColors.ControlText || c.ForeColor == Color.Black || c.ForeColor.ToArgb() == Color.White.ToArgb())
                    c.ForeColor = cText;
            }
            else if (c is RadioButton || c is CheckBox || c is Button)
            {
                brickStyleButton((ButtonBase)c);
            }
            else if (c is ComboBox)
            {
                ComboBox cb = (ComboBox)c;
                cb.FlatStyle = FlatStyle.Flat;
                cb.BackColor = cBtn;
                cb.ForeColor = cText;
            }
            else if (c is NumericUpDown)
            {
                c.BackColor = cBtn;
                c.ForeColor = cText;
            }
            else if (c is TextBox)
            {
                TextBox tb = (TextBox)c;
                // frequency displays keep their own colours, only the background goes dark
                tb.BackColor = cField;
                if (tb.ForeColor == SystemColors.WindowText) tb.ForeColor = cText;
                tb.BorderStyle = BorderStyle.None;
            }
            else if (c is TabControl)
            {
                c.BackColor = cBg; c.ForeColor = cText;
                TabControl tc = (TabControl)c;
                tc.DrawMode = TabDrawMode.OwnerDrawFixed;
                tc.DrawItem += brickDrawTab;
            }
            else if (c is TabPage)
            {
                c.BackColor = cCard; c.ForeColor = cText;
                ((TabPage)c).UseVisualStyleBackColor = false;
            }
            else if (c is ListView || c is TreeView || c is ListBox || c is RichTextBox || c is CheckedListBox)
            {
                c.BackColor = cField; c.ForeColor = cText;
            }
            else if (c is DataGridView)
            {
                DataGridView dg = (DataGridView)c;
                dg.BackgroundColor = cField; dg.GridColor = cLine;
                dg.EnableHeadersVisualStyles = false;
                dg.ColumnHeadersDefaultCellStyle.BackColor = cBtn; dg.ColumnHeadersDefaultCellStyle.ForeColor = cText;
                dg.RowHeadersDefaultCellStyle.BackColor = cBtn;
                dg.DefaultCellStyle.BackColor = cField; dg.DefaultCellStyle.ForeColor = cText;
                dg.DefaultCellStyle.SelectionBackColor = cAccent; dg.DefaultCellStyle.SelectionForeColor = Color.Black;
            }
            else if (c is Panel && c.Name != "pnlDisplay")
            {
                if (c.BackgroundImage != null && c != panelDisplay) c.BackgroundImage = null;
                if (c.BackColor == SystemColors.Control || c.BackColor == SystemColors.ControlDark || c.BackColor.ToArgb() == Color.Transparent.ToArgb())
                    c.BackColor = c.Parent != null ? c.Parent.BackColor : cCard;
            }

            foreach (Control child in c.Controls) brickStyleControl(child);
        }

        private void brickStyleButton(ButtonBase b)
        {
            CheckBox cbx = b as CheckBox;
            // real tick boxes keep the normal look, only the colours change
            RadioButton rbn = b as RadioButton;
            if (rbn != null && rbn.Appearance == Appearance.Normal)
            {
                b.BackColor = Color.Transparent;
                b.ForeColor = cText;
                return;
            }
            if (cbx != null && cbx.Appearance == Appearance.Normal)
            {
                b.BackColor = Color.Transparent;
                b.ForeColor = cText;
                return;
            }

            if (b is CheckBox) ((CheckBox)b).Appearance = Appearance.Button;
            if (b is RadioButton) ((RadioButton)b).Appearance = Appearance.Button;

            b.FlatStyle = FlatStyle.Flat;
            b.UseVisualStyleBackColor = false;
            b.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Regular);
            b.TextAlign = ContentAlignment.MiddleCenter;
            b.Image = null;
            b.BackgroundImage = null;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = cLine;
            b.MouseEnter += (s, e) => { brickHover.Add(b); b.Invalidate(); };
            b.MouseLeave += (s, e) => { brickHover.Remove(b); b.Invalidate(); };

            bool isTx = b == chkMOX || b == chkTUN || b == chk2TONE || b == chkVOX || b == chkVFOATX || b == chkVFOBTX;
            Color on = isTx ? cTx : (b == chkPower ? Color.FromArgb(60, 190, 90) : cAccent);
            if (b is CheckBox) ((CheckBox)b).FlatAppearance.CheckedBackColor = on;
            if (b is RadioButton) ((RadioButton)b).FlatAppearance.CheckedBackColor = on;

            Action apply = () =>
            {
                bool state = b is CheckBox ? ((CheckBox)b).Checked : (b is RadioButton ? ((RadioButton)b).Checked : false);
                Color back = state ? on : cBtn;
                Color fore = state ? Color.FromArgb(20, 20, 24) : cText;
                if (!b.Enabled) fore = cMuted;
                if (b.Image != null) b.Image = null;
                if (b.BackgroundImage != null) b.BackgroundImage = null;
                if (b.BackColor != back) b.BackColor = back;
                if (b.ForeColor != fore) b.ForeColor = fore;
                b.Invalidate();
            };
            b.Paint += (s, e) =>
            {
                bool st = b is CheckBox ? ((CheckBox)b).Checked : (b is RadioButton ? ((RadioButton)b).Checked : false);
                if (b == chkMUT && brickMuteAll) brickPaintButton(b, e, Color.FromArgb(215, 45, 45), true);
                else brickPaintButton(b, e, on, st);
            };

            // the old code puts its glossy button pictures back whenever the state changes
            b.Invalidated += (s, e) =>
            {
                if (b.Image != null) b.Image = null;
                if (b.BackgroundImage != null) b.BackgroundImage = null;
            };
            b.BackColorChanged += (s, e) => apply();
            b.ForeColorChanged += (s, e) => apply();
            b.EnabledChanged += (s, e) => apply();
            if (b is CheckBox) ((CheckBox)b).CheckedChanged += (s, e) => apply();
            if (b is RadioButton) ((RadioButton)b).CheckedChanged += (s, e) => apply();
            apply();
        }

        #endregion

        #region live values

        private bool brickTxAvgValid;
        private double brickPwrAvg, brickSwrAvg = 1.0;

        // SWR 1..2 takes two thirds of the bar, 2..3 the last third
        private static double brickSwrFraction(double s)
        {
            if (s < 1) s = 1;
            if (s > 3) s = 3;
            return s <= 2 ? (s - 1.0) * (2.0 / 3.0) : 2.0 / 3.0 + (s - 2.0) / 3.0;
        }

        private string brickSwrText(float avg = float.NaN)
        {
            float s = float.IsNaN(avg) ? alex_swr : avg;
            if (float.IsNaN(s) || float.IsInfinity(s) || s < 1.0f) return "1.0";
            if (s > 9.9f) s = 9.9f;
            return s.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        private bool brickPureSignalOn()
        {
            return psform != null && psform.PSEnabled;
        }

        // the meter is repainted by the old code only on rx data, keep it moving while transmitting
        private void brickTick(object sender, EventArgs e)
        {
            if (_mox != brickLastMox) { brickLastMox = _mox; layoutTop(); }
            if (!initializing && !IsSetupFormNull)
            {
                // moving a volume slider ends MUTE
                if (!brickSlidersUnmute) { brickSlidersUnmute = true; m_bRXAFSlidersWillUnmute = true; }
                // show the TX attenuation of the band that is now selected, not the one of the last transmission
                if (!_mox && _tx_band != brickLastTxBand && _tx_band > Band.FIRST && _tx_band < Band.LAST)
                {
                    brickLastTxBand = _tx_band;
                    SetupForm.ATTOnTX = getTXstepAttenuatorForBand(_tx_band);
                }
            }
            foreach (Action a in brickAgcSyncs) a();
            {
                // tune power: lit while Setup - Transmit makes TUNE or 2TONE use this slider
                bool tuneOn = TuneDrivePowerOrigin == DrivePowerSource.TUNE_SLIDER || TwoToneDrivePowerOrigin == DrivePowerSource.TUNE_SLIDER;
                Color want = tuneOn ? lblPWR.ForeColor : cMuted;
                if (!lblTune.Enabled) lblTune.Enabled = true;   // a disabled label would ignore the colour
                if (lblTune.ForeColor != want) lblTune.ForeColor = want;
                if (lblTune.Text.StartsWith("Tu | 2T")) lblTune.Text = "Tune" + lblTune.Text.Substring(7);
                // like Drive: while transmitting, the slider works only for the mode that uses it (TUNE / 2TONE), not for a normal over
                bool tuneCan;
                if (chkTUN.Checked) tuneCan = TuneDrivePowerOrigin == DrivePowerSource.TUNE_SLIDER;
                else if (chk2TONE.Checked) tuneCan = TwoToneDrivePowerOrigin == DrivePowerSource.TUNE_SLIDER;
                else if (_mox) tuneCan = false;
                else tuneCan = tuneOn;
                if (ptbTune.Enabled != tuneCan) ptbTune.Enabled = tuneCan;
            }
            brickKeepBandFont(txtVFOABand); brickKeepBandFont(txtVFOBBand);
            pbAutoAttWarningRX1.Visible = false; pbAutoAttWarningRX2.Visible = false;
            brickMirrorLabel(lblFbMirror, infoBar.FeedbackLabel); brickMirrorLabel(lblPsMirror, infoBar.PureSignalLabel);
            if (_mox && picMultiMeterDigital != null) picMultiMeterDigital.Invalidate();
            if (picRX2Meter != null && picRX2Meter.Visible) picRX2Meter.Invalidate();
        }

        #endregion

        #region s-meter

        private readonly double[] brickMeterAvg = { -200.0, -200.0 };
        private readonly double[] brickMeterPeak = { -200.0, -200.0 };
        private readonly DateTime[] brickMeterPeakTime = { DateTime.MinValue, DateTime.MinValue };

        private Rectangle brickPwrLabelRect = Rectangle.Empty;
        private string BrickPowerFile { get { return Path.Combine(AppDataPath, "power_scale.txt"); } }

        // default full scale of the power bar for the connected board (from the MAC), 0 when unknown
        private static float brickMacDefaultMax()
        {
            string m = BrickHardware.Mac;
            if (m.Length < 6) return 0;
            switch (m.Substring(0, 6))
            {
                case "02B201": return 25f;
                case "02B203": return 60f;
                case "02B301": return 25f;
                case "02B303": return 60f;
            }
            return 0;
        }

        private float brickUserMax()
        {
            try
            {
                string key = BrickHardware.Mac.Length >= 6 ? BrickHardware.Mac.Substring(0, 6) : "MODEL";
                if (File.Exists(BrickPowerFile))
                    foreach (string line in File.ReadAllLines(BrickPowerFile))
                    {
                        string[] kv = line.Split('=');
                        float v;
                        if (kv.Length == 2 && kv[0].Trim() == key && float.TryParse(kv[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) && v > 0) return v;
                    }
            }
            catch { }
            return 0;
        }

        private void brickSetUserMax(float v)
        {
            try
            {
                string key = BrickHardware.Mac.Length >= 6 ? BrickHardware.Mac.Substring(0, 6) : "MODEL";
                List<string> lines = new List<string>();
                if (File.Exists(BrickPowerFile))
                    foreach (string line in File.ReadAllLines(BrickPowerFile)) if (!line.StartsWith(key + "=")) lines.Add(line);
                lines.Add(key + "=" + v.ToString(System.Globalization.CultureInfo.InvariantCulture));
                File.WriteAllLines(BrickPowerFile, lines.ToArray());
            }
            catch { }
        }

        private void brickAskPowerMax()
        {
            using (Form f = new Form())
            using (TextBox tb = new TextBox())
            using (Button ok = new Button())
            {
                f.Text = "Power scale, W";
                f.FormBorderStyle = FormBorderStyle.FixedToolWindow;
                f.StartPosition = FormStartPosition.CenterParent;
                f.ClientSize = new Size(220, 64);
                tb.SetBounds(10, 10, 200, 22);
                tb.Text = brickPaMax().ToString("0");
                ok.Text = "OK"; ok.DialogResult = DialogResult.OK; ok.SetBounds(140, 36, 70, 22);
                f.Controls.Add(tb); f.Controls.Add(ok);
                f.AcceptButton = ok;
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    float v;
                    if (float.TryParse(tb.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) && v >= 1 && v <= 2000)
                        brickSetUserMax(v);
                }
            }
        }

        // power the scale of the transmit bar ends at
        private float brickPaMax()
        {
            float u = brickUserMax();
            if (u > 0) return u;
            float m = brickMacDefaultMax();
            if (m > 0) return m;
            switch (HardwareSpecific.Model)
            {
                case HPSDRModel.HERMES:
                case HPSDRModel.ANAN10:
                case HPSDRModel.ANAN10E:
                case HPSDRModel.HERMESLITE:
                    return 10f;
                case HPSDRModel.ANAN200D:
                case HPSDRModel.ANAN8000D:
                    return 200f;
                default:
                    return 100f;
            }
        }

        private static string brickSUnits(double dbm)
        {
            if (dbm <= -199) return "S -";
            double s = (dbm + 127.0) / 6.0;
            if (s < 1) return "S 0";
            if (s <= 9.0) return "S " + ((int)Math.Round(s)).ToString();
            return "S9+" + ((int)Math.Round(dbm + 73.0)).ToString();
        }

        private static string brickMicroVolts(double dbm)
        {
            if (dbm <= -199) return "";
            double p = Math.Pow(10.0, dbm / 10.0) / 1000.0;
            double uv = Math.Sqrt(p * 50.0) * 1e6;
            return uv.ToString("0.00") + " µV";
        }

        // rxScale: green up to S9, then yellow and red towards +60
        private static void brickBar(Graphics g, int x, int y, int w, int h, double fraction, bool rxScale = false, bool swrScale = false)
        {
            if (fraction < 0) fraction = 0;
            if (fraction > 1) fraction = 1;
            using (System.Drawing.Drawing2D.GraphicsPath gp = brickRoundPath(new Rectangle(x, y, w, h), h / 2))
            using (SolidBrush groove = new SolidBrush(Color.FromArgb(40, 44, 58)))
                g.FillPath(groove, gp);
            int fw = (int)Math.Round(w * fraction);
            if (fw < 2) return;
            Rectangle fr = new Rectangle(x, y, fw, h);
            using (System.Drawing.Drawing2D.GraphicsPath gp = brickRoundPath(fr, h / 2))
            using (System.Drawing.Drawing2D.LinearGradientBrush lg = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Rectangle(x, y, w, h), Color.FromArgb(60, 200, 80), Color.FromArgb(230, 60, 50), System.Drawing.Drawing2D.LinearGradientMode.Horizontal))
            {
                Color gr = Color.FromArgb(60, 200, 80), ye = Color.FromArgb(235, 210, 60), re = Color.FromArgb(230, 60, 50);
                System.Drawing.Drawing2D.ColorBlend cb;
                if (swrScale)
                {
                    // SWR 1 / 1.5 / 2 / 3 at equal distances: green, yellow, red from 2 on
                    cb = new System.Drawing.Drawing2D.ColorBlend(4);
                    cb.Colors = new Color[] { gr, ye, re, re };
                    cb.Positions = new float[] { 0f, 1f / 3f, 2f / 3f, 1f };
                }
                else if (rxScale)
                {
                    // smooth: green at the left, yellow by S9, orange by S9+10, going red from S9+20
                    cb = new System.Drawing.Drawing2D.ColorBlend(6);
                    cb.Colors = new Color[] { gr, ye, Color.FromArgb(245, 150, 40), Color.FromArgb(238, 95, 45), re, re };
                    cb.Positions = new float[] { 0f, 0.474f, 0.562f, 0.649f, 0.82f, 1f };
                }
                else
                {
                    cb = new System.Drawing.Drawing2D.ColorBlend(3);
                    cb.Colors = new Color[] { gr, ye, re };
                    cb.Positions = new float[] { 0f, 0.55f, 1f };
                }
                lg.InterpolationColors = cb;
                g.FillPath(lg, gp);
            }
        }

        private void brickPaintSMeter(Graphics g, Size size, bool rx2)
        {
            int W = size.Width, H = size.Height;
            bool big = H >= 100;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(cCard);
            int r = rx2 ? 1 : 0;

            double dbm;
            if (rx2)
            {
                if (rx2_meter_data_ready)
                {
                    rx2_meter_current_data = rx2_meter_new_data;
                    rx2_meter_data_ready = false;
                }
                dbm = rx2_meter_current_data;
            }
            else
            {
                if (meter_data_ready)
                {
                    current_meter_data = new_meter_data;
                    current_swrmeter_data = new_swrmeter_data;
                    meter_data_ready = false;
                }
                dbm = current_meter_data;
            }

            bool tx = !rx2 && _mox;
            float pw = alex_fwd;
            double swNow = alex_swr < 1 ? 1 : alex_swr;
            if (!tx) brickTxAvgValid = false;
            else
            {
                // averaged readings: the numbers and bars do not jump
                if (!brickTxAvgValid) { brickPwrAvg = pw; brickSwrAvg = swNow; brickTxAvgValid = true; }
                else { brickPwrAvg += (pw - brickPwrAvg) * 0.2; brickSwrAvg += (swNow - brickSwrAvg) * 0.2; }
                pw = (float)brickPwrAvg;
            }
            string pwText = Math.Round(pw).ToString("0") + " W";

            float fBig = big ? 28f : (rx2 ? 14f : 20f), fSmall = big ? 12f : (rx2 ? 8.5f : 10.5f), fTick = big ? 8f : (rx2 ? 6f : 7f);
            int bh = big ? 14 : (rx2 ? 8 : 10);
            // low window: the meter texts shrink with the meter
            float ms = Math.Min(1f, H / (big ? 128f : (rx2 ? 52f : 74f)));
            if (ms < 1f) { fBig = Math.Max(9f, fBig * ms); fSmall = Math.Max(6.5f, fSmall * ms); fTick = Math.Max(5.5f, fTick * ms); bh = Math.Max(5, (int)(bh * ms)); }
            using (Font fb = new Font("Segoe UI", fBig, FontStyle.Bold))
            using (Font fs = new Font("Segoe UI", fSmall))
            using (Font ft = new Font("Segoe UI", fTick))
            using (Brush bo = new SolidBrush(tx ? cTx : cAccent))
            using (Brush bt = new SolidBrush(cText))
            using (Brush bm = new SolidBrush(cMuted))
            {
                float x0 = 2;
                if (rx2 || !big)
                {
                    g.DrawString(rx2 ? "RX2" : "RX1", ft, bm, 2, big ? 8 : 6);
                    x0 = 30;
                }

                if (tx)
                {
                    // two bars: power and SWR, both texts in the same size
                    g.DrawString(pwText, fb, bo, x0, -2);
                    SizeF sb = g.MeasureString(pwText, fb);
                    using (Brush yellow = new SolidBrush(Color.FromArgb(255, 214, 60)))
                    {
                        string swrText = "SWR " + brickSwrText((float)brickSwrAvg);
                        SizeF ss = g.MeasureString(swrText, fb);
                        // fixed place: the text is as wide as the largest value (9.9), so it does not move
                        SizeF sm = g.MeasureString("SWR 9.9", fb);
                        g.DrawString(swrText, fb, yellow, W - sm.Width - 2, -2);
                    }

                    float max = brickPaMax();
                    int bx = 32, bw = W - bx - 4;
                    int y1 = big ? 58 : 34;
                    int y2 = big ? 98 : 60;
                    g.DrawString("PWR", ft, bm, 2, y1 - 1);
                    g.DrawString("SWR", ft, bm, 2, y2 - 1);
                    brickBar(g, bx, y1, bw, bh, pw / max);
                    brickBar(g, bx, y2, bw, bh, brickSwrFraction(brickSwrAvg), false, true);
                    brickPwrLabelRect = Rectangle.Empty;
                    // marks on the SWR bar: yellow at 1.5, red at 2.0
                    using (Pen pyel = new Pen(Color.FromArgb(255, 214, 60), 2f))
                    using (Pen pred = new Pen(Color.FromArgb(235, 60, 50), 2f))
                    {
                        int mx1 = bx + (int)Math.Round(bw / 3.0), mx2 = bx + (int)Math.Round(bw * 2 / 3.0);
                        g.DrawLine(pyel, mx1, y2 - 2, mx1, y2 + bh + 2);
                        g.DrawLine(pred, mx2, y2 - 2, mx2, y2 + bh + 2);
                    }
                    if (big)
                    {
                        int steps = 4;
                        for (int i = 0; i <= steps; i++)
                        {
                            string s1 = (max * i / steps).ToString(max >= 100 || i % 1 == 0 ? "0" : "0.#");
                            SizeF sz = g.MeasureString(s1, ft);
                            float lx = Math.Min(Math.Max(0, bx + bw * i / (float)steps - sz.Width / 2), W - sz.Width);
                            g.DrawString(s1, ft, bm, lx, y1 - 14);
                        }
                        string[] swrTicks = { "1.0", "1.5", "2.0", "3.0" };
                        for (int i = 0; i < swrTicks.Length; i++)
                        {
                            SizeF sz2 = g.MeasureString(swrTicks[i], ft);
                            float lx2 = Math.Min(Math.Max(0, bx + bw * i / 3f - sz2.Width / 2), W - sz2.Width);
                            Color tc = i == 1 ? Color.FromArgb(255, 214, 60) : (i >= 2 ? Color.FromArgb(235, 60, 50) : cMuted);
                            using (Brush tb = new SolidBrush(tc)) g.DrawString(swrTicks[i], ft, tb, lx2, y2 - 14);
                        }
                        brickPwrLabelRect = new Rectangle(bx, y1 - 16, bw, 16);
                    }
                    return;
                }

                if (brickMeterAvg[r] <= -199 || double.IsNaN(brickMeterAvg[r])) brickMeterAvg[r] = dbm;
                brickMeterAvg[r] = dbm > brickMeterAvg[r] ? dbm * 0.8 + brickMeterAvg[r] * 0.2 : dbm * 0.2 + brickMeterAvg[r] * 0.8;
                double v = brickMeterAvg[r];
                if (v >= brickMeterPeak[r] || (DateTime.Now - brickMeterPeakTime[r]).TotalMilliseconds > 1200)
                {
                    brickMeterPeak[r] = v;
                    brickMeterPeakTime[r] = DateTime.Now;
                }

                string bigText2 = brickSUnits(v);
                g.DrawString(bigText2, fb, bo, x0, -2);
                SizeF szBig = g.MeasureString(bigText2, fb);
                if (v > -199)
                {
                    string dbmText = v.ToString("0.0") + " dBm";
                    g.DrawString(dbmText, fs, bt, x0 + szBig.Width - 4, big ? 12 : 5);
                    string uv = brickMicroVolts(v);
                    SizeF su = g.MeasureString(uv, ft);
                    g.DrawString(uv, ft, bm, W - su.Width - 2, big ? 4 : 3);
                    if (big)
                        g.DrawString("PK " + brickMeterPeak[r].ToString("0.0") + " dBm", ft, bm, x0 + szBig.Width - 4, 34);
                }

                int by = big ? H - 46 : H - bh - 16;
                int bxx = 2, bww = W - 4;
                brickBar(g, bxx, by, bww, bh, (v + 127.0) / 114.0, true);
                if (brickMeterPeak[r] > -199)
                {
                    double pf = Math.Min(1.0, Math.Max(0.0, (brickMeterPeak[r] + 127.0) / 114.0));
                    int px = bxx + (int)Math.Round(bww * pf);
                    using (Pen pk = new Pen(cTx, 2f)) g.DrawLine(pk, px, by - 3, px, by + bh + 3);
                }

                string[] labels = { "1", "3", "5", "7", "9", "+20", "+40", "+60" };
                double[] pos = { 0.053, 0.158, 0.263, 0.368, 0.474, 0.649, 0.825, 1.0 };
                for (int i = 0; i < labels.Length; i++)
                {
                    int x = bxx + (int)Math.Round(bww * pos[i]);
                    SizeF sz = g.MeasureString(labels[i], ft);
                    float lx = Math.Min(Math.Max(0, x - sz.Width / 2), W - sz.Width);
                    g.DrawString(labels[i], ft, bm, lx, by + bh + 1);
                }
            }
        }

        #endregion


        #region other windows

        private void brickDrawTab(object sender, DrawItemEventArgs e)
        {
            TabControl tc = (TabControl)sender;
            bool sel = e.Index == tc.SelectedIndex;
            Rectangle r = e.Bounds;
            using (Brush b = new SolidBrush(sel ? cCard : cBg)) e.Graphics.FillRectangle(b, r);
            if (sel) using (Pen p = new Pen(cAccent, 2f)) e.Graphics.DrawLine(p, r.Left, r.Top + 1, r.Right, r.Top + 1);
            TextRenderer.DrawText(e.Graphics, tc.TabPages[e.Index].Text, tc.Font, r, sel ? cAccent : cText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private readonly HashSet<Form> brickThemedForms = new HashSet<Form>();

        // dark colours for a window of the old code (setup, equalizer, ...); done once per window
        private void brickThemeForm(Form f)
        {
            if (f == null || f == this || f.IsDisposed || brickThemedForms.Contains(f)) return;
            string n = f.GetType().Name;
            if (n == "Splash" || n == "LogForm") return;
            brickThemedForms.Add(f);
            f.SuspendLayout();
            if (f.IsHandleCreated) Common.UseImmersiveDarkMode(f.Handle, true);
            else f.HandleCreated += (s, e) => Common.UseImmersiveDarkMode(f.Handle, true);
            f.BackColor = cBg;
            f.ForeColor = cText;
            foreach (Control c in f.Controls) brickStyleControl(c);
            f.ResumeLayout(false);
        }

        private void brickThemeOpenForms()
        {
            if (!IsSetupFormNull) brickThemeForm(SetupForm);
            foreach (Form f in new List<Form>(Application.OpenForms.Cast<Form>()))
                if (f != this) brickThemeForm(f);
        }

        #endregion

        #region debug

        private void brickDumpControlTree()
        {
            string path = Environment.GetEnvironmentVariable("BRICK_DUMP_UI");
            if (string.IsNullOrEmpty(path)) return;

            StringBuilder sb = new StringBuilder();
            dumpControl(this, 0, sb);
            File.WriteAllText(path, sb.ToString());
        }

        private static void dumpControl(Control c, int depth, StringBuilder sb)
        {
            string text = c.Text ?? "";
            if (text.Length > 30) text = text.Substring(0, 30);
            sb.Append(new string(' ', depth * 2))
              .Append(c.Name).Append(" [").Append(c.GetType().Name).Append("] ")
              .Append(c.Bounds.X).Append(',').Append(c.Bounds.Y).Append(' ').Append(c.Bounds.Width).Append('x').Append(c.Bounds.Height)
              .Append(c.Visible ? "" : " HIDDEN").Append(c.Enabled ? "" : " DISABLED").Append(c is CheckBox && ((CheckBox)c).Checked ? " CHECKED" : "").Append(" dock=").Append(c.Dock)
              .Append(" '").Append(text.Replace("\r", " ").Replace("\n", " ")).Append("'\r\n");
            foreach (Control child in c.Controls) dumpControl(child, depth + 1, sb);
        }

        #endregion
    }
}
