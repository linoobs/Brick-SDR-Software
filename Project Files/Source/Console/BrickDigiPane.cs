/*  BrickDigiPane.cs

    The RTTY / CW terminal, in the Brick-SDR style: docked under the panadapter or in its own window.
    The decoders and the modem are Kainos's (K7JUS, GPL v3), see BrickDigital.cs.
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
        internal sealed class BrickDigiPane : Panel
        {
            private readonly Console c;
            private readonly RichTextBox rx;
            private readonly BrickFrame rxFrame;
            private readonly BrickField type, my, their;
            private readonly Label status, title, lblMy, lblTheir;
            private readonly Panel settings;
            private readonly BrickBtn bTx, bEnd, bAbort, bClear, bPop, bClose;
            private readonly List<BrickBtn> macroButtons = new List<BrickBtn>();
            private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 80 };
            private readonly MeterBar meter = new MeterBar();
            private string mode = "";
            private bool lastSent;
            private string hintText = "";

            // RTTY settings
            private readonly BrickDropDown shift, baud;
            private readonly BrickBtn rev, afc;
            private readonly BrickNum rsql, rlevel;
            // CW settings
            private readonly BrickNum csql, wpm;
            private readonly Label lblShift, lblBaud, lblSql, lblLevel, lblWpm;

            private static readonly string[][] Macros =
            {
                new[] { "CQ", "CQ CQ CQ DE {MY} {MY} {MY} PSE K\r\n" },
                new[] { "Answer", "{CALL} DE {MY} {MY} KN\r\n" },
                new[] { "Report", "{CALL} DE {MY} UR RST 599 599 NAME OP QTH HR = HW? {CALL} DE {MY} KN\r\n" },
                new[] { "73", "{CALL} DE {MY} TNX FER QSO 73 SK\r\n" },
            };

            public BrickDigiPane(Console console)
            {
                c = console;
                BackColor = cCard;
                ForeColor = cText;
                Font = new Font("Segoe UI", 9f);
                Size = new Size(760, 270);

                title = new Label { AutoSize = false, Left = 8, Top = 6, Width = 54, Height = 26, Font = new Font("Segoe UI Semibold", 12f), ForeColor = cAccent, TextAlign = ContentAlignment.MiddleLeft, BackColor = cCard };
                lblMy = lbl("My call"); lblTheir = lbl("Their call");
                my = new BrickField(); their = new BrickField();
                my.CharacterCasing = CharacterCasing.Upper; their.CharacterCasing = CharacterCasing.Upper;
                my.Text = c._digiMyCall;
                my.TextChangedX += (s, e) => { c._digiMyCall = my.Text.Trim().ToUpperInvariant(); c.digiSaveSettings(); };
                bClear = mk("Clear", 60, 26, () => rx.Clear());
                bPop = mk("Pop out", 70, 26, () => c.digiTogglePop());
                bClose = mk("Close", 60, 26, () => c.digiClose());

                settings = new Panel { BackColor = cCard, Height = 28 };
                shift = new BrickDropDown { Width = 62, Height = 24 };
                shift.Items.AddRange(new[] { "170", "200", "425", "850" });
                baud = new BrickDropDown { Width = 66, Height = 24 };
                baud.Items.AddRange(new[] { "45.45", "50", "75", "100" });
                rev = mk("Reverse", 70, 24, null); rev.Toggle = true; rev.Checked = c._rttyReverse;
                afc = mk("AFC", 50, 24, null); afc.Toggle = true; afc.Checked = c._rttyAfc;
                rsql = num(0, 60, 5, c._rttySql, "");
                rlevel = num(-40, 0, 1, c._rttyLevel, " dB");
                csql = num(0, 100, 5, c._cwSql, "");
                wpm = num(5, 60, 1, 20, " WPM");
                lblShift = lbl("Shift"); lblBaud = lbl("Baud"); lblSql = lbl("SQL"); lblLevel = lbl("TX level"); lblWpm = lbl("TX speed");
                shift.SelectedChanged += (s, e) => { c._rttyShift = double.Parse(shift.Items[shift.SelectedIndex], CultureInfo.InvariantCulture); c.digiSaveSettings(); };
                baud.SelectedChanged += (s, e) => { c._rttyBaud = double.Parse(baud.Items[baud.SelectedIndex], CultureInfo.InvariantCulture); c.digiSaveSettings(); };
                rev.CheckedChanged += (s, e) => { c._rttyReverse = rev.Checked; c.digiSaveSettings(); };
                afc.CheckedChanged += (s, e) => { c._rttyAfc = afc.Checked; c.digiSaveSettings(); };
                rsql.ValueChanged += (s, e) => { c._rttySql = (int)rsql.Value; c.digiSaveSettings(); };
                rlevel.ValueChanged += (s, e) => { c._rttyLevel = (int)rlevel.Value; c.digiSaveSettings(); };
                csql.ValueChanged += (s, e) => { c._cwSql = (int)csql.Value; c.digiSaveSettings(); };
                wpm.ValueChanged += (s, e) => { try { if (c.CWXForm.WPM != (int)wpm.Value) c.CWXForm.WPM = (int)wpm.Value; } catch { } };

                rx = new RichTextBox
                {
                    ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = cField, ForeColor = cText, DetectUrls = false,
                    Font = new Font("Consolas", 12f), ScrollBars = RichTextBoxScrollBars.Vertical, HideSelection = false, TabStop = false
                };
                rxFrame = new BrickFrame(rx);
                meter.Height = 5;
                status = new Label { AutoSize = false, Height = 18, ForeColor = cMuted, BackColor = cCard, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
                type = new BrickField(new Font("Consolas", 12f)) { CharacterCasing = CharacterCasing.Upper };
                type.KeyPressX += typeKey;
                bTx = mk("TX", 70, 28, () => { if (mode == "RTTY") c.RttySend("", false); });
                bEnd = mk("RX", 70, 28, () => { if (mode == "RTTY") c.RttyReceive(); else c.CwStopQueueSafe(); });
                bAbort = mk("ABORT", 80, 28, () => { if (mode == "RTTY") c.RttyAbort(); else c.CwAbort(); });
                bAbort.OnColor = cTx; bAbort.ForeColor = cTx;
                foreach (string[] m in Macros)
                {
                    string text = m[1];
                    macroButtons.Add(mk(m[0], 64, 28, () => macro(text)));
                }

                Controls.AddRange(new Control[] { title, lblMy, my, lblTheir, their, bClear, bPop, bClose, settings, rxFrame, meter, status, type, bTx, bEnd, bAbort });
                foreach (BrickBtn b in macroButtons) Controls.Add(b);
                settings.Controls.AddRange(new Control[] { lblShift, shift, lblBaud, baud, rev, afc, lblSql, rsql, lblLevel, rlevel, csql, lblWpm, wpm });
                Resize += (s, e) => layout();
                timer.Tick += (s, e) => tick();
                VisibleChanged += (s, e) => { if (Visible) { layout(); timer.Start(); } else timer.Stop(); };
                layout();
            }

            private Label lbl(string t)
            {
                return new Label { Text = t, AutoSize = true, ForeColor = cMuted, BackColor = cCard, Font = new Font("Segoe UI", 9f) };
            }

            private BrickNum num(int min, int max, int inc, int v, string suffix)
            {
                return new BrickNum { Minimum = min, Maximum = max, Increment = inc, Value = Math.Max(min, Math.Min(max, v)), Suffix = suffix, Width = suffix.Length > 0 ? 100 : 84, Height = 24 };
            }

            private BrickBtn mk(string text, int w, int h, Action act)
            {
                BrickBtn b = new BrickBtn { Text = text, Width = w, Height = h };
                if (act != null) b.Click += (s, e) => { act(); type.FocusBox(); };
                return b;
            }

            public void SetPopped(bool popped) { bPop.Text = popped ? "Dock" : "Pop out"; }

            public void SetMode(string m)
            {
                mode = m;
                title.Text = m == "RTTY" ? "RTTY" : "CW";
                bool r = m == "RTTY";
                foreach (Control k in new Control[] { lblShift, shift, lblBaud, baud, rev, afc, lblLevel, rlevel, rsql }) k.Visible = r;
                foreach (Control k in new Control[] { lblWpm, wpm, csql }) k.Visible = !r;
                lblSql.Visible = true;
                bTx.Visible = r;
                bEnd.Text = r ? "RX" : "STOP";
                if (!r) { try { wpm.Value = Math.Max(wpm.Minimum, Math.Min(wpm.Maximum, c.CWXForm.WPM)); } catch { } }
                int si = shift.Items.IndexOf(c._rttyShift.ToString("0", CultureInfo.InvariantCulture)); if (si >= 0) shift.SelectedIndex = si;
                int bi = baud.Items.IndexOf(c._rttyBaud.ToString("0.##", CultureInfo.InvariantCulture)); if (bi >= 0) baud.SelectedIndex = bi;
                layout();
                sync();
            }

            private void sync()
            {
            }

            private void layoutSettings()
            {
                int x = 0;
                Action<Control, int> put = (ctl, gap) =>
                {
                    if (!ctl.Visible) return;
                    ctl.Left = x;
                    ctl.Top = ctl is Label ? (settings.Height - ctl.Height) / 2 : (settings.Height - ctl.Height) / 2;
                    x += ctl.Width + gap;
                };
                foreach (Label l in new[] { lblShift, lblBaud, lblSql, lblLevel, lblWpm }) { l.AutoSize = true; }
                if (mode == "RTTY")
                {
                    put(lblShift, 4); put(shift, 12); put(lblBaud, 4); put(baud, 12); put(rev, 6); put(afc, 14); put(lblSql, 4); put(rsql, 12); put(lblLevel, 4); put(rlevel, 0);
                }
                else
                {
                    put(lblSql, 4); put(csql, 14); put(lblWpm, 4); put(wpm, 0);
                }
                settings.Width = Math.Max(x, 10);
            }

            private void layout()
            {
                int w = Width, h = Height;
                if (w < 100 || h < 100) return;
                // row 1: title, receiver, call signs; the window buttons at the right
                bClose.SetBounds(w - 8 - bClose.Width, 6, bClose.Width, 26);
                bPop.SetBounds(bClose.Left - 6 - bPop.Width, 6, bPop.Width, 26);
                bClear.SetBounds(bPop.Left - 6 - bClear.Width, 6, bClear.Width, 26);
                lblMy.Location = new Point(74, 11); my.SetBounds(124, 6, 96, 26);
                lblTheir.Location = new Point(230, 11); their.SetBounds(300, 6, 96, 26);
                // row 2: the settings
                settings.Location = new Point(8, 36);
                layoutSettings();
                settings.Height = 28;
                // bottom row: macros, the type-ahead line, TX / RX / ABORT
                int rowY = h - 34;
                int bx = 8;
                foreach (BrickBtn b in macroButtons) { b.Left = bx; b.Top = rowY; bx += b.Width + 6; }
                bAbort.Location = new Point(w - 8 - bAbort.Width, rowY);
                bEnd.Location = new Point(bAbort.Left - 6 - bEnd.Width, rowY);
                bTx.Location = new Point(bEnd.Left - 6 - bTx.Width, rowY);
                int endX = bTx.Visible ? bTx.Left : bEnd.Left;
                type.SetBounds(bx + 6, rowY, Math.Max(60, endX - 6 - (bx + 6)), 28);
                // the readings of the decoder on their own line above the bottom row, the text above them
                status.SetBounds(8, rowY - 22, w - 16, 18);
                meter.SetBounds(8, status.Top - 7, w - 16, 5);
                rxFrame.SetBounds(8, 68, w - 16, Math.Max(30, meter.Top - 3 - 68));
            }

            public void Append(char ch, bool sent)
            {
                if (ch == '\r') return;
                if (rx.TextLength > 60000) { rx.Select(0, 20000); rx.SelectedText = ""; }
                if (sent != lastSent && ch != '\n')
                {
                    if (rx.TextLength > 0 && !rx.Text.EndsWith("\n")) rx.AppendText("\n");
                    lastSent = sent;
                }
                if (ch == '\n' && rx.TextLength > 0 && rx.Text.EndsWith("\n")) return;
                rx.SelectionStart = rx.TextLength;
                rx.SelectionLength = 0;
                rx.SelectionColor = sent ? cAccent : cText;
                rx.AppendText(ch.ToString());
            }

            private void macro(string text)
            {
                string t = text.Replace("{MY}", c._digiMyCall).Replace("{CALL}", their.Text.Trim().ToUpperInvariant());
                if (c._digiMyCall.Length == 0) { status.Text = "Enter your call sign first"; return; }
                if (mode == "RTTY") c.RttySend(t, true);
                else c.CwSend(t.Replace("\r\n", " "));
            }

            private void typeKey(object s, KeyPressEventArgs e)
            {
                if (e.KeyChar == (char)Keys.Enter)
                {
                    e.Handled = true;
                    string t = type.Text;
                    type.Clear();
                    if (mode == "RTTY")
                    {
                        if (c.RttyTransmitting) { foreach (char ch in "\r\n") c.RttyType(ch); }
                        else if (t.Length > 0) c.RttySend(t + "\r\n", false);
                    }
                    else if (t.Length > 0) c.CwSend(t);
                    return;
                }
                // while RTTY is on the air, characters go out as they are typed
                if (mode == "RTTY" && c.RttyTransmitting && e.KeyChar >= ' ')
                {
                    e.Handled = true;
                    c.RttyType(char.ToUpperInvariant(e.KeyChar));
                }
            }

            private void tick()
            {
                c.DigiUiTick(this);
                string problem = mode == "RTTY" ? c.RttyStartProblem : c.CwStartProblem;
                bool ok = mode == "RTTY" ? c.RttyModeOk : c.CwModeOk;
                string stats = "";
                if (mode == "RTTY")
                {
                    RttyDemod d = c._rttyDemod;
                    if (d != null)
                    {
                        meter.Value = d.Quality;
                        stats = string.Format(CultureInfo.InvariantCulture, "Quality {0:0}%   tones centre {1:0} Hz", d.Quality * 100, d.Center);
                    }
                    hintText = c.RttyTransmitting ? "TRANSMITTING" : (ok ? "" : "Use DIGL / LSB");
                    status.ForeColor = c.RttyTransmitting ? cTx : cMuted;
                    bTx.On = c.RttyTransmitting;
                    bTx.OnColor = cTx;
                }
                else
                {
                    CwDecoder d = c._cwDecoder;
                    if (d != null)
                    {
                        meter.Value = d.Level;
                        stats = string.Format(CultureInfo.InvariantCulture, "{0:0} WPM   tone {1:0} Hz   S/N {2:0.0}{3}", d.Wpm, d.TonePitch, d.Snr, d.KeyDown ? "   key down" : "");
                    }
                    hintText = c.CwSending ? "SENDING" : (ok ? "" : "Use CWL / CWU");
                    status.ForeColor = c.CwSending ? cTx : cMuted;
                }
                if (c._digiRx == 1) hintText = "RX2   " + hintText;
                string text = hintText.Length > 0 ? (stats.Length > 0 ? hintText + "   " + stats : hintText) : stats;
                if (problem != null && !c.PowerOn) text = problem;
                if (status.Text != text) status.Text = text;
            }
        }
    }
}
