/*  BrickDigiSetup.cs

    Setup - General - DIGI MODES (after Navigation): a tab for the digital modes with a page each for FT8 / FT4, RTTY and FreeDV.
    The FT8 / FT4 page has the Network Services group with PSK Reporter spotting (BrickPskReporter.cs).
    Built at run time like the other Brick additions to Setup; the choices are kept in digital.txt, not in the Setup database.
*/

using System;
using System.Drawing;
using System.Windows.Forms;

namespace BrickSDR
{
    public partial class Setup
    {
        private TabPage tpBrickDigi;
        private CheckBox brickChkPsk;
        private Label brickPskStatus;
        private Timer brickDigiTimer;

        public void BrickAddDigiModesTab()
        {
            if (tpBrickDigi != null) return;
            tpBrickDigi = new TabPage("DIGI MODES") { Name = "tpBrickDigiModes", BackColor = SystemColors.Control };
            TabControl tc = new TabControl { Name = "brickTcDigiModes", Dock = DockStyle.Fill };
            TabPage ft = new TabPage("FT8/FT4") { BackColor = SystemColors.Control };
            TabPage rtty = new TabPage("RTTY") { BackColor = SystemColors.Control };
            TabPage fdv = new TabPage("FreeDV") { BackColor = SystemColors.Control };
            tc.TabPages.AddRange(new[] { ft, rtty, fdv });
            tpBrickDigi.Controls.Add(tc);

            // FT8 / FT4: Network Services
            GroupBox grp = new GroupBox { Text = "Network Services", Location = new Point(10, 10), Size = new Size(680, 128) };
            brickChkPsk = new CheckBox { Name = "brickChkFtPsk", Text = "Enable PSK Reporter Spotting", AutoSize = true, Location = new Point(16, 26) };
            brickChkPsk.Checked = console.FtPskEnabled;
            brickChkPsk.CheckedChanged += (s, e) => { console.FtSetPsk(brickChkPsk.Checked); brickPskStatus.Text = "Status: " + console.FtPskStatus; };
            Label info = new Label
            {
                AutoSize = false, Location = new Point(34, 50), Size = new Size(630, 44),
                Text = "Sends the stations decoded in FT8 / FT4 (call sign, locator, SNR, frequency, mode and time of the signal), with your call sign and " +
                       "locator from the FT8 / FT4 window, to pskreporter.info. A report goes out about every five minutes while FT8 / FT4 is running."
            };
            brickPskStatus = new Label { AutoSize = false, Location = new Point(34, 98), Size = new Size(630, 20), Text = "Status: " + console.FtPskStatus };
            grp.Controls.AddRange(new Control[] { brickChkPsk, info, brickPskStatus });
            ft.Controls.Add(grp);

            rtty.Controls.Add(new Label { AutoSize = false, Location = new Point(12, 14), Size = new Size(680, 40), Text = "The RTTY settings (shift, baud, reverse, AFC, squelch, TX level) are in the RTTY window: DIGI menu - RTTY." });
            fdv.Controls.Add(new Label { AutoSize = false, Location = new Point(12, 14), Size = new Size(680, 40), Text = "The FreeDV settings (call sign, version, noise reduction, AGC, equalizer, Reporter) are in the FreeDV window: the Settings button of the FreeDV strip." });

            tcGeneral.TabPages.Add(tpBrickDigi);       // the last page, after Navigation

            // the status line follows the reporter while the tab is shown
            brickDigiTimer = new Timer { Interval = 1000 };
            brickDigiTimer.Tick += (s, e) =>
            {
                if (!tpBrickDigi.Visible) return;
                string t = "Status: " + console.FtPskStatus;
                if (brickPskStatus.Text != t) brickPskStatus.Text = t;
                bool on = console.FtPskEnabled;
                if (brickChkPsk.Checked != on) brickChkPsk.Checked = on;
            };
            brickDigiTimer.Start();
        }
    }
}
