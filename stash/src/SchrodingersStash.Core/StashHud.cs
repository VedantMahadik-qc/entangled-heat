using SchrodingersStash.Core.Atlas;
using SchrodingersStash.Core.Quantum;

namespace SchrodingersStash.Core
{
    /// <summary>
    /// The corner HUD line, e.g. "STASH | ATLAS OK | $20,000 | 2 PAIRS LEFT | NOISY CHIP | new job 1a2b3c4d | |01>".
    /// Plain ASCII plus GTA colour codes: the game fonts don't have special symbols.
    /// </summary>
    public static class StashHud
    {
        public static string Line(AtlasStatus status, bool running, int cash, int pairsLeft, Shot lastShot, int lastPair = -1, string startKey = "F9")
        {
            // A fresh live result proves Atlas came back after a failed startup check.
            if (status == AtlasStatus.Offline && lastShot != null && lastShot.Source != ShotSource.OfflineCache)
                status = AtlasStatus.Connected;
            var text = "~p~STASH~s~ | " + Status(status);
            if (!running)
                return text + $" ~s~| ~c~{startKey}: start a stash run";
            text += $" ~s~| ~g~${cash:N0}~s~ | {pairsLeft} PAIR{(pairsLeft == 1 ? "" : "S")} LEFT";
            if (lastShot != null)
            {
                var bits = lastPair >= 0 ? lastShot.Bits.Substring(2 * lastPair, 2) : lastShot.Bits;
                text += $" ~s~| {Source(lastShot.Source)} ~s~| {(lastShot.Fresh ? "new " : "")}job {lastShot.ShortJobId} | ~y~|{bits}>";
            }
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
                case ShotSource.NoisyChip: return "~y~NOISY CHIP";
                case ShotSource.OfflineCache: return "~o~CACHED (offline)";
                default: return "~b~SIMULATOR";
            }
        }
    }
}
