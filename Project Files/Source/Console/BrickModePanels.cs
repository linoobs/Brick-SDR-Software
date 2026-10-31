using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BrickSDR
{
    // Mode specific panel at the bottom of the left column (SSB, CW, FM, digital):
    // re-arranges the old controls and swaps number boxes / combo boxes for the Brick-SDR ones.
    public partial class Console
    {
        // "-  value  +" for any numeric up-down; holding a button repeats. The old box stays out of sight and does the work.
        private List<Control> brickValueStepper(Control parent, NumericUpDown nud, int x, int y, int w, string suffix = "")
        {
            List<Control> made = new List<Control>();
            nud.Parent = parent;
            nud.Location = new Point(-3000, 0);

            Button minus = new Button { Text = "−" };
            Button plus = new Button { Text = "+" };
            Label val = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI Semibold", 10f), ForeColor = cAccent, BackColor = Color.Transparent };
            int bw = 26;
            minus.SetBounds(x, y, bw, 24);
            val.SetBounds(x + bw + 2, y, w - 2 * bw - 4, 24);
            plus.SetBounds(x + w - bw, y, bw, 24);

            int dir = 0;
            System.Windows.Forms.Timer rep = new System.Windows.Forms.Timer { Interval = 350 };
            Action stepOnce = () =>
            {
                decimal v = nud.Value + dir * nud.Increment;
                if (v < nud.Minimum) v = nud.Minimum;
                if (v > nud.Maximum) v = nud.Maximum;
                if (v != nud.Value) nud.Value = v;
            };
            rep.Tick += (s, e) => { rep.Interval = 70; stepOnce(); };
            minus.MouseDown += (s, e) => { dir = -1; stepOnce(); rep.Interval = 350; rep.Start(); };
            plus.MouseDown += (s, e) => { dir = 1; stepOnce(); rep.Interval = 350; rep.Start(); };
            minus.MouseUp += (s, e) => rep.Stop(); plus.MouseUp += (s, e) => rep.Stop();
            minus.MouseLeave += (s, e) => rep.Stop(); plus.MouseLeave += (s, e) => rep.Stop();

            brickAgcSyncs.Add(() => { foreach (Control c in made) if (c.Enabled != nud.Enabled) c.Enabled = nud.Enabled; });

            Action show = () => { string t = nud.Value.ToString("F" + nud.DecimalPlaces) + suffix; if (val.Text != t) val.Text = t; };
            nud.ValueChanged += (s, e) => show();
            show();

            parent.Controls.Add(minus); parent.Controls.Add(val); parent.Controls.Add(plus);
            made.Add(minus); made.Add(val); made.Add(plus);
            return made;
        }

        // flat drop-down for a combo box; the combo stays out of sight and does the work
        private BrickDropDown brickComboDropDown(Control parent, ComboBox combo, int x, int y, int w)
        {
            combo.Parent = parent;
            combo.Location = new Point(-3000, 0);
            BrickDropDown dd = new BrickDropDown();
            dd.SetBounds(x, y, w, 24);
            string sig = null;
            dd.SelectedChanged += (s, e) => { if (dd.SelectedIndex >= 0 && combo.SelectedIndex != dd.SelectedIndex) combo.SelectedIndex = dd.SelectedIndex; };
            Action sync = () =>
            {
                string now = combo.Items.Count.ToString();
                for (int i = 0; i < combo.Items.Count; i++) now += "|" + combo.GetItemText(combo.Items[i]);
                if (now != sig)
                {
                    sig = now;
                    List<string> names = new List<string>();
                    for (int i = 0; i < combo.Items.Count; i++) names.Add(combo.GetItemText(combo.Items[i]));
                    dd.SetItems(names);
                }
                if (combo.SelectedIndex >= 0 && dd.SelectedIndex != combo.SelectedIndex) dd.SelectedIndex = combo.SelectedIndex;
                if (combo.SelectedIndex < 0 && combo.Text.Length > 0 && dd.Text != combo.Text) dd.Text = combo.Text;
                if (dd.Enabled != combo.Enabled) dd.Enabled = combo.Enabled;
            };
            combo.SelectedIndexChanged += (s, e) => sync();
            brickAgcSyncs.Add(sync);
            parent.Controls.Add(dd);
            sync();
            return dd;
        }

        private void brickStyleModePanels()
        {
            // ---- SSB / AM: no Low / High, the EQ and filter buttons move up next to MIC and COMP
            foreach (Control c in new Control[] { udTXFilterLow, udTXFilterHigh, lblTXLow, lblTXHigh })
            {
                Control cc = c;
                cc.Visible = false;
                // the old code shows them again on every mode change
                cc.VisibleChanged += (s, e) => { if (cc.Visible) cc.Visible = false; };
            }
            chkRXEQ.Text = "RX EQ"; chkTXEQ.Text = "TX EQ"; chkShowTXFilter.Text = "TX FL";
            Put(chkRXEQ, panelModeSpecificPhone, 214, 5, 56, 23);
            Put(chkTXEQ, panelModeSpecificPhone, 274, 5, 56, 23);
            Put(chkShowTXFilter, panelModeSpecificPhone, 274, 36, 56, 23);
            // MIC, COMP, VOX and DEXP in rows of the same pitch (31 px) as TX EQ / TX FL
            PrettyTrackBar[] sl = { ptbMic, ptbCPDR, ptbVOX, ptbNoiseGate };
            CheckBox[] bt = { chkMicMute, chkCPDR, chkVOX, chkNoiseGate };
            Label[] vl = { lblMicVal, lblCPDRVal, lblVOXVal, lblNoiseGateVal };
            ptbNoiseGate.HeadImage = ptbMic.HeadImage;
            for (int i = 0; i < 4; i++)
            {
                int ry = 5 + i * 31;
                Put(bt[i], panelModeSpecificPhone, 5, ry, 50, 23);
                if (i < 3) { Put(sl[i], panelModeSpecificPhone, 58, ry, 100, 24); Put(vl[i], panelModeSpecificPhone, 162, ry, 50, 23); }
            }
            Put(picVOX, panelModeSpecificPhone, 68, 5 + 2 * 31 + 21, 83, 3);
            picNoiseGate.Visible = false;
            // DEXP has no slider of its own (its threshold is the VOX one), keep it hidden
            foreach (Control c in new Control[] { ptbNoiseGate, lblNoiseGateVal })
            {
                Control cc = c;
                cc.Visible = false;
                cc.VisibleChanged += (s, e) => { if (cc.Visible) cc.Visible = false; };
            }
            // ---- CW (break-in above, pitch below)
            lblCWPitchFreq.AutoSize = false;
            Put(lblCWPitchFreq, panelModeSpecificCW, 118, 143, 100, 17);
            lblCWPitchFreq.TextAlign = ContentAlignment.MiddleRight;
            brickValueStepper(panelModeSpecificCW, udCWPitch, 224, 139, 106);
            // the Zero beat button of Thetis (the strongest peak of the passband goes to the CW pitch), it was hidden before (GitHub issue 2)
            Put(btnZeroBeat, panelModeSpecificCW, 8, 139, 100, 24);
            btnZeroBeat.Text = "Zero beat"; btnZeroBeat.Visible = true;
            grpSemiBreakIn.SetBounds(112, 92, 218, 45);
            Put(chkQSK, grpSemiBreakIn, 4, 16, 48, 23);
            Put(lblCWBreakInDelay, grpSemiBreakIn, 54, 21, 66, 17);
            brickValueStepper(grpSemiBreakIn, udCWBreakInDelay, 122, 15, 90);

            // ---- FM: more air between the rows, RPTR offset with Simplex and Rev in the right column
            Put(lblFMMic, panelModeSpecificFM, 8, 7, 32, 20);
            Put(ptbFMMic, panelModeSpecificFM, 55, 5, 100, 24);
            Put(lblMicValFM, panelModeSpecificFM, 158, 5, 49, 23);
            Put(lblFMDeviation, panelModeSpecificFM, 8, 41, 68, 20);
            Put(radFMDeviation2kHz, panelModeSpecificFM, 76, 40, 50, 24);
            Put(radFMDeviation5kHz, panelModeSpecificFM, 128, 40, 50, 24);
            Put(chkFMCTCSS, panelModeSpecificFM, 8, 74, 70, 24);
            brickComboDropDown(panelModeSpecificFM, comboFMCTCSS, 106, 74, 84);
            Put(btnFMMemory, panelModeSpecificFM, 8, 108, 70, 24);
            Put(btnFMMemoryDown, panelModeSpecificFM, 80, 108, 24, 24);
            brickComboDropDown(panelModeSpecificFM, comboFMMemory, 106, 108, 86);
            Put(btnFMMemoryUp, panelModeSpecificFM, 194, 108, 24, 24);

            lblFMOffset.AutoSize = false;
            Put(lblFMOffset, panelModeSpecificFM, 212, 8, 124, 17);
            lblFMOffset.Text = "RPTR offset, MHz";
            lblFMOffset.TextAlign = ContentAlignment.MiddleCenter;
            brickValueStepper(panelModeSpecificFM, udFMOffset, 224, 30, 106);
            Put(chkFMTXLow, panelModeSpecificFM, 224, 70, 26, 24);
            Put(chkFMTXSimplex, panelModeSpecificFM, 252, 70, 50, 24);
            Put(chkFMTXHigh, panelModeSpecificFM, 304, 70, 26, 24);
            Put(chkFMTXRev, panelModeSpecificFM, 252, 100, 50, 24);
            // ---- digital: sample rate and channels at the top, level with the VAC1 caption
            grpDIGSampleRate.Visible = false; grpVACStereo.Visible = false;
            chkVACStereo.Parent = panelModeSpecificDigital;
            chkVACStereo.Appearance = Appearance.Button;
            chkVACStereo.Text = "Stereo";
            chkVACStereo.SetBounds(250, 4, 78, 24);
            chkVACStereo.Visible = true;
            comboVACSampleRate.Parent = panelModeSpecificDigital;
            brickComboDropDown(panelModeSpecificDigital, comboVACSampleRate, 164, 4, 82);
        }
    }
}
