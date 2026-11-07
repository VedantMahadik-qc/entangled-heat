using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace QuantumGetaway.Core
{
    /// <summary>Settings from QuantumGetaway.ini. Missing keys keep their defaults.</summary>
    public sealed class Config
    {
        public string ApiKey { get; private set; } = "";
        public string ApiBase { get; private set; } = "https://api.mothquantum.com";

        /// <summary>sim = exact simulator, fake = noisy snapshot of a real IBM chip, qpu = real IBM hardware.</summary>
        public string Mode { get; private set; } = "sim";

        /// <summary>
        /// For the entangled orb pairs (graph-v1): emu = perfect simulator, partners always opposite;
        /// fake = noisy snapshot of a real IBM chip, so about 1 pair in 70 comes out both gold (or both plain).
        /// </summary>
        public string OrbMode { get; private set; } = "fake";

        /// <summary>Also run orb pairs on real IBM hardware; its shots take over whenever a job has finished.</summary>
        public bool OrbHardware { get; private set; } = true;

        /// <summary>The IBM chip for fake/qpu (e.g. marrakesh, fez). For qpu, empty means least busy.</summary>
        public string Backend { get; private set; } = "marrakesh";

        /// <summary>Keyboard key that opens a portal. Controllers use L3 (left stick click).</summary>
        public string TunnelKey { get; private set; } = "T";

        public bool ControllerL3 { get; private set; } = true;

        /// <summary>Metres per grid cell. The grid reaches 31 cells ahead and 15-16 to each side.</summary>
        public float CellSize { get; private set; } = 8f;

        public int MaxCharges { get; private set; } = 3;
        public int StartCharges { get; private set; } = 1;

        /// <summary>Quantum orbs kept on the streets around you, and how many while you're wanted.</summary>
        public int Orbs { get; private set; } = 4;
        public int OrbsWanted { get; private set; } = 8;

        /// <summary>A portal you don't drive through closes after this long and gives the charge back.</summary>
        public float PortalSeconds { get; private set; } = 6f;

        /// <summary>When you go through a portal while wanted, the police keep searching where you were.</summary>
        public bool ShakePolice { get; private set; } = true;

        public bool OfflineFallback { get; private set; } = true;

        public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

        public static Config Load(string path) =>
            File.Exists(path) ? Parse(File.ReadAllText(path)) : new Config();

        /// <summary>
        /// Load path; if it has no API key, borrow [Atlas] ApiKey from keyFallbackPath (Entangled
        /// Heat's ini), so a player who installs both only pastes the key once.
        /// </summary>
        public static Config Load(string path, string keyFallbackPath)
        {
            var c = Load(path);
            if (!c.HasApiKey && File.Exists(keyFallbackPath))
                c.ApiKey = Parse(File.ReadAllText(keyFallbackPath)).ApiKey;
            return c;
        }

        public static Config Parse(string text)
        {
            var ini = ReadIni(text);
            var c = new Config();
            c.ApiKey = Get(ini, "Atlas", "ApiKey", c.ApiKey).Trim();
            c.ApiBase = Get(ini, "Atlas", "ApiBase", c.ApiBase).Trim().TrimEnd('/');
            var mode = Get(ini, "Atlas", "Mode", c.Mode).Trim().ToLowerInvariant();
            c.Mode = mode == "sim" || mode == "fake" || mode == "qpu" ? mode : c.Mode;
            var orbMode = Get(ini, "Atlas", "OrbMode", c.OrbMode).Trim().ToLowerInvariant();
            c.OrbMode = orbMode == "emu" || orbMode == "fake" || orbMode == "qpu" ? orbMode : c.OrbMode;
            c.OrbHardware = GetBool(ini, "Atlas", "OrbHardware", c.OrbHardware);
            c.Backend = Get(ini, "Atlas", "Backend", c.Backend).Trim();

            c.TunnelKey = Get(ini, "Gameplay", "TunnelKey", c.TunnelKey).Trim();
            c.ControllerL3 = GetBool(ini, "Gameplay", "ControllerL3", c.ControllerL3);
            c.CellSize = Clamp(GetFloat(ini, "Gameplay", "CellSize", c.CellSize), 2f, 20f);
            c.MaxCharges = (int)Clamp(GetInt(ini, "Gameplay", "MaxCharges", c.MaxCharges), 1, 9);
            c.StartCharges = (int)Clamp(GetInt(ini, "Gameplay", "StartCharges", c.StartCharges), 0, c.MaxCharges);
            c.Orbs = (int)Clamp(GetInt(ini, "Gameplay", "Orbs", c.Orbs), 0, 20);
            c.OrbsWanted = (int)Clamp(GetInt(ini, "Gameplay", "OrbsWanted", c.OrbsWanted), 0, 20);
            c.PortalSeconds = Clamp(GetFloat(ini, "Gameplay", "PortalSeconds", c.PortalSeconds), 2f, 30f);
            c.ShakePolice = GetBool(ini, "Gameplay", "ShakePolice", c.ShakePolice);
            c.OfflineFallback = GetBool(ini, "Gameplay", "OfflineFallback", c.OfflineFallback);
            return c;
        }

        static float Clamp(float v, float min, float max) => Math.Min(Math.Max(v, min), max);

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
