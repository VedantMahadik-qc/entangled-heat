using System.Collections.Generic;
using System.Linq;
using EntangledHeat.Core;
using EntangledHeat.Core.Heat;
using EntangledHeat.Core.Quantum;
using GTA;
using GTA.Math;
using GTA.UI;

namespace EntangledHeat
{
    /// <summary>
    /// Superposed safehouses: from 2 stars, three ghost safehouses appear. The first one you
    /// reach triggers a measurement that decides where the real safehouse is.
    /// </summary>
    sealed class Hideouts
    {
        const int Branches = 3;
        const float ArrivalDistance = 12f;
        const float MinDistance = 300f;
        const float MaxDistance = 2500f;

        // Approximate street-level spots around the map; snapped to the nearest road at runtime.
        // TODO: tune in game once we can test.
        static readonly Vector3[] Spots =
        {
            new Vector3(-14f, -1441f, 31f),     // Strawberry
            new Vector3(1986f, 3815f, 32f),     // Sandy Shores
            new Vector3(-1150f, -1521f, 10f),   // Vespucci canals
            new Vector3(7f, 538f, 175f),        // Vinewood Hills
            new Vector3(720f, -1070f, 22f),     // La Mesa
            new Vector3(-1600f, -1040f, 13f),   // Del Perro
            new Vector3(-300f, 6260f, 31f),     // Paleto Bay
            new Vector3(1690f, 4785f, 42f),     // Grapeseed
            new Vector3(-560f, 300f, 83f),      // West Vinewood
            new Vector3(1200f, -3100f, 6f),     // Port of Los Santos
        };

        readonly QuantumPool _pool;
        readonly System.Action<StoryEvent> _onEvent;
        readonly List<(Vector3 Position, Blip Blip)> _branches = new List<(Vector3, Blip)>();
        int _real = -1;
        int _level;

        public Hideouts(QuantumPool pool, System.Action<StoryEvent> onEvent)
        {
            _pool = pool;
            _onEvent = onEvent;
        }

        public void Update(int wantedLevel)
        {
            _level = wantedLevel;
            if (_branches.Count == 0)
            {
                if (wantedLevel >= 2)
                    Spawn();
                return;
            }

            var position = Game.Player.Character.Position;
            for (var i = 0; i < _branches.Count; i++)
            {
                if (position.DistanceTo(_branches[i].Position) > ArrivalDistance)
                    continue;

                if (_real < 0)
                    Measure(i);
                else if (i == _real)
                    Escape();
                return;
            }
        }

        void Spawn()
        {
            var position = Game.Player.Character.Position;
            var candidates = Spots
                .Select(p => World.GetNextPositionOnStreet(p))
                .Where(p => p.DistanceTo(position) > MinDistance)
                .OrderBy(p => p.DistanceTo(position) > MaxDistance ? 1 : 0)
                .ThenBy(p => p.DistanceTo(position))
                .Take(Branches)
                .ToList();

            foreach (var spot in candidates)
            {
                var blip = World.CreateBlip(spot);
                blip.Color = BlipColor.Purple;
                blip.Alpha = 110;
                blip.Name = "Superposed safehouse";
                _branches.Add((spot, blip));
            }
            _real = -1;
            Notification.Show("~p~Safehouse in superposition~s~: it exists in 3 places until you get there.");
            _onEvent(StoryEvent.SafehouseSuperposed);
        }

        void Measure(int arrived)
        {
            var real = HeatCollapse.CollapseChoice(() => _pool.Draw(_level), _branches.Count);
            if (real < 0)
            {
                // No quantum data: be upfront and let this one be real.
                real = arrived;
                Notification.Show("~o~No quantum data~s~: safehouse resolved classically.");
            }
            _real = real;
            Log.Info($"safehouse measured: arrived {arrived}, real {real}");

            if (real == arrived)
            {
                Escape();
                return;
            }

            for (var i = 0; i < _branches.Count; i++)
                if (i != real)
                    _branches[i].Blip.Delete();
            var blipReal = _branches[real].Blip;
            blipReal.Alpha = 255;
            blipReal.ShowRoute = true;
            blipReal.Name = "Safehouse (collapsed)";
            Notification.Show("~p~Wavefunction collapsed elsewhere~s~: the real safehouse is marked. Go!");
            _onEvent(StoryEvent.SafehouseMissed);
        }

        void Escape()
        {
            Game.Player.WantedLevel = 0;
            Notification.Show("~g~Safehouse collapsed here.~s~ The heat is gone.");
            Clear(); // the script texts "escaped" when the wanted level hits zero
        }

        public void Clear()
        {
            foreach (var branch in _branches)
                if (branch.Blip != null && branch.Blip.Exists())
                    branch.Blip.Delete();
            _branches.Clear();
            _real = -1;
        }
    }
}
