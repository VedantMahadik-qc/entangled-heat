using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using EntangledHeat.Core;
using EntangledHeat.Core.Heat;
using EntangledHeat.Core.Quantum;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;

namespace EntangledHeat
{
    /// <summary>Turns each quantum collapse into orders for the nearest police cars, and makes the entanglement visible.</summary>
    sealed class HeatDirector
    {
        const float SearchRadius = 250f;
        const float ArrivalDistance = 15f;
        const float DrawDistance = 180f;
        static readonly TimeSpan DecoherenceTime = TimeSpan.FromSeconds(25);
        static readonly TimeSpan PulseTime = TimeSpan.FromMilliseconds(350);
        static readonly Color ThreadColor = Color.FromArgb(220, 190, 90, 255);   // entanglement link between partners
        static readonly Color PursueColor = Color.FromArgb(200, 230, 40, 40);
        static readonly Color InterceptColor = Color.FromArgb(200, 40, 120, 255);
        static readonly Color LostColor = Color.FromArgb(160, 220, 220, 220);

        sealed class Unit
        {
            public Vehicle Vehicle;
            public Ped Driver;
            public int Qubit;
            public UnitOrder Order;
            public Vector3 Target;
            public bool Arrived;
            public DateTime LoseTrailUntil;
            public Blip Blip;

            /// <summary>Partners share a pair number: qubits 0-1 are pair 1, 2-3 pair 2, 4-5 pair 3.</summary>
            public int Pair => Qubit / 2 + 1;
        }

        readonly QuantumPool _pool;
        readonly Action<StoryEvent> _onEvent;
        readonly List<Unit> _units = new List<Unit>();
        DateTime _pulseEndsAt; // the collapse screen pulse; the effect never ends by itself

        public HeatDirector(QuantumPool pool, Action<StoryEvent> onEvent)
        {
            _pool = pool;
            _onEvent = onEvent;
        }

        /// <summary>The shot behind the current orders, for the HUD. Null when no unit is directed.</summary>
        public Shot LastShot { get; private set; }

        public void Collapse(int wantedLevel)
        {
            var cars = FindPoliceCars();
            if (cars.Count == 0)
                return;

            var shot = _pool.Draw(wantedLevel);
            if (shot == null)
                return; // No quantum data: leave the vanilla police AI alone.

            var orders = HeatCollapse.Orders(shot, cars.Count);
            Release();
            LastShot = shot;
            var player = Game.Player.Character;
            for (var i = 0; i < cars.Count; i++)
            {
                var unit = new Unit { Vehicle = cars[i], Driver = cars[i].Driver, Qubit = i % shot.Bits.Length };
                _units.Add(unit);
                Apply(unit, orders[i], player);
            }

            // The collapse moment: a short pulse so the player feels the measurement happen.
            Audio.PlaySoundFrontend("Hack_Success", "DLC_HEIST_BIOLAB_PREP_HACKING_SOUNDS");
            Screen.StartEffect(ScreenEffect.SwitchHudIn);
            _pulseEndsAt = DateTime.UtcNow + PulseTime;

            var pursuing = orders.Count(o => o == UnitOrder.Pursue);
            Notification.Show($"~p~Atlas~s~ heat collapsed ~y~|{shot.Bits}>~s~\n{pursuing} chasing, {orders.Length - pursuing} cutting you off\n~c~{shot.SourceLabel} · {shot.JobLabel}");
            Log.Info($"collapse L{wantedLevel} |{shot.Bits}> job {shot.JobId} ({shot.Source}{(shot.Fresh ? ", new job" : shot.Source == ShotSource.OfflineCache ? "" : ", earlier job")}) -> {string.Join(",", orders)}");
            _onEvent(StoryEvent.Collapse);
        }

        public void Update()
        {
            var player = Game.Player.Character;
            foreach (var unit in _units.ToList())
            {
                if (!IsAlive(unit))
                {
                    _units.Remove(unit);
                    unit.Blip?.Delete();
                    Decohere(HeatCollapse.PartnerOf(unit.Qubit), player);
                    continue;
                }

                if (unit.Order == UnitOrder.Intercept && !unit.Arrived && unit.Vehicle.Position.DistanceTo(unit.Target) < ArrivalDistance)
                {
                    // Roadblock: stop, get out and hold the street.
                    unit.Arrived = true;
                    unit.Driver.Task.LeaveVehicle();
                    unit.Driver.Task.FightAgainst(player);
                }

                if (unit.Order == UnitOrder.LoseTrail && DateTime.UtcNow >= unit.LoseTrailUntil)
                    Apply(unit, UnitOrder.Pursue, player);
            }
        }

        /// <summary>
        /// Every frame: a purple thread between entangled partners and a cone over each car showing its order.
        /// A broken thread (partner wrecked) simply disappears, which is the decoherence the player caused.
        /// </summary>
        public void Draw()
        {
            if (_pulseEndsAt != DateTime.MinValue && DateTime.UtcNow >= _pulseEndsAt)
                StopPulse();
            if (_units.Count == 0)
                return;
            var player = Game.Player.Character.Position;
            foreach (var unit in _units)
            {
                if (!unit.Vehicle.Exists() || unit.Vehicle.Position.DistanceTo(player) > DrawDistance)
                    continue;
                var top = unit.Vehicle.Position + new Vector3(0f, 0f, 2.2f);
                World.DrawMarker(MarkerType.UpsideDownCone, top, Vector3.Zero, Vector3.Zero, new Vector3(0.6f, 0.6f, 0.6f),
                    OrderColor(unit.Order), bobUpAndDown: true, faceCamera: true);

                if (unit.Qubit % 2 != 0 || unit.Order == UnitOrder.LoseTrail)
                    continue;
                foreach (var partner in _units.Where(u => u.Qubit == unit.Qubit + 1 && u.Order != UnitOrder.LoseTrail && u.Vehicle.Exists()))
                    World.DrawLine(top, partner.Vehicle.Position + new Vector3(0f, 0f, 2.2f), ThreadColor);
            }
        }

        /// <summary>Hand every car back to the game's normal police AI.</summary>
        public void Release()
        {
            StopPulse();
            foreach (var unit in _units)
            {
                unit.Blip?.Delete();
                if (unit.Driver != null && unit.Driver.Exists())
                    unit.Driver.BlockPermanentEvents = false;
            }
            _units.Clear();
            LastShot = null;
        }

        void Decohere(int qubit, Ped player)
        {
            foreach (var partner in _units.Where(u => u.Qubit == qubit && u.Order != UnitOrder.LoseTrail))
            {
                Apply(partner, UnitOrder.LoseTrail, player);
                Notification.Show($"~p~Entanglement broken~s~: pair {partner.Pair}'s partner car lost your trail");
                Log.Info($"decoherence: qubit {qubit} lost trail");
                _onEvent(StoryEvent.Decoherence);
            }
        }

        void Apply(Unit unit, UnitOrder order, Ped player)
        {
            unit.Order = order;
            unit.Arrived = false;
            unit.Driver.BlockPermanentEvents = true;
            unit.Driver.Task.ClearAll();

            switch (order)
            {
                case UnitOrder.Pursue:
                    unit.Vehicle.IsSirenActive = true;
                    unit.Driver.Task.VehicleChase(player);
                    SetBlip(unit, BlipColor.Red, $"Entangled pair {unit.Pair}: chasing");
                    break;

                case UnitOrder.Intercept:
                    unit.Target = InterceptPoint(player);
                    unit.Vehicle.IsSirenActive = true;
                    unit.Driver.Task.DriveTo(unit.Vehicle, unit.Target, ArrivalDistance, 40f, DrivingStyle.Rushed);
                    SetBlip(unit, BlipColor.Blue, $"Entangled pair {unit.Pair}: cutting you off");
                    break;

                case UnitOrder.LoseTrail:
                    unit.LoseTrailUntil = DateTime.UtcNow + DecoherenceTime;
                    unit.Vehicle.IsSirenActive = false;
                    unit.Driver.Task.CruiseWithVehicle(unit.Vehicle, 15f, DrivingStyle.Normal);
                    SetBlip(unit, BlipColor.White, $"Decohered (pair {unit.Pair}): lost your trail");
                    break;
            }
        }

        void StopPulse()
        {
            if (_pulseEndsAt == DateTime.MinValue)
                return;
            Screen.StopEffect(ScreenEffect.SwitchHudIn);
            _pulseEndsAt = DateTime.MinValue;
        }

        static Color OrderColor(UnitOrder order) =>
            order == UnitOrder.Pursue ? PursueColor : order == UnitOrder.Intercept ? InterceptColor : LostColor;

        static Vector3 InterceptPoint(Ped player)
        {
            // Aim where the player will be in ~8 s (or 150 m ahead when slow), snapped to a road.
            var velocity = player.Velocity;
            var ahead = velocity.Length() > 5f ? velocity * 8f : player.ForwardVector * 150f;
            return World.GetNextPositionOnStreet(player.Position + ahead);
        }

        static void SetBlip(Unit unit, BlipColor color, string name)
        {
            if (unit.Blip == null || !unit.Blip.Exists())
                unit.Blip = unit.Vehicle.AddBlip();
            unit.Blip.Color = color;
            unit.Blip.Scale = 0.8f;
            unit.Blip.NumberLabel = unit.Pair; // partners show the same number on the map
            unit.Blip.Name = name;
        }

        /// <summary>A unit is lost when its car is wrecked or its officer is down. Cops chasing on foot still count.</summary>
        static bool IsAlive(Unit unit) =>
            unit.Vehicle.Exists() && !unit.Vehicle.IsDead && unit.Driver.Exists() && !unit.Driver.IsDead;

        static List<Vehicle> FindPoliceCars()
        {
            var player = Game.Player.Character;
            var own = player.CurrentVehicle;
            return World.GetNearbyVehicles(player.Position, SearchRadius)
                .Where(v => v.Exists() && !v.IsDead && v != own)
                .Where(v => v.Driver != null && v.Driver.Exists() && !v.Driver.IsDead)
                .Where(v => Function.Call<bool>(Hash.IS_PED_IN_ANY_POLICE_VEHICLE, v.Driver))
                .OrderBy(v => v.Position.DistanceTo(player.Position))
                .Take(Core.Atlas.HeatRequests.Units)
                .ToList();
        }
    }
}
