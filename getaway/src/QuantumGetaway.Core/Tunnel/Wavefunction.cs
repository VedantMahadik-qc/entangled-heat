using System;
using System.Collections.Generic;
using System.Linq;

namespace QuantumGetaway.Core.Tunnel
{
    public enum ShotSource { Simulator, NoisyChip, IbmHardware, OfflineCache }

    /// <summary>One grid cell relative to you: Dx cells to your right (negative = left), Dy cells ahead.</summary>
    public readonly struct Cell
    {
        public Cell(int dx, int dy, double p)
        {
            Dx = dx;
            Dy = dy;
            P = p;
        }

        public int Dx { get; }
        public int Dy { get; }
        public double P { get; }

        /// <summary>Measured onto your own cell: the barrier held and you didn't move.</summary>
        public bool IsStay => Dx == 0 && Dy == 0;

        public override string ToString() => $"({Dx:+0;-0;0},{Dy:+0;-0;0})";
    }

    /// <summary>Two mirrored exits, Side cells right and left of your path and Dy ahead. Side 0 = straight ahead (no twin).</summary>
    public readonly struct ExitPair
    {
        public ExitPair(int side, int dy, double p)
        {
            Side = side;
            Dy = dy;
            P = p;
        }

        public int Side { get; }
        public int Dy { get; }
        public double P { get; }
        public bool HasTwin => Side != 0;
        public Cell Right => new Cell(Side, Dy, 0);
        public Cell Left => new Cell(-Side, Dy, 0);
        public Cell Middle => new Cell(0, Dy, 0);
    }

    /// <summary>
    /// Where you might come out of a tunnel: the blur-v1 output for one bright pixel (you) on a dark
    /// grid. QuantumBlur writes each pixel's measurement probability as its brightness, so brightness
    /// over the total is the chance of landing in that cell. Row 0 is level with you and rows grow
    /// ahead of you; you sit at (OriginX, OriginY).
    /// </summary>
    public sealed class Wavefunction
    {
        readonly double[] _p;
        readonly double[] _cumulative;

        public Wavefunction(TunnelKind kind, int size, byte[] brightness, string jobId, ShotSource source)
        {
            if (brightness.Length != size * size)
                throw new ArgumentException($"expected {size}x{size} pixels, got {brightness.Length}");
            double total = brightness.Sum(b => (double)b);
            if (total <= 0)
                throw new ArgumentException("the blurred image is completely dark: nowhere to land");

            Kind = kind;
            Size = size;
            JobId = jobId;
            Source = source;
            Brightness = brightness;
            _p = brightness.Select(b => b / total).ToArray();
            _cumulative = new double[_p.Length];
            double run = 0;
            for (var i = 0; i < _p.Length; i++)
                _cumulative[i] = run += _p[i];
            _cumulative[_p.Length - 1] = 1.0;
        }

        public TunnelKind Kind { get; }
        public int Size { get; }
        public string JobId { get; }
        public ShotSource Source { get; }

        /// <summary>The raw pixels, kept for the offline cache.</summary>
        public byte[] Brightness { get; }

        public string ShortJobId => JobId != null && JobId.Length >= 8 ? JobId.Substring(0, 8) : JobId ?? "?";

        public double P(int dx, int dy)
        {
            int x = Tunnels.OriginX + dx, y = Tunnels.OriginY + dy;
            return x < 0 || y < 0 || x >= Size || y >= Size ? 0 : _p[y * Size + x];
        }

        public double StayProbability => P(0, 0);

        /// <summary>The measurement: one cell, drawn with the probability in its pixel.</summary>
        public Cell Sample(Random rng)
        {
            var u = rng.NextDouble();
            var i = Array.BinarySearch(_cumulative, u);
            if (i < 0)
                i = ~i;
            // Skip zero-probability cells that share a cumulative value with the one before them.
            while (_p[i] == 0 && i < _p.Length - 1)
                i++;
            return CellAt(i);
        }

        /// <summary>The n most likely cells with at least minP, most likely first (for drawing the preview).</summary>
        public IReadOnlyList<Cell> Top(int n, double minP = 0)
        {
            return Enumerable.Range(0, _p.Length)
                .Where(i => _p[i] > 0 && _p[i] >= minP)
                .OrderByDescending(i => _p[i])
                .Take(n)
                .Select(CellAt)
                .ToList();
        }

        /// <summary>
        /// The exits as mirrored twins: (+Dx, Dy) on your right and (-Dx, Dy) on your left, with their
        /// probabilities added. Blur measures which pair you go to; graph-v1 then measures which twin.
        /// Cells straight ahead (Dx = 0) have no twin. Most likely pairs first.
        /// </summary>
        public IReadOnlyList<ExitPair> ExitPairs(int n, double minP = 0)
        {
            var pairs = new Dictionary<(int, int), double>();
            for (var i = 0; i < _p.Length; i++)
            {
                if (_p[i] == 0)
                    continue;
                var c = CellAt(i);
                var key = (Math.Abs(c.Dx), c.Dy);
                pairs[key] = (pairs.TryGetValue(key, out var p) ? p : 0) + _p[i];
            }
            return pairs
                .Where(kv => kv.Value >= minP)
                .OrderByDescending(kv => kv.Value)
                .Take(n)
                .Select(kv => new ExitPair(kv.Key.Item1, kv.Key.Item2, kv.Value))
                .ToList();
        }

        /// <summary>
        /// The exit options a portal shows: twin pairs at least minDy cells ahead, each at least
        /// minGap cells (along either axis) from the ones already picked, so the choices are clearly
        /// different places. With rng, pairs are drawn from the blur probabilities (so every portal
        /// shows a different set); without it, the likeliest pairs are taken.
        /// </summary>
        public IReadOnlyList<ExitPair> ExitOptions(int count, int minDy, int minGap, Random rng = null)
        {
            var candidates = ExitPairs(int.MaxValue).Where(p => p.HasTwin && p.Dy >= minDy).ToList();
            var picked = new List<ExitPair>();
            while (picked.Count < count && candidates.Count > 0)
            {
                var pair = rng == null ? candidates[0] : candidates[Choose(candidates, rng)];
                picked.Add(pair);
                candidates.RemoveAll(o => Math.Max(Math.Abs(o.Side - pair.Side), Math.Abs(o.Dy - pair.Dy)) < minGap);
            }
            return picked.OrderByDescending(p => p.P).ToList();
        }

        /// <summary>The measurement among the shown options: option i with probability P_i / sum of P.</summary>
        public static int Choose(IReadOnlyList<ExitPair> options, Random rng)
        {
            var total = options.Sum(o => o.P);
            var u = rng.NextDouble() * total;
            for (var i = 0; i < options.Count; i++)
            {
                u -= options[i].P;
                if (u < 0)
                    return i;
            }
            return options.Count - 1;
        }

        /// <summary>The twin pair a measured cell belongs to.</summary>
        public ExitPair PairOf(Cell cell) => new ExitPair(Math.Abs(cell.Dx), cell.Dy, P(cell.Dx, cell.Dy) + (cell.Dx != 0 ? P(-cell.Dx, cell.Dy) : 0));

        /// <summary>Expected jump distance in cells, for the log and the write-up.</summary>
        public double MeanDistance()
        {
            double d = 0;
            for (var i = 0; i < _p.Length; i++)
            {
                var c = CellAt(i);
                d += _p[i] * Math.Sqrt(c.Dx * c.Dx + c.Dy * c.Dy);
            }
            return d;
        }

        Cell CellAt(int i) => new Cell(i % Size - Tunnels.OriginX, i / Size - Tunnels.OriginY, _p[i]);
    }
}
