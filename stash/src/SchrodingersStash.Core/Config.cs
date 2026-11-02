using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace SchrodingersStash.Core
{
    /// <summary>Settings from SchrodingersStash.ini. Missing keys keep their defaults.</summary>
    public sealed class Config
    {
        public string ApiKey { get; private set; } = "";
        public string ApiBase { get; private set; } = "https://api.mothquantum.com";

        /// <summary>emu = perfect simulator, fake = noisy snapshot of a real IBM chip, qpu = real IBM hardware.</summary>
        public string Mode { get; private set; } = "fake";

        /// <summary>The IBM chip for fake mode (e.g. marrakesh, fez). For qpu, empty means least busy.</summary>
        public string Backend { get; private set; } = "marrakesh";

        public string StartKey { get; private set; } = "F9";
        public int CashPerStash { get; private set; } = 10000;
        public int AlarmStars { get; private set; } = 2;
        public float RunMinutes { get; private set; } = 10f;
        public bool OfflineFallback { get; private set; } = true;
        public bool PhoneTexts { get; private set; } = true;

        public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

        public static Config Load(string path) =>
            File.Exists(path) ? Parse(File.ReadAllText(path)) : new Config();

        public static Config Parse(string text)
        {
            var ini = ReadIni(text);
            var c = new Config();
            c.ApiKey = Get(ini, "Atlas", "ApiKey", c.ApiKey).Trim();
            c.ApiBase = Get(ini, "Atlas", "ApiBase", c.ApiBase).Trim().TrimEnd('/');
            var mode = Get(ini, "Atlas", "Mode", c.Mode).Trim().ToLowerInvariant();
            c.Mode = mode == "emu" || mode == "fake" || mode == "qpu" ? mode : c.Mode;
            c.Backend = Get(ini, "Atlas", "Backend", c.Backend).Trim();

            c.StartKey = Get(ini, "Gameplay", "StartKey", c.StartKey).Trim();
            c.CashPerStash = Math.Max(0, GetInt(ini, "Gameplay", "CashPerStash", c.CashPerStash));
            c.AlarmStars = Math.Min(Math.Max(GetInt(ini, "Gameplay", "AlarmStars", c.AlarmStars), 0), 5);
            c.RunMinutes = Math.Max(2f, GetFloat(ini, "Gameplay", "RunMinutes", c.RunMinutes));
            c.OfflineFallback = GetBool(ini, "Gameplay", "OfflineFallback", c.OfflineFallback);
            c.PhoneTexts = GetBool(ini, "Gameplay", "PhoneTexts", c.PhoneTexts);
            return c;
        }

        static Dictionary<string, string> ReadIni(string text)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var section = "";
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#')
                    continue;
                if (line[0] == '[' && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }
                var eq = line.IndexOf('=');
                if (eq > 0)
                    values[section + "." + line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return values;
        }

        static string Get(Dictionary<string, string> ini, string section, string key, string fallback) =>
            ini.TryGetValue(section + "." + key, out var v) ? v : fallback;

        static bool GetBool(Dictionary<string, string> ini, string section, string key, bool fallback) =>
            bool.TryParse(Get(ini, section, key, ""), out var v) ? v : fallback;

        static int GetInt(Dictionary<string, string> ini, string section, string key, int fallback) =>
            int.TryParse(Get(ini, section, key, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        static float GetFloat(Dictionary<string, string> ini, string section, string key, float fallback) =>
            float.TryParse(Get(ini, section, key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
}
