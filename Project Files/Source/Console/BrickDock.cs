/*  BrickDock.cs

    The strip under the panadapter where the RTTY / CW terminal and the FreeDV strip dock. One thing is docked at a time;
    the panadapter gives up the height. A bar at its top drags the height.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;

namespace BrickSDR
{
    public partial class Console
    {
        private Panel brickDispCard;                    // the card holding the panadapter (BrickLayout.buildCenter)
        private Panel _dockGrip;
        private string brickDockKey;                    // what is docked now ("digi", "rade"), null when nothing
        private readonly Dictionary<string, int> brickDockHeights = new Dictionary<string, int> { { "digi", 270 }, { "rade", 118 }, { "ft", 280 } };
        private readonly Dictionary<string, int> brickDockMin = new Dictionary<string, int> { { "digi", 210 }, { "rade", 100 }, { "ft", 220 } };

        private void brickDockEnsure()
        {
            if (_pnlDigi != null || brickDispCard == null) return;
            _pnlDigi = new Panel { Dock = DockStyle.Bottom, Height = 250, BackColor = cCard, Visible = false, Name = "brickDockPanel" };
            Panel grip = _dockGrip = new Panel { Dock = DockStyle.Top, Height = 6, BackColor = cLine, Cursor = Cursors.SizeNS, Name = "brickDockGrip" };
            grip.MouseMove += (s, e) =>
            {
                if (e.Button != MouseButtons.Left || brickDockKey == null) return;
                int min = brickDockMin[brickDockKey], max = Math.Max(min, brickDispCard.Height - 170);
                int h = Math.Max(min, Math.Min(max, _pnlDigi.Height - e.Y));
                if (h != _pnlDigi.Height) { _pnlDigi.Height = h; brickDockHeights[brickDockKey] = h; }
            };
            _pnlDigi.Controls.Add(grip);
            brickDispCard.Controls.Add(_pnlDigi);
            brickDispCard.Controls.SetChildIndex(_pnlDigi, 1);      // behind the panadapter (Fill goes last), in front of the RX2 pan row
        }

        private void brickDockShow(string key, Control pane)
        {
            brickDockEnsure();
            if (_pnlDigi == null) return;
            // whatever else is docked makes room
            foreach (Control k in new List<Control>(_pnlDigi.Controls.Cast<Control>()))
                if (k != pane && k.Name != "brickDockGrip") { k.Visible = false; _pnlDigi.Controls.Remove(k); }
            if (pane.Parent != _pnlDigi) { pane.Dock = DockStyle.Fill; _pnlDigi.Controls.Add(pane); }
            pane.Dock = DockStyle.Fill;
            pane.Visible = true;
            pane.BringToFront();
            _pnlDigi.Controls.SetChildIndex(_dockGrip, 0);
            brickDockKey = key;
            _pnlDigi.Height = Math.Max(brickDockMin[key], Math.Min(brickDockHeights[key], Math.Max(brickDockMin[key], brickDispCard.Height - 170)));
            _pnlDigi.Visible = true;
        }

        private void brickDockHide(string key)
        {
            if (_pnlDigi == null || brickDockKey != key) return;
            _pnlDigi.Visible = false;
            brickDockKey = null;
        }

        // something else wants the strip: whatever holds it lets go (it stays alive, just not shown)
        private void brickDockRelease(string wanting)
        {
            if (_pnlDigi == null || brickDockKey == null || brickDockKey == wanting) return;
            string held = brickDockKey;
            _pnlDigi.Visible = false;
            brickDockKey = null;
            if (held == "digi") digiClose();
            else if (held == "ft") ftRelease();
        }
    }
}
