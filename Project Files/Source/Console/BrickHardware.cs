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

        // MAC of the connected radio, 12 hex digits without separators ("" when unknown)
        public static string Mac { get { return _mac; } }

        // "Brick2" / "Brick3", or "" when the connected radio is not a Brick board
        public static string Name
        {
            get { return _name; }
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
                    name = "Brick2";
                    return true;
                case "B3":
                    model = HPSDRModel.ANAN100D;
                    name = "Brick3";
                    return true;
            }

            return false;
        }

        // Called before connecting. Selects the model matching the radio MAC (when it is a Brick board)
        // and remembers the board name for the title bar.
        public static void ApplyForRadio(Console console, RadioInfo radio)
        {
            _name = "";
            _mac = radio == null || radio.MacAddress == null ? "" : radio.MacAddress.Replace(":", "").Replace("-", "").ToUpperInvariant();

            if (radio == null || radio.IsCustom || console.IsSetupFormNull) return;

            HPSDRModel model;
            string name;
            if (!TryGetModel(radio.MacAddress, out model, out name)) return;

            _name = name;
            console.SetupForm.SelectRadioModel(model);
        }
    }
}
