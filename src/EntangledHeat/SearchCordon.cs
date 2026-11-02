using System;
using System.Collections.Generic;
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
    /// <summary>
    /// Police search grid (labyrinth-v1). When you break line of sight (stars flash grey), one measured
    /// shot decides which of the 4×4 blocks around your last known position get swept, and police
    /// roadblocks seal the streets around every searched cluster.
    /// </summary>
    sealed class SearchCordon
    {
        const float BlockSize = 90f;            // metres; roughly one Los Santos city block
        const int MaxRoadblocks = 4;            // nearest cordon streets only, to keep spawns cheap
        const float MinSpawnDistance = 60f;     // never pop a roadblock in right next to the player
        static readonly Model CarModel = new Model(VehicleHash.Police);
        static readonly Model CopModel = new Model(PedHash.Cop01SMY);

        readonly QuantumPool _pool;
        readonly Action<StoryEvent> _onEvent;
        readonly List<Blip> _zones = new List<Blip>();
        readonly List<Entity> _spawned = new List<Entity>();
        readonly List<Blip> _roadblockBlips = new List<Blip>();
        bool _searching;

        public SearchCordon(QuantumPool pool, Action<StoryEvent> onEvent)
        {
            _pool = pool;
            _onEvent = onEvent;
        }

        /// <summary>The shot behind the current search, for the HUD. Null when no search is active.</summary>
        public Shot LastShot { get; private set; }

        public void Update(int wantedLevel)
        {
            _pool.PrefetchGrid(wantedLevel);
            // Load models a little each tick so spawning never stalls the game thread.
            CarModel.Request();
            CopModel.Request();

            var greyedOut = Function.Call<bool>(Hash.ARE_PLAYER_STARS_GREYED_OUT, Game.Player);
            if (greyedOut && !_searching)
                Collapse();
            else if (!greyedOut && _searching)
                End("spotted again");
        }

        void Collapse()
        {
            _searching = true;
            var shot = _pool.DrawGrid();
            if (shot == null)
                return; // No quantum data: the vanilla search carries on, untouched.

            var layout = SearchGrid.Layout(shot);
            LastShot = shot;
            var centre = Function.Call<Vector3>(Hash.GET_PLAYER_WANTED_CENTRE_POSITION, Game.Player);
            var player = Game.Player.Character.Position;

            for (var block = 0; block < layout.Searched.Length; block++)
            {
                if (!layout.Searched[block])
                    continue;
                layout.BlockCentre(block, out var bx, out var by);
                var zone = World.CreateBlip(ToWorld(centre, bx, by), BlockSize / 2f);
                zone.Color = BlipColor.Red;
                zone.Alpha = 70;
                _zones.Add(zone);
            }

            var px = (player.X - centre.X) / BlockSize;
            var py = -(player.Y - centre.Y) / BlockSize;
            var placed = 0;
            foreach (var point in layout.Nearest(px, py, MaxRoadblocks))
                if (SpawnRoadblock(ToWorld(centre, point.X, point.Y), ToWorld(centre, layout, point.Block), player))
                    placed++;

            Notification.Show($"~p~Atlas~s~ search grid collapsed\n{layout.SearchedCount}/16 blocks swept, {placed} roadblocks\n~c~{shot.SourceLabel} · {shot.JobLabel}");
            Log.Info($"search grid |{shot.Bits}> job {shot.JobId} ({shot.Source}{(shot.Fresh ? ", new job" : shot.Source == ShotSource.OfflineCache ? "" : ", earlier job")}): {layout.SearchedCount} blocks, {layout.Cordon.Count} cordon streets, {placed} roadblocks");
            _onEvent(StoryEvent.SearchGrid);
        }

        bool SpawnRoadblock(Vector3 spot, Vector3 blockCentre, Vector3 player)
        {
            var street = World.GetNextPositionOnStreet(spot);
            if (street.DistanceTo(player) < MinSpawnDistance || !CarModel.IsLoaded || !CopModel.IsLoaded)
                return false;

            // Park across the street, nose towards the block it seals.
            var toBlock = blockCentre - street;
            var heading = (float)(Math.Atan2(-toBlock.X, toBlock.Y) * 180.0 / Math.PI) + 90f;
            var car = World.CreateVehicle(CarModel, street, heading);
            if (car == null)
                return false;
            car.IsSirenActive = true;
            _spawned.Add(car);

            foreach (var seat in new[] { VehicleSeat.Driver, VehicleSeat.Passenger })
            {
                var cop = car.CreatePedOnSeat(seat, CopModel);
                if (cop == null)
                    continue;
                cop.Weapons.Give(WeaponHash.Pistol, 120, true, true);
                cop.Task.LeaveVehicle();
                _spawned.Add(cop);
            }

            var blip = car.AddBlip();
            blip.Color = BlipColor.Blue;
            blip.Scale = 0.7f;
            blip.Name = "Quantum cordon";
            _roadblockBlips.Add(blip);
            return true;
        }

        /// <summary>Hide the search zones and hand the roadblocks back to the game to clean up naturally.</summary>
        public void End(string reason)
        {
            if (_searching && LastShot != null)
                Log.Info($"search grid ended: {reason}");
            _searching = false;
            LastShot = null;

            foreach (var blip in _zones.Concat(_roadblockBlips))
                if (blip.Exists())
                    blip.Delete();
            _zones.Clear();
            _roadblockBlips.Clear();

            foreach (var entity in _spawned)
                if (entity.Exists())
                    entity.MarkAsNoLongerNeeded();
            _spawned.Clear();
        }

        // Grid units: X east, Y south. GTA world: +Y is north.
        static Vector3 ToWorld(Vector3 centre, float x, float y) => centre + new Vector3(x * BlockSize, -y * BlockSize, 0f);

        static Vector3 ToWorld(Vector3 centre, SearchLayout layout, int block)
        {
            layout.BlockCentre(block, out var x, out var y);
            return ToWorld(centre, x, y);
        }
    }
}
