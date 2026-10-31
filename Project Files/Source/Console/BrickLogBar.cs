/*  BrickLogBar.cs

    The log bar under the panadapter / waterfall: call sign, received report, name and QTH and a Save button. It is for the modes that don't
    log by themselves like FT8 / FT4 do (SSB, AM, FM, CW, RTTY by hand...); it is hidden while the FT8 / FT4 window is open and can be switched
    off in the DIGI menu (Log bar, `logbar=` in digital.txt). When a call sign is typed, name, QTH and locator are looked up on the internet
    (BrickCallLookup.cs) and filled in unless they were typed by hand. Save goes through QsoLogAdd (BrickQsoLog.cs): the local ADIF log and
    the online logbooks switched on in Setup.
*/

using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace BrickSDR
{
    public partial class Console
    {
        internal bool _digiLogBar = true;

        private Panel _pnlLogBar;
        private BrickField _lbCall, _lbRst, _lbName, _lbQth;
        private BrickBtn _lbSave, _lbClear, _lbQrz, _lbWeb;
        private Label _lbStatus;
        private System.Windows.Forms.Timer _lbDebounce;
        private DateTime _lbStart;
        private bool _lbNameEdited, _lbQthEdited, _lbFilling;
        private string _lbGrid = "", _lbLooked = "";
        private int _lbToken;

        private void brickLogBarEnsure()
        {
            if (_pnlLogBar != null || brickDispCard == null) return;
            _pnlLogBar = new Panel { Dock = DockStyle.Bottom, Height = 62, BackColor = cCard, Visible = false, Name = "brickLogBar" };
            Func<string, int, Label> lbl = (t, x) => { Label l = new Label { Text = t, AutoSize = true, ForeColor = cMuted, BackColor = cCard, Font = new Font("Segoe UI", 9f), Left = x, Top = 10 }; _pnlLogBar.Controls.Add(l); return l; };
            Func<int, int, int, BrickField> fld = (x, w, max) =>
            {
                BrickField f = new BrickField { Left = x, Width = w, MaxLength = max };
                f.Top = 5;
                _pnlLogBar.Controls.Add(f);
                return f;
            };
            Func<string, int, int, Action, BrickBtn> btn = (t, x, w, act) =>
            {
                BrickBtn b = new BrickBtn { Text = t, Left = x, Width = w, Height = 26, Top = 5 };
                b.Click += (s, e) => act();
                _pnlLogBar.Controls.Add(b);
                return b;
            };
            int px = 8;
            lbl("Call", px); _lbCall = fld(px + 28, 92, 13); _lbCall.CharacterCasing = CharacterCasing.Upper;
            px += 28 + 92 + 8;
            lbl("RST", px); _lbRst = fld(px + 28, 40, 3);
            px += 28 + 40 + 8;
            lbl("Name", px); _lbName = fld(px + 38, 92, 30);
            px += 38 + 92 + 8;
            lbl("QTH", px); _lbQth = fld(px + 30, 124, 40);
            px += 30 + 124 + 8;
            _lbSave = btn("Save", px, 52, brickLogBarSave); _lbSave.OnColor = cAccent;
            px += 52 + 5;
            _lbClear = btn("Clear", px, 46, brickLogBarClear);
            px += 46 + 5;
            _lbQrz = btn("QRZ.com", px, 64, () => brickLogBarWeb("https://www.qrz.com/db/"));
            px += 64 + 5;
            _lbWeb = btn("Google", px, 54, () => brickLogBarWeb("https://www.google.com/search?q=ham+radio+"));
            px += 54;
            _lbStatus = new Label { Left = 8, Top = 38, Height = 20, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, ForeColor = cMuted, BackColor = cCard, Font = new Font("Segoe UI", 9f), Text = "Call sign, report received, name and QTH are saved as a QSO" };
            _lbStatus.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _pnlLogBar.Controls.Add(_lbStatus);
            _pnlLogBar.Resize += (s, e) => { _lbStatus.Width = Math.Max(10, _pnlLogBar.Width - 16); };

            _lbDebounce = new System.Windows.Forms.Timer { Interval = 700 };
            _lbDebounce.Tick += (s, e) => { _lbDebounce.Stop(); brickLogBarLookup(); };
            _lbCall.TextChangedX += (s, e) =>
            {
                if (_lbFilling) return;
                string c = _lbCall.Text.Trim();
                if (c.Length > 0 && _lbStart == default(DateTime)) { _lbStart = DateTime.UtcNow; if (_lbRst.Text.Length == 0) _lbRst.Text = brickLogBarDefaultRst(); }
                if (c.Length == 0) { _lbStart = default(DateTime); _lbGrid = ""; _lbLooked = ""; }
                if (c != _lbLooked) { _lbNameEdited = false; _lbQthEdited = false; }
                _lbDebounce.Stop(); _lbDebounce.Start();
            };
            _lbName.TextChangedX += (s, e) => { if (!_lbFilling) _lbNameEdited = true; };
            _lbQth.TextChangedX += (s, e) => { if (!_lbFilling) _lbQthEdited = true; };
            _lbCall.KeyPressX += (s, e) => { if (e.KeyChar == (char)Keys.Enter) { e.Handled = true; _lbDebounce.Stop(); brickLogBarLookup(); } };
            _lbQth.KeyPressX += (s, e) => { if (e.KeyChar == (char)Keys.Enter) { e.Handled = true; brickLogBarSave(); } };

            brickDispCard.Controls.Add(_pnlLogBar);
            brickDispCard.Controls.SetChildIndex(_pnlLogBar, brickDispCard.Controls.Count - 1);       // docked first: the lowest edge, under everything else
        }

        // the report that is usual in the mode of the receiver
        private string brickLogBarDefaultRst()
        {
            DSPMode m = _rx1_dsp_mode;
            return m == DSPMode.LSB || m == DSPMode.USB || m == DSPMode.AM || m == DSPMode.SAM || m == DSPMode.FM || m == DSPMode.DSB ? "59" : "599";
        }

        // every 200 ms from brickTick
        private void brickLogBarUpdate()
        {
            if (brickDispCard == null) return;
            brickLogBarEnsure();
            if (_pnlLogBar == null) return;
            bool show = _digiLogBar && brickDockKey != "ft" && brickActive;
            if (_pnlLogBar.Visible != show) _pnlLogBar.Visible = show;
        }

        private void brickLogBarLookup()
        {
            string call = _lbCall.Text.Trim().ToUpperInvariant();
            string bc = CallLookup.BaseCall(call);
            if (bc.Length == 0 || call == _lbLooked) return;
            _lbLooked = call;
            int token = ++_lbToken;
            _lbStatus.Text = "Looking up " + bc + " ...";
            CallLookup.Run(call, DigiModesFolder, (info, src) =>
            {
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (token != _lbToken) return;
                        _lbFilling = true;
                        if (!_lbNameEdited && info.Name.Length > 0) _lbName.Text = info.Name;
                        if (!_lbQthEdited && info.Qth.Length > 0) _lbQth.Text = info.Qth;
                        _lbFilling = false;
                        _lbGrid = info.Grid;
                        _lbStatus.Text = (info.Country.Length > 0 ? info.Country + "   " : "") + (info.Grid.Length > 0 ? info.Grid + "   " : "") + "[" + string.Join(", ", info.Sources) + "]";
                    }));
                }
                catch { }
            }).ContinueWith(t =>
            {
                try
                {
                    BeginInvoke(new Action(() => { if (token == _lbToken && _lbStatus.Text.StartsWith("Looking up")) _lbStatus.Text = "Nothing found for " + bc + " (see the QRZ.com and Google buttons)"; }));
                }
                catch { }
            });
        }

        private void brickLogBarWeb(string baseUrl)
        {
            string bc = CallLookup.BaseCall(_lbCall.Text);
            if (bc.Length == 0) { _lbStatus.Text = "Type a call sign first"; return; }
            try { Process.Start(baseUrl + Uri.EscapeDataString(bc)); } catch { }
        }

        private void brickLogBarClear()
        {
            _lbFilling = true;
            _lbCall.Text = ""; _lbRst.Text = ""; _lbName.Text = ""; _lbQth.Text = "";
            _lbFilling = false;
            _lbStart = default(DateTime); _lbGrid = ""; _lbLooked = ""; _lbNameEdited = _lbQthEdited = false; _lbToken++;
            _lbStatus.Text = "Call sign, report received, name and QTH are saved as a QSO";
            _lbCall.FocusBox();
        }

        private void brickLogBarSave()
        {
            string call = _lbCall.Text.Trim().ToUpperInvariant();
            if (call.Length < 3) { _lbStatus.Text = "Type the call sign first"; _lbCall.FocusBox(); return; }
            string mode, sub;
            qsoModeOf(_rx1_dsp_mode, _digiOpenKey, out mode, out sub);
            Qso q = new Qso
            {
                Call = call, Mhz = VFOAFreq, Mode = mode, Submode = sub, Grid = _lbGrid,
                RstSent = brickLogBarDefaultRst(), RstRcvd = _lbRst.Text.Trim(), Name = _lbName.Text.Trim(), Qth = _lbQth.Text.Trim(),
                On = _lbStart == default(DateTime) ? DateTime.UtcNow : _lbStart, Off = DateTime.UtcNow
            };
            string msg = QsoLogAdd(q);
            brickLogBarClear();
            _lbStatus.Text = msg + "  " + q.Mhz.ToString("0.000", CultureInfo.InvariantCulture) + " MHz " + mode + (sub.Length > 0 ? " " + sub : "");
        }
    }
}
