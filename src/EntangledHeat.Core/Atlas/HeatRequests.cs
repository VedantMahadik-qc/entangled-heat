using System;
using Newtonsoft.Json.Linq;

namespace EntangledHeat.Core.Atlas
{
    public static class EngineIds
    {
        /// <summary>Quantum Graph Engine; the bounty's "Quantum Drive Engine" (samples graph states).</summary>
        public const string Graph = "graph-v1";
        public const string Labyrinth = "labyrinth-v1";
    }

    /// <summary>Request bodies for the Atlas engines. Keep in sync with src/tools/atlas_probe.py.</summary>
    public static class HeatRequests
    {
        public const int Units = 6;

        /// <summary>
        /// Partner link strength per wanted level. Measured on emu (29 Oct 2026): partners agree
        /// ~73% at 0.1, ~85% at 0.25, ~92% at 0.5 and 100% at 1.0, so more stars = tighter squad.
        /// </summary>
        static readonly double[] PairFraction = { 0, 0.1, 0.25, 0.5, 1.0, 1.0 };

        /// <summary>Link strength between neighbouring pairs at 5 stars: whole squad agrees ~50% of shots.</summary>
        public const double SquadFraction = 0.25;

        /// <summary>
        /// Police cars as three entangled pairs (0-1, 2-3, 4-5). Every car starts in superposition
        /// (X=1), then each pair gets a ZZ=+1 link that tightens with the wanted level.
        /// At 5 stars the pairs are also chained (1-2, 3-4) so the squad often moves as one.
        /// Links are applied in chain order; other orders were measured to cancel each other out.
        /// </summary>
        public static JObject HeatGraph(int wantedLevel, string mode, int shots = 1024)
        {
            var level = Math.Min(Math.Max(wantedLevel, 1), 5);
            var squad = level >= 5;

            var ops = new JArray();
            for (var q = 0; q < Units; q++)
                ops.Add(Bloch(q, "X", 1.0));

            var coupling = new JArray();
            for (var i = 0; i + 1 < Units; i++)
            {
                var isPair = i % 2 == 0;
                if (!isPair && !squad)
                    continue;
                var edge = new JArray(i, i + 1);
                coupling.Add(edge);
                ops.Add(Relationship(edge, 1.0, isPair ? PairFraction[level] : SquadFraction));
            }

            return new JObject
            {
                ["num_qubits"] = Units,
                ["coupling_map"] = coupling,
                ["operations"] = ops,
                ["shots"] = shots,
                ["mode"] = mode,
            };
        }

        /// <summary>Grid of city blocks around the player; every adjacent pair is a candidate open street.</summary>
        public static JObject SearchGrid(int rows, int cols, string mode, int shots = 4096)
        {
            var edges = new JArray();
            for (var r = 0; r < rows; r++)
                for (var c = 0; c < cols; c++)
                {
                    var i = r * cols + c;
                    if (c + 1 < cols) edges.Add(new JArray(i, i + 1));
                    if (r + 1 < rows) edges.Add(new JArray(i, i + cols));
                }

            return new JObject
            {
                ["level_data"] = new JObject
                {
                    ["name"] = "search-grid",
                    ["grid_size"] = new JObject { ["rows"] = rows, ["cols"] = cols },
                    ["num_qubits"] = rows * cols,
                    ["coupling_map"] = edges,
                },
                ["shots"] = shots,
                ["top_n"] = -1, // keep every measured layout, not just the top 16
                ["mode"] = mode,
            };
        }

        static JObject Bloch(int qubit, string pauli, double value) => new JObject
        {
            ["type"] = "bloch",
            ["qubit"] = qubit,
            ["paulis"] = new JObject { [pauli] = value },
        };

        static JObject Relationship(JArray qubits, double zz, double fraction) => new JObject
        {
            ["type"] = "relationship",
            ["qubits"] = qubits.DeepClone(),
            ["paulis"] = new JObject { ["ZZ"] = zz },
            ["fraction"] = fraction,
        };
    }
}
