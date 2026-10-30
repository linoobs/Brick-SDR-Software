/*  BrickGgMorse.cs

    CW decoder that follows ggmorse (https://github.com/ggerganov/ggmorse, MIT, Copyright (c) 2021 Georgi Gerganov), ported to C#
    from its source: the audio goes to 4000 Hz, a short-time FFT (512 points, step 128) finds the pitch, a sliding Goertzel filter
    (Hamming window of 128 samples) makes the envelope of that tone, and for every frame the speed (5 to 55 WPM) and the level of
    the switching threshold are searched: for each candidate the on and off intervals are measured in dot lengths and the one
    with the lowest cost (how far dots, dashes and gaps are from 1, 3, 7 dot lengths) wins and is read out as characters.
    Differences from the original: samples are pushed in (Process) instead of pulled by a callback, the low pass filter is a
    real first-order one (the original's biquad has a bug that has no effect on the first-order case), and the characters go to
    an event instead of stdout.

    MIT License

    Copyright (c) 2021 Georgi Gerganov

    Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation
    files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy,
    modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the
    Software is furnished to do so, subject to the following conditions: The above copyright notice and this permission notice
    shall be included in all copies or substantial portions of the Software. THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY
    OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
    PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR
    THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.Text;

namespace BrickSDR
{
    internal sealed class GgMorseDecoder
    {
        private const int BaseRate = 4000, Frame = 128, FftSize = 512, GoertzelWindow = 128;
        private const float WindowSeconds = 3.0f;

        public event Action<char> Decoded;

        // what the decoder knows now (for the status line)
        public double Pitch, Wpm, Cost = 1e6, Level, Contrast;
        public static double MinContrast = 5.0;
        public bool Decoding;
        public double FrequencyHz = -1;                          // > 0: use this tone instead of the pitch detection
        public double MinHz = 200, MaxHz = 1200;

        private static readonly Dictionary<string, char> Alphabet = new Dictionary<string, char>
        {
            {"01",'A'},{"1000",'B'},{"1010",'C'},{"100",'D'},{"0",'E'},{"0010",'F'},{"110",'G'},{"0000",'H'},{"00",'I'},{"0111",'J'},
            {"101",'K'},{"0100",'L'},{"11",'M'},{"10",'N'},{"111",'O'},{"0110",'P'},{"1101",'Q'},{"010",'R'},{"000",'S'},{"1",'T'},
            {"001",'U'},{"0001",'V'},{"011",'W'},{"1001",'X'},{"1011",'Y'},{"1100",'Z'},{"01111",'1'},{"00111",'2'},{"00011",'3'},
            {"00001",'4'},{"00000",'5'},{"10000",'6'},{"11000",'7'},{"11100",'8'},{"11110",'9'},{"11111",'0'},{"010101",'.'},
            {"110011",','},{"001100",'?'},{"011110",'\''},{"101011",'!'},{"10010",'/'},{"10110",'('},{"101101",')'},{"01000",'&'},
            {"111000",':'},{"101010",';'},{"10001",'='},{"01010",'+'},{"100001",'-'},{"001101",'_'},{"010010",'"'},{"0001001",'$'},
            {"011010",'@'}
        };

        private sealed class Interval
        {
            public int Signal, Start, End, Type;                  // Type: on: 0 dot, 1 dash; off: 0 long gap, 1 inside a letter, 2 between letters, 3 between words
            public float Avg, Len;
        }

        private readonly int rate;
        private readonly int decim;
        private readonly bool simple;                            // the rate is a multiple of 4000: filter and take every n-th sample
        private double lpA0, lpA1, lpB1, lpX1, lpY1;             // first-order low pass at 1200 Hz at the input rate
        private double hpA0, hpA1, hpB1, hpX1, hpY1;             // first-order high pass at 200 Hz at 4000 Hz
        private int decimCount;
        private double resIn, resNext = 1.0;                      // for the other rates: linear interpolation
        private float resPrev;
        private readonly float[] frameBuf = new float[Frame];
        private int frameFill;

        // short-time FFT
        private readonly float[] hammingF = new float[FftSize];
        private readonly float[] hist;                           // 3 s of samples
        private int histHead;
        private int neededSamples = Frame;
        private readonly float[][] spec;                         // rows of power
        private int specHead;
        private readonly float[] fftRe = new float[FftSize], fftIm = new float[FftSize], cosT = new float[FftSize / 2], sinT = new float[FftSize / 2];

        // Goertzel
        private readonly float[] hammingG = new float[GoertzelWindow];
        private readonly float[] gHist;
        private int gHeadH, gProcessed, gHeadF;
        private readonly float[] filtered, filteredOut;
        private double gCoeff, gCos, gSin;

        private readonly List<Interval>[][] intervalsAll = new List<Interval>[100][];
        private double estPitch, estSpeed, signalThreshold;
        private int framesWithSpeed;
        private int lastSignal;
        private readonly StringBuilder curLetter = new StringBuilder();

        public GgMorseDecoder(int inputRate)
        {
            rate = inputRate;
            simple = rate % BaseRate == 0;
            decim = Math.Max(1, rate / BaseRate);
            lowPass(1200.0, rate);
            highPass(200.0, BaseRate);
            for (int i = 0; i < FftSize; i++) hammingF[i] = (float)(0.54 - 0.46 * Math.Cos(2.0 * Math.PI * i / FftSize));
            for (int i = 0; i < FftSize / 2; i++) { cosT[i] = (float)Math.Cos(2 * Math.PI * i / FftSize); sinT[i] = (float)-Math.Sin(2 * Math.PI * i / FftSize); }
            int histSamples = (int)(WindowSeconds * BaseRate);
            hist = new float[histSamples];
            int steps = 1 + (histSamples - FftSize) / Frame;
            spec = new float[steps][];
            for (int i = 0; i < steps; i++) spec[i] = new float[FftSize];
            for (int i = 0; i < GoertzelWindow; i++) hammingG[i] = (float)(0.54 - 0.46 * Math.Cos(2.0 * Math.PI * i / GoertzelWindow));
            gHist = new float[histSamples];
            filtered = new float[histSamples - GoertzelWindow];
            filteredOut = new float[histSamples - GoertzelWindow];
            for (int s = 0; s < 100; s++) { intervalsAll[s] = new List<Interval>[100]; for (int l = 0; l < 100; l++) intervalsAll[s][l] = new List<Interval>(100); }
        }

        private void lowPass(double fc, double fs)
        {
            double th = 2.0 * Math.PI * fc / fs, g = Math.Cos(th) / (1.0 + Math.Sin(th));
            lpA0 = (1.0 - g) / 2.0; lpA1 = (1.0 - g) / 2.0; lpB1 = -g;
        }

        private void highPass(double fc, double fs)
        {
            double th = 2.0 * Math.PI * fc / fs, g = Math.Cos(th) / (1.0 + Math.Sin(th));
            hpA0 = (1.0 + g) / 2.0; hpA1 = -(1.0 + g) / 2.0; hpB1 = -g;
        }

        // audio of the receiver at its own rate
        public void Process(float[] buf, int n)
        {
            for (int i = 0; i < n; i++)
            {
                double x = buf[i];
                double y = lpA0 * x + lpA1 * lpX1 - lpB1 * lpY1;               // low pass at the input rate
                lpX1 = x; lpY1 = y;
                if (simple)
                {
                    if (++decimCount >= decim) { decimCount = 0; push((float)y); }
                }
                else
                {
                    // linear interpolation to 4000 Hz
                    double step = rate / (double)BaseRate;
                    resIn += 1.0;
                    while (resNext <= resIn)
                    {
                        double frac = resNext - (resIn - 1.0);
                        push((float)(resPrev + (y - resPrev) * frac));
                        resNext += step;
                    }
                    resPrev = (float)y;
                }
            }
        }

        private void push(float v)
        {
            // first-order high pass at 4000 Hz
            double y = hpA0 * v + hpA1 * hpX1 - hpB1 * hpY1;
            hpX1 = v; hpY1 = y;
            frameBuf[frameFill++] = (float)y;
            if (frameFill == Frame) { frameFill = 0; decodeFrame(); }
        }

        // ---- short-time FFT ----

        private void fft(float[] re, float[] im)
        {
            int n = FftSize;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { float t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                int half = len >> 1, step = n / len;
                for (int i = 0; i < n; i += len)
                    for (int k = 0; k < half; k++)
                    {
                        float wr = cosT[k * step], wi = sinT[k * step];
                        int a = i + k, b = a + half;
                        float xr = re[b] * wr - im[b] * wi, xi = re[b] * wi + im[b] * wr;
                        re[b] = re[a] - xr; im[b] = im[a] - xi;
                        re[a] += xr; im[a] += xi;
                    }
            }
        }

        private void stfftProcess(float[] samples, int n)
        {
            int nh = hist.Length, ns = spec.Length;
            for (int i = 0; i < n; i++)
            {
                hist[histHead] = samples[i];
                if (++histHead >= nh) histHead = 0;
                if (--neededSamples == 0)
                {
                    int idx = histHead - FftSize; if (idx < 0) idx += nh;
                    for (int k = 0; k < FftSize; k++) { fftRe[k] = hammingF[k] * hist[idx++]; fftIm[k] = 0; if (idx >= nh) idx = 0; }
                    fft(fftRe, fftIm);
                    float[] dst = spec[specHead];
                    for (int k = 0; k < FftSize; k++) dst[k] = fftRe[k] * fftRe[k] + fftIm[k] * fftIm[k];
                    if (++specHead >= ns) specHead = 0;
                    neededSamples = Frame;
                }
            }
        }

        // the bin with the most power over the newest half of the 3 s
        private float stfftPitch(float fMin, float fMax)
        {
            int ns = spec.Length;
            float maxSignal = 0, best = 0, df = (float)BaseRate / FftSize;
            for (int j = 0; j < FftSize / 2; j++)
            {
                float f = j * df;
                if (f < fMin || f > fMax) continue;
                float cur = 0;
                int ih = specHead + ns / 2; if (ih >= ns) ih = 0;
                for (int i = 0; i < ns / 2; i++) { cur += spec[ih][j]; if (++ih >= ns) ih = 0; }
                if (cur > maxSignal) { maxSignal = cur; best = f; }
            }
            return best;
        }

        // ---- Goertzel ----

        private void goertzelSetFrequency(double f)
        {
            double w = 2 * Math.PI * f / BaseRate;
            gCos = Math.Cos(w); gSin = Math.Sin(w); gCoeff = 2.0 * gCos;
        }

        private float goertzelAt(int idx)
        {
            if (idx < 0) idx += gHist.Length;
            double sprev = 0, sprev2 = 0;
            for (int i = 0; i < GoertzelWindow; i++)
            {
                double s = hammingG[i] * gHist[idx++] + gCoeff * sprev - sprev2;
                if (idx >= gHist.Length) idx = 0;
                sprev2 = sprev; sprev = s;
            }
            double real = sprev * gCos - sprev2, imag = -sprev * gSin;
            return (float)(real * real + imag * imag);
        }

        private void goertzelProcess(float[] samples, int n)
        {
            int nh = gHist.Length, nf = filtered.Length;
            for (int i = 0; i < n; i++)
            {
                gHist[gHeadH] = samples[i];
                if (++gHeadH >= nh) gHeadH = 0;
                if (++gProcessed >= GoertzelWindow)
                {
                    filtered[gHeadF] = goertzelAt(gHeadH - GoertzelWindow);
                    if (++gHeadF >= nf) gHeadF = 0;
                }
            }
        }

        private void goertzelRecompute()
        {
            int nh = gHist.Length, nf = filtered.Length;
            gProcessed = 0;
            for (int i = 0; i < nh; i++)
            {
                if (++gHeadH >= nh) gHeadH = 0;
                if (++gProcessed >= GoertzelWindow)
                {
                    filtered[gHeadF] = goertzelAt(gHeadH - GoertzelWindow);
                    if (++gHeadF >= nf) gHeadF = 0;
                }
            }
        }

        // ---- one frame of 128 samples at 4000 Hz ----

        private static float lenDotMs(float wpm) { return 60000.0f / (50.0f * wpm); }

        private void emit(char c) { Action<char> d = Decoded; if (d != null) d(c); }

        private void decodeFrame()
        {
            float[] wave = frameBuf;
            stfftProcess(wave, Frame);

            double frequency = FrequencyHz;
            if (frequency <= 0) frequency = stfftPitch((float)MinHz, (float)MaxHz);

            int windowToAnalyze = (int)(WindowSeconds * BaseRate);

            if (Math.Abs(frequency - estPitch) > 50.0)
            {
                goertzelSetFrequency(frequency);
                goertzelRecompute();
                emit('\n');
                lastSignal = 0;
                curLetter.Length = 0;
            }
            estPitch = frequency; Pitch = frequency;
            goertzelSetFrequency(frequency);
            goertzelProcess(wave, Frame);

            int nSamples = filtered.Length;
            int nFramesInWindow = windowToAnalyze / Frame;
            // oldest first
            float[] f = filteredOut;
            int j0 = gHeadF;
            for (int i = 0; i < nSamples; i++) { f[i] = filtered[j0]; if (++j0 >= nSamples) j0 = 0; }

            int nDown = 1;
            while ((nSamples % 2 == 0) && (windowToAnalyze > 500 * (int)WindowSeconds)) { nDown *= 2; nSamples /= 2; windowToAnalyze /= 2; }

            double mean = 0;
            for (int i = 0; i < nSamples; i++)
            {
                float sum = 0;
                for (int j = 0; j < nDown; j++) sum += f[i * nDown + j];
                sum /= nDown;
                f[i] = sum;
                mean += f[i];
            }
            mean /= nSamples;

            float bestCost = 1e6f;
            int bestLevel = 0, bestSpeed = 0;
            int s0 = 0, s1 = 50, ds = 10, nModes = 2;

            for (int mode = 0; mode < nModes; mode++)
            {
                if (mode == 1)
                {
                    s0 = (int)Math.Min(Math.Max(0.0, Math.Round(estSpeed - 5.0 - 2.0)), 50.0);
                    s1 = (int)Math.Min(Math.Max(0.0, Math.Round(estSpeed - 5.0 + 2.0)), 50.0);
                    ds = 1;
                }
                int lOld = (int)Math.Min(Math.Max(20.0, 100.0 * signalThreshold), 80.0);
                int l0 = mode == 0 ? 10 : lOld - 10, l1 = mode == 0 ? 90 : lOld + 10, dl = mode == 0 ? 20 : 2;

                for (int s = s0; s <= s1 && s < 55; s += ds)
                {
                    float lenDotSamples = BaseRate * (1e-3f * lenDotMs(5 + s)) / nDown;
                    for (int l = l0; l <= l1; l += dl)
                    {
                        float level = (float)((0.01 * mean) * l);
                        int last = f[0] > level ? 1 : 0;
                        Interval cur = new Interval { Signal = last, Start = 0, Avg = f[0] };
                        List<Interval> intervals = intervalsAll[s][l];
                        intervals.Clear();
                        for (int i = 1; i < nSamples; i++)
                        {
                            int sig = f[i] > level ? 1 : 0;
                            if (sig != last)
                            {
                                cur.End = i;
                                cur.Avg /= (i - cur.Start);
                                cur.Len = (cur.End - cur.Start) / lenDotSamples;
                                intervals.Add(cur);
                                cur = new Interval { Signal = sig, Start = i, Avg = f[i] };
                                last = sig;
                            }
                            else cur.Avg += f[i];
                        }
                        cur.End = nSamples;
                        intervals.Add(cur);
                        int nIntervals = intervals.Count;

                        for (int i = 0; i < nIntervals; i++)
                        {
                            if (intervals[i].Signal == 0) { intervals[i].Type = 0; continue; }
                            intervals[i].Type = intervals[i].Len > 2 ? 1 : 0;
                        }

                        int nDots = 0, nDahs = 0;
                        float avgDot = 0, avgDah = 0;
                        for (int i = 1; i < nIntervals - 1; i++)
                        {
                            Interval c = intervals[i];
                            if (c.Signal == 0) continue;
                            if (c.Type == 0) { nDots++; avgDot += c.Len; }
                            if (c.Type == 1) { nDahs++; avgDah += c.Len; }
                        }
                        avgDot = nDots > 0 ? avgDot / nDots : 1.0f;
                        avgDah = nDahs > 0 ? avgDah / nDahs : 3.0f;

                        for (int i = 1; i < nIntervals - 1; i++)
                        {
                            Interval c = intervals[i];
                            if (c.Signal == 0) continue;
                            float mid = 0.5f * (c.Start + c.End);
                            if (c.Type == 0) c.Len *= 1.0f / avgDot; else c.Len *= 3.0f / avgDah;
                            intervals[i - 1].End = c.Start = (int)(mid - 0.5f * c.Len * lenDotSamples);
                            intervals[i - 1].Len = (intervals[i - 1].End - intervals[i - 1].Start) / lenDotSamples;
                            intervals[i + 1].Start = c.End = (int)(mid + 0.5f * c.Len * lenDotSamples);
                            intervals[i + 1].Len = (intervals[i + 1].End - intervals[i + 1].Start) / lenDotSamples;
                        }

                        nDots = 0; nDahs = 0; int nSpaces = 0;
                        float costDots = 0, costDahs = 0, costSpaces = 0;
                        for (int i = 1; i < nIntervals - 1; i++)
                        {
                            Interval c = intervals[i];
                            if (c.Signal == 0)
                            {
                                c.Type = 0;
                                if (c.Len < 8.0)
                                {
                                    float c1 = (float)Math.Pow(c.Len - 1.0, 2), c3 = (float)Math.Pow(c.Len - 3.0, 2), c7 = (float)Math.Pow(c.Len - 7.0, 2);
                                    if (c1 < c3 && c1 < c7) { c.Type = 1; costSpaces += Math.Min(Math.Min(c1, c3), c7); nSpaces++; }
                                    else if (c3 < c1 && c3 < c7) c.Type = 2;
                                    else if (c7 < c1 && c7 < c3) c.Type = 3;
                                }
                                continue;
                            }
                            if (c.Type == 0) { nDots++; costDots += (float)Math.Pow(c.Len - 1.0, 2); }
                            if (c.Type == 1) { nDahs++; costDahs += (float)Math.Pow(c.Len - 3.0, 2); }
                        }
                        if (nSpaces == 0) { nSpaces = 1; costSpaces = 100.0f; }
                        if (nDots < 1) { nDots = 1; costDots = 100.0f; }
                        if (nDahs < 1) { nDahs = 1; costDahs = 100.0f; }
                        float curCost = costDots / nDots + costDahs / nDahs + costSpaces / nSpaces;
                        if (avgDah / avgDot < 2.5 || avgDah / avgDot > 3.5) curCost += 100.0f;
                        if (curCost < bestCost) { bestCost = curCost; bestLevel = l; bestSpeed = s; }
                    }
                }
            }

            Cost = bestCost;
            // Not in the original: with no tone the search still finds something that fits (it likes 55 wpm), so the characters are read only when the envelope
            // really has a tone in it: the mean power above the best threshold is several times the mean power below it
            float lv = (float)((0.01 * mean) * bestLevel);
            double onSum = 0, offSum = 0; int nOn = 0, nOff = 0;
            for (int i = 0; i < nSamples; i++) { if (f[i] > lv) { onSum += f[i]; nOn++; } else { offSum += f[i]; nOff++; } }
            Contrast = nOn > 0 && nOff > 0 ? (onSum / nOn) / Math.Max(offSum / nOff, 1e-12) : 0;
            bool isDecoding = bestCost < 1.0f && Contrast >= MinContrast && bestSpeed + 5 <= 50;
            Decoding = isDecoding;
            List<Interval> best = intervalsAll[bestSpeed][bestLevel];
            double speed = 5 + bestSpeed;
            if (Math.Abs(estSpeed - speed) > 2.0) framesWithSpeed = 0;
            estSpeed = speed; Wpm = speed;
            framesWithSpeed++;
            signalThreshold = 0.01 * bestLevel;
            Level = Math.Min(1.0, mean * 20);

            int w0 = 2 * nFramesInWindow / 6, w1 = 2 * nFramesInWindow / 6;
            if (speed >= 15.0)
            {
                if (framesWithSpeed == nFramesInWindow) w1 = (5 * nFramesInWindow) / 6;
                if (framesWithSpeed > nFramesInWindow) { w0 = (5 * nFramesInWindow) / 6; w1 = (5 * nFramesInWindow) / 6; }
            }

            int jj = 0;
            for (int w = w0; w <= w1; w++)
                for (int i = 0; i < Frame / nDown; i++)
                {
                    int s = w * Frame / nDown + i;
                    while (jj < best.Count - 1 && s >= best[jj].End) jj++;
                    Interval iv = best[jj];
                    if (lastSignal != iv.Signal)
                    {
                        if (isDecoding)
                        {
                            if (iv.Signal == 1) curLetter.Append(iv.Type == 1 ? '1' : '0');
                            else
                            {
                                if (iv.Type == 0 || iv.Type == 2 || iv.Type == 3)
                                {
                                    char ch;
                                    emit(Alphabet.TryGetValue(curLetter.ToString(), out ch) ? ch : '?');
                                    curLetter.Length = 0;
                                }
                                // between words and after a long gap a space, otherwise nothing
                                if (iv.Type == 3 || iv.Type == 0) emit(' ');
                            }
                        }
                        lastSignal = iv.Signal;
                    }
                }
        }
    }
}
