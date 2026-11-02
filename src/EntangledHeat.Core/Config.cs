using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace EntangledHeat.Core
{
    /// <summary>Settings from EntangledHeat.ini. Missing keys keep their defaults.</summary>
    public sealed class Config
    {
        public string ApiKey { get; private set; } = "";
        public string ApiBase { get; private set; } = "https://api.mothquantum.com";
        public bool UseIbm { get; private set; } = true;
        public int IbmFromWantedLevel { get; private set; } = 4;

        /// <summary>Measure every collapse with its own new Atlas job instead of reusing one job's shots.</summary>
        public bool FreshJobPerCollapse { get; private set; } = true;

        public bool EnableHeatSystem { get; private set; } = true;
        /// <summary>Hand the police back to the game while a story mission runs (missions script their own cops).</summary>
        public bool PauseDuringMissions { get; private set; } = true;
        public bool EnableSuperposedHideouts { get; private set; } = true;
        public bool EnableSearchGrid { get; private set; } = true;
        public float CollapseIntervalSeconds { get; private set; } = 20f;
        public bool OfflineFallback { get; private set; } = true;
        public bool PhoneTexts { get; private set; } = true;
        public bool Tips { get; private set; } = true;

        /// <summary>Key name (System.Windows.Forms.Keys) that switches quantum police on and off.</summary>
        public string ToggleKey { get; private set; } = "F10";

        public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

        public static Config Load(string path) =>
            File.Exists(path) ? Parse(File.ReadAllText(path)) : new Config();

        public static Config Parse(string text)
        {
            var ini = ReadIni(text);
            var c = new Config();
            c.ApiKey = Get(ini, "Atlas", "ApiKey", c.ApiKey).Trim();
            c.ApiBase = Get(ini, "Atlas", "ApiBase", c.ApiBase).Trim().TrimEnd('/');
            c.UseIbm = GetBool(ini, "Atlas", "UseIbm", c.UseIbm);
            c.IbmFromWantedLevel = GetInt(ini, "Atlas", "IbmFromWantedLevel", c.IbmFromWantedLevel);
            c.FreshJobPerCollapse = GetBool(ini, "Atlas", "FreshJobPerCollapse", c.FreshJobPerCollapse);

            c.EnableHeatSystem = GetBool(ini, "Gameplay", "EnableHeatSystem", c.EnableHeatSystem);
            c.PauseDuringMissions = GetBool(ini, "Gameplay", "PauseDuringMissions", c.PauseDuringMissions);
            c.EnableSuperposedHideouts = GetBool(ini, "Gameplay", "EnableSuperposedHideouts", c.EnableSuperposedHideouts);
            c.EnableSearchGrid = GetBool(ini, "Gameplay", "EnableSearchGrid", c.EnableSearchGrid);
            c.CollapseIntervalSeconds = Math.Max(5f, GetFloat(ini, "Gameplay", "CollapseIntervalSeconds", c.CollapseIntervalSeconds));
            c.OfflineFallback = GetBool(ini, "Gameplay", "OfflineFallback", c.OfflineFallback);
            c.PhoneTexts = GetBool(ini, "Gameplay", "PhoneTexts", c.PhoneTexts);
            c.Tips = GetBool(ini, "Gameplay", "Tips", c.Tips);
            c.ToggleKey = Get(ini, "Controls", "ToggleKey", c.ToggleKey).Trim();
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
