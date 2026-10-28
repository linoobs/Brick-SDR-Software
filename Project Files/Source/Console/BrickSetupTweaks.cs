using System;
using System.Drawing;
using System.Windows.Forms;

namespace BrickSDR
{
    public partial class Setup
    {
        // NR3 / NR4 are both served by the neural noise reduction (NNR) of WDSP 2.11,
        // only the reduction depth is adjustable
        public void BrickSimplifyNR()
        {
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
            Label hint = new Label { Text = "Use \"Ignore spectral area image\" to turn it off.", Location = new Point(8, 84), Size = new Size(148, 52) };
            g.Controls.Add(path); g.Controls.Add(browse); g.Controls.Add(reset); g.Controls.Add(hint);
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
