/*  BrickAppearance.cs

    Look settings chosen in Setup > Appearance (appearance.txt in the data folder):
    the font of the frequency display (default, seven-segment, any installed font or a font file the user loaded),
    the colour of the frequency and the colour of lit buttons (the "accent", orange by default).
*/

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BrickSDR
{
    internal static class BrickAppearance
    {
        public static readonly Color DefaultAccent = Color.FromArgb(255, 138, 31);
        public const string FontDefault = "";            // Consolas, as before
        public const string FontSevenSeg = "@7seg";      // Digital-7, built into the program

        public static Color Accent = DefaultAccent;
        public static Color? VfoColor;                   // null = follow the accent
        public static string VfoFontKey = FontDefault;
        public static int VfoSizePercent = 100;          // size of the frequency digits, 100 = as designed (the box shrinks them when they do not fit)

        public static event Action<Color, Color> AccentChanged;   // old, new
        public static event Action Changed;                       // font / frequency colour

        private static string dir = "";
        private static readonly PrivateFontCollection userFonts = new PrivateFontCollection();
        private static PrivateFontCollection sevenSeg;
        private static IntPtr sevenSegMem = IntPtr.Zero;
        private static readonly Dictionary<string, FontFamily> cache = new Dictionary<string, FontFamily>();

        public sealed class FontItem
        {
            public string Text, Key;
            public override string ToString() { return Text; }
        }

        private static string File_ { get { return Path.Combine(dir, "appearance.txt"); } }
        private static string FontsDir { get { return Path.Combine(dir, "fonts"); } }

        public static void Load(string dataDir)
        {
            dir = dataDir ?? "";
            try
            {
                if (Directory.Exists(FontsDir))
                    foreach (string f in Directory.GetFiles(FontsDir))
                    {
                        string e = Path.GetExtension(f).ToLowerInvariant();
                        if (e == ".ttf" || e == ".otf") try { userFonts.AddFontFile(f); } catch { }
                    }
            }
            catch { }
            try
            {
                if (!File.Exists(File_)) return;
                foreach (string line in File.ReadAllLines(File_))
                {
                    int i = line.IndexOf('=');
                    if (i < 1) continue;
                    string k = line.Substring(0, i).Trim().ToLowerInvariant(), v = line.Substring(i + 1).Trim();
                    if (k == "vfofont") VfoFontKey = v;
                    else if (k == "vfosize") { int n; if (int.TryParse(v, out n)) VfoSizePercent = Math.Max(40, Math.Min(300, n)); }
                    else if (k == "vfocolor") VfoColor = ParseColor(v);
                    else if (k == "accent") { Color? c = ParseColor(v); if (c.HasValue) Accent = c.Value; }
                }
            }
            catch { }
        }

        private static Color? ParseColor(string v)
        {
            if (string.IsNullOrEmpty(v)) return null;
            try
            {
                v = v.TrimStart('#');
                if (v.Length != 6) return null;
                return Color.FromArgb(Convert.ToInt32(v.Substring(0, 2), 16), Convert.ToInt32(v.Substring(2, 2), 16), Convert.ToInt32(v.Substring(4, 2), 16));
            }
            catch { return null; }
        }

        private static string Hex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

        private static void Save()
        {
            try
            {
                File.WriteAllText(File_, "vfofont=" + VfoFontKey + "\r\nvfosize=" + VfoSizePercent + "\r\nvfocolor=" + (VfoColor.HasValue ? Hex(VfoColor.Value) : "") + "\r\naccent=" + (Accent == DefaultAccent ? "" : Hex(Accent)) + "\r\n");
            }
            catch { }
        }

        public static void SetVfoFont(string key) { VfoFontKey = key ?? FontDefault; Save(); if (Changed != null) Changed(); }
        public static void SetVfoSize(int percent) { VfoSizePercent = Math.Max(40, Math.Min(300, percent)); Save(); if (Changed != null) Changed(); }
        public static void SetVfoColor(Color? c) { VfoColor = c; Save(); if (Changed != null) Changed(); }

        public static void SetAccent(Color c)
        {
            Color old = Accent;
            Accent = c;
            Save();
            if (old != c && AccentChanged != null) AccentChanged(old, c);
        }

        // controls that took the accent colour when they were built (labels, update notice ...) follow a change: every open window is walked
        public static void RecolorAll(Color o, Color n)
        {
            try
            {
                foreach (Form f in Application.OpenForms) Recolor(f, o, n);
            }
            catch { }
        }

        private static void Recolor(Control c, Color o, Color n)
        {
            try
            {
                if (c.Tag is string && (string)c.Tag == "brickSwatch") return;     // the colour samples of Setup show their own colour
                if (c.ForeColor == o) c.ForeColor = n;
                if (c.BackColor == o && !(c is Form)) c.BackColor = n;
                ToolStrip ts = c as ToolStrip;
                if (ts != null) foreach (ToolStripItem it in ts.Items) RecolorItem(it, o, n);
                foreach (Control k in c.Controls) Recolor(k, o, n);
                c.Invalidate();
            }
            catch { }
        }

        private static void RecolorItem(ToolStripItem it, Color o, Color n)
        {
            if (it.ForeColor == o) it.ForeColor = n;
            ToolStripDropDownItem dd = it as ToolStripDropDownItem;
            if (dd != null) foreach (ToolStripItem k in dd.DropDownItems) RecolorItem(k, o, n);
        }

        // the colour the frequency digits get when the VFO is active
        // (independent of the colour of lit buttons: Default is the original orange)
        public static Color FrequencyColor { get { return VfoColor.HasValue ? VfoColor.Value : DefaultAccent; } }

        // the smaller Hz digits: the old darker orange while nothing was changed, otherwise a darker shade of the chosen colour
        public static Color FrequencySmallColor
        {
            get
            {
                Color b = FrequencyColor;
                if (b == DefaultAccent) return Color.FromArgb(200, 110, 30);
                return Color.FromArgb((int)(b.R * 0.72), (int)(b.G * 0.72), (int)(b.B * 0.72));
            }
        }

        // ---- fonts
        private static FontFamily SevenSegFamily()
        {
            if (sevenSeg != null) return sevenSeg.Families.Length > 0 ? sevenSeg.Families[0] : null;
            try
            {
                byte[] data = Properties.Resources.digital7;
                sevenSegMem = Marshal.AllocCoTaskMem(data.Length);      // has to stay allocated while the font is in use
                Marshal.Copy(data, 0, sevenSegMem, data.Length);
                PrivateFontCollection p = new PrivateFontCollection();
                p.AddMemoryFont(sevenSegMem, data.Length);
                sevenSeg = p;
                return p.Families.Length > 0 ? p.Families[0] : null;
            }
            catch { sevenSeg = new PrivateFontCollection(); return null; }
        }

        private static FontFamily FindFamily(string key)
        {
            FontFamily f;
            if (cache.TryGetValue(key, out f)) return f;
            f = null;
            try
            {
                if (key == FontSevenSeg) f = SevenSegFamily();
                else if (key == FontDefault) f = new FontFamily("Consolas");
                else
                {
                    foreach (FontFamily u in userFonts.Families) if (u.Name == key) { f = u; break; }
                    if (f == null) f = new FontFamily(key);
                }
            }
            catch { f = null; }
            if (f == null) { try { f = new FontFamily("Consolas"); } catch { f = FontFamily.GenericMonospace; } }
            cache[key] = f;
            return f;
        }

        // a new font (the caller disposes it) of the chosen face for the frequency digits
        public static Font MakeVfoFont(float size)
        {
            FontFamily f = FindFamily(VfoFontKey);
            if (VfoFontKey == FontSevenSeg) size *= 1.4f;      // the seven-segment digits are drawn small for their em size
            size *= VfoSizePercent / 100f;
            FontStyle st = FontStyle.Bold;
            if (!f.IsStyleAvailable(st)) st = FontStyle.Regular;
            if (!f.IsStyleAvailable(st)) st = f.IsStyleAvailable(FontStyle.Italic) ? FontStyle.Italic : FontStyle.Bold;
            try { return new Font(f, size, st, GraphicsUnit.Point); }
            catch { return new Font("Consolas", size, FontStyle.Bold); }
        }

        // entries of the font drop-down: default, seven-segment, loaded fonts, then everything installed
        public static List<FontItem> FontItems()
        {
            List<FontItem> l = new List<FontItem>();
            l.Add(new FontItem { Text = "Default (Consolas)", Key = FontDefault });
            l.Add(new FontItem { Text = "Seven-segment", Key = FontSevenSeg });
            HashSet<string> seen = new HashSet<string>();
            foreach (FontFamily u in userFonts.Families)
                if (seen.Add(u.Name)) l.Add(new FontItem { Text = u.Name + " (loaded)", Key = u.Name });
            try
            {
                using (InstalledFontCollection ic = new InstalledFontCollection())
                    foreach (FontFamily f in ic.Families)
                        if (seen.Add(f.Name) && (f.IsStyleAvailable(FontStyle.Regular) || f.IsStyleAvailable(FontStyle.Bold)))
                            l.Add(new FontItem { Text = f.Name, Key = f.Name });
            }
            catch { }
            return l;
        }

        // copies a font file into the data folder (kept for the next start) and makes it usable; returns the family name or null
        public static string AddFontFile(string path)
        {
            try
            {
                Directory.CreateDirectory(FontsDir);
                string dest = Path.Combine(FontsDir, Path.GetFileName(path));
                if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase)) File.Copy(path, dest, true);
                string name = null;
                using (PrivateFontCollection t = new PrivateFontCollection())
                {
                    t.AddFontFile(dest);
                    if (t.Families.Length > 0) name = t.Families[0].Name;
                }
                if (name == null) return null;
                userFonts.AddFontFile(dest);
                cache.Clear();
                return name;
            }
            catch { return null; }
        }
    }
}
