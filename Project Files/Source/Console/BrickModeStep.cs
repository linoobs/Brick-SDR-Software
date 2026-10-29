/*  BrickModeStep.cs

    The tuning step follows the mode: 500 Hz for SSB and the digital modes, 10 Hz for CW, 1 kHz for AM / SAM / FM / DRM.
*/

namespace BrickSDR
{
    public partial class Console
    {
        private void brickApplyModeStep(DSPMode mode)
        {
            string name;
            switch (mode)
            {
                case DSPMode.CWL:
                case DSPMode.CWU:
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
