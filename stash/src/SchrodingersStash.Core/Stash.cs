using Newtonsoft.Json.Linq;
using SchrodingersStash.Core.Quantum;

namespace SchrodingersStash.Core
{
    /// <summary>What a stash pair turned out to be, seen from the stash the player opened.</summary>
    public enum PairResult
    {
        /// <summary>Cash here, so the partner collapsed to empty. The normal entangled outcome.</summary>
        CashHere,
        /// <summary>Empty here (alarm), so the partner collapsed to cash. Also the normal outcome.</summary>
        CashThere,
        /// <summary>Both full: noise broke the anti-correlation. Only seen on noisy chips or hardware.</summary>
        Jackpot,
        /// <summary>Both empty: noise again, the unlucky way.</summary>
        Bust,
    }

    public static class Stash
    {
        public const string Engine = "graph-v1";
        public const int Pairs = 3;
        public const int Qubits = Pairs * 2;

        /// <summary>
        /// Six stashes as three entangled pairs (0-1, 2-3, 4-5). Every stash starts in superposition
        /// (X=1, cash or empty 50/50) and each pair gets a ZZ=-1 link: partners always disagree.
        /// Measured on Atlas (2 Nov 2026): 100% opposite on emu, 98-99% on fake Marrakesh/Fez.
        /// Pairs are not linked to each other, so each pair can be measured from its own job.
        /// </summary>
        public static JObject PairGraph(string mode, string backend, int shots = 1024)
        {
            var ops = new JArray();
            for (var q = 0; q < Qubits; q++)
                ops.Add(new JObject { ["type"] = "bloch", ["qubit"] = q, ["paulis"] = new JObject { ["X"] = 1.0 } });

            var coupling = new JArray();
            for (var p = 0; p < Pairs; p++)
            {
                var edge = new JArray(2 * p, 2 * p + 1);
                coupling.Add(edge);
                ops.Add(new JObject
                {
                    ["type"] = "relationship",
                    ["qubits"] = edge.DeepClone(),
                    ["paulis"] = new JObject { ["ZZ"] = -1.0 },
                    ["fraction"] = 1.0,
                });
            }

            var request = new JObject
            {
                ["num_qubits"] = Qubits,
                ["coupling_map"] = coupling,
                ["operations"] = ops,
                ["shots"] = shots,
                ["mode"] = mode,
            };
            if (mode != "emu" && !string.IsNullOrWhiteSpace(backend))
                request["backend_name"] = backend;
            return request;
        }

        /// <summary>Bit 1 = cash. side 0 is the pair's first stash (qubit 2p), side 1 its partner.</summary>
        public static PairResult Resolve(Shot shot, int pair, int side)
        {
            var here = shot[2 * pair + side];
            var there = shot[2 * pair + 1 - side];
            if (here && there) return PairResult.Jackpot;
            if (!here && !there) return PairResult.Bust;
            return here ? PairResult.CashHere : PairResult.CashThere;
        }

        public static ShotSource SourceOf(string mode) =>
            mode == "qpu" ? ShotSource.IbmHardware : mode == "fake" ? ShotSource.NoisyChip : ShotSource.Simulator;
    }
}
