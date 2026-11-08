using QuantumGetaway.Core.Atlas;
using QuantumGetaway.Core.Tunnel;

namespace QuantumGetaway.Core
{
    /// <summary>
    /// The corner HUD line, e.g. "GETAWAY | ATLAS OK | PORTALS 2/3 | L3: open | ORBS: IBM HARDWARE (ibm_pittsburgh) | 142m | SIMULATOR | new job 1a2b3c4d | exit (+3,+17) p 1.8%".
    /// Plain ASCII plus GTA colour codes: the game fonts don't have special symbols.
    /// </summary>
    public static class GetawayHud
    {
        /// <param name="orbs">Where orb and twin results come from now (see Orbs), or null to leave it out.</param>
        public static string Line(AtlasStatus status, string button, int charges, int maxCharges, bool portalOpen, Landing last, float lastMetres, string orbs = null)
        {
            // A fresh live result proves Atlas came back after a failed startup check.
            if (status == AtlasStatus.Offline && last != null && last.Wave.Source != ShotSource.OfflineCache)
                status = AtlasStatus.Connected;
            var text = "~p~GETAWAY~s~ | " + Status(status) + " ~s~| "
                + (charges > 0 ? $"~p~PORTALS {charges}/{maxCharges}" : $"~c~PORTALS 0/{maxCharges} (find an orb)")
                + (portalOpen ? " ~s~| ~p~PORTAL OPEN: drive through" : charges > 0 ? $" ~s~| {button}: open" : "")
                + (orbs != null ? $" ~s~| ORBS: {orbs}" : "");
            if (last != null)
            {
                var what = last.Cell.IsStay ? "~o~REFLECTED" : $"{lastMetres:0}m";
                text += $" ~s~| {what} ~s~| {Source(last.Wave.Source)} ~s~| {(last.Fresh ? "new " : "")}job {last.Wave.ShortJobId}"
                    + $" | ~y~exit {last.Cell} p {Percent(last.Cell.P)}";
            }
            return text;
        }

        /// <summary>e.g. "IBM HARDWARE (ibm_pittsburgh)" in yellow, so real hardware is visible on screen.</summary>
        public static string Orbs(ShotSource source, string chip) =>
            source == ShotSource.IbmHardware && !string.IsNullOrEmpty(chip) ? $"~y~IBM HARDWARE ({chip})" : Source(source);

        public static string Percent(double p) => p >= 0.1 ? $"{p * 100:0}%" : $"{p * 100:0.0}%";

        static string Status(AtlasStatus status)
        {
            switch (status)
            {
                case AtlasStatus.Connected: return "~p~ATLAS ~g~OK";
                case AtlasStatus.Checking: return "~p~ATLAS ~c~connecting...";
                case AtlasStatus.InvalidKey: return "~p~ATLAS ~r~KEY INVALID";
                case AtlasStatus.Offline: return "~p~ATLAS ~o~OFFLINE";
                default: return "~p~ATLAS ~r~NO KEY";
            }
        }

        public static string Source(ShotSource source)
        {
            switch (source)
            {
                case ShotSource.IbmHardware: return "~y~IBM HARDWARE";
                case ShotSource.NoisyChip: return "~y~NOISY CHIP";
                case ShotSource.OfflineCache: return "~o~CACHED (offline)";
                default: return "~b~SIMULATOR";
            }
        }
    }
}
