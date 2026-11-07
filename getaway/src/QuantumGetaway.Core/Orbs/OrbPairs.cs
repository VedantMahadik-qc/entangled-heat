using Newtonsoft.Json.Linq;
using QuantumGetaway.Core.Tunnel;

namespace QuantumGetaway.Core.Orbs
{
    /// <summary>What an orb pair turned out to be, seen from the orb the player grabbed.</summary>
    public enum OrbResult
    {
        /// <summary>This orb is gold (2 charges), so its partner collapsed to plain. The normal entangled outcome.</summary>
        GoldHere,
        /// <summary>This orb is plain (1 charge), so its partner collapsed to gold. Also normal.</summary>
        GoldThere,
        /// <summary>Both gold: noise broke the anti-correlation. Only seen on noisy chips or hardware.</summary>
        BothGold,
        /// <summary>Both plain: noise again, the unlucky way.</summary>
        BothPlain,
    }

    /// <summary>Which of a portal's two mirrored exits you came out of.</summary>
    public enum TwinResult { Right, Left, Both, Neither }

    /// <summary>
    /// Entangled power-ups: orbs spawn in pairs on opposite sides of you. Each orb is gold or plain
    /// in superposition and partners are anti-correlated (ZZ=-1), so grabbing one measures the pair
    /// and decides its partner across the map at the same instant.
    /// </summary>
    public static class OrbPairs
    {
        public const string Engine = "graph-v1";
        public const int PairsPerJob = 3;
        public const int Qubits = PairsPerJob * 2;
        public const int GoldCharges = 2;
        public const int PlainCharges = 1;

        /// <summary>
        /// Three pairs (qubits 0-1, 2-3, 4-5): each qubit starts in superposition (X=1, gold or plain
        /// 50/50) and each pair gets a ZZ=-1 link. Same circuit Schrödinger's Stash measured on Atlas
        /// (2 Nov 2026): partners opposite 100% of the time on emu, 98-99% on fake Marrakesh/Fez.
        /// mode: emu | fake | qpu (graph-v1's own names).
        /// </summary>
        public static JObject PairGraph(string mode, string backend, int shots = 1024)
        {
            var ops = new JArray();
            for (var q = 0; q < Qubits; q++)
                ops.Add(new JObject { ["type"] = "bloch", ["qubit"] = q, ["paulis"] = new JObject { ["X"] = 1.0 } });

            var coupling = new JArray();
            for (var p = 0; p < PairsPerJob; p++)
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

        /// <summary>Bit 1 = gold. slot 0-2 picks the pair's qubits in the shot; side 0/1 is which orb of the pair you grabbed.</summary>
        public static OrbResult Resolve(Shot shot, int slot, int side)
        {
            var here = shot[2 * slot + side];
            var there = shot[2 * slot + 1 - side];
            if (here && there) return OrbResult.BothGold;
            if (!here && !there) return OrbResult.BothPlain;
            return here ? OrbResult.GoldHere : OrbResult.GoldThere;
        }

        /// <summary>
        /// A portal's exit twins use the first pair of a fresh shot: qubit 0 = the right twin, qubit 1 = the left.
        /// 10 right, 01 left; noise gives 11 (you AND a quantum copy of you come out) or 00 (neither: the middle).
        /// </summary>
        public static TwinResult ResolveTwins(Shot shot)
        {
            bool right = shot[0], left = shot[1];
            if (right && left) return TwinResult.Both;
            if (!right && !left) return TwinResult.Neither;
            return right ? TwinResult.Right : TwinResult.Left;
        }

        public static bool HereIsGold(OrbResult r) => r == OrbResult.GoldHere || r == OrbResult.BothGold;

        public static bool ThereIsGold(OrbResult r) => r == OrbResult.GoldThere || r == OrbResult.BothGold;

        public static ShotSource SourceOf(string mode) =>
            mode == "qpu" ? ShotSource.IbmHardware : mode == "fake" ? ShotSource.NoisyChip : ShotSource.Simulator;
    }
}
