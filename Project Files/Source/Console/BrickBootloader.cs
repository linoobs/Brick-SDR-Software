using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace BrickSDR
{
    // Programming of a board that runs its bootloader (MODE1 grounded, fixed MAC 11:22:33:44:55:66). The bootloader does not use IP:
    // it talks raw Ethernet frames of type 0xEFFE, so they are sent and received through Npcap / WinPcap (wpcap.dll), like HPSDRBootloader does.
    //   frame      : dst 11:22:33:44:55:66, src = MAC of this PC, 0xEFFE, 0x03, command, ...
    //   command 1  : program, 4 bytes (ignored by the board) + 256 bytes of data, answered by 0x02 (send the next block)
    //   command 2  : erase the top megabyte of the EPCS16 (the bootloader itself is below it), answered by 0x01 after about 3 s
    //   command 3  : read the MAC address, answered by 0x03 + the MAC
    // The file name does not matter (HPSDRBootloader insists on "hermes" or "angelia" in it).
    internal static class BrickBootloader
    {
        public static readonly byte[] BootMac = { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66 };
        private const int BlockSize = 256;

        internal sealed class Adapter
        {
            public string PcapName, Name, Ip, MacText;
            public byte[] Mac;
            public override string ToString() { return Name + "   " + Ip + "   " + MacText; }
        }

        // ---- Npcap / WinPcap ----
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetDllDirectory(string path);
        [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int pcap_findalldevs(out IntPtr alldevs, StringBuilder errbuf);
        [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void pcap_freealldevs(IntPtr alldevs);
        [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private static extern IntPtr pcap_open_live(string device, int snaplen, int promisc, int toMs, StringBuilder errbuf);
        [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int pcap_sendpacket(IntPtr p, byte[] buf, int size);
        [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int pcap_next_ex(IntPtr p, out IntPtr hdr, out IntPtr data);
        [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void pcap_close(IntPtr p);

        private static bool _dirSet;
        private static void SetupDll()
        {
            if (_dirSet) return;
            _dirSet = true;
            try
            {
                string np = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Npcap");
                if (File.Exists(Path.Combine(np, "wpcap.dll"))) SetDllDirectory(np);
            }
            catch { }
        }

        // the adapters that are up with an IPv4 address and that Npcap knows, null + a reason when Npcap is missing
        public static List<Adapter> Adapters(out string problem)
        {
            problem = null;
            List<Adapter> list = new List<Adapter>();
            try
            {
                SetupDll();
                IntPtr devs;
                StringBuilder eb = new StringBuilder(256);
                if (pcap_findalldevs(out devs, eb) != 0) { problem = "Npcap: " + eb; return list; }
                HashSet<string> known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (IntPtr d = devs; d != IntPtr.Zero; d = Marshal.ReadIntPtr(d))
                    known.Add(Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(d, IntPtr.Size)) ?? "");
                pcap_freealldevs(devs);
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType != NetworkInterfaceType.Ethernet && ni.NetworkInterfaceType != NetworkInterfaceType.GigabitEthernet && ni.NetworkInterfaceType != NetworkInterfaceType.Wireless80211) continue;
                    string pn = "\\Device\\NPF_" + ni.Id;
                    if (!known.Contains(pn)) continue;
                    string ip = "";
                    foreach (UnicastIPAddressInformation u in ni.GetIPProperties().UnicastAddresses)
                        if (u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) { ip = u.Address.ToString(); break; }
                    byte[] mac = ni.GetPhysicalAddress().GetAddressBytes();
                    if (mac.Length != 6) continue;
                    list.Add(new Adapter { PcapName = pn, Name = ni.Name, Ip = ip, Mac = mac, MacText = BitConverter.ToString(mac) });
                }
                if (list.Count == 0) problem = "No network adapter that Npcap can use was found.";
            }
            catch (DllNotFoundException) { problem = "Npcap (or WinPcap) is not installed. Install Npcap from npcap.com, then open this window again."; }
            catch (EntryPointNotFoundException) { problem = "The installed packet capture library is too old, install Npcap from npcap.com."; }
            catch (Exception ex) { problem = ex.Message; }
            return list;
        }

        private sealed class Link : IDisposable
        {
            public IntPtr H;
            public byte[] Mac;
            public Link(Adapter a)
            {
                StringBuilder eb = new StringBuilder(256);
                H = pcap_open_live(a.PcapName, 2048, 1, 20, eb);
                if (H == IntPtr.Zero) throw new InvalidOperationException("Can't open the adapter: " + eb);
                Mac = a.Mac;
            }
            public void Dispose() { if (H != IntPtr.Zero) { pcap_close(H); H = IntPtr.Zero; } }

            public void Send(byte cmd, byte[] extra)
            {
                // 14 byte header + 0x03 + command + data, padded to the 60 byte minimum
                int len = Math.Max(60, 16 + (extra == null ? 0 : extra.Length));
                byte[] f = new byte[len];
                Array.Copy(BootMac, 0, f, 0, 6);
                Array.Copy(Mac, 0, f, 6, 6);
                f[12] = 0xEF; f[13] = 0xFE; f[14] = 0x03; f[15] = cmd;
                if (extra != null) Array.Copy(extra, 0, f, 16, extra.Length);
                if (pcap_sendpacket(H, f, f.Length) != 0) throw new InvalidOperationException("The frame could not be sent.");
            }

            // waits for an answer of the bootloader to this PC with the given return code, returns the frame or null
            public byte[] Wait(byte code, int ms, Func<bool> cancel)
            {
                DateTime end = DateTime.Now.AddMilliseconds(ms);
                while (DateTime.Now < end)
                {
                    if (cancel != null && cancel()) return null;
                    IntPtr hdr, data;
                    int r = pcap_next_ex(H, out hdr, out data);
                    if (r <= 0) { if (r < 0) return null; continue; }
                    int cap = Marshal.ReadInt32(hdr, 8);
                    if (cap < 17 || cap > 2048) continue;
                    byte[] p = new byte[cap];
                    Marshal.Copy(data, p, 0, cap);
                    if (p[12] != 0xEF || p[13] != 0xFE || p[14] != 0x03 || p[15] != code) continue;
                    bool fromBoot = true, toMe = true;
                    for (int i = 0; i < 6; i++) { if (p[6 + i] != BootMac[i]) fromBoot = false; if (p[i] != Mac[i]) toMe = false; }
                    if (fromBoot && toMe) return p;
                }
                return null;
            }

            public void Drain() { IntPtr h, d; for (int i = 0; i < 200 && pcap_next_ex(H, out h, out d) > 0; i++) { } }
        }

        // the MAC address of the board in bootloader mode, or an error text in err
        public static string ReadBoardMac(Adapter a, out string err)
        {
            err = null;
            try
            {
                using (Link l = new Link(a))
                {
                    l.Drain();
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        l.Send(0x03, null);
                        byte[] r = l.Wait(0x03, 800, null);
                        if (r != null && r.Length >= 22) { byte[] m = new byte[6]; Array.Copy(r, 16, m, 0, 6); return BitConverter.ToString(m); }
                    }
                }
                err = "No answer. The board must run its bootloader (MODE1 to ground, power cycle) and be on the same cable or switch.";
            }
            catch (Exception ex) { err = ex.Message; }
            return null;
        }

        // returns an error text, or null when the whole file was written
        public static string Program(Adapter a, byte[] rbf, Action<string> log, Action<int> progress, Func<bool> cancel)
        {
            int blocks = (rbf.Length + BlockSize - 1) / BlockSize;
            using (Link l = new Link(a))
            {
                l.Drain();
                log("Erasing the flash (about 3 seconds) ...");
                l.Send(0x02, null);
                byte[] er = l.Wait(0x01, 60000, cancel);
                if (cancel()) return "Cancelled before anything was written.";
                if (er == null) return "The board did not confirm the erase. Is it in bootloader mode?";
                log("Erased. Writing " + blocks + " blocks ...");
                for (int i = 0; i < blocks; i++)
                {
                    if (cancel()) return "Cancelled after block " + i + " of " + blocks + ". The board is left erased: program it again, power cycle first.";
                    byte[] d = new byte[4 + BlockSize];
                    d[0] = (byte)(i >> 24); d[1] = (byte)(i >> 16); d[2] = (byte)(i >> 8); d[3] = (byte)i;
                    for (int k = 0; k < BlockSize; k++) d[4 + k] = 0xFF;
                    Array.Copy(rbf, i * BlockSize, d, 4, Math.Min(BlockSize, rbf.Length - i * BlockSize));
                    l.Send(0x01, d);
                    // never send a block twice: the board writes in order, a repeated block would shift the rest of the file
                    byte[] ack = l.Wait(0x02, 4000, cancel);
                    if (ack == null) return "No answer from the board at block " + (i + 1) + " of " + blocks + ". The board is left erased: power cycle it and program again.";
                    progress((i + 1) * 100 / blocks);
                }
            }
            return null;
        }
    }

    public partial class Setup
    {
        // "Bootloader" button of Setup > General > H/W Select: next to Discover
        public void BrickAddBootloaderButton()
        {
            Button b = new Button { Name = "brickBootloader", Text = "Bootloader", Size = new Size(btnDiscoverRadios.Width, 28) };
            b.Location = new Point(btnDiscoverRadios.Left, chkAdvancedNetworkingSettings.Bottom + 8);
            b.Click += (s, e) => BrickBootloaderWindow();
            btnDiscoverRadios.Parent.Controls.Add(b);
            new ToolTip().SetToolTip(b, "Program a board that runs its bootloader (MODE1 to ground)");
        }

        private void BrickBootloaderWindow()
        {
            if (!BrickRadioIdle()) return;
            string problem;
            List<BrickBootloader.Adapter> adapters = BrickBootloader.Adapters(out problem);
            using (Form f = BrickToolForm("Bootloader", 560, 440))
            {
                BrickToolLabel(f, "Programs a board that runs its bootloader (jumper MODE1 to ground, then power on). The board is found by its fixed address, no IP is used.", 12, 10, 536, 30);
                BrickToolLabel(f, "Computer interface", 12, 48, 130);
                ComboBox nic = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(12, 68, 536, 24) };
                foreach (BrickBootloader.Adapter a in adapters) nic.Items.Add(a);
                IPAddressText sel = new IPAddressText();
                try
                {
                    ucRadioList rl = SelectedRadioList;
                    NicRadioScanResult sn = rl == null ? null : rl.SelectedNICDetails;
                    if (sn != null && sn.LocalIPv4 != null) sel.Value = sn.LocalIPv4.ToString();
                }
                catch { }
                int pick = 0;
                for (int i = 0; i < adapters.Count; i++) if (adapters[i].Ip == sel.Value) pick = i;
                if (nic.Items.Count > 0) nic.SelectedIndex = pick;

                Button test = new Button { Text = "Test for bootloader", Bounds = new Rectangle(12, 102, 150, 28) };
                Label found = BrickToolLabel(f, "", 172, 102, 376, 36);
                BrickToolLabel(f, "Firmware file (.rbf, any file name)", 12, 144, 250);
                TextBox file = new TextBox { Bounds = new Rectangle(12, 164, 448, 22) };
                Button browse = new Button { Text = "Browse...", Bounds = new Rectangle(468, 162, 80, 26) };
                ProgressBar bar = new ProgressBar { Bounds = new Rectangle(12, 200, 536, 20) };
                TextBox log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Bounds = new Rectangle(12, 228, 536, 130) };
                Button start = new Button { Text = "Program", Bounds = new Rectangle(380, 400, 80, 28) };
                Button close = new Button { Text = "Close", Bounds = new Rectangle(468, 400, 80, 28), DialogResult = DialogResult.Cancel };
                BrickToolLabel(f, "Only the application part of the flash is erased and written, the bootloader stays. Do not unplug the cable or the power while it runs. When it is done, power cycle the board.", 12, 364, 536, 30);
                f.Controls.AddRange(new Control[] { nic, test, file, browse, bar, log, start, close });
                f.CancelButton = close;
                if (problem != null) log.AppendText(problem + "\r\n");
                if (problem != null && problem.StartsWith("Npcap (or WinPcap) is not installed"))
                {
                    // the bootloader talks raw Ethernet frames, which Windows only allows through the Npcap driver (free for personal use, can't be bundled)
                    Button get = new Button { Text = "Get Npcap", Bounds = new Rectangle(440, 102, 108, 28) };
                    get.Click += (s, e) => { try { System.Diagnostics.Process.Start("https://npcap.com/#download"); } catch { } };
                    f.Controls.Add(get);
                    test.Enabled = false; start.Enabled = false;
                    log.AppendText("Download Npcap (npcap.com, free for personal use), install it once with administrator rights, then open this window again.\r\n");
                    log.AppendText("Brick-SDR itself works without it: Npcap is needed only for a board in bootloader mode. A board with working firmware can be programmed without it, with Program in the radio list of this tab.\r\n");
                    log.AppendText("To put a board into bootloader mode: jumper MODE1 to ground, power cycle the board, then press Test for bootloader.\r\n");
                }

                bool running = false, cancel = false;
                Action<string> addLog = t => { if (f.IsHandleCreated) f.BeginInvoke(new Action(() => log.AppendText(t + "\r\n"))); };
                browse.Click += (s, e) =>
                {
                    using (OpenFileDialog d = new OpenFileDialog { Filter = "FPGA firmware (*.rbf)|*.rbf|All files|*.*" })
                        if (d.ShowDialog(f) == DialogResult.OK) file.Text = d.FileName;
                };
                f.FormClosing += (s, e) => { if (running) { cancel = true; e.Cancel = true; addLog("Stopping ..."); } };
                test.Click += (s, e) =>
                {
                    BrickBootloader.Adapter a = nic.SelectedItem as BrickBootloader.Adapter;
                    if (a == null) { found.Text = "Choose an interface first."; return; }
                    found.Text = "Asking the board ...";
                    f.Update();
                    string err;
                    string mac = BrickBootloader.ReadBoardMac(a, out err);
                    found.Text = mac != null ? "Bootloader found, board MAC " + mac : err;
                };
                start.Click += (s, e) =>
                {
                    if (running) return;
                    BrickBootloader.Adapter a = nic.SelectedItem as BrickBootloader.Adapter;
                    if (a == null) { log.AppendText("Choose an interface first.\r\n"); return; }
                    if (!File.Exists(file.Text)) { log.AppendText("Choose a firmware file first.\r\n"); return; }
                    byte[] rbf = File.ReadAllBytes(file.Text);
                    if (rbf.Length < 1024) { log.AppendText("The file is too small for a firmware.\r\n"); return; }
                    if (MessageBox.Show(f, "The firmware part of the board is erased and written again with\r\n" + Path.GetFileName(file.Text) + " (" + rbf.Length + " bytes).\r\nA wrong file leaves the board without a working firmware; the bootloader itself stays,\r\nso it can be programmed again.\r\n\r\nContinue?",
                        "Bootloader", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    running = true; cancel = false; start.Enabled = false; test.Enabled = false; bar.Value = 0;
                    ThreadPool.QueueUserWorkItem(o =>
                    {
                        string err;
                        try { err = BrickBootloader.Program(a, rbf, addLog, pc => { if (f.IsHandleCreated) f.BeginInvoke(new Action(() => bar.Value = Math.Min(100, pc))); }, () => cancel); }
                        catch (Exception ex) { err = ex.Message; }
                        addLog(err == null ? "Programming finished. Power cycle the board (and put MODE1 high to start the new firmware)." : err);
                        running = false;
                        if (f.IsHandleCreated) f.BeginInvoke(new Action(() => { start.Enabled = true; test.Enabled = true; }));
                    });
                };
                f.ShowDialog(this);
            }
        }

        private sealed class IPAddressText { public string Value = ""; }
    }
}
