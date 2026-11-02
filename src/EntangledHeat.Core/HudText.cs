using EntangledHeat.Core.Atlas;
using EntangledHeat.Core.Quantum;

namespace EntangledHeat.Core
{
    /// <summary>
    /// The persistent corner HUD line, e.g. "ATLAS OK | 4 STARS | IBM HARDWARE | job 1a2b3c4d | |010110>".
    /// Plain ASCII plus GTA colour codes: the game fonts don't have ✓ or ★.
    /// </summary>
    public static class HudText
    {
        public static string Line(AtlasStatus status, int wantedLevel, Shot lastShot, bool quantumOn = true, string toggleKey = "F10", bool missionPaused = false)
        {
            // A fresh live result proves Atlas came back after a failed startup check.
            if (status == AtlasStatus.Offline && lastShot != null && lastShot.Source != ShotSource.OfflineCache)
                status = AtlasStatus.Connected;
            var text = Status(status);
            if (!quantumOn)
                return text + $" ~s~| ~c~QUANTUM POLICE OFF ({toggleKey})";
            if (missionPaused)
                return text + " ~s~| ~c~QUANTUM POLICE PAUSED: STORY MISSION";
            if (wantedLevel > 0)
                text += $" ~s~| {wantedLevel} STAR{(wantedLevel == 1 ? "" : "S")}";
            if (lastShot != null)
                text += $" ~s~| {Source(lastShot.Source)} ~s~| {(lastShot.Fresh ? "new " : "")}job {lastShot.ShortJobId} | ~y~|{lastShot.Bits}>";
            return text;
        }

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

        static string Source(ShotSource source)
        {
            switch (source)
            {
                case ShotSource.IbmHardware: return "~y~IBM HARDWARE";
                case ShotSource.OfflineCache: return "~o~CACHED (offline)";
                default: return "~b~SIMULATOR";
            }
        }
    }
}
