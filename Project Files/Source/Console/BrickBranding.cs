using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace BrickSDR
{
    // Product name, logo and icon shared by every window
    internal static class BrickBranding
    {
        public const string ProductName = "Brick-SDR";
        public const string Version = "1.3.4_dev";

        private static Icon _icon;
        private static Bitmap _logo;

        // the icon embedded in the executable (Brick-SDR-icon.ico)
        public static Icon AppIcon
        {
            get
            {
                if (_icon == null)
                {
                    try { _icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
                    catch { _icon = SystemIcons.Application; }
                }
                return _icon;
            }
        }

        // logo tile cut from logo1.jpg
        public static Bitmap Logo
        {
            get
            {
                if (_logo == null)
                {
                    try
                    {
                        using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("brick-logo.png"))
                        {
                            _logo = new Bitmap(s);
                        }
                    }
                    catch { _logo = new Bitmap(1, 1); }
                }
                return _logo;
            }
        }
    }
}
