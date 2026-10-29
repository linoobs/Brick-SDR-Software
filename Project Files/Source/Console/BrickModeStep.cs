/*  BrickModeStep.cs

    The tuning step follows the mode: 500 Hz for LSB / USB, 10 Hz for CW and the digital modes, 1 kHz for AM / SAM / FM / DRM.
*/

using System;
using System.IO;

namespace BrickSDR
{
    public partial class Console
    {
        // A receiver changed its mode: what depended on the old one goes away. RADE is switched off (the audio comes back),
        // and the RTTY / CW terminal or the FreeDV strip under the panadapter closes when the new mode isn't theirs.
        // the dial frequency a receiver had before the DIGI menu took it to a digital mode (0 = none), kept in digi_return.txt
        private readonly double[] _digiReturnMhz = new double[2];
        private readonly DSPMode[] _brickPrevMode = new DSPMode[2];
        private bool _digiReturnLoaded;
        private string DigiReturnFile { get { return Path.Combine(AppDataPath, "digi_return.txt"); } }

        private void digiReturnLoad()
        {
            if (_digiReturnLoaded) return;
            _digiReturnLoaded = true;
            try
            {
                if (!File.Exists(DigiReturnFile)) return;
                string[] l = File.ReadAllLines(DigiReturnFile);
                for (int i = 0; i < 2 && i < l.Length; i++) double.TryParse(l[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _digiReturnMhz[i]);
            }
            catch { }
        }

        private void digiReturnSave()
        {
            try { File.WriteAllLines(DigiReturnFile, new[] { _digiReturnMhz[0].ToString(System.Globalization.CultureInfo.InvariantCulture), _digiReturnMhz[1].ToString(System.Globalization.CultureInfo.InvariantCulture) }); } catch { }
        }

        // called before the DIGI menu changes a receiver's mode or frequency: from a non-digital mode the frequency is kept for the way back
        private void brickRememberBeforeDigi(bool rx2)
        {
            try
            {
                digiReturnLoad();
                int i = rx2 ? 1 : 0;
                DSPMode m = rx2 ? RX2DSPMode : RX1DSPMode;
                if (m == DSPMode.DIGU || m == DSPMode.DIGL) return;          // already digital (FT8 to FT4, RTTY...): keep the first one
                _digiReturnMhz[i] = rx2 ? VFOBFreq : VFOAFreq;
                digiReturnSave();
            }
            catch { }
        }

        // back to LSB / USB from a digital mode: the receiver goes back to the frequency it had before, unless it was moved to another band meanwhile
        private void brickReturnFromDigi(int i, DSPMode m)
        {
            digiReturnLoad();
            double saved = _digiReturnMhz[i];
            if (saved <= 0) return;
            _digiReturnMhz[i] = 0;
            digiReturnSave();
            if (m != DSPMode.LSB && m != DSPMode.USB) return;
            double cur = i == 1 ? VFOBFreq : VFOAFreq;
            if (BandByFreq(saved, -1, current_region) != BandByFreq(cur, -1, current_region) || Math.Abs(cur - saved) < 0.000001) return;
            if (i == 1) VFOBFreq = saved; else VFOAFreq = saved;
        }

        private void brickDigitalModeChanged(int rx)
        {
            if (rx < 1 || rx > 2) return;
            BeginInvoke(new Action(() =>
            {
                try
                {
                    int i = rx - 1;
                    DSPMode m = rxMode(i);
                    bool digital = m == DSPMode.DIGU || m == DSPMode.DIGL;
                    DSPMode prev = _brickPrevMode[i];
                    _brickPrevMode[i] = m;
                    if ((prev == DSPMode.DIGU || prev == DSPMode.DIGL) && !digital) brickReturnFromDigi(i, m);
                    if (RadeEnabledOn(i) && m != _radeForcedMode[i]) SetRadeEnabled(i, false);
                    if (_digiMode != "" && _digiRx == i)
                    {
                        bool fits = _digiMode == "RTTY" ? digital : (m == DSPMode.CWL || m == DSPMode.CWU);
                        if (!fits) digiClose();
                    }
                    if (brickDockKey == "ft" && _digiRx == i && m != DSPMode.USB && m != DSPMode.DIGU) brickFtClose();
                    if (_radeStrip != null && brickDockKey == "rade" && _radeStrip.Rx == i && !digital && !(RadeEnabledOn(i) && m == _radeForcedMode[i])) brickRadeClose();
                    brickEnsureSsbLeveler();
                }
                catch { }
            }));
        }

        // The TX leveler is on by default in SSB, without it the microphone is quiet. Thetis switches it off while a digital mode is selected
        // and a program started in DIGU keeps it off, so a change to LSB / USB (and the start in one) switches it on again.
        // In the other modes it stays as the user or the TX profile has it.
        internal void brickEnsureSsbLeveler()
        {
            try
            {
                if (IsSetupFormNull) return;
                DSPMode m = (RX2Enabled && VFOBTX) ? RX2DSPMode : RX1DSPMode;
                if ((m == DSPMode.LSB || m == DSPMode.USB) && !SetupForm.TXLevelerOn) SetupForm.TXLevelerOn = true;
            }
            catch { }
        }

        private void brickApplyModeStep(DSPMode mode)
        {
            string name;
            switch (mode)
            {
                case DSPMode.CWL:
                case DSPMode.CWU:
                case DSPMode.DIGL:
                case DSPMode.DIGU:
                    name = "10Hz";
                    break;
                case DSPMode.AM:
                case DSPMode.SAM:
                case DSPMode.FM:
                case DSPMode.DRM:
                    name = "1kHz";
                    break;
                default:
                    name = "500Hz";
                    break;
            }
            int idx = TuneStepLookup(name);
            if (idx >= 0 && idx != tune_step_index) TuneStepIndex = idx;
        }
    }
}
