/*  BrickDeepCw.cs

    CW decoder with the DeepCW neural network (https://github.com/e04/deepcw-engine, MIT licence, Copyright (c) 2026 e04; the
    model and its licence are in deepcw\ next to the program). The audio of the receiver goes to 3200 Hz, a spectrogram of
    400 - 1200 Hz (65 bins of 12.5 Hz, FFT 256, hop 48) is normalised by the noise level and the network answers with the
    probabilities of the characters, read out greedily (CTC). The network runs through ONNX Runtime (Microsoft, MIT).

    Streaming is done the way the engine's examples do it in principle: every second the last 12 seconds are decoded and the
    characters that are more than 2 seconds behind the newest audio (so they won't change any more) are given out once.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace BrickSDR
{
    // the network and the spectrogram: a window of audio at 3200 Hz in, the characters with their positions out
    internal sealed class DeepCwEngine : IDisposable
    {
        public const int Rate = 3200, Fft = 256, Hop = 48, Bins = 65, FirstBin = 32, Stride = 2, Blank = 41;
        public const int OutHop = Hop * Stride;                                       // samples per output frame (30 ms)
        private static readonly char[] Chars = ",./0123456789?ABCDEFGHIJKLMNOPQRSTUVWXYZ ".ToCharArray();

        private readonly InferenceSession session;
        private readonly string inName, outName;
        private readonly float[] window = new float[Fft];
        private readonly float[] cosT = new float[Fft / 2], sinT = new float[Fft / 2];

        public struct Hit { public char Ch; public int Frame; public float Strength; }        // Strength: the highest peak of the spectrogram near the character, in times the noise level

        private DeepCwEngine(InferenceSession s)
        {
            session = s;
            foreach (var k in s.InputMetadata.Keys) { inName = k; break; }
            foreach (var k in s.OutputMetadata.Keys) { outName = k; break; }
            for (int i = 0; i < Fft; i++) window[i] = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * i / Fft)));      // periodic Hann
            for (int i = 0; i < Fft / 2; i++) { cosT[i] = (float)Math.Cos(2 * Math.PI * i / Fft); sinT[i] = (float)-Math.Sin(2 * Math.PI * i / Fft); }
        }

        public static DeepCwEngine Create(string modelFile, out string error)
        {
            error = null;
            try
            {
                if (!File.Exists(modelFile)) { error = "model.onnx not found"; return null; }
                SessionOptions o = new SessionOptions();
                o.IntraOpNumThreads = 2;
                o.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                return new DeepCwEngine(new InferenceSession(modelFile, o));
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        // in-place radix-2 FFT of Fft points
        private void fft(float[] re, float[] im)
        {
            int n = Fft;
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

        // spectrogram of the window, normalised like the engine does it (noise_q90_log1p)
        private float[] spectrogram(float[] a, int start, int len, out int frames)
        {
            int pad = Fft / 2;
            frames = 1 + len / Hop;
            float[] padded = new float[len + 2 * pad];
            for (int i = 0; i < len; i++) padded[pad + i] = a[start + i];
            for (int i = 1; i <= pad; i++) { padded[pad - i] = a[start + Math.Min(len - 1, i)]; padded[pad + len - 1 + i] = a[start + Math.Max(0, len - 1 - i)]; }    // reflect
            float[] mag = new float[frames * Bins];
            float[] re = new float[Fft], im = new float[Fft];
            for (int f = 0; f < frames; f++)
            {
                int s = f * Hop;
                for (int i = 0; i < Fft; i++) { re[i] = padded[s + i] * window[i]; im[i] = 0; }
                fft(re, im);
                for (int b = 0; b < Bins; b++) { float x = re[FirstBin + b], y = im[FirstBin + b]; mag[f * Bins + b] = (float)Math.Sqrt(x * x + y * y); }
            }
            float[] sorted = (float[])mag.Clone();
            Array.Sort(sorted);
            double rayleigh = Math.Sqrt(-2.0 * Math.Log(1.0 - 0.9));
            double noise = Math.Max(Math.Max(sorted[(int)Math.Floor(0.9 * (sorted.Length - 1))] / rayleigh, sorted[sorted.Length - 1] * 1e-3), 1e-5);
            for (int i = 0; i < mag.Length; i++) mag[i] = (float)Math.Log(1.0 + mag[i] / noise);
            return mag;
        }

        // the characters of a window of audio at 3200 Hz; Frame is the output frame (30 ms) the character was read at
        public List<Hit> Decode(float[] audio, int start, int len)
        {
            List<Hit> hits = new List<Hit>();
            if (len < Fft) return hits;
            int frames;
            float[] spec = spectrogram(audio, start, len, out frames);
            DenseTensor<float> input = new DenseTensor<float>(spec, new[] { 1, 1, frames, Bins });
            float[] peak = new float[frames];
            for (int f = 0; f < frames; f++) { float m = 0; for (int b = 0; b < Bins; b++) m = Math.Max(m, spec[f * Bins + b]); peak[f] = m; }
            using (var res = session.Run(new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inName, input) }))
            {
                foreach (var r in res)
                {
                    Tensor<float> o = r.AsTensor<float>();
                    int T = o.Dimensions[1], C = o.Dimensions[2];
                    int prev = -1;
                    for (int t = 0; t < T; t++)
                    {
                        int best = 0; float bv = float.MinValue;
                        for (int c = 0; c < C; c++) { float v = o[0, t, c]; if (v > bv) { bv = v; best = c; } }
                        if (best != prev && best != Blank && best < Chars.Length)
                        {
                            float pk = 0;
                            for (int f = Math.Max(0, t * Stride - 8); f <= Math.Min(frames - 1, t * Stride + 8); f++) pk = Math.Max(pk, peak[f]);
                            hits.Add(new Hit { Ch = Chars[best], Frame = t, Strength = (float)(Math.Exp(pk) - 1.0) });
                        }
                        prev = best;
                    }
                    break;
                }
            }
            return hits;
        }

        public void Dispose() { if (session != null) session.Dispose(); }
    }

    // the stream: audio from the receiver in, characters out
    internal sealed class DeepCwDecoder : IDisposable
    {
        public static float MinStrength = 5.0f;
        private const int MaxWindow = 12 * DeepCwEngine.Rate, Guard = 2 * DeepCwEngine.Rate, MinAudio = 2 * DeepCwEngine.Rate;

        public event Action<char> Decoded;
        public string Status = "Listening";
        public double Level;                                    // 0..1, for the bar
        public double Pitch = 700;                              // tone of the receiver's CW pitch, only to tell when it is outside the band of the network

        private readonly DeepCwEngine engine;
        private readonly int rate;
        private readonly float[] fir;
        private readonly int half;
        private readonly object gate = new object();
        private float[] hist;                                   // input at the receiver's rate (for the filter)
        private long histTotal;
        private double nextOut;                                 // position (input samples) of the next 3200 Hz sample
        private float[] pcm = new float[MaxWindow + DeepCwEngine.Rate * 4];
        private long pcmTotal;                                  // 3200 Hz samples made so far
        private long lastEmit;                                  // everything before this 3200 Hz sample was given out
        private long nextDecode;
        private Thread thread;
        private volatile bool run = true;

        public DeepCwDecoder(DeepCwEngine engine, int inputRate)
        {
            this.engine = engine; rate = inputRate;
            // low pass at 1500 Hz before going down to 3200 Hz
            half = Math.Max(16, (int)(rate / 1500.0 * 2.2));
            fir = new float[2 * half + 1];
            double fc = 1500.0 / rate, sum = 0;
            for (int i = -half; i <= half; i++)
            {
                double s = i == 0 ? 2 * fc : Math.Sin(2 * Math.PI * fc * i) / (Math.PI * i);
                double w = 0.54 + 0.46 * Math.Cos(Math.PI * i / (half + 1));
                fir[i + half] = (float)(s * w); sum += fir[i + half];
            }
            for (int i = 0; i < fir.Length; i++) fir[i] = (float)(fir[i] / sum);
            hist = new float[rate * 2 + 4 * half + 2];
            thread = new Thread(Work) { IsBackground = true, Name = "DeepCW" };
            thread.Start();
        }

        // audio of the receiver at its own rate (called by the engine thread)
        public void Process(float[] buf, int n)
        {
            double sq = 0;
            lock (gate)
            {
                for (int i = 0; i < n; i++)
                {
                    float v = buf[i]; sq += v * v;
                    hist[(int)(histTotal % hist.Length)] = v; histTotal++;
                }
                double step = rate / (double)DeepCwEngine.Rate;
                // a 3200 Hz sample can be made when the filter has all of its input
                while (nextOut + half < histTotal)
                {
                    long c = (long)Math.Round(nextOut);
                    double acc = 0;
                    for (int k = -half; k <= half; k++) acc += fir[k + half] * hist[(int)(((c + k) % hist.Length + hist.Length) % hist.Length)];
                    nextOut += step;
                    if (pcmTotal >= pcm.Length) { Array.Copy(pcm, pcm.Length / 2, pcm, 0, pcm.Length / 2); pcmBase += pcm.Length / 2; pcmTotal -= pcm.Length / 2; }
                    pcm[pcmTotal++] = (float)acc;
                }
            }
            if (n > 0) Level = Math.Min(1, Math.Sqrt(sq / n) * 4);
        }

        private long pcmBase;                                   // 3200 Hz samples dropped from the start of pcm

        private void Work()
        {
            while (run)
            {
                try
                {
                    long have, total;
                    float[] snap = null; int len = 0; long winStart = 0;
                    lock (gate)
                    {
                        have = pcmTotal; total = pcmBase + pcmTotal;
                        if (total >= MinAudio && total >= nextDecode)
                        {
                            len = (int)Math.Min(MaxWindow, pcmTotal);
                            snap = new float[len];
                            Array.Copy(pcm, pcmTotal - len, snap, 0, len);
                            winStart = total - len;
                            nextDecode = total + DeepCwEngine.Rate;                // once a second
                        }
                    }
                    if (snap != null)
                    {
                        long end = winStart + len;
                        List<DeepCwEngine.Hit> hits = engine.Decode(snap, 0, len);
                        long limit = end - Guard;
                        // The place of a character shifts a little from one window to the next: look a bit before what was given out already and drop
                        // only what is the same character at (nearly) the same place
                        long from = lastEmit - 12 * DeepCwEngine.OutHop;
                        foreach (var h in hits)
                        {
                            long at = winStart + (long)h.Frame * DeepCwEngine.OutHop;
                            if (at <= from || at > limit) continue;
                            if (h.Ch != ' ' && h.Strength < MinStrength) continue;           // no tone there: the network is making characters out of noise
                            bool dup = false;
                            foreach (var e in emitted) if (e.Ch == h.Ch && Math.Abs(e.At - at) <= 3 * DeepCwEngine.OutHop) { dup = true; break; }
                            if (dup) continue;
                            if (h.Ch == ' ' && (lastWasSpace || !anyEmitted)) continue;              // a space only between characters
                            lastWasSpace = h.Ch == ' ';
                            if (!lastWasSpace) anyEmitted = true;
                            emitted.Add(new Emitted { Ch = h.Ch, At = at });
                            Action<char> d = Decoded; if (d != null) d(h.Ch);
                        }
                        emitted.RemoveAll(e => e.At < limit - 6 * DeepCwEngine.Rate);
                        if (limit > lastEmit) lastEmit = limit;
                        Status = Pitch < 400 || Pitch > 1200 ? "Set the CW pitch between 400 and 1200 Hz" : "Listening";
                    }
                }
                catch (Exception ex) { Status = "Error: " + ex.Message; }
                Thread.Sleep(100);
            }
        }

        private bool lastWasSpace;
        private bool anyEmitted;
        private struct Emitted { public char Ch; public long At; }
        private readonly List<Emitted> emitted = new List<Emitted>();

        public void Dispose() { run = false; }
    }
}
