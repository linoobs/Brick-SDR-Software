/*  BrickRadeSetup.cs

    Setup side of the RADE meters (from Kainos / Thetis-RADE): the "Hide if no RADE" option of a meter container and
    the place of the RADE meters in the list of meter types.
*/

using System;
using System.Drawing;
using System.Windows.Forms;

namespace BrickSDR
{
    public partial class Setup
    {
        private CheckBox chkContainer_hideRADEnotenabled;

        // Meter containers (Setup - Appearance - Meters/Gadgets): "Hide if no RADE". A plain CheckBox: the setting belongs
        // to the selected container, which saves it itself.
        public void BrickAddRadeMeterOption()
        {
            if (chkContainer_hideRADEnotenabled != null) return;
            chkContainer_hideRADEnotenabled = new CheckBox
            {
                Name = "chkContainer_hideRADEnotenabled",
                Text = "Hide if\r\nno RADE",
                AutoSize = true,
                CheckAlign = chkContainer_hidewhennotused.CheckAlign,
                TextAlign = chkContainer_hidewhennotused.TextAlign,
                RightToLeft = chkContainer_hidewhennotused.RightToLeft,
                Font = chkContainer_hidewhennotused.Font
            };
            toolTip1.SetToolTip(chkContainer_hideRADEnotenabled, "Hide this container while its receiver (RX1 or RX2 data) doesn't have RADE on.");
            chkContainer_hideRADEnotenabled.CheckedChanged += (s, e) =>
            {
                if (initializing) return;
                clsContainerComboboxItem cci = (clsContainerComboboxItem)comboContainerSelect.SelectedItem;
                if (cci != null) MeterManager.ContainerHidesWhenRADENotEnabled(cci.ID, chkContainer_hideRADEnotenabled.Checked);
            };
            grpMultiMeterHolder.Controls.Add(chkContainer_hideRADEnotenabled);
            // in the column of container options, right-aligned under "Hide if RX not in use"
            chkContainer_hideRADEnotenabled.Location = new Point(chkContainer_hidewhennotused.Right - chkContainer_hideRADEnotenabled.PreferredSize.Width, 140);
            clsContainerComboboxItem sel = comboContainerSelect.SelectedItem as clsContainerComboboxItem;
            if (sel != null) chkContainer_hideRADEnotenabled.Checked = MeterManager.ContainerHidesWhenRADENotEnabled(sel.ID);
        }

        // RADE meter types sit in the RX (0) or TX (1) part of the meters list; -1 for any other type
        private static int radeMeterBlock(MeterType t)
        {
            switch (t)
            {
                case MeterType.RADAE_SYNC:
                case MeterType.RADAE_SNR_DB:
                case MeterType.RADAE_RX_LEVEL_DB:
                case MeterType.RADAE_CLIP:
                case MeterType.RADAE_EOO_DECODE:
                    return 0;
                case MeterType.RADAE_TX_MIC_LEVEL_DB:
                case MeterType.RADAE_TX_MIC_CLIP:
                    return 1;
                default:
                    return -1;
            }
        }
    }
}
