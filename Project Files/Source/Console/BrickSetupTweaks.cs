using System;
using System.Drawing;
using System.Windows.Forms;

namespace BrickSDR
{
    public partial class Setup
    {
        // CFC on/off as set in Setup - Transmit (restored after a RADE over)
        internal bool TXCFCOn { get { return chkCFCEnable.Checked; } }

        // NR3 / NR4 are both served by the neural noise reduction (NNR) of WDSP 2.11,
        // only the reduction depth is adjustable
        // there are no skins in Brick-SDR (the interface is built in code): the skin list, Remove and Open Skins Folder go,
        // the two background options stay
        private void BrickHideSkins()
        {
            comboAppSkin.Visible = false; btnRemoveSkin.Visible = false; btnOpenSkinsFolder.Visible = false;
            grpAppSkins.Text = "Spectrum background";
            chkDisablePicDisplayBackgroundImage.Top = comboAppSkin.Top + 2;
            chkMaintainBackgroundAspectRatio.Top = chkDisablePicDisplayBackgroundImage.Bottom + 6;
            // the "General" block of Appearance does nothing in this interface (LED font, button and slider limit colours, dark title bars) except
            // "Allow smeared waterfalls": that one moves up next to the background options, the block goes
            chkWaterfall_smear.Parent = grpAppSkins;
            chkWaterfall_smear.Location = new Point(chkMaintainBackgroundAspectRatio.Left, chkMaintainBackgroundAspectRatio.Bottom + 4);
            grpAppSkins.Height = chkWaterfall_smear.Bottom + 12;
            groupBoxTS31.Visible = false;
            groupBoxTS70.Top = grpAppSkins.Bottom + 8;
        }

        // Brick2 (Hermes) and Brick3 (ANAN-100D) in the model list: Alex is present (ticked), Apollo does not exist (unticked, greyed)
        public void BrickApplyModelRestrictions()
        {
            string m = comboRadioModel.Text ?? "";
            if (!m.Equals("Brick2", StringComparison.OrdinalIgnoreCase) && !m.Equals("Brick3", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                chkApolloPresent.Checked = false;
                chkApolloPresent.Enabled = false;
                chkAlexPresent.Enabled = true;
                chkAlexPresent.Checked = true;
            }
            catch { }
        }

        // Appearance: font and colour of the frequency display, colour of lit buttons
        private void BrickAddAppearanceOptions()
        {
            try
            {
                GroupBox g = new GroupBox { Name = "brickGrpLook", Text = "Frequency display and colours" };
                int top = Math.Min(groupBoxTS70.Bottom + 8, tpAppearanceGeneral.ClientSize.Height - 158);
                g.SetBounds(8, top, 294, 152);

                g.Controls.Add(new Label { Text = "Frequency font", AutoSize = true, Location = new Point(8, 26) });
                ComboBox cb = new ComboBox { Name = "brickVfoFont", DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(96, 22), Width = 114, DropDownWidth = 280, MaxDropDownItems = 16 };
                Action fill = () =>
                {
                    cb.Items.Clear();
                    int sel = 0;
                    foreach (BrickAppearance.FontItem it in BrickAppearance.FontItems())
                    {
                        int i = cb.Items.Add(it);
                        if (it.Key == BrickAppearance.VfoFontKey) sel = i;
                    }
                    cb.SelectedIndex = sel;
                };
                fill();
                cb.SelectedIndexChanged += (s, e) =>
                {
                    BrickAppearance.FontItem it = cb.SelectedItem as BrickAppearance.FontItem;
                    if (it != null) BrickAppearance.SetVfoFont(it.Key);
                };
                Button load = new Button { Name = "brickLoadFont", Text = "Load font...", Location = new Point(216, 21), Size = new Size(70, 24) };
                load.Click += (s, e) =>
                {
                    using (OpenFileDialog ofd = new OpenFileDialog())
                    {
                        ofd.Filter = "Fonts (*.ttf, *.otf)|*.ttf;*.otf";
                        if (ofd.ShowDialog(this) != DialogResult.OK) return;
                        string fam = BrickAppearance.AddFontFile(ofd.FileName);
                        if (fam == null) { MessageBox.Show(this, "This file is not a usable font.", "Load font", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                        BrickAppearance.SetVfoFont(fam);
                        fill();
                    }
                };
                g.Controls.Add(cb); g.Controls.Add(load);

                // size of the digits of any font, in % of the designed size (the box makes them smaller when they do not fit its width)
                g.Controls.Add(new Label { Text = "Font size, %", AutoSize = true, Location = new Point(8, 58) });
                NumericUpDown nsz = new NumericUpDown { Name = "brickVfoSize", Minimum = 40, Maximum = 300, Increment = 5, Location = new Point(96, 54), Width = 60 };
                nsz.Value = Math.Max(nsz.Minimum, Math.Min(nsz.Maximum, BrickAppearance.VfoSizePercent));
                nsz.ValueChanged += (s, e) => BrickAppearance.SetVfoSize((int)nsz.Value);
                Button szDef = new Button { Name = "brickVfoSizeDefault", Text = "Default", Location = new Point(164, 53), Size = new Size(60, 24) };
                szDef.Click += (s, e) => nsz.Value = 100;
                g.Controls.Add(nsz); g.Controls.Add(szDef);

                // colour pickers: a swatch (click to choose) and Default
                Func<string, int, Func<Color>, Action<Color>, Action, Button> picker = (text, y, get, set, reset) =>
                {
                    g.Controls.Add(new Label { Text = text, AutoSize = true, Location = new Point(8, y + 5) });
                    Button sw = new Button { Location = new Point(126, y), Size = new Size(60, 24), FlatStyle = FlatStyle.Flat, BackColor = get(), Tag = "brickSwatch" };
                    sw.FlatAppearance.BorderColor = Color.Gray;
                    sw.Click += (s, e) =>
                    {
                        using (ColorDialog d = new ColorDialog { Color = get(), FullOpen = true })
                            if (d.ShowDialog(this) == DialogResult.OK) { set(d.Color); sw.BackColor = get(); }
                    };
                    Button df = new Button { Text = "Default", Location = new Point(194, y), Size = new Size(60, 24) };
                    df.Click += (s, e) => { reset(); sw.BackColor = get(); };
                    g.Controls.Add(sw); g.Controls.Add(df);
                    return sw;
                };
                picker("Frequency colour", 88,() => BrickAppearance.FrequencyColor, c => BrickAppearance.SetVfoColor(c), () => BrickAppearance.SetVfoColor(null));
                picker("Lit buttons colour", 120,() => BrickAppearance.Accent, c => BrickAppearance.SetAccent(c), () => BrickAppearance.SetAccent(BrickAppearance.DefaultAccent));

                tpAppearanceGeneral.Controls.Add(g);
                g.BringToFront();
            }
            catch { }
        }

        public void BrickSimplifyNR()
        {
            BrickHideSkins();
            BrickAddAppearanceOptions();
            BrickAddBootloaderButton();
            BrickAddRadeMeterOption();
            BrickAddDigiModesTab();
            BrickAddKeepStepOption();
            grpRNnoise.Visible = false;
            groupBoxTS55.Text = "NNR (NR3 / NR4) RX1";
            groupBoxTS56.Text = "NNR (NR3 / NR4) RX2";
            foreach (GroupBox g in new GroupBox[] { groupBoxTS55, groupBoxTS56 })
            {
                foreach (Control c in g.Controls)
                {
                    bool keep = c == nudNR4_red_rx1 || c == nudNR4_red_rx2;
                    if (c is Label)
                    {
                        string t = c.Text ?? "";
                        keep = t == "Reduction" || t == "dB";
                    }
                    c.Visible = keep;
                }
                g.Height = 70;
            }
            groupBoxTS56.Top = groupBoxTS55.Bottom + 6;
        }

        // "Keep my tuning step" under "Recover TuneStep per Mode": no automatic step per mode (SSB 500 Hz, CW 10 Hz...)
        private void BrickAddKeepStepOption()
        {
            try
            {
                CheckBox c = new CheckBox();
                c.Name = "brickChkKeepStep";
                c.Text = "Keep my tuning step";
                c.AutoSize = false;
                c.Size = new System.Drawing.Size(240, 16);
                c.ForeColor = chkTuneStepPerModeRX1.ForeColor;
                c.Location = new System.Drawing.Point(chkTuneStepPerModeRX1.Left, chkTuneStepPerModeRX1.Bottom);
                c.Checked = console.BrickKeepStep;
                c.CheckedChanged += (s, e) => console.BrickKeepStep = c.Checked;
                chkTuneStepPerModeRX1.Parent.Controls.Add(c);
                Control g = chkTuneStepPerModeRX1.Parent;      // the group grows by the new line and moves up so it still fits the tab page
                g.Height = c.Bottom + 6;
                g.Top = Math.Max(2, Math.Min(g.Top, g.Parent.ClientSize.Height - g.Height - 2));
            }
            catch { }
        }

        // the panadapter / waterfall mode menus of the main window live in the Panadapter groups of RX1 / RX2,
        // and the spectrum background picture is chosen on the General tab
        public void BrickAddDisplayModes(ComboBox rx1, ComboBox rx2)
        {
            addModeCombo(grpDisplayAverage, rx1);
            addModeCombo(grpRX2DisplayAverage, rx2);

            GroupBox g = new GroupBox();
            g.Text = "Spectrum background picture";
            g.SetBounds(398, 148, 162, 142);
            TextBox path = new TextBox { Name = "brickBgPath", ReadOnly = true, Location = new Point(8, 22), Width = 146 };
            path.Text = console.BrickBackgroundPath;
            Button browse = new Button { Text = "Browse...", Location = new Point(8, 52), Size = new Size(70, 24) };
            Button reset = new Button { Text = "Default", Location = new Point(84, 52), Size = new Size(70, 24) };
            browse.Click += (s, e) =>
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    ofd.Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*";
                    if (ofd.ShowDialog(this) == DialogResult.OK)
                    {
                        console.BrickSetBackground(ofd.FileName);
                        path.Text = ofd.FileName;
                        chkDisablePicDisplayBackgroundImage.Checked = false;
                    }
                }
            };
            reset.Click += (s, e) =>
            {
                console.BrickSetBackground(null);
                path.Text = console.BrickBackgroundPath;
                chkDisablePicDisplayBackgroundImage.Checked = false;
            };
            Label hint = new Label { Text = "Use \"Ignore spectral area image\" to turn it off.", Location = new Point(8, 80), Size = new Size(148, 28) };
            g.Controls.Add(path); g.Controls.Add(browse); g.Controls.Add(reset); g.Controls.Add(hint);
            // how the 3D panadapter draws its ridges (no free room on the page: it shares this group)
            Label l3 = new Label { Name = "brick3DStyleLabel", Text = "3D", AutoSize = true, Location = new Point(8, 115) };
            ComboBox st = new ComboBox { Name = "brick3DStyle", DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(30, 111), Width = 84, DropDownWidth = 120 };
            st.Items.AddRange(new object[] { "Classic", "Proportional", "Custom" });
            // Custom: the height of the peaks as % of the area (classic is 58)
            NumericUpDown nh = new NumericUpDown { Name = "brick3DHeight", Minimum = 20, Maximum = 300, Increment = 5, Location = new Point(118, 111), Width = 38 };
            nh.Value = Math.Max(nh.Minimum, Math.Min(nh.Maximum, (decimal)console.Brick3DCustom));
            nh.Enabled = console.Brick3DStyle == 2;
            new ToolTip().SetToolTip(nh, "Peak height in % of the area (Classic = 58)");
            st.SelectedIndex = Math.Max(0, Math.Min(2, console.Brick3DStyle));
            st.SelectedIndexChanged += (s, e) => { console.Brick3DStyle = st.SelectedIndex; nh.Enabled = st.SelectedIndex == 2; };
            nh.ValueChanged += (s, e) => console.Brick3DCustom = (float)nh.Value;
            g.Controls.Add(l3); g.Controls.Add(st); g.Controls.Add(nh);
            tpDisplayGeneral.Controls.Add(g);
            g.BringToFront();
        }

        // colouring of the spectrum line, next to "Fill Panadapter"
        public void BrickAddLineColouring()
        {
            ComboBox cb = new ComboBox { Name = "brickLineMode", DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(112, 111), Width = 156 };
            cb.Items.AddRange(new object[] { "Line: standard", "Line: waterfall colours", "Line: noise std + signal WF", "Line: noise fill colour + WF" });
            cb.SelectedIndex = console.BrickLineMode;
            cb.SelectedIndexChanged += (s, e) => console.BrickSetLineMode(cb.SelectedIndex);
            grpDisplayRefreshRates.Controls.Add(cb);

            // colour of the spectrum fill, inside "Phase Mode" on the right (the same setting as Appearance - Display - Data Fill);
            // a click on the swatch opens the colour dialog
            Label fl = new Label { Name = "brickFillLabel", Text = "Fill", AutoSize = true, Location = new Point(132, 18) };
            Button swatch = new Button { Name = "brickFillSwatch", Text = "", FlatStyle = FlatStyle.Flat, Location = new Point(158, 14), Size = new Size(32, 22), BackColor = Color.FromArgb(255, clrbtnDataFill.Color) };
            swatch.FlatAppearance.BorderColor = Color.Gray;
            swatch.Click += (s, e) =>
            {
                using (ColorDialog d = new ColorDialog { Color = clrbtnDataFill.Color, FullOpen = true })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    clrbtnDataFill.Color = d.Color;           // saved with the other options
                    Display.DataFillColor = Color.FromArgb(tbDataFillAlpha.Value, d.Color);
                    swatch.BackColor = d.Color;
                }
            };
            clrbtnDataFill.Changed += (s, e) => swatch.BackColor = Color.FromArgb(255, clrbtnDataFill.Color);
            grpDisplayPhase.Controls.Add(fl); grpDisplayPhase.Controls.Add(swatch);
            swatch.BringToFront();
        }

        private void addModeCombo(GroupBox host, ComboBox c)
        {
            Label l = new Label { Text = "Panadapter mode", AutoSize = true, Location = new Point(6, 204) };
            host.Controls.Add(l);
            c.FlatStyle = FlatStyle.Standard;
            c.BackColor = SystemColors.Window;
            c.ForeColor = SystemColors.WindowText;
            c.Dock = DockStyle.None;
            c.Visible = true;
            c.SetBounds(6, 222, 140, 21);
            host.Controls.Add(c);
        }
    }
}
