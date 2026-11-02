using System;
using EntangledHeat.Core.Quantum;

namespace EntangledHeat.Core.Heat
{
    public enum UnitOrder { Pursue, Intercept, LoseTrail }

    /// <summary>The rules that turn one measured shot into police orders.</summary>
    public static class HeatCollapse
    {
        /// <summary>Entangled partner pairs are (0,1), (2,3), (4,5).</summary>
        public static int PartnerOf(int qubit) => qubit ^ 1;

        /// <summary>
        /// Car i (nearest first) reads qubit i % 6: 1 = Pursue, 0 = Intercept.
        /// Extra cars beyond six share a qubit, and so share its fate.
        /// </summary>
        public static UnitOrder[] Orders(Shot shot, int unitCount)
        {
            if (shot == null)
                throw new ArgumentNullException(nameof(shot));
            var orders = new UnitOrder[unitCount];
            for (var i = 0; i < unitCount; i++)
                orders[i] = shot[i % shot.Bits.Length] ? UnitOrder.Pursue : UnitOrder.Intercept;
            return orders;
        }

        /// <summary>
        /// Picks one of <paramref name="options"/> outcomes from real shots by reading the first
        /// ceil(log2 options) bits and redrawing when the value is out of range (unbiased).
        /// Returns -1 if there is no quantum data.
        /// </summary>
        public static int CollapseChoice(Func<Shot> draw, int options, int maxDraws = 16)
        {
            if (options < 2)
                return 0;
            var bits = 0;
            while ((1 << bits) < options)
                bits++;

            for (var attempt = 0; attempt < maxDraws; attempt++)
            {
                var shot = draw();
                if (shot == null)
                    return -1;
                if (shot.Bits.Length < bits)
                    return -1;
                var value = 0;
                for (var b = 0; b < bits; b++)
                    value = (value << 1) | (shot[b] ? 1 : 0);
                if (value < options)
                    return value;
            }
            return -1;
        }
    }
}
