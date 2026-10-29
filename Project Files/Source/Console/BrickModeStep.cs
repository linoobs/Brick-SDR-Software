/*  BrickModeStep.cs

    The tuning step follows the mode: 500 Hz for LSB / USB, 10 Hz for CW and the digital modes, 1 kHz for AM / SAM / FM / DRM.
*/

using System;

namespace BrickSDR
{
    public partial class Console
    {
        // A receiver changed its mode: what depended on the old one goes away. RADE is switched off (the audio comes back),
        // and the RTTY / CW terminal or the FreeDV strip under the panadapter closes when the new mode isn't theirs.
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
