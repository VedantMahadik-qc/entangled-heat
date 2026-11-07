using System;
using System.Collections.Generic;
using System.Linq;
using QuantumGetaway.Core.Tunnel;

namespace QuantumGetaway.Core.Orbs
{
    /// <summary>One measured outcome from an Atlas job. Bits[i] is qubit i (qubit 0 leftmost).</summary>
    public sealed class Shot
    {
        public Shot(string bits, string jobId, ShotSource source, bool fresh = false)
        {
            Bits = bits;
            JobId = jobId;
            Source = source;
            Fresh = fresh;
        }

        public string Bits { get; }
        public string JobId { get; }
        public ShotSource Source { get; }

        /// <summary>True for the first shot drawn from a live job: this measurement got its own brand-new Atlas job.</summary>
        public bool Fresh { get; }

        public bool this[int qubit] => Bits[qubit] == '1';

        public string ShortJobId => JobId == null ? "?" : JobId.Length <= 8 ? JobId : JobId.Substring(0, 8);

        /// <summary>For notifications, e.g. "new job 1a2b3c4d", or "earlier job 1a2b3c4d" for another shot of a job already used.</summary>
        public string JobLabel =>
            Fresh ? $"new job {ShortJobId}" : Source == ShotSource.OfflineCache ? $"job {ShortJobId}" : $"earlier job {ShortJobId}";

        public string SourceLabel
        {
            get
            {
                switch (Source)
                {
                    case ShotSource.IbmHardware: return "IBM quantum hardware";
                    case ShotSource.NoisyChip: return "noisy IBM chip snapshot";
                    case ShotSource.OfflineCache: return "OFFLINE, cached Atlas job";
                    default: return "Atlas simulator";
                }
            }
        }
    }

    /// <summary>
    /// The individual shots of one job, each handed out once. Atlas returns aggregated counts,
    /// so the order is shuffled; every shot handed out is still a real measurement.
    /// </summary>
    public sealed class ShotDeck
    {
        readonly Queue<string> _shots;

        public ShotDeck(IEnumerable<KeyValuePair<string, int>> counts, string jobId, ShotSource source, Random rng)
        {
            var all = counts.SelectMany(c => Enumerable.Repeat(c.Key, c.Value)).ToList();
            for (var i = all.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                var tmp = all[i];
                all[i] = all[j];
                all[j] = tmp;
            }
            _shots = new Queue<string>(all);
            Total = all.Count;
            JobId = jobId;
            Source = source;
        }

        public int Total { get; }
        public int Remaining => _shots.Count;
        public string JobId { get; }
        public ShotSource Source { get; }

        /// <summary>True once three quarters of the deck is used, so a refill can start early.</summary>
        public bool RunningLow => Remaining <= Total / 4;

        /// <summary>True once any shot has been handed out.</summary>
        public bool Used => Remaining < Total;

        /// <summary>Next shot, or null when empty. The first shot of a live job is marked Fresh.</summary>
        public Shot Draw()
        {
            if (_shots.Count == 0)
                return null;
            var fresh = !Used && Source != ShotSource.OfflineCache;
            return new Shot(_shots.Dequeue(), JobId, Source, fresh);
        }
    }
}
