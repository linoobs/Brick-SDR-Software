/*  BrickHolidays.cs

    The start picture: on a holiday of the world, of Ukraine or of Germany the splash screen shows a drawn scene for it (Halloween, Christmas, New Year ...).
    Nothing is stored as a picture: every scene is drawn here with GDI+ at the size of the splash, so the program has no art files to carry.

    Off: an empty file  no_holiday_splash.txt  in the data folder.  To look at a scene on another day: environment variable BRICKSDR_HOLIDAY=halloween
    (halloween xmas newyear hearts flowers radio eggs flagua flagde nicholas advent).
*/

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;

namespace BrickSDR
{
    internal sealed class Holiday
    {
        public string Name, Scene, Title, Native;
        public Func<int, DateTime> Date;            // the day in a year
        public int Before, After;                   // days around it
        public int Priority;                        // when two overlap, the higher wins
    }

    internal static class BrickHolidays
    {
        // Easter of the Western churches (Meeus / Jones / Butcher) and of the Orthodox (Julian, plus 13 days for 1900 - 2099)
        public static DateTime EasterWestern(int y)
        {
            int a = y % 19, b = y / 100, c = y % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
            int h = (19 * a + b - d - g + 15) % 30, i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7, m = (a + 11 * h + 22 * l) / 451;
            int month = (h + l - 7 * m + 114) / 31, day = (h + l - 7 * m + 114) % 31 + 1;
            return new DateTime(y, month, day);
        }

        public static DateTime EasterOrthodox(int y)
        {
            int a = y % 4, b = y % 7, c = y % 19, d = (19 * c + 15) % 30, e = (2 * a + 4 * b - d + 34) % 7;
            int month = (d + e + 114) / 31, day = (d + e + 114) % 31 + 1;
            return new DateTime(y, month, day).AddDays(13);
        }

        // the four Sundays before Christmas: n = 1 (first) .. 4
        private static DateTime Advent(int y, int n)
        {
            DateTime x = new DateTime(y, 12, 24);
            while (x.DayOfWeek != DayOfWeek.Sunday) x = x.AddDays(-1);                      // the Sunday before (or on) the 24th = the fourth Advent
            return x.AddDays(-7 * (4 - n));
        }

        private static Holiday H(string name, string scene, string title, string native, Func<int, DateTime> date, int before, int after, int prio)
        {
            return new Holiday { Name = name, Scene = scene, Title = title, Native = native, Date = date, Before = before, After = after, Priority = prio };
        }

        public static readonly List<Holiday> All = new List<Holiday>
        {
            // the world
            H("New Year's Eve", "newyear", "Happy New Year!", "Frohes neues Jahr!  ·  З Новим роком!", y => new DateTime(y, 1, 1), 1, 1, 5),
            H("Valentine's Day", "hearts", "Happy Valentine's Day!", "Alles Liebe  ·  З Днем закоханих!", y => new DateTime(y, 2, 14), 0, 0, 3),
            H("World Amateur Radio Day", "radio", "World Amateur Radio Day", "73 de Brick-SDR  ·  Welttag des Amateurfunks", y => new DateTime(y, 4, 18), 0, 0, 4),
            H("Halloween", "halloween", "Happy Halloween!", "Fröhliches Halloween  ·  Щасливого Гелловіну!", y => new DateTime(y, 10, 31), 0, 0, 4),
            H("Christmas", "xmas", "Merry Christmas!", "Frohe Weihnachten  ·  З Різдвом Христовим!", y => new DateTime(y, 12, 25), 1, 1, 6),
            // Ukraine
            H("Unity Day of Ukraine", "flagua", "Day of Unity of Ukraine", "День Соборності України", y => new DateTime(y, 1, 22), 0, 0, 3),
            H("Orthodox Christmas", "xmas", "Merry Christmas!", "Різдво Христове  ·  З Різдвом!", y => new DateTime(y, 1, 7), 1, 0, 4),
            H("International Women's Day", "flowers", "Happy Women's Day!", "З 8 Березня!  ·  Frauentag", y => new DateTime(y, 3, 8), 0, 0, 3),
            H("Constitution Day of Ukraine", "flagua", "Constitution Day of Ukraine", "День Конституції України", y => new DateTime(y, 6, 28), 0, 0, 3),
            H("Flag Day of Ukraine", "flagua", "Flag Day of Ukraine", "День Державного Прапора України", y => new DateTime(y, 8, 23), 0, 0, 3),
            H("Independence Day of Ukraine", "flagua", "Independence Day of Ukraine", "З Днем Незалежності України!", y => new DateTime(y, 8, 24), 0, 0, 4),
            H("Defenders Day of Ukraine", "flagua", "Defenders Day of Ukraine", "День захисників і захисниць України", y => new DateTime(y, 10, 1), 0, 0, 3),
            H("Easter (Orthodox)", "eggs", "Happy Easter!", "Христос воскрес!  ·  Воістину воскрес!", y => EasterOrthodox(y), 1, 1, 4),
            // Germany
            H("Easter", "eggs", "Happy Easter!", "Frohe Ostern!", y => EasterWestern(y), 1, 1, 4),
            H("Labour Day", "flowers", "Happy May Day!", "Tag der Arbeit  ·  День праці", y => new DateTime(y, 5, 1), 0, 0, 2),
            H("Day of German Unity", "flagde", "Day of German Unity", "Tag der Deutschen Einheit", y => new DateTime(y, 10, 3), 0, 0, 4),
            H("Saint Nicholas Day", "nicholas", "Happy St. Nicholas Day!", "Nikolaustag  ·  День святого Миколая", y => new DateTime(y, 12, 6), 0, 0, 3),
            H("First Advent", "advent", "First Sunday of Advent", "1. Advent", y => Advent(y, 1), 0, 0, 2),
            H("Second Advent", "advent", "Second Sunday of Advent", "2. Advent", y => Advent(y, 2), 0, 0, 2),
            H("Third Advent", "advent", "Third Sunday of Advent", "3. Advent", y => Advent(y, 3), 0, 0, 2),
            H("Fourth Advent", "advent", "Fourth Sunday of Advent", "4. Advent", y => Advent(y, 4), 0, 0, 2),
            H("Christmas Eve", "xmas", "Merry Christmas!", "Frohe Weihnachten  ·  Щасливого Святвечора!", y => new DateTime(y, 12, 24), 0, 0, 7),
            H("New Year's Eve (Silvester)", "newyear", "Happy New Year!", "Guten Rutsch!  ·  З Новим роком!", y => new DateTime(y, 12, 31), 0, 0, 6),
        };

        // the holiday of a day, or null
        public static Holiday On(DateTime day)
        {
            Holiday best = null; int bestDist = int.MaxValue;
            foreach (Holiday h in All)
                for (int y = day.Year - 1; y <= day.Year + 1; y++)
                {
                    DateTime d = h.Date(y);
                    int off = (int)(day.Date - d.Date).TotalDays;
                    if (off < -h.Before || off > h.After) continue;
                    int dist = Math.Abs(off);
                    if (best == null || dist < bestDist || (dist == bestDist && h.Priority > best.Priority)) { best = h; bestDist = dist; }
                }
            return best;
        }

        // the scene to show now (null: the normal splash): a holiday of today, or the one of BRICKSDR_HOLIDAY, unless it is switched off
        public static Holiday Current(string dataFolder, out int advent)
        {
            advent = 0;
            try
            {
                string env = Environment.GetEnvironmentVariable("BRICKSDR_HOLIDAY");
                if (!string.IsNullOrEmpty(env))
                {
                    foreach (Holiday h in All) if (h.Scene.Equals(env, StringComparison.OrdinalIgnoreCase)) { advent = 3; return h; }
                }
                if (dataFolder != null && File.Exists(Path.Combine(dataFolder, "no_holiday_splash.txt"))) return null;
                Holiday t = On(DateTime.Today);
                if (t != null && t.Scene == "advent") advent = t.Name.StartsWith("First") ? 1 : t.Name.StartsWith("Second") ? 2 : t.Name.StartsWith("Third") ? 3 : 4;
                return t;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------ drawing ------------------------------------------------------------------

        private static Color A(int a, Color c) { return Color.FromArgb(a, c); }

        private static void Sky(Graphics g, int w, int h, Color top, Color bottom)
        {
            using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, w, h + 1), top, bottom, 90f)) g.FillRectangle(b, 0, 0, w, h);
        }

        private static void Stars(Graphics g, int w, int h, int n, int seed, int maxY)
        {
            Random r = new Random(seed);
            for (int i = 0; i < n; i++)
            {
                float x = r.Next(w), y = r.Next(maxY), s = 0.8f + (float)r.NextDouble() * 1.8f;
                using (Brush b = new SolidBrush(Color.FromArgb(90 + r.Next(150), 255, 255, 230))) g.FillEllipse(b, x, y, s, s);
            }
        }

        private static void Glow(Graphics g, float cx, float cy, float rad, Color c, int alpha)
        {
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddEllipse(cx - rad, cy - rad, rad * 2, rad * 2);
                using (PathGradientBrush b = new PathGradientBrush(p))
                {
                    b.CenterColor = A(alpha, c);
                    b.SurroundColors = new[] { A(0, c) };
                    g.FillPath(b, p);
                }
            }
        }

        private static GraphicsPath Heart(float cx, float cy, float s)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddBezier(cx, cy + s * 0.9f, cx - s * 1.6f, cy - s * 0.1f, cx - s * 0.9f, cy - s * 1.3f, cx, cy - s * 0.45f);
            p.AddBezier(cx, cy - s * 0.45f, cx + s * 0.9f, cy - s * 1.3f, cx + s * 1.6f, cy - s * 0.1f, cx, cy + s * 0.9f);
            p.CloseFigure();
            return p;
        }

        private static GraphicsPath Star5(float cx, float cy, float ro, float ri)
        {
            PointF[] pt = new PointF[10];
            for (int i = 0; i < 10; i++)
            {
                double a = -Math.PI / 2 + i * Math.PI / 5, r = i % 2 == 0 ? ro : ri;
                pt[i] = new PointF(cx + (float)(Math.Cos(a) * r), cy + (float)(Math.Sin(a) * r));
            }
            GraphicsPath p = new GraphicsPath(); p.AddPolygon(pt); return p;
        }

        private static void Snow(Graphics g, int w, int h, int n, int seed)
        {
            Random r = new Random(seed);
            for (int i = 0; i < n; i++)
            {
                float x = r.Next(w), y = r.Next(h), s = 1.5f + (float)r.NextDouble() * 3.2f;
                using (Brush b = new SolidBrush(Color.FromArgb(120 + r.Next(130), 255, 255, 255))) g.FillEllipse(b, x, y, s, s);
            }
        }

        private static void Hills(Graphics g, int w, int h, int y0, Color c, int seed, float amp)
        {
            Random r = new Random(seed);
            List<PointF> pts = new List<PointF> { new PointF(0, h) };
            double ph = r.NextDouble() * 6, f = 0.012 + r.NextDouble() * 0.01;
            for (int x = 0; x <= w; x += 10) pts.Add(new PointF(x, y0 + (float)(Math.Sin(x * f + ph) * amp + Math.Sin(x * f * 2.3 + ph * 2) * amp * 0.4)));
            pts.Add(new PointF(w, h));
            using (Brush b = new SolidBrush(c)) g.FillPolygon(b, pts.ToArray());
        }

        // ---- the scenes ----

        private static void Halloween(Graphics g, int w, int h)
        {
            Sky(g, w, h, Color.FromArgb(18, 10, 38), Color.FromArgb(120, 48, 18));
            Stars(g, w, h, 70, 3, h / 2);
            Glow(g, 450, 105, 150, Color.FromArgb(255, 200, 90), 90);
            using (Brush b = new SolidBrush(Color.FromArgb(255, 240, 200))) g.FillEllipse(b, 450 - 58, 105 - 58, 116, 116);
            using (Brush b = new SolidBrush(A(60, Color.FromArgb(200, 170, 120)))) { g.FillEllipse(b, 425, 85, 26, 22); g.FillEllipse(b, 462, 112, 18, 15); g.FillEllipse(b, 440, 128, 12, 10); }
            Hills(g, w, h, 300, Color.FromArgb(22, 12, 30), 9, 14);
            // bare tree
            using (Pen p = new Pen(Color.FromArgb(10, 6, 16), 9f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(p, 90, h, 96, 215);
                p.Width = 5; g.DrawLine(p, 96, 250, 40, 195); g.DrawLine(p, 96, 232, 150, 178); g.DrawLine(p, 94, 275, 140, 235);
                p.Width = 3; g.DrawLine(p, 40, 195, 14, 190); g.DrawLine(p, 40, 195, 30, 160); g.DrawLine(p, 150, 178, 178, 170); g.DrawLine(p, 150, 178, 148, 148); g.DrawLine(p, 140, 235, 172, 232);
            }
            // bats
            using (Brush b = new SolidBrush(Color.FromArgb(10, 6, 16)))
            {
                float[][] bats = { new[] { 330f, 60f, 1.0f }, new[] { 520f, 190f, 0.8f }, new[] { 250f, 150f, 0.65f }, new[] { 560f, 52f, 0.7f } };
                foreach (float[] t in bats)
                {
                    float x = t[0], y = t[1], s = t[2];
                    GraphicsPath p = new GraphicsPath();
                    p.AddBezier(x, y, x - 14 * s, y - 20 * s, x - 34 * s, y - 14 * s, x - 38 * s, y - 2 * s);
                    p.AddBezier(x - 38 * s, y - 2 * s, x - 30 * s, y + 2 * s, x - 22 * s, y - 2 * s, x - 16 * s, y + 8 * s);
                    p.AddBezier(x - 16 * s, y + 8 * s, x - 8 * s, y + 2 * s, x - 3 * s, y + 6 * s, x, y + 12 * s);
                    p.AddBezier(x, y + 12 * s, x + 3 * s, y + 6 * s, x + 8 * s, y + 2 * s, x + 16 * s, y + 8 * s);
                    p.AddBezier(x + 16 * s, y + 8 * s, x + 22 * s, y - 2 * s, x + 30 * s, y + 2 * s, x + 38 * s, y - 2 * s);
                    p.AddBezier(x + 38 * s, y - 2 * s, x + 34 * s, y - 14 * s, x + 14 * s, y - 20 * s, x, y);
                    g.FillPath(b, p); p.Dispose();
                    g.FillPolygon(b, new[] { new PointF(x - 4 * s, y - 2 * s), new PointF(x - 2 * s, y - 10 * s), new PointF(x, y - 3 * s), new PointF(x + 2 * s, y - 10 * s), new PointF(x + 4 * s, y - 2 * s) });
                }
            }
            // pumpkins
            Pumpkin(g, 400, 352, 62, true); Pumpkin(g, 490, 366, 44, false); Pumpkin(g, 340, 374, 34, false); Pumpkin(g, 560, 362, 50, true);
        }

        private static void Pumpkin(Graphics g, float cx, float by, float s, bool face)
        {
            Glow(g, cx, by - s * 0.5f, s * 1.4f, Color.FromArgb(255, 140, 20), face ? 70 : 30);
            Color body = Color.FromArgb(235, 110, 20), dark = Color.FromArgb(170, 70, 10);
            for (int i = -2; i <= 2; i++)
            {
                float x = cx + i * s * 0.28f, rw = s * (i == 0 ? 0.62f : i % 2 == 0 ? 0.5f : 0.46f);
                using (Brush b = new LinearGradientBrush(new RectangleF(x - rw, by - s * 0.95f, rw * 2, s * 0.95f), i == 0 ? body : dark, i == 0 ? Color.FromArgb(255, 150, 40) : body, 0f))
                    g.FillEllipse(b, x - rw, by - s * 0.95f, rw * 2, s * 0.95f);
            }
            using (Brush b = new SolidBrush(Color.FromArgb(70, 100, 30))) g.FillPolygon(b, new[] { new PointF(cx - s * 0.07f, by - s * 0.93f), new PointF(cx + s * 0.09f, by - s * 0.93f), new PointF(cx + s * 0.16f, by - s * 1.12f), new PointF(cx + s * 0.02f, by - s * 1.1f) });
            if (!face) return;
            Color lit = Color.FromArgb(255, 226, 90);
            using (Brush b = new SolidBrush(lit))
            {
                g.FillPolygon(b, new[] { new PointF(cx - s * 0.36f, by - s * 0.62f), new PointF(cx - s * 0.16f, by - s * 0.62f), new PointF(cx - s * 0.26f, by - s * 0.8f) });
                g.FillPolygon(b, new[] { new PointF(cx + s * 0.16f, by - s * 0.62f), new PointF(cx + s * 0.36f, by - s * 0.62f), new PointF(cx + s * 0.26f, by - s * 0.8f) });
                PointF[] m = { new PointF(cx - s * 0.4f, by - s * 0.42f), new PointF(cx - s * 0.24f, by - s * 0.3f), new PointF(cx - s * 0.12f, by - s * 0.4f), new PointF(cx, by - s * 0.28f), new PointF(cx + s * 0.12f, by - s * 0.4f), new PointF(cx + s * 0.24f, by - s * 0.3f), new PointF(cx + s * 0.4f, by - s * 0.42f), new PointF(cx + s * 0.3f, by - s * 0.18f), new PointF(cx, by - s * 0.1f), new PointF(cx - s * 0.3f, by - s * 0.18f) };
                g.FillPolygon(b, m);
            }
        }

        private static void Xmas(Graphics g, int w, int h)
        {
            Sky(g, w, h, Color.FromArgb(8, 18, 52), Color.FromArgb(40, 70, 120));
            Stars(g, w, h, 90, 5, h * 2 / 3);
            Glow(g, 90, 80, 90, Color.FromArgb(220, 230, 255), 70);
            using (Brush b = new SolidBrush(Color.FromArgb(240, 245, 255))) g.FillEllipse(b, 90 - 30, 80 - 30, 60, 60);
            using (Brush b = new SolidBrush(Color.FromArgb(8, 18, 52))) g.FillEllipse(b, 90 - 20, 80 - 36, 60, 60);        // a crescent
            Hills(g, w, h, 305, Color.FromArgb(205, 220, 240), 4, 12);
            Hills(g, w, h, 335, Color.FromArgb(240, 246, 255), 6, 10);
            // far firs
            using (Brush b = new SolidBrush(Color.FromArgb(22, 62, 52)))
                foreach (float x in new[] { 40f, 120f, 480f, 545f, 590f })
                    for (int i = 0; i < 3; i++) g.FillPolygon(b, new[] { new PointF(x, 215 + i * 28 + (x % 3) * 8), new PointF(x - 34 + i * 4, 260 + i * 28 + (x % 3) * 8), new PointF(x + 34 - i * 4, 260 + i * 28 + (x % 3) * 8) });
            // the tree
            float cx = 330;
            using (Brush b = new SolidBrush(Color.FromArgb(90, 58, 32))) g.FillRectangle(b, cx - 12, 326, 24, 34);
            Color[] greens = { Color.FromArgb(18, 94, 58), Color.FromArgb(26, 120, 70), Color.FromArgb(34, 146, 84) };
            for (int i = 0; i < 5; i++)
            {
                float top = 90 + i * 48, wd = 54 + i * 24, bot = top + 78;
                using (Brush b = new LinearGradientBrush(new PointF(cx - wd, 0), new PointF(cx + wd, 0), greens[0], greens[2])) g.FillPolygon(b, new[] { new PointF(cx, top), new PointF(cx - wd, bot), new PointF(cx + wd, bot) });
                using (Brush b = new SolidBrush(A(190, Color.White))) g.FillPolygon(b, new[] { new PointF(cx - wd * 0.9f, bot - 6), new PointF(cx - wd * 0.62f, bot - 14), new PointF(cx - wd * 0.32f, bot - 4), new PointF(cx, bot - 12), new PointF(cx + wd * 0.34f, bot - 4), new PointF(cx + wd * 0.64f, bot - 14), new PointF(cx + wd * 0.9f, bot - 6), new PointF(cx + wd, bot), new PointF(cx - wd, bot) });
            }
            Random r = new Random(11);
            Color[] cols = { Color.FromArgb(230, 40, 50), Color.FromArgb(250, 200, 50), Color.FromArgb(70, 140, 255), Color.FromArgb(255, 255, 255), Color.FromArgb(250, 110, 190) };
            for (int i = 0; i < 26; i++)
            {
                float y = 120 + r.Next(210), half = (y - 90) / 330f * 140 + 14;
                float x = cx + (float)((r.NextDouble() * 2 - 1) * half * 0.85);
                Color c = cols[r.Next(cols.Length)];
                Glow(g, x, y, 11, c, 80);
                using (Brush b = new SolidBrush(c)) g.FillEllipse(b, x - 4.5f, y - 4.5f, 9, 9);
                using (Brush b = new SolidBrush(A(150, Color.White))) g.FillEllipse(b, x - 2.5f, y - 3.5f, 3, 3);
            }
            using (GraphicsPath s = Star5(cx, 82, 22, 9))
            {
                Glow(g, cx, 82, 48, Color.FromArgb(255, 220, 90), 140);
                using (Brush b = new SolidBrush(Color.FromArgb(255, 218, 70))) g.FillPath(b, s);
            }
            // gifts
            Gift(g, 400, 352, 46, 36, Color.FromArgb(200, 40, 50), Color.FromArgb(250, 210, 80));
            Gift(g, 444, 362, 34, 28, Color.FromArgb(40, 110, 220), Color.FromArgb(240, 240, 250));
            Gift(g, 478, 356, 52, 40, Color.FromArgb(250, 200, 60), Color.FromArgb(200, 40, 50));
            Gift(g, 534, 366, 36, 30, Color.FromArgb(60, 170, 100), Color.FromArgb(250, 235, 120));
            Snow(g, w, h, 140, 8);
        }

        private static void Gift(Graphics g, float x, float by, float wd, float ht, Color box, Color ribbon)
        {
            using (Brush b = new SolidBrush(box)) g.FillRectangle(b, x, by - ht, wd, ht);
            using (Brush b = new SolidBrush(A(50, Color.Black))) g.FillRectangle(b, x + wd * 0.6f, by - ht, wd * 0.4f, ht);
            using (Brush b = new SolidBrush(ribbon)) { g.FillRectangle(b, x + wd * 0.42f, by - ht, wd * 0.16f, ht); g.FillRectangle(b, x, by - ht * 0.58f, wd, ht * 0.14f); g.FillEllipse(b, x + wd * 0.2f, by - ht - 8, wd * 0.3f, 12); g.FillEllipse(b, x + wd * 0.5f, by - ht - 8, wd * 0.3f, 12); }
        }

        private static void NewYear(Graphics g, int w, int h)
        {
            Sky(g, w, h, Color.FromArgb(4, 6, 24), Color.FromArgb(32, 24, 70));
            Stars(g, w, h, 60, 7, h / 2);
            Color[] cols = { Color.FromArgb(255, 90, 90), Color.FromArgb(255, 210, 70), Color.FromArgb(90, 190, 255), Color.FromArgb(180, 120, 255), Color.FromArgb(120, 255, 170), Color.FromArgb(255, 150, 60) };
            float[][] bursts = { new[] { 120f, 120f, 70f }, new[] { 300f, 80f, 85f }, new[] { 470f, 135f, 75f }, new[] { 560f, 60f, 45f }, new[] { 40f, 56f, 36f }, new[] { 380f, 190f, 40f } };
            Random r = new Random(21);
            for (int bi = 0; bi < bursts.Length; bi++)
            {
                float cx = bursts[bi][0], cy = bursts[bi][1], rad = bursts[bi][2];
                Color c = cols[bi % cols.Length];
                Glow(g, cx, cy, rad * 1.5f, c, 55);
                int rays = 28 + (int)rad / 4;
                for (int i = 0; i < rays; i++)
                {
                    double a = i * 2 * Math.PI / rays + bi;
                    float len = rad * (0.7f + (float)r.NextDouble() * 0.3f);
                    float x1 = cx + (float)Math.Cos(a) * rad * 0.25f, y1 = cy + (float)Math.Sin(a) * rad * 0.25f, x2 = cx + (float)Math.Cos(a) * len, y2 = cy + (float)Math.Sin(a) * len;
                    using (LinearGradientBrush lb = new LinearGradientBrush(new PointF(x1, y1), new PointF(x2, y2), A(30, c), A(240, c)))
                    using (Pen p = new Pen(lb, 1.6f)) g.DrawLine(p, x1, y1, x2, y2);
                    using (Brush b = new SolidBrush(Color.White)) g.FillEllipse(b, x2 - 1.6f, y2 - 1.6f, 3.2f, 3.2f);
                    float x3 = cx + (float)Math.Cos(a) * rad * 1.12f, y3 = cy + (float)Math.Sin(a) * rad * 1.12f + 6;
                    using (Brush b = new SolidBrush(A(150, c))) g.FillEllipse(b, x3 - 1.2f, y3 - 1.2f, 2.4f, 2.4f);
                }
            }
            // the city
            Random rb = new Random(4);
            using (Brush b = new SolidBrush(Color.FromArgb(10, 10, 26)))
            {
                int x = 0;
                while (x < w) { int bw = 24 + rb.Next(34), bh = 50 + rb.Next(110); g.FillRectangle(b, x, h - bh, bw, bh); x += bw + 2; }
            }
            using (Brush b = new SolidBrush(Color.FromArgb(255, 210, 110)))
                for (int i = 0; i < 90; i++) { int x = rb.Next(w), y = h - 8 - rb.Next(120); g.FillRectangle(b, x, y, 3, 4); }
        }

        private static void Hearts(Graphics g, int w, int h)
        {
            Sky(g, w, h, Color.FromArgb(120, 20, 70), Color.FromArgb(235, 90, 130));
            Glow(g, 300, 210, 260, Color.FromArgb(255, 180, 200), 90);
            Random r = new Random(14);
            for (int i = 0; i < 38; i++)
            {
                float s = 8 + (float)Math.Pow(r.NextDouble(), 2) * 46, x = r.Next(w), y = r.Next(h);
                Color c = Color.FromArgb(255, 70 + r.Next(80), 100 + r.Next(110));
                using (GraphicsPath p = Heart(x, y, s))
                {
                    using (Brush b = new LinearGradientBrush(new RectangleF(x - s * 1.5f, y - s * 1.3f, s * 3, s * 2.4f), A(210, Color.FromArgb(255, 160, 190)), A(230, c), 60f)) g.FillPath(b, p);
                    using (Pen pen = new Pen(A(120, Color.White), 1.2f)) g.DrawPath(pen, p);
                }
            }
            using (GraphicsPath p = Heart(450, 200, 62))
            {
                Glow(g, 450, 200, 120, Color.FromArgb(255, 80, 110), 120);
                using (Brush b = new LinearGradientBrush(new RectangleF(380, 120, 140, 130), Color.FromArgb(255, 120, 150), Color.FromArgb(205, 20, 70), 70f)) g.FillPath(b, p);
                using (Brush b = new SolidBrush(A(120, Color.White))) g.FillEllipse(b, 405, 140, 26, 14);
            }
        }

        private static void Tulip(Graphics g, float x, float by, float s, Color c)
        {
            using (Pen p = new Pen(Color.FromArgb(40, 140, 60), 3f)) g.DrawBezier(p, x, by, x + 5, by - s * 0.4f, x - 5, by - s * 0.7f, x, by - s);
            using (Brush b = new SolidBrush(Color.FromArgb(50, 160, 70))) g.FillEllipse(b, x - 4, by - s * 0.5f, 22, 8);
            float t = by - s;
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddBezier(x - 13, t - 18, x - 15, t + 6, x - 5, t + 12, x, t + 12);
                p.AddBezier(x, t + 12, x + 5, t + 12, x + 15, t + 6, x + 13, t - 18);
                p.AddLine(x + 13, t - 18, x + 6, t - 8); p.AddLine(x + 6, t - 8, x, t - 20); p.AddLine(x, t - 20, x - 6, t - 8); p.AddLine(x - 6, t - 8, x - 13, t - 18);
                using (Brush b = new LinearGradientBrush(new PointF(x - 14, t), new PointF(x + 14, t), c, ControlPaint2.Lighter(c))) g.FillPath(b, p);
            }
        }

        private static class ControlPaint2
        {
            public static Color Lighter(Color c) { return Color.FromArgb(c.A, Math.Min(255, c.R + 60), Math.Min(255, c.G + 60), Math.Min(255, c.B + 60)); }
        }

        private static void Flowers(Graphics g, int w, int h)
        {
            Sky(g, w, h, Color.FromArgb(120, 200, 240), Color.FromArgb(250, 240, 200));
            Glow(g, 520, 70, 140, Color.FromArgb(255, 240, 150), 160);
            using (Brush b = new SolidBrush(Color.FromArgb(255, 230, 110))) g.FillEllipse(b, 490, 40, 60, 60);
            Hills(g, w, h, 285, Color.FromArgb(130, 200, 110), 2, 16);
            Hills(g, w, h, 325, Color.FromArgb(78, 170, 84), 5, 12);
            Random r = new Random(6);
            Color[] cols = { Color.FromArgb(240, 60, 90), Color.FromArgb(255, 150, 40), Color.FromArgb(250, 220, 60), Color.FromArgb(230, 90, 190), Color.FromArgb(255, 255, 255), Color.FromArgb(160, 90, 220) };
            for (int i = 0; i < 17; i++) Tulip(g, 26 + i * 35 + r.Next(14), 392 - r.Next(16), 70 + r.Next(60), cols[r.Next(cols.Length)]);
            // mimosa
            using (Pen p = new Pen(Color.FromArgb(70, 130, 60), 3f)) g.DrawBezier(p, 40, 280, 70, 230, 120, 215, 170, 195);
            Random rm = new Random(2);
            for (int i = 0; i < 60; i++)
            {
                double t = rm.NextDouble(); float x = 40 + (float)t * 130 + rm.Next(-14, 14), y = 280 - (float)Math.Pow(t, 0.9) * 90 + rm.Next(-14, 14);
                using (Brush b = new SolidBrush(Color.FromArgb(255, 220 + rm.Next(30), 40 + rm.Next(40)))) g.FillEllipse(b, x, y, 9, 9);
            }
        }

        private static void Radio(Graphics g, int w, int h)
        {
            Sky(g, w, h, Color.FromArgb(5, 10, 34), Color.FromArgb(24, 62, 96));
            Stars(g, w, h, 100, 12, h * 2 / 3);
            Hills(g, w, h, 330, Color.FromArgb(8, 18, 30), 3, 10);
            // waves
            Color wave = Color.FromArgb(255, 138, 31);
            for (int i = 1; i <= 9; i++)
                using (Pen p = new Pen(A(Math.Max(18, 220 - i * 24), wave), 2.2f))
                    g.DrawArc(p, 330 - i * 46, 150 - i * 46, i * 92, i * 92, 200, 140);
            // the mast
            using (Pen p = new Pen(Color.FromArgb(200, 205, 215), 3f))
            {
                g.DrawLine(p, 330, 345, 330, 150);
                for (int y = 345; y > 160; y -= 22) { g.DrawLine(p, 318, y, 342, y - 22); g.DrawLine(p, 342, y, 318, y - 22); }
                p.Width = 2; g.DrawLine(p, 330, 150, 330, 128);
                g.DrawLine(p, 262, 168, 398, 168); g.DrawLine(p, 280, 168, 280, 182); g.DrawLine(p, 380, 168, 380, 182);
            }
            using (Brush b = new SolidBrush(Color.FromArgb(255, 70, 70))) g.FillEllipse(b, 326, 122, 8, 8);
            Glow(g, 330, 126, 22, Color.FromArgb(255, 60, 60), 160);
            // morse: "73" = --... ...--
            string morse = "--... ...--";
            float x0 = 56, yy = 372; using (Brush b = new SolidBrush(wave))
                foreach (char c in morse) { if (c == '-') { g.FillRectangle(b, x0, yy, 30, 8); x0 += 36; } else if (c == '.') { g.FillRectangle(b, x0, yy, 8, 8); x0 += 14; } else x0 += 30; }
            using (Font f = new Font("Segoe UI", 12f, FontStyle.Bold)) g.DrawString("73", f, Brushes.White, 460, 20);
        }

        private static void Eggs(Graphics g, int w, int h)
        {
            Sky(g, w, h, Color.FromArgb(170, 220, 250), Color.FromArgb(255, 245, 210));
            Glow(g, 90, 70, 120, Color.FromArgb(255, 245, 160), 150);
            Hills(g, w, h, 280, Color.FromArgb(150, 214, 118), 7, 16);
            Hills(g, w, h, 335, Color.FromArgb(94, 180, 86), 2, 12);
            Color[][] pal = { new[] { Color.FromArgb(240, 90, 120), Color.FromArgb(255, 230, 100) }, new[] { Color.FromArgb(80, 160, 240), Color.FromArgb(255, 255, 255) }, new[] { Color.FromArgb(255, 160, 50), Color.FromArgb(120, 70, 190) }, new[] { Color.FromArgb(110, 200, 110), Color.FromArgb(255, 100, 130) }, new[] { Color.FromArgb(180, 110, 230), Color.FromArgb(255, 230, 120) } };
            float[][] pos = { new[] { 330f, 372f, 46f }, new[] { 400f, 384f, 38f }, new[] { 470f, 378f, 54f }, new[] { 540f, 386f, 40f }, new[] { 580f, 372f, 34f } };
            for (int i = 0; i < pos.Length; i++) Egg(g, pos[i][0], pos[i][1], pos[i][2], pal[i][0], pal[i][1]);
            // a branch with catkins and flowers
            using (Pen p = new Pen(Color.FromArgb(110, 80, 50), 4f)) g.DrawBezier(p, 600, 40, 540, 70, 500, 60, 440, 110);
            Random r = new Random(5);
            for (int i = 0; i < 18; i++)
            {
                float t = (float)r.NextDouble(); float x = 600 - t * 160, y = 40 + t * 70 + r.Next(-6, 24);
                using (Brush b = new SolidBrush(i % 3 == 0 ? Color.FromArgb(255, 255, 255) : Color.FromArgb(255, 190, 220))) g.FillEllipse(b, x, y, 12, 12);
                using (Brush b = new SolidBrush(Color.FromArgb(255, 220, 60))) g.FillEllipse(b, x + 4, y + 4, 4, 4);
            }
        }

        private static void Egg(Graphics g, float cx, float by, float s, Color a, Color b2)
        {
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddBezier(cx, by - s * 1.5f, cx + s * 0.95f, by - s * 1.5f, cx + s * 0.95f, by, cx, by);
                p.AddBezier(cx, by, cx - s * 0.95f, by, cx - s * 0.95f, by - s * 1.5f, cx, by - s * 1.5f);
                using (Brush b = new LinearGradientBrush(new RectangleF(cx - s, by - s * 1.5f, s * 2, s * 1.5f), ControlPaint2.Lighter(a), a, 45f)) g.FillPath(b, p);
                g.SetClip(p, CombineMode.Intersect);
                using (Pen pn = new Pen(b2, Math.Max(2f, s * 0.12f)))
                {
                    PointF[] z = new PointF[9];
                    for (int i = 0; i < 9; i++) z[i] = new PointF(cx - s + i * s / 4f, by - s * 0.78f + (i % 2 == 0 ? -s * 0.1f : s * 0.1f));
                    g.DrawLines(pn, z);
                    for (int i = 0; i < 9; i++) z[i] = new PointF(cx - s + i * s / 4f, by - s * 0.38f + (i % 2 == 0 ? s * 0.1f : -s * 0.1f));
                    g.DrawLines(pn, z);
                }
                using (Brush b = new SolidBrush(A(110, Color.White))) g.FillEllipse(b, cx - s * 0.55f, by - s * 1.3f, s * 0.3f, s * 0.5f);
                g.ResetClip();
            }
        }

        private static void FlagUa(Graphics g, int w, int h)
        {
            Sky(g, w, h, Color.FromArgb(0, 87, 183), Color.FromArgb(110, 175, 235));
            Glow(g, 470, 90, 160, Color.FromArgb(255, 235, 150), 130);
            using (Brush b = new SolidBrush(Color.FromArgb(255, 221, 0))) g.FillEllipse(b, 440, 62, 60, 60);
            using (Brush b = new LinearGradientBrush(new Rectangle(0, 200, w, h - 200), Color.FromArgb(255, 213, 0), Color.FromArgb(230, 160, 0), 90f)) g.FillRectangle(b, 0, 205, w, h - 205);
            // wheat
            Random r = new Random(9);
            for (int i = 0; i < 90; i++)
            {
                float x = r.Next(w), by = h + 8, len = 60 + r.Next(110), top = by - len, lean = r.Next(-16, 16);
                using (Pen p = new Pen(Color.FromArgb(170, 120, 20), 1.8f)) g.DrawLine(p, x, by, x + lean, top);
                using (Brush b = new SolidBrush(Color.FromArgb(240, 190, 40)))
                    for (int k = 0; k < 6; k++) { g.FillEllipse(b, x + lean - 5, top + k * 7 - 2, 5, 9); g.FillEllipse(b, x + lean + 1, top + k * 7 + 1, 5, 9); }
            }
            // a ribbon: the flag in the sky
            GraphicsPath flag = new GraphicsPath();
            PointF[] top2 = new PointF[31], bot = new PointF[31];
            for (int i = 0; i <= 30; i++) { float x = 40 + i * 12; float y = 70 + (float)Math.Sin(i * 0.38) * 14; top2[i] = new PointF(x, y); bot[i] = new PointF(x, y + 72); }
            for (int part = 0; part < 2; part++)
            {
                GraphicsPath p = new GraphicsPath();
                PointF[] a = new PointF[31], b2 = new PointF[31];
                for (int i = 0; i <= 30; i++) { a[i] = new PointF(top2[i].X, top2[i].Y + part * 36); b2[30 - i] = new PointF(top2[i].X, top2[i].Y + (part + 1) * 36); }
                p.AddPolygon(MergeP(a, b2));
                using (Brush br = new SolidBrush(part == 0 ? Color.FromArgb(0, 87, 183) : Color.FromArgb(255, 213, 0))) g.FillPath(br, p);
                p.Dispose();
            }
            using (Pen pole = new Pen(Color.FromArgb(70, 50, 30), 4f)) g.DrawLine(pole, 40, 50, 40, 330);
            using (Pen pn = new Pen(A(120, Color.White), 1.5f)) g.DrawLines(pn, top2);
        }

        private static PointF[] MergeP(PointF[] a, PointF[] b) { PointF[] r = new PointF[a.Length + b.Length]; a.CopyTo(r, 0); b.CopyTo(r, a.Length); return r; }

        private static void FlagDe(Graphics g, int w, int h)
        {
            Sky(g, w, h, Color.FromArgb(30, 34, 52), Color.FromArgb(70, 80, 110));
            Color[] c = { Color.FromArgb(18, 18, 18), Color.FromArgb(221, 0, 0), Color.FromArgb(255, 206, 0) };
            for (int band = 0; band < 3; band++)
            {
                List<PointF> pts = new List<PointF>();
                for (int x = 0; x <= w; x += 6) pts.Add(new PointF(x, 70 + band * 100 + (float)(Math.Sin(x * 0.02 + band * 0.4) * 22)));
                for (int x = w; x >= 0; x -= 6) pts.Add(new PointF(x, 170 + band * 100 + (float)(Math.Sin(x * 0.02 + band * 0.4) * 22)));
                using (Brush b = new LinearGradientBrush(new Rectangle(0, 70 + band * 100, w, 140), ControlPaint2.Lighter(c[band]), c[band], 90f)) g.FillPolygon(b, pts.ToArray());
            }
            // the Brandenburg Gate
            using (Brush b = new SolidBrush(A(235, Color.FromArgb(20, 20, 26))))
            {
                float x0 = 200, y0 = 215, wd = 200, ht = 140;
                g.FillRectangle(b, x0 - 12, y0, wd + 24, 18);
                for (int i = 0; i < 6; i++) g.FillRectangle(b, x0 + i * (wd - 18) / 5f, y0 + 18, 18, ht - 18);
                g.FillRectangle(b, x0 - 12, y0 + ht, wd + 24, 10);
                g.FillRectangle(b, x0 + 20, y0 - 30, wd - 40, 30);
                g.FillRectangle(b, x0 + wd / 2 - 20, y0 - 56, 40, 26);
                g.FillPolygon(b, new[] { new PointF(x0 + wd / 2 - 24, y0 - 56), new PointF(x0 + wd / 2, y0 - 82), new PointF(x0 + wd / 2 + 24, y0 - 56) });
            }
            using (Brush b = new SolidBrush(A(70, Color.White))) for (int i = 0; i < 40; i++) { }
        }

        private static void Nicholas(Graphics g, int w, int h)
        {
            Sky(g, w, h, Color.FromArgb(70, 12, 34), Color.FromArgb(150, 50, 70));
            Stars(g, w, h, 80, 17, h / 2);
            Glow(g, 300, 140, 200, Color.FromArgb(255, 210, 120), 70);
            using (GraphicsPath s = Star5(300, 120, 44, 18)) { using (Brush b = new SolidBrush(Color.FromArgb(255, 218, 80))) g.FillPath(b, s); }
            Hills(g, w, h, 320, Color.FromArgb(235, 238, 250), 8, 12);
            // a boot full of sweets
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddPolygon(new[] { new PointF(250, 190), new PointF(332, 190), new PointF(334, 300), new PointF(410, 322), new PointF(414, 366), new PointF(250, 366) });
                using (Brush b = new LinearGradientBrush(new RectangleF(250, 190, 170, 180), Color.FromArgb(215, 45, 55), Color.FromArgb(140, 20, 34), 90f)) g.FillPath(b, p);
            }
            using (Brush b = new SolidBrush(Color.FromArgb(250, 250, 255))) g.FillRectangle(b, 244, 180, 96, 26);
            using (Brush b = new SolidBrush(Color.FromArgb(255, 230, 120))) g.FillRectangle(b, 250, 330, 160, 8);
            Random r = new Random(3); Color[] cols = { Color.FromArgb(250, 90, 90), Color.FromArgb(250, 220, 70), Color.FromArgb(90, 190, 250), Color.FromArgb(120, 220, 130) };
            for (int i = 0; i < 9; i++) using (Brush b = new SolidBrush(cols[r.Next(cols.Length)])) g.FillEllipse(b, 262 + r.Next(60), 170 + r.Next(20), 18, 18);
            using (Pen p = new Pen(Color.White, 6f)) { g.DrawLine(p, 346, 120, 346, 180); p.Color = Color.FromArgb(230, 40, 50); for (int y = 124; y < 176; y += 14) g.DrawLine(p, 343, y, 349, y + 6); }
            using (Pen p = new Pen(Color.White, 6f)) { g.DrawArc(p, 336, 100, 22, 22, 180, 180); }
            Gift(g, 470, 366, 54, 42, Color.FromArgb(60, 140, 230), Color.FromArgb(250, 220, 80));
            Gift(g, 530, 366, 58, 46, Color.FromArgb(240, 190, 50), Color.FromArgb(210, 40, 50));
            Snow(g, w, h, 120, 19);
        }

        private static void AdventScene(Graphics g, int w, int h, int lit)
        {
            Sky(g, w, h, Color.FromArgb(20, 14, 12), Color.FromArgb(70, 38, 22));
            Glow(g, 300, 215, 280, Color.FromArgb(255, 170, 70), 70);
            // the wreath
            Random r = new Random(7);
            using (Pen p = new Pen(Color.FromArgb(24, 100, 52), 34f)) g.DrawEllipse(p, 130, 250, 340, 110);
            for (int i = 0; i < 340; i++)
            {
                double a = r.NextDouble() * Math.PI * 2; float rx = 170 + (float)((r.NextDouble() - 0.5) * 40), ry = 55 + (float)((r.NextDouble() - 0.5) * 38);
                float x = 300 + (float)Math.Cos(a) * rx, y = 305 + (float)Math.Sin(a) * ry;
                using (Brush b = new SolidBrush(Color.FromArgb(24 + r.Next(30), 100 + r.Next(60), 50 + r.Next(30)))) g.FillEllipse(b, x - 5, y - 3, 10, 6);
            }
            for (int i = 0; i < 14; i++) { double a = i * Math.PI * 2 / 14; using (Brush b = new SolidBrush(Color.FromArgb(210, 30, 40))) g.FillEllipse(b, 300 + (float)Math.Cos(a) * 170 - 5, 305 + (float)Math.Sin(a) * 55 - 5, 10, 10); }
            using (Brush b = new SolidBrush(Color.FromArgb(220, 40, 50))) g.FillPolygon(b, new[] { new PointF(278, 360), new PointF(300, 346), new PointF(322, 360), new PointF(322, 376), new PointF(300, 362), new PointF(278, 376) });
            // the four candles
            float[] xs = { 165f, 245f, 355f, 435f };
            float[] ys = { 292f, 262f, 262f, 292f };
            for (int i = 0; i < 4; i++)
            {
                float x = xs[i], y = ys[i];
                using (Brush b = new LinearGradientBrush(new RectangleF(x - 11, y - 76, 22, 76), Color.FromArgb(235, 40, 50), Color.FromArgb(150, 20, 34), 0f)) g.FillRectangle(b, x - 11, y - 76, 22, 76);
                using (Pen p = new Pen(Color.FromArgb(40, 30, 20), 2f)) g.DrawLine(p, x, y - 76, x, y - 83);
                if (i < lit)
                {
                    Glow(g, x, y - 100, 60, Color.FromArgb(255, 190, 80), 160);
                    using (GraphicsPath f = new GraphicsPath())
                    {
                        f.AddBezier(x, y - 128, x + 12, y - 110, x + 10, y - 92, x, y - 84);
                        f.AddBezier(x, y - 84, x - 10, y - 92, x - 12, y - 110, x, y - 128);
                        using (Brush b = new LinearGradientBrush(new RectangleF(x - 12, y - 128, 24, 44), Color.FromArgb(255, 250, 200), Color.FromArgb(255, 150, 20), 90f)) g.FillPath(b, f);
                    }
                }
            }
        }

        // a black veil from y0 to y1 whose strength goes from a0 to a1 (one line at a time: a gradient brush leaves hairlines)
        private static void Veil(Graphics g, int w, int y0, int y1, int a0, int a1)
        {
            for (int y = y0; y < y1; y++)
            {
                int a = a0 + (a1 - a0) * (y - y0) / Math.Max(1, y1 - y0 - 1);
                using (Brush b = new SolidBrush(Color.FromArgb(Math.Max(0, Math.Min(255, a)), 0, 0, 0))) g.FillRectangle(b, 0, y, w, 1);
            }
        }

        // draws the scene of a holiday into a bitmap of the splash size
        public static Bitmap Render(Holiday h, int advent, int w, int ht)
        {
            Bitmap bmp = new Bitmap(w, ht);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                switch (h.Scene)
                {
                    case "halloween": Halloween(g, w, ht); break;
                    case "xmas": Xmas(g, w, ht); break;
                    case "newyear": NewYear(g, w, ht); break;
                    case "hearts": Hearts(g, w, ht); break;
                    case "flowers": Flowers(g, w, ht); break;
                    case "radio": Radio(g, w, ht); break;
                    case "eggs": Eggs(g, w, ht); break;
                    case "flagua": FlagUa(g, w, ht); break;
                    case "flagde": FlagDe(g, w, ht); break;
                    case "nicholas": Nicholas(g, w, ht); break;
                    case "advent": AdventScene(g, w, ht, Math.Max(1, advent)); break;
                    default: Sky(g, w, ht, Color.FromArgb(22, 24, 33), Color.FromArgb(40, 44, 60)); break;
                }
                // the text sits on a darkening at the bottom, the information of the splash on a veil at the top
                Veil(g, w, 0, 270, 175, 0);
                Veil(g, w, ht - 110, ht, 0, 200);
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                string title = h.Title;
                if (h.Scene == "newyear") title += "  " + (DateTime.Today.Month == 1 || DateTime.Today.Day < 3 ? DateTime.Today.Year : DateTime.Today.Year + 1);
                using (Font f = new Font("Segoe UI", 22f, FontStyle.Bold))
                using (Font f2 = new Font("Segoe UI", 10.5f))
                {
                    g.DrawString(title, f, Brushes.Black, 25, ht - 77);
                    g.DrawString(title, f, Brushes.White, 24, ht - 78);
                    g.DrawString(h.Native, f2, Brushes.Black, 27, ht - 36);
                    using (Brush b = new SolidBrush(Color.FromArgb(255, 200, 120))) g.DrawString(h.Native, f2, b, 26, ht - 37);
                }
            }
            return bmp;
        }
    }
}
