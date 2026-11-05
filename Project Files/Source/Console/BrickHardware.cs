using System;

namespace BrickSDR
{
    // Maps the MAC address reported by the radio to a Brick board and the
    // HPSDR model that has to be selected for it.
    //   02:B2:xx:xx:xx:xx -> Brick2 (Hermes)
    //   02:B3:xx:xx:xx:xx -> Brick3 (Anan-100D)
    internal static class BrickHardware
    {
        private static string _name = "";
        private static string _mac = "";

        // local address of the network card the radio is reached through (set when connecting)
        public static string HostIp = "";

        private static System.Net.NetworkInformation.NetworkInterface _nic;
        private static string _nicIp = "";
        private static long _lastRx, _lastTx;
        private static DateTime _lastT = DateTime.MinValue;
        private static string _speed = "";

        // " RX 2.4 Mb/s TX 0.3 Mb/s": traffic of the network card of the radio, refreshed at most once a second ("" while not connected)
        public const string ZeroSpeed = "RX 0.00 Mb/s  TX 0.00 Mb/s";

        public static string SpeedText(bool connected)
        {
            if (!connected || HostIp == "") { _lastT = DateTime.MinValue; return _speed = ZeroSpeed; }
            if ((DateTime.UtcNow - _lastT).TotalMilliseconds < 900) return _speed;
            try
            {
                if (_nic == null || _nicIp != HostIp)
                {
                    _nic = null; _nicIp = HostIp;
                    foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                        foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                            if (ua.Address.ToString() == HostIp) _nic = ni;
                    _lastT = DateTime.MinValue;
                }
                if (_nic == null) return _speed = ZeroSpeed;
                var st = _nic.GetIPv4Statistics();
                DateTime now = DateTime.UtcNow;
                long rx = st.BytesReceived, tx = st.BytesSent;
                if (_lastT != DateTime.MinValue)
                {
                    double dt = (now - _lastT).TotalSeconds;
                    if (dt > 0.2)
                        _speed = " RX " + ((rx - _lastRx) * 8 / dt / 1e6).ToString("0.00") + " Mb/s  TX " + ((tx - _lastTx) * 8 / dt / 1e6).ToString("0.00") + " Mb/s";
                }
                _lastRx = rx; _lastTx = tx; _lastT = now;
            }
            catch { _speed = ZeroSpeed; }
            return _speed;
        }

        // MAC of the connected radio, 12 hex digits without separators ("" when unknown)
        public static string Mac { get { return _mac; } }

        // "Brick2 15W" / "Brick3 50W", or "" when the connected radio is not a Brick board
        public static string Name
        {
            get { return _name; }
        }

        // third MAC byte: 01 = 15 W board, 03 = 50 W board (02:B2:01 Brick2 15W, 02:B2:03 Brick2 50W, 02:B3:01 Brick3 15W); anything else falls back to the usual power of the model
        private static string PowerClass(string hex, string fallback)
        {
            switch (hex.Substring(4, 2))
            {
                case "01": return "15W";
                case "03": return "50W";
            }
            return fallback;
        }

        // short display name of the board for a MAC address ("Brick2 15W"), or "" when it is not a Brick board
        public static string DisplayName(string mac)
        {
            HPSDRModel m; string n;
            return TryGetModel(mac, out m, out n) ? n : "";
        }

        public static bool TryGetModel(string mac, out HPSDRModel model, out string name)
        {
            model = HPSDRModel.FIRST;
            name = "";

            if (string.IsNullOrWhiteSpace(mac)) return false;

            // discovery reports "02-B2-..", accept ':' and '-' separators or none at all
            string hex = mac.Replace(":", "").Replace("-", "").Trim().ToUpperInvariant();
            if (hex.Length != 12) return false;
            if (!hex.StartsWith("02")) return false;

            switch (hex.Substring(2, 2))
            {
                case "B2":
                    model = HPSDRModel.HERMES;
                    name = "Brick2 " + PowerClass(hex, "15W");
                    return true;
                case "B3":
                    model = HPSDRModel.ANAN100D;
                    name = ("Brick3 " + PowerClass(hex, "")).TrimEnd();
                    return true;
            }

            return false;
        }

        // Called before connecting. Selects the model matching the radio MAC (when it is a Brick board)
        // and remembers the board name for the title bar.
        public static void ApplyForRadio(Console console, RadioInfo radio)
        {
            _name = "";
            IdentifiedBrick = null;
            _mac = radio == null || radio.MacAddress == null ? "" : radio.MacAddress.Replace(":", "").Replace("-", "").ToUpperInvariant();

            if (radio == null || radio.IsCustom || console.IsSetupFormNull) return;

            try { ApplyForRadioModel(console, radio); } finally { console.brickApplyModelRanges(); }
        }

        private static void ApplyForRadioModel(Console console, RadioInfo radio)
        {
            HPSDRModel model;
            string name;
            if (!TryGetModel(radio.MacAddress, out model, out name))
            {
                // not a Brick board: the model has to fit the board id the radio reports in discovery (Hermes Lite 2, Hermes, Angelia, Orion, Saturn ...);
                // when the model chosen in Setup already belongs to that hardware family it stays (ANAN-10 / ANAN-100 / Hermes, ...)
                IdentifiedBrick = false;
                HPSDRModel def;
                if (DefaultModelFor(radio.DeviceType, out def) && HardwareSpecific.Hardware != radio.DeviceType)
                    console.SetupForm.SelectRadioModel(def);
                return;
            }

            IdentifiedBrick = true;
            _name = name;
            console.SetupForm.SelectRadioModel(model);
            BrickProfiles.ApplyOnce(console, name, _mac);
        }

        // true / false once a radio has been identified (Brick2 / Brick3 or anything else), null before
        public static bool? IdentifiedBrick;

        private static bool DefaultModelFor(HPSDRHW hw, out HPSDRModel m)
        {
            switch (hw)
            {
                case HPSDRHW.Atlas: m = HPSDRModel.HPSDR; return true;
                case HPSDRHW.Hermes: m = HPSDRModel.HERMES; return true;
                case HPSDRHW.HermesII: m = HPSDRModel.ANAN10E; return true;
                case HPSDRHW.Angelia: m = HPSDRModel.ANAN100D; return true;
                case HPSDRHW.Orion: m = HPSDRModel.ANAN200D; return true;
                case HPSDRHW.OrionMKII: m = HPSDRModel.ORIONMKII; return true;
                case HPSDRHW.HermesLite: m = HPSDRModel.HERMESLITE; return true;
                case HPSDRHW.Saturn: m = HPSDRModel.ANAN_G2; return true;
                case HPSDRHW.HermesC10: m = HPSDRModel.ANAN_G2E; return true;
            }
            m = HPSDRModel.HERMES;
            return false;
        }
    }
}

namespace BrickSDR
{
    public partial class Console
    {
        // Ranges that depend on the radio: Hermes Lite 2 has 15 drive steps (one wheel click = one step) and an LNA range of -28..+31 dB,
        // every other board 0..100 and 0..31 dB. Applied at start and again whenever the model is set from the radio, so nothing of the
        // Hermes Lite 2 stays behind after another board is connected.
        public void brickApplyModelRanges()
        {
            try
            {
                if (HardwareSpecific.Model == HPSDRModel.HERMESLITE)
                {
                    ptbPWR.Maximum = 90; ptbPWR.LargeChange = 6; ptbPWR.SmallChange = 6;
                    ptbTune.Maximum = 99; ptbTune.LargeChange = 3; ptbTune.SmallChange = 3;
                    udRX1StepAttData.Minimum = -28; udRX2StepAttData.Minimum = -28; udTXStepAttData.Minimum = -28;
                    udRX1StepAttData.Maximum = 31; udRX2StepAttData.Maximum = 31;
                }
                else
                {
                    ptbPWR.Maximum = 100; ptbPWR.LargeChange = 1; ptbPWR.SmallChange = 1;
                    ptbTune.Maximum = 100; ptbTune.LargeChange = 1; ptbTune.SmallChange = 1;
                    udRX1StepAttData.Minimum = 0; udRX2StepAttData.Minimum = 0; udTXStepAttData.Minimum = 0;
                    if (udRX1StepAttData.Maximum < 31) udRX1StepAttData.Maximum = 31;
                    if (udRX2StepAttData.Maximum < 31) udRX2StepAttData.Maximum = 31;
                }
            }
            catch { }
        }
    }
}

namespace BrickSDR
{
    public partial class Console
    {
        // With VAC1 as the transmit source (VAC on, no bypass) WDSP's mic gain is not used, the audio is scaled by the VAC TX gain of Setup.
        // The MIC slider of the main window then sets that gain, so it always does what its name says (digital modes keep their own VAC gain).
        public void brickMicToVac()
        {
            try
            {
                if (IsSetupFormNull || HardwareSpecific.Model == HPSDRModel.HERMESLITE) return;     // Hermes Lite 2 does this in ptbMic_Scroll
                if (!vac_enabled || Audio.VACBypass) return;
                DSPMode m = radio.GetDSPTX(0).CurrentDSPMode;
                if (m == DSPMode.DIGU || m == DSPMode.DIGL || m == DSPMode.FM) return;
                if (SetupForm.VACTXGain != ptbMic.Value) SetupForm.VACTXGain = ptbMic.Value;
                vac_tx_gain = ptbMic.Value;
            }
            catch { }
        }
    }
}
