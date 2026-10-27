using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;

namespace BrickSDR
{
    // Firmware loading and IP change of a discovered board. The packets follow the HPSDRProgrammer utility
    // (KV0S, GPL 2): every packet goes to UDP port 1024 and starts with a 4 byte sequence, then the command.
    //   0x03 set IP   : MAC (6) + new IP (4), broadcast; 0.0.0.0 = DHCP
    //   0x04 erase    : unicast to the board, answered twice (started / done)
    //   0x05 program  : total number of 256 byte blocks (4, big endian) + one block
    internal static class BrickProgrammer
    {
        private const int Port = 1024;
        private const int BlockSize = 256;

        public static byte[] ParseMac(string mac)
        {
            string hex = (mac ?? "").Replace(":", "").Replace("-", "").Trim();
            if (hex.Length != 12) return null;
            byte[] b = new byte[6];
            for (int i = 0; i < 6; i++)
            {
                byte v;
                if (!byte.TryParse(hex.Substring(i * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out v)) return null;
                b[i] = v;
            }
            return b;
        }

        private static UdpClient Open(string localIp)
        {
            IPAddress ip;
            if (!IPAddress.TryParse(localIp, out ip)) ip = IPAddress.Any;
            UdpClient u = new UdpClient(new IPEndPoint(ip, 0));
            u.EnableBroadcast = true;
            u.Client.ReceiveTimeout = 1000;
            return u;
        }

        private static bool WaitReply(UdpClient u, int ms)
        {
            DateTime end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end)
            {
                int left = (int)Math.Max(50, (end - DateTime.Now).TotalMilliseconds);
                u.Client.ReceiveTimeout = left;
                try
                {
                    IPEndPoint from = null;
                    byte[] d = u.Receive(ref from);
                    if (d != null && d.Length > 4 && d[4] != 0x02) return true; // anything but a discovery answer
                }
                catch (SocketException) { }
            }
            return false;
        }

        // new IP address, 0.0.0.0 asks the board for DHCP
        public static void SetIp(string localIp, byte[] mac, IPAddress newIp)
        {
            byte[] p = new byte[115];
            p[4] = 0x03;
            Array.Copy(mac, 0, p, 5, 6);
            Array.Copy(newIp.GetAddressBytes(), 0, p, 11, 4);
            using (UdpClient u = Open(localIp))
                u.Send(p, p.Length, new IPEndPoint(IPAddress.Broadcast, Port));
        }

        // returns an error text, or null when everything was sent
        public static string Program(string localIp, string radioIp, byte[] rbf, Action<string> log, Action<int> progress, Func<bool> cancel)
        {
            IPEndPoint board = new IPEndPoint(IPAddress.Parse(radioIp), Port);
            int blocks = (rbf.Length + BlockSize - 1) / BlockSize;
            using (UdpClient u = Open(localIp))
            {
                log("Erasing the flash, this takes a while ...");
                byte[] er = new byte[115];
                er[4] = 0x04;
                u.Send(er, er.Length, board);
                if (!WaitReply(u, 5000)) return "The board did not answer the erase command.";
                DateTime end = DateTime.Now.AddSeconds(180);
                bool done = false;
                while (DateTime.Now < end && !done)
                {
                    if (cancel()) return "Cancelled.";
                    done = WaitReply(u, 1000);
                    progress(0);
                }
                if (!done) return "The erase did not finish in time.";
                log("Erase complete.");

                bool acks = false;
                for (int i = 0; i < blocks; i++)
                {
                    if (cancel()) return "Cancelled after block " + i + " of " + blocks + ", the board is left erased.";
                    byte[] pk = new byte[9 + BlockSize];
                    pk[0] = (byte)(i >> 24); pk[1] = (byte)(i >> 16); pk[2] = (byte)(i >> 8); pk[3] = (byte)i;
                    pk[4] = 0x05;
                    pk[5] = (byte)(blocks >> 24); pk[6] = (byte)(blocks >> 16); pk[7] = (byte)(blocks >> 8); pk[8] = (byte)blocks;
                    int n = Math.Min(BlockSize, rbf.Length - i * BlockSize);
                    for (int k = 0; k < BlockSize; k++) pk[9 + k] = 0xFF;
                    Array.Copy(rbf, i * BlockSize, pk, 9, n);

                    bool ok = false;
                    for (int attempt = 0; attempt < 3 && !ok; attempt++)
                    {
                        u.Send(pk, pk.Length, board);
                        if (i == 0 || acks)
                        {
                            ok = WaitReply(u, i == 0 ? 1500 : 3000);
                            if (i == 0 && ok) acks = true;
                            if (i == 0 && !ok) { ok = true; log("The board does not acknowledge blocks, sending without waiting."); }
                        }
                        else { ok = true; Thread.Sleep(3); }
                    }
                    if (!ok) return "No answer from the board at block " + i + " of " + blocks + ". The board is left erased, repeat the programming.";
                    progress((i + 1) * 100 / blocks);
                }
            }
            return null;
        }
    }

    public partial class Setup
    {
        // Program / IP Set buttons of the radio list
        public void BrickHookRadioTools()
        {
            if (ucRadioList_Radios == null) return;
            ucRadioList_Radios.ToolRequested += (key, tool) =>
            {
                string rip, mac, nic, model; int port;
                if (!ucRadioList_Radios.TryGetRadio(key, out rip, out port, out mac, out nic, out model)) return;
                if (tool == 1) BrickProgramWindow(rip, mac, nic, model);
                else BrickIpSetWindow(rip, mac, nic, model);
            };
        }

        private bool BrickRadioIdle()
        {
            if (!console.PowerOn) return true;
            MessageBox.Show(this, "Switch the radio off (POWER) first, it is in use.", "Brick-SDR", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        private static Form BrickToolForm(string title, int w, int h)
        {
            Form f = new Form();
            f.Text = title;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.StartPosition = FormStartPosition.CenterParent;
            f.MaximizeBox = false; f.MinimizeBox = false; f.ShowInTaskbar = false;
            f.ClientSize = new Size(w, h);
            return f;
        }

        private static Label BrickToolLabel(Form f, string text, int x, int y, int w, int h = 18)
        {
            Label l = new Label { Text = text, AutoSize = false, Bounds = new Rectangle(x, y, w, h) };
            f.Controls.Add(l);
            return l;
        }

        private void BrickIpSetWindow(string radioIp, string mac, string nicIp, string model)
        {
            if (!BrickRadioIdle()) return;
            byte[] mb = BrickProgrammer.ParseMac(mac);
            if (mb == null) { MessageBox.Show(this, "The MAC address of this radio is unknown.", "IP Set"); return; }
            using (Form f = BrickToolForm("IP Set  " + model, 380, 214))
            {
                BrickToolLabel(f, "Radio: " + model + "   MAC " + mac, 12, 12, 356);
                BrickToolLabel(f, "Current IP: " + radioIp, 12, 34, 356);
                BrickToolLabel(f, "New IP address", 12, 66, 110);
                TextBox tb = new TextBox { Bounds = new Rectangle(126, 62, 150, 22), Text = radioIp };
                CheckBox dhcp = new CheckBox { Text = "DHCP (0.0.0.0)", Bounds = new Rectangle(126, 90, 150, 22) };
                dhcp.CheckedChanged += (s, e) => { tb.Enabled = !dhcp.Checked; };
                Label st = BrickToolLabel(f, "Use a free address of your subnet. The radio takes it after a few seconds, then press Discover again.", 12, 120, 356, 44);
                Button ok = new Button { Text = "Set IP", Bounds = new Rectangle(200, 172, 80, 26) };
                Button close = new Button { Text = "Close", Bounds = new Rectangle(288, 172, 80, 26), DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { tb, dhcp, ok, close });
                f.CancelButton = close;
                ok.Click += (s, e) =>
                {
                    IPAddress ip = IPAddress.Any;
                    if (!dhcp.Checked && (!IPAddress.TryParse(tb.Text.Trim(), out ip) || ip.AddressFamily != AddressFamily.InterNetwork))
                    { st.Text = "This is not a valid IPv4 address."; return; }
                    string what = dhcp.Checked ? "ask for an address by DHCP" : "use the address " + ip;
                    if (MessageBox.Show(f, "The radio " + mac + " will " + what + ". Continue?", "IP Set", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    try
                    {
                        BrickProgrammer.SetIp(nicIp, mb, ip);
                        st.Text = "Sent: " + (dhcp.Checked ? "DHCP (0.0.0.0)" : ip.ToString()) + ". Press Discover to find the radio at its new address.";
                    }
                    catch (Exception ex) { st.Text = "Failed: " + ex.Message; }
                };
                f.ShowDialog(this);
            }
        }

        private void BrickProgramWindow(string radioIp, string mac, string nicIp, string model)
        {
            if (!BrickRadioIdle()) return;
            using (Form f = BrickToolForm("Program  " + model, 460, 330))
            {
                BrickToolLabel(f, "Radio: " + model + "   MAC " + mac + "   IP " + radioIp, 12, 12, 436);
                BrickToolLabel(f, "Firmware file (.rbf)", 12, 40, 120);
                TextBox file = new TextBox { Bounds = new Rectangle(12, 60, 348, 22) };
                Button browse = new Button { Text = "Browse...", Bounds = new Rectangle(366, 58, 80, 26) };
                ProgressBar bar = new ProgressBar { Bounds = new Rectangle(12, 96, 436, 20) };
                TextBox log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Bounds = new Rectangle(12, 124, 436, 150) };
                Button start = new Button { Text = "Program", Bounds = new Rectangle(280, 288, 80, 28) };
                Button close = new Button { Text = "Close", Bounds = new Rectangle(368, 288, 80, 28), DialogResult = DialogResult.Cancel };
                BrickToolLabel(f, "Do not switch the radio off or unplug the cable while it is programmed.", 12, 294, 260, 30);
                f.Controls.AddRange(new Control[] { file, browse, bar, log, start, close });
                f.CancelButton = close;
                bool running = false, cancel = false;
                Action<string> addLog = t => { if (f.IsHandleCreated) f.BeginInvoke(new Action(() => log.AppendText(t + "\r\n"))); };
                browse.Click += (s, e) =>
                {
                    using (OpenFileDialog d = new OpenFileDialog { Filter = "FPGA firmware (*.rbf)|*.rbf|All files|*.*" })
                        if (d.ShowDialog(f) == DialogResult.OK) file.Text = d.FileName;
                };
                f.FormClosing += (s, e) => { if (running) { cancel = true; e.Cancel = true; addLog("Stopping ..."); } };
                start.Click += (s, e) =>
                {
                    if (running) return;
                    if (!File.Exists(file.Text)) { log.AppendText("Choose a firmware file first.\r\n"); return; }
                    byte[] rbf = File.ReadAllBytes(file.Text);
                    if (rbf.Length < 1024) { log.AppendText("The file is too small for a firmware.\r\n"); return; }
                    if (MessageBox.Show(f, "The flash of the radio is erased and written again with\r\n" + Path.GetFileName(file.Text) + " (" + rbf.Length + " bytes).\r\nA wrong file can leave the radio without firmware. Continue?",
                        "Program", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    running = true; cancel = false; start.Enabled = false; bar.Value = 0;
                    ThreadPool.QueueUserWorkItem(o =>
                    {
                        string err;
                        try { err = BrickProgrammer.Program(nicIp, radioIp, rbf, addLog, pc => { if (f.IsHandleCreated && pc > 0) f.BeginInvoke(new Action(() => bar.Value = Math.Min(100, pc))); }, () => cancel); }
                        catch (Exception ex) { err = ex.Message; }
                        addLog(err == null ? "Programming finished. Power cycle the radio, then press Discover." : err);
                        running = false;
                        if (f.IsHandleCreated) f.BeginInvoke(new Action(() => start.Enabled = true));
                    });
                };
                f.ShowDialog(this);
            }
        }
    }
}
