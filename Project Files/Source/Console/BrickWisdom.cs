using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace BrickSDR
{
    // Builds the FFTW wisdom file on a worker thread and reports the progress in the start-up window
    // instead of the console window wdsp.dll opens on its own.
    internal static class BrickWisdom
    {
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_HIDE = 0;

        // file name used by WDSP 2.11
        private const string WisdomFile = "wdspWisdom01";

        // returns true when the wisdom file had to be rebuilt
        public static bool Run(string directory)
        {
            // a wisdom file shipped next to the exe avoids the long first-start measurement
            try
            {
                string target = System.IO.Path.Combine(directory, WisdomFile);
                string shipped = System.IO.Path.Combine(Application.StartupPath, WisdomFile);
                if (!System.IO.File.Exists(target) && System.IO.File.Exists(shipped) && Splash.AskUseBundledWisdom())
                {
                    System.IO.Directory.CreateDirectory(directory);
                    System.IO.File.Copy(shipped, target);
                }
            }
            catch { }

            int result = 0;
            Thread worker = new Thread(() => { result = WDSP.WDSPwisdom(directory); });
            worker.IsBackground = true;
            worker.Name = "FFT wisdom";

            DateTime start = DateTime.Now;
            string last = "";
            bool setup = false;

            worker.Start();
            while (worker.IsAlive)
            {
                IntPtr console = GetConsoleWindow();
                if (console != IntPtr.Zero) ShowWindow(console, SW_HIDE);

                string status = readStatus();
                if (status.Length > 0 && status != last)
                {
                    last = status;
                    if (!setup)
                    {
                        setup = true;
                        Splash.SetSetupMode();
                    }
                    Splash.SetStatus(status, false);
                    Splash.SetProgress(progressOf(status));
                }
                Thread.Sleep(100);
            }

            if (setup)
            {
                TimeSpan t = DateTime.Now - start;
                Splash.SetStatus("Setup completed (" + (int)t.TotalMinutes + ":" + t.Seconds.ToString("00") + " min)", false);
                Splash.SetProgress(1.0);
            }

            return result == 1;
        }

        private static string readStatus()
        {
            try
            {
                IntPtr p = WDSP.wisdom_get_status();
                if (p == IntPtr.Zero) return "";
                string s = Marshal.PtrToStringAnsi(p);
                return s == null ? "" : s.Trim();
            }
            catch { return ""; }
        }

        // "Planning COMPLEX FORWARD  FFT size 4096" -> 0..1 (complex plans first, then real plans)
        private static double progressOf(string status)
        {
            int at = status.LastIndexOf(' ');
            int size;
            if (at < 0 || !int.TryParse(status.Substring(at + 1), out size) || size < 64) return 0.0;

            double f = (Math.Log(size, 2) - 6.0) / 12.0;
            if (f < 0) f = 0;
            if (f > 1) f = 1;
            return status.Contains("REAL") ? 0.5 + 0.5 * f : 0.5 * f;
        }
    }
}
