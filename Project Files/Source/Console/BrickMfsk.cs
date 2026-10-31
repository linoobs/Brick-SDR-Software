/*  BrickMfsk.cs

    P/Invoke for mfsk.dll, the C library of jl1nie's mfsk-core (GPL-3.0): the WSJT-X decoders and encoders for FT8 and FT4
    (and the other WSJT modes), reimplemented in Rust with WSJT-X's sensitivity. The library is in lib\mfsk and is copied
    next to the program. Only what the FT8 / FT4 window needs is declared here.
*/

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace BrickSDR
{
    internal static class Mfsk
    {
        public const uint FT8 = 0, FT4 = 1;
        public const long PeriodNone = long.MinValue;

        [StructLayout(LayoutKind.Sequential)]
        public struct Decode
        {
            public uint size;
            public int mode;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] text;
            public float freq_hz, dt_sec, snr_db, sync_score, sync_cv;
            public uint hard_errors;
            public ushort info_bits;
            public byte pass, flags, key_bits;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)] public byte[] key;
            public int delivery;
            public byte stage;
            public uint hash22;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Params
        {
            public uint size, depth, flags, ap_mode, contest, qso_progress;
            public float band_lo_hz, band_hi_hz, rx_freq_hz, tol_hz, tx_freq_hz;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] mycall;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] mygrid;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] hiscall;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] hisgrid;
        }

        [DllImport("mfsk.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int mfsk_params_init(uint mode, ref Params p);
        [DllImport("mfsk.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr mfsk_decoder_open(uint mode, ref Params p, IntPtr extras, out int status);
        [DllImport("mfsk.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void mfsk_decoder_close(IntPtr dec);
        [DllImport("mfsk.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int mfsk_decoder_set_params(IntPtr dec, ref Params p);
        [DllImport("mfsk.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int mfsk_decoder_decode_i16(IntPtr dec, short[] samples, UIntPtr n, uint rate, long period, [In, Out] Decode[] output, UIntPtr cap, out UIntPtr len);
        [DllImport("mfsk.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int mfsk_decoder_decode_prefix_i16(IntPtr dec, short[] samples, UIntPtr n, uint rate, long period, [In, Out] Decode[] output, UIntPtr cap, out UIntPtr len);
        [DllImport("mfsk.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int mfsk_encode(uint mode, byte[] text, uint flags, uint rate, float freqHz, float[] output, UIntPtr cap, out UIntPtr len);
        [DllImport("mfsk.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int mfsk_decoder_add_callsign(IntPtr dec, byte[] call);
        [DllImport("mfsk.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr mfsk_last_error();

        public static string LastError()
        {
            try { IntPtr p = mfsk_last_error(); return p == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(p); } catch { return ""; }
        }

        public static bool Available()
        {
            try { Params p = NewParams(FT8); return mfsk_params_init(FT8, ref p) == 0; } catch { return false; }
        }

        private static byte[] str(string s, int n)
        {
            byte[] b = new byte[n];
            byte[] a = Encoding.ASCII.GetBytes(s ?? "");
            Array.Copy(a, b, Math.Min(a.Length, n - 1));
            return b;
        }

        private static Params NewParams(uint mode)
        {
            Params p = new Params { mycall = new byte[16], mygrid = new byte[8], hiscall = new byte[16], hisgrid = new byte[8] };
            p.size = (uint)Marshal.SizeOf(typeof(Params));
            return p;
        }

        public static void SetParams(ref Params p, string mycall, string grid, string hiscall, string hisgrid, float rxHz, float tolHz)
        {
            p.mycall = str(mycall, 16); p.mygrid = str(grid, 8); p.hiscall = str(hiscall, 16); p.hisgrid = str(hisgrid, 8);
            p.rx_freq_hz = rxHz; p.tol_hz = tolHz;
        }

        // one decoder of one mode; keeps the callsign hash table between periods like WSJT-X's does
        internal sealed class Decoder : IDisposable
        {
            private IntPtr h;
            private Params p;
            public readonly uint Mode;

            public Decoder(uint mode)
            {
                Mode = mode;
                p = NewParams(mode);
                int st = mfsk_params_init(mode, ref p);
                if (st != 0) throw new InvalidOperationException("mfsk_params_init " + st + " " + LastError());
                int s;
                h = mfsk_decoder_open(mode, ref p, IntPtr.Zero, out s);
                if (h == IntPtr.Zero) throw new InvalidOperationException("mfsk_decoder_open " + s + " " + LastError());
            }

            // the QSO context (AP decoding): my call and grid, the station worked, the receive frequency
            public void Context(string mycall, string grid, string hiscall, string hisgrid, float rxHz)
            {
                if (h == IntPtr.Zero) return;
                SetParams(ref p, mycall, grid, hiscall, hisgrid, rxHz, 20f);
                mfsk_decoder_set_params(h, ref p);
            }

            public struct Row { public string Text; public float Hz, Dt, Snr; }

            public List<Row> DecodePeriod(short[] samples, int rate, long period, bool prefix = false)
            {
                List<Row> res = new List<Row>();
                if (h == IntPtr.Zero) return res;
                int cap = 64;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    Decode[] o = new Decode[cap];
                    for (int i = 0; i < cap; i++) { o[i].text = new byte[64]; o[i].key = new byte[10]; }
                    o[0].size = (uint)Marshal.SizeOf(typeof(Decode));
                    UIntPtr len;
                    int st = prefix ? mfsk_decoder_decode_prefix_i16(h, samples, (UIntPtr)(uint)samples.Length, (uint)rate, period, o, (UIntPtr)(uint)cap, out len)
                                      : mfsk_decoder_decode_i16(h, samples, (UIntPtr)(uint)samples.Length, (uint)rate, period, o, (UIntPtr)(uint)cap, out len);
                    int n = (int)len;
                    if (st == -2 && n > cap) { cap = n + 8; continue; }
                    if (st != 0) throw new InvalidOperationException("decode " + st + " " + LastError());
                    for (int i = 0; i < n && i < cap; i++)
                    {
                        int z = Array.IndexOf(o[i].text, (byte)0);
                        string t = Encoding.ASCII.GetString(o[i].text, 0, z < 0 ? 64 : z).Trim();
                        res.Add(new Row { Text = t, Hz = o[i].freq_hz, Dt = o[i].dt_sec, Snr = o[i].snr_db });
                    }
                    break;
                }
                return res;
            }

            public void AddCall(string call)
            {
                if (h != IntPtr.Zero && !string.IsNullOrEmpty(call)) mfsk_decoder_add_callsign(h, str(call, 16));
            }

            public void Dispose()
            {
                IntPtr x = h; h = IntPtr.Zero;
                if (x != IntPtr.Zero) mfsk_decoder_close(x);
            }
        }

        // the audio of one transmission at `rate`, unit amplitude, starting at sample 0 of the frame; null if the message can't be sent
        public static float[] Encode(uint mode, string text, int rate, float freqHz, out string error)
        {
            error = null;
            float[] buf = new float[rate * 20];
            UIntPtr len;
            int st = mfsk_encode(mode, Encoding.ASCII.GetBytes(text + "\0"), 0, (uint)rate, freqHz, buf, (UIntPtr)(uint)buf.Length, out len);
            if (st != 0) { error = LastError(); if (string.IsNullOrEmpty(error)) error = "encode " + st; return null; }
            float[] r = new float[(int)len];
            Array.Copy(buf, r, r.Length);
            return r;
        }
    }
}
