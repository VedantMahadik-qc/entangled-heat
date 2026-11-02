using System;
using System.Collections.Generic;
using System.Linq;
using EntangledHeat.Core.Quantum;

namespace EntangledHeat.Core.Heat
{
    /// <summary>A roadblock spot on the street between a searched block and an unsearched one.</summary>
    public struct CordonPoint
    {
        /// <summary>Offset from the grid centre in block units: X east, Y south.</summary>
        public float X;
        public float Y;
        /// <summary>The searched block this roadblock seals in.</summary>
        public int Block;
    }

    /// <summary>One measured search: which blocks the police sweep and where they seal them off.</summary>
    public sealed class SearchLayout
    {
        public int Rows { get; internal set; }
        public int Cols { get; internal set; }
        public bool[] Searched { get; internal set; }
        public List<CordonPoint> Cordon { get; internal set; }
        public Shot Shot { get; internal set; }

        public int SearchedCount => Searched.Count(s => s);

        /// <summary>Centre of a block, in block units from the grid centre (X east, Y south).</summary>
        public void BlockCentre(int block, out float x, out float y)
        {
            x = block % Cols + 0.5f - Cols / 2f;
            y = block / Cols + 0.5f - Rows / 2f;
        }

        /// <summary>The <paramref name="max"/> cordon points closest to (x, y), nearest first.</summary>
        public List<CordonPoint> Nearest(float x, float y, int max) =>
            Cordon.OrderBy(p => (p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y)).Take(max).ToList();
    }

    /// <summary>
    /// labyrinth-v1 search grid. Qubit i is city block i (row-major) around where you were last seen:
    /// 1 = the police sweep that block, 0 = they leave it. Every street on an open corridor is a ZZ=+1
    /// link, so searched blocks come in connected clusters. Outside the grid counts as unsearched,
    /// so a roadblock goes on every street around the edge of a searched cluster.
    /// </summary>
    public static class SearchGrid
    {
        public const int Rows = 4;
        public const int Cols = 4;
        public const int Blocks = Rows * Cols;

        public static SearchLayout Layout(Shot shot, int rows = Rows, int cols = Cols)
        {
            if (shot == null)
                throw new ArgumentNullException(nameof(shot));
            if (shot.Bits.Length != rows * cols)
                throw new ArgumentException($"expected {rows * cols} bits, got {shot.Bits.Length}", nameof(shot));

            var searched = shot.Bits.Select(b => b == '1').ToArray();
            var layout = new SearchLayout { Rows = rows, Cols = cols, Searched = searched, Shot = shot, Cordon = new List<CordonPoint>() };

            for (var block = 0; block < searched.Length; block++)
            {
                if (!searched[block])
                    continue;
                layout.BlockCentre(block, out var x, out var y);
                int r = block / cols, c = block % cols;
                // Half a block towards each neighbour that isn't being searched (or is off the grid).
                if (r == 0 || !searched[block - cols]) layout.Cordon.Add(new CordonPoint { X = x, Y = y - 0.5f, Block = block });
                if (r == rows - 1 || !searched[block + cols]) layout.Cordon.Add(new CordonPoint { X = x, Y = y + 0.5f, Block = block });
                if (c == 0 || !searched[block - 1]) layout.Cordon.Add(new CordonPoint { X = x - 0.5f, Y = y, Block = block });
                if (c == cols - 1 || !searched[block + 1]) layout.Cordon.Add(new CordonPoint { X = x + 0.5f, Y = y, Block = block });
            }
            return layout;
        }
    }
}
