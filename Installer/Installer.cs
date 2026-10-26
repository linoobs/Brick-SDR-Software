using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// Brick-SDR installer / uninstaller. The program files are embedded as payload.zip.
namespace BrickSetup
{
    static class Program
    {
        public const string AppName = "Brick-SDR";
        public const string ExeName = "BrickSDR.exe";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Brick-SDR";
        public static readonly string DefaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Brick-SDR");

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool un = args.Length > 0 && args[0].Equals("/uninstall", StringComparison.OrdinalIgnoreCase);
            Application.Run(new SetupForm(un));
        }

        public static string FindOldDir()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(UninstallKey))
                {
                    if (k != null)
                    {
                        string loc = k.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(loc) && Directory.Exists(loc)) return loc;
                    }
                }
            }
            catch { }
            return File.Exists(Path.Combine(DefaultDir, ExeName)) ? DefaultDir : null;
        }

        public static string OldVersion(string dir)
        {
            try { return FileVersionInfo.GetVersionInfo(Path.Combine(dir, ExeName)).FileVersion; } catch { return "unknown"; }
        }

        public static string PayloadVersion()
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("version.txt"))
            using (StreamReader r = new StreamReader(s)) return r.ReadToEnd().Trim();
        }
    }

    class SetupForm : Form
    {
        static readonly Color Bg = Color.FromArgb(22, 24, 33), Card = Color.FromArgb(30, 33, 45), Accent = Color.FromArgb(255, 138, 31), Txt = Color.FromArgb(225, 228, 235), Muted = Color.FromArgb(150, 156, 170);
        readonly bool uninstall;
        readonly string oldDir;
        Label info, status;
        ProgressBar bar;
        Button go, cancel;
        CheckBox launch, desktop, gpu;

        public SetupForm(bool un)
        {
            uninstall = un;
            oldDir = Program.FindOldDir();
            Text = Program.AppName + (un ? " Uninstall" : " Setup");
            ClientSize = new Size(520, 350);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg; ForeColor = Txt;
            Font = new Font("Segoe UI", 9.5f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Label title = new Label { Text = Program.AppName, Font = new Font("Segoe UI", 22f, FontStyle.Bold), ForeColor = Accent, Location = new Point(24, 16), AutoSize = true };
            Label sub = new Label { Text = un ? "Remove the program" : "Version " + Program.PayloadVersion() + " - installer", ForeColor = Muted, Location = new Point(28, 64), AutoSize = true };
            Controls.Add(title); Controls.Add(sub);

            string msg;
            if (un) msg = "Brick-SDR will be removed from:\r\n" + (oldDir ?? Program.DefaultDir) + "\r\n\r\nYour settings and database in %APPDATA%\\OpenHPSDR are kept.";
            else if (oldDir != null) msg = "An older version of Brick-SDR is installed in:\r\n" + oldDir + "\r\n\r\nIt will be REMOVED and replaced by this version. Your settings and database in %APPDATA%\\OpenHPSDR are kept.";
            else msg = "Brick-SDR will be installed to:\r\n" + Program.DefaultDir;
            info = new Label { Text = msg, Location = new Point(28, 100), Size = new Size(464, 100), ForeColor = oldDir != null && !un ? Color.FromArgb(255, 214, 60) : Txt };
            Controls.Add(info);

            desktop = new CheckBox { Text = "Desktop shortcut", Checked = true, Location = new Point(28, 206), AutoSize = true, Visible = !un };
            launch = new CheckBox { Text = "Start Brick-SDR when finished", Checked = true, Location = new Point(200, 206), AutoSize = true, Visible = !un };
            gpu = new CheckBox { Text = "Use the high-performance graphics card (Windows Graphics settings)", Checked = true, Location = new Point(28, 226), AutoSize = true, Visible = !un };
            Controls.Add(desktop); Controls.Add(launch); Controls.Add(gpu);

            bar = new ProgressBar { Location = new Point(28, 252), Size = new Size(464, 14), Visible = false };
            status = new Label { Location = new Point(28, 268), Size = new Size(464, 20), ForeColor = Muted };
            Controls.Add(bar); Controls.Add(status);

            go = new Button { Text = un ? "Uninstall" : (oldDir != null ? "Replace old version" : "Install"), Location = new Point(250, 306), Size = new Size(160, 32), BackColor = Accent, ForeColor = Color.Black, FlatStyle = FlatStyle.Flat };
            cancel = new Button { Text = "Cancel", Location = new Point(420, 306), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, ForeColor = Txt, BackColor = Card };
            go.Click += OnGo; cancel.Click += (s, e) => Close();
            Controls.Add(go); Controls.Add(cancel);
        }

        void Step(string text, int pct)
        {
            status.Text = text; bar.Value = Math.Max(0, Math.Min(100, pct)); Application.DoEvents();
        }

        void OnGo(object sender, EventArgs e)
        {
            if (uninstall)
            {
                if (MessageBox.Show(this, "Remove Brick-SDR from this computer?", Program.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            }
            else if (oldDir != null)
            {
                if (MessageBox.Show(this, "An older version of Brick-SDR was found in\r\n" + oldDir + "\r\n\r\nIt will be deleted and replaced. Continue?", "Old version found", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }

            go.Enabled = false; cancel.Enabled = false; bar.Visible = true;
            try
            {
                Step("Closing Brick-SDR...", 5);
                CloseRunning();

                string target = oldDir ?? Program.DefaultDir;
                if (Directory.Exists(target))
                {
                    Step("Removing the old version...", 15);
                    RemoveDir(target, uninstall);
                }
                RemoveShortcutsAndRegistry();

                if (uninstall)
                {
                    Step("Done", 100);
                    MessageBox.Show(this, "Brick-SDR has been removed.", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Close();
                    return;
                }

                Step("Copying files...", 30);
                Directory.CreateDirectory(Program.DefaultDir);
                Extract(Program.DefaultDir);
                File.Copy(Application.ExecutablePath, Path.Combine(Program.DefaultDir, "Uninstall.exe"), true);

                Step("Creating shortcuts...", 85);
                string exe = Path.Combine(Program.DefaultDir, Program.ExeName);
                Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Brick-SDR.lnk"), exe, Program.DefaultDir);
                if (desktop.Checked) Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Brick-SDR.lnk"), exe, Program.DefaultDir);

                Step("Registering...", 92);
                Register(exe);
                if (gpu.Checked) SetGpuPreference(exe, true);
                FirewallRules(exe, true);

                Step("Done", 100);
                MessageBox.Show(this, "Brick-SDR " + Program.PayloadVersion() + " is installed.", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                if (launch.Checked) Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Program.DefaultDir });
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Setup failed:\r\n" + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                cancel.Enabled = true;
            }
        }

        static void CloseRunning()
        {
            foreach (Process p in Process.GetProcessesByName("BrickSDR"))
            {
                try { p.CloseMainWindow(); if (!p.WaitForExit(4000)) p.Kill(); p.WaitForExit(3000); } catch { }
            }
        }

        static void RemoveDir(string dir, bool selfInside)
        {
            string self = Application.ExecutablePath;
            bool selfHere = selfInside && self.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        if (selfHere && f.Equals(self, StringComparison.OrdinalIgnoreCase)) continue;
                        File.SetAttributes(f, FileAttributes.Normal);
                        File.Delete(f);
                    }
                    if (!selfHere) Directory.Delete(dir, true);
                    break;
                }
                catch { Thread.Sleep(600); }
            }
            if (selfHere)
                Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + dir + "\"") { CreateNoWindow = true, UseShellExecute = false });
        }

        static void RemoveShortcutsAndRegistry()
        {
            foreach (string p in new[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Brick-SDR.lnk"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Brick-SDR.lnk") })
                try { if (File.Exists(p)) File.Delete(p); } catch { }
            try { Registry.LocalMachine.DeleteSubKeyTree(Program.UninstallKey, false); } catch { }
            SetGpuPreference(Path.Combine(Program.DefaultDir, Program.ExeName), false);
            FirewallRules(null, false);
        }

        // Windows Firewall rules through the firewall COM API, so the program path is stored in the rule
        static void FirewallRules(string exe, bool add)
        {
            try
            {
                Type pt = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                dynamic policy = Activator.CreateInstance(pt);
                // remove every rule with our name (also empty ones left behind by older setups)
                for (int i = 0; i < 20; i++)
                {
                    bool found = false;
                    foreach (dynamic r in policy.Rules)
                        if ((string)r.Name == "Brick-SDR") { found = true; break; }
                    if (!found) break;
                    policy.Rules.Remove("Brick-SDR");
                }
                if (!add) return;
                foreach (int dir in new[] { 1, 2 }) // 1 = in, 2 = out
                {
                    dynamic rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule"));
                    rule.Name = "Brick-SDR";
                    rule.ApplicationName = exe;
                    rule.Direction = dir;
                    rule.Action = 1; // allow
                    rule.Profiles = 0x7FFFFFFF; // all profiles
                    rule.Enabled = true;
                    policy.Rules.Add(rule);
                }
            }
            catch { }
        }

        static void Extract(string dir)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            using (ZipArchive z = new ZipArchive(s, ZipArchiveMode.Read))
                foreach (ZipArchiveEntry en in z.Entries)
                {
                    if (en.FullName.EndsWith("/")) continue;
                    string dest = Path.Combine(dir, en.FullName.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    en.ExtractToFile(dest, true);
                }
        }

        static void Shortcut(string lnk, string target, string workDir)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic sh = Activator.CreateInstance(t);
            dynamic l = sh.CreateShortcut(lnk);
            l.TargetPath = target; l.WorkingDirectory = workDir; l.IconLocation = target + ",0"; l.Description = "Brick-SDR";
            l.Save();
        }

        // Windows "Graphics settings" (Settings > System > Display > Graphics): per-app GPU preference.
        // GpuPreference=2 means high performance.
        static void SetGpuPreference(string exe, bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences"))
                {
                    if (on) k.SetValue(exe, "GpuPreference=2;", RegistryValueKind.String);
                    else k.DeleteValue(exe, false);
                }
            }
            catch { }
        }

        static void Register(string exe)
        {
            using (RegistryKey k = Registry.LocalMachine.CreateSubKey(Program.UninstallKey))
            {
                k.SetValue("DisplayName", "Brick-SDR");
                k.SetValue("DisplayVersion", Program.PayloadVersion());
                k.SetValue("Publisher", "Brick-SDR");
                k.SetValue("InstallLocation", Program.DefaultDir);
                k.SetValue("DisplayIcon", exe);
                k.SetValue("UninstallString", "\"" + Path.Combine(Program.DefaultDir, "Uninstall.exe") + "\" /uninstall");
                k.SetValue("NoModify", 1); k.SetValue("NoRepair", 1);
            }
        }

        static void Run(string file, string args)
        {
            try { Process p = Process.Start(new ProcessStartInfo(file, args) { CreateNoWindow = true, UseShellExecute = false }); p.WaitForExit(8000); } catch { }
        }
    }
}
