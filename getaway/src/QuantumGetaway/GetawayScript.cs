using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using QuantumGetaway.Core;
using QuantumGetaway.Core.Atlas;
using QuantumGetaway.Core.Orbs;
using QuantumGetaway.Core.Tunnel;

namespace QuantumGetaway
{
    /// <summary>
    /// Quantum Getaway: drive through entangled quantum orbs (graph-v1) to collect portal charges.
    /// Press L3 (or T) and a portal opens on the road ahead, with four labelled exits: two twin pairs
    /// drawn from the Quantum Blur (blur-v1) of you as one bright pixel on a grid. Drive in and the
    /// exit is measured: blur picks the pair, an entangled graph-v1 pair picks the twin.
    /// </summary>
    public class GetawayScript : Script
    {
        const TunnelKind Kind = TunnelKind.Leap;
        const int ExitPairCount = 2;  // two entangled twin pairs = four exits
        const int ExitMinDy = 6;      // exits at least 6 cells (~50 m) ahead
        const int ExitGap = 4;        // and the two pairs at least 4 cells apart
        const float PortalRadius = 4.5f;
        const float PortalRadiusOnFoot = 2f;
        const float OrbCollectRadius = 5f;
        const float OrbCollectRadiusOnFoot = 2.5f;
        const float OrbMinDistance = 120f;
        const float OrbMaxDistance = 450f;
        const float OrbForgetDistance = 700f;
        const float OrbDrawDistance = 300f;
        const float OrbGap = 80f;
        const string FxAsset = "scr_rcbarry2";
        const string FxName = "scr_clown_appears";
        static readonly TimeSpan LogicInterval = TimeSpan.FromMilliseconds(250);
        static readonly TimeSpan StartupHudTime = TimeSpan.FromSeconds(15);
        static readonly TimeSpan AfterPortalHudTime = TimeSpan.FromSeconds(10);
        static readonly TimeSpan FlashTime = TimeSpan.FromMilliseconds(400);
        static readonly TimeSpan ExitPortalTime = TimeSpan.FromSeconds(1.5);
        static readonly Color OrbColor = Color.FromArgb(200, 170, 80, 255);
        static readonly Color PortalOuter = Color.FromArgb(220, 170, 80, 255);
        static readonly Color PortalInner = Color.FromArgb(200, 80, 220, 255);

        enum OrbState { Superposed, Gold, Plain }

        sealed class Orb
        {
            public Vector3 Position;
            public Blip Blip;
            public int Pair;   // shown on the map: partners share the number
            public int Side;   // 0 or 1 within the pair
            public OrbState State;
        }

        /// <summary>One of the four exit portals, e.g. "1A": pair 1, left twin.</summary>
        sealed class Exit
        {
            public Vector3 Centre; // ring centre, about 2 m above the ground
            public Cell Cell;
            public int Pair;       // 0 or 1
            public string Label;
            public double Chance;  // shown odds: the pair's blur share, split between the twins
            public Blip Blip;
        }

        sealed class Portal
        {
            public Vector3 Ground;   // where the grid starts: you are its origin pixel
            public Vector3 Forward;
            public DateTime OpenedAt;
            public Wavefunction Wave;
            public IReadOnlyList<ExitPair> Options;
            public readonly List<Exit> Exits = new List<Exit>();
        }

        static readonly TimeSpan BlurPanelAfterTime = TimeSpan.FromSeconds(5);
        static readonly TimeSpan CopyLifetime = TimeSpan.FromSeconds(45);
        static readonly Color GoldColor = Color.FromArgb(220, 255, 200, 40);
        static readonly Color PlainColor = Color.FromArgb(170, 220, 230, 255);
        static readonly Color[] PairColors = { Color.FromArgb(220, 80, 220, 255), Color.FromArgb(220, 255, 90, 200) };

        readonly Config _config;
        readonly TunnelPool _pool;
        readonly OrbPool _orbPool;
        readonly TextElement _exitLabel = new TextElement("", PointF.Empty, 0.5f, Color.White, GTA.UI.Font.ChaletComprimeCologne, Alignment.Center, true, true);
        readonly TextElement _panelTitle = new TextElement("", new PointF(0f, 0f), 0.3f, Color.White, GTA.UI.Font.ChaletLondon, Alignment.Left, true, true);
        int _nextPair = 1;
        Wavefunction _panelWave;
        byte _panelMax;
        readonly Keys _key;
        readonly Random _rng = new Random();
        readonly ParticleEffectAsset _fx = new ParticleEffectAsset(FxAsset);
        readonly TextElement _hud = new TextElement("", new PointF(8f, 46f), 0.32f, Color.White, GTA.UI.Font.ChaletLondon, Alignment.Left, true, true);
        readonly DateTime _loadedAt = DateTime.UtcNow;
        readonly List<Orb> _orbs = new List<Orb>();
        readonly List<(Vehicle Car, Ped Driver, DateTime Until)> _copies = new List<(Vehicle Car, Ped Driver, DateTime Until)>();
        int _status = (int)AtlasStatus.NoKey; // written by the background key check
        AtlasStatus _announced = AtlasStatus.NoKey;

        int _charges;
        Portal _portal;
        bool _keyWasDown;
        Landing _last;
        float _lastMetres;
        DateTime _lastPortalAt;
        Vector3 _exitPortal;
        DateTime _exitPortalUntil;
        DateTime _flashEndsAt;
        DateTime _nextLogic;
        bool _taughtOrb;
        bool _taughtPortal;

        public GetawayScript()
        {
            var dataDir = Path.Combine(BaseDirectory, "QuantumGetaway");
            Directory.CreateDirectory(dataDir);
            Log.Init(Path.Combine(dataDir, "QuantumGetaway.log"));

            // Installed with Entangled Heat, the key in EntangledHeat.ini is enough.
            _config = Config.Load(Path.Combine(BaseDirectory, "QuantumGetaway.ini"), Path.Combine(BaseDirectory, "EntangledHeat.ini"));
            if (!Enum.TryParse(_config.TunnelKey, true, out _key))
                _key = Keys.T;
            _charges = _config.StartCharges;
            var client = _config.HasApiKey ? new AtlasClient(_config.ApiBase, _config.ApiKey) : null;
            _pool = new TunnelPool(client, _config, Path.Combine(dataDir, "cache"), Kind);
            _orbPool = new OrbPool(client, _config, Path.Combine(dataDir, "cache"));

            Log.Info($"loaded: api key {(_config.HasApiKey ? "set" : "MISSING")}, portal mode {_config.Mode}, orb mode {_config.OrbMode}, chip {_config.Backend}, key {_key}, controller L3 {_config.ControllerL3}");
            if (client != null)
                CheckKey(client);
            else
                Notification.Show("~p~Quantum Getaway~s~: ~r~no Atlas API key~s~ in scripts\\QuantumGetaway.ini or EntangledHeat.ini. Using cached results only.");

            Interval = 0;
            Tick += OnTick;
            Aborted += OnAborted;
        }

        /// <summary>GET /api/v1/me on a background task; the result shows in the HUD and a notification.</summary>
        void CheckKey(AtlasClient client)
        {
            _status = (int)AtlasStatus.Checking;
            Task.Run(async () =>
            {
                var status = await client.CheckAsync(CancellationToken.None).ConfigureAwait(false);
                Interlocked.Exchange(ref _status, (int)status);
                Log.Info($"atlas key check: {status}");
            });
        }

        string Button => _config.ControllerL3 && Game.LastInputMethod == InputMethod.GamePad ? "L3" : _key.ToString();

        void OnTick(object sender, EventArgs e)
        {
            var now = DateTime.UtcNow;
            if (now >= _nextLogic)
            {
                _nextLogic = now + LogicInterval;
                Logic(now);
            }

            var player = Game.Player.Character;
            if (!player.IsDead)
            {
                if (OpenPressed(player))
                    OpenPortal(player, now);
                CollectOrbs(player);
                if (_portal != null && player.Position.DistanceTo(_portal.Ground + new Vector3(0f, 0f, 1.5f)) < (player.IsInVehicle() ? PortalRadius : PortalRadiusOnFoot))
                    GoThrough(player, now);
            }

            DrawOrbs(player.Position);
            DrawPortal(now);
            if (_config.ShowBlurMap) DrawBlurPanel(now);
            if (_flashEndsAt != DateTime.MinValue && now >= _flashEndsAt)
            {
                GTA.UI.Screen.StopEffect(ScreenEffect.SwitchShortNeutralIn);
                _flashEndsAt = DateTime.MinValue;
            }
            DrawHud(now);
        }

        void Logic(DateTime now)
        {
            ShowStatusNoticeOnce();
            while (_pool.TryDequeueNotice(out var notice))
                Notification.Show(notice);
            while (_orbPool.TryDequeueNotice(out var notice))
                Notification.Show(notice);
            // Keep one new job of each engine waiting, so every portal and every orb pair gets its own measurement.
            _pool.Prefetch();
            _orbPool.Prefetch();
            if (!_fx.IsLoaded)
                _fx.Request();
            MaintainOrbs();
            ReleaseCopies(false);

            if (_portal != null && (now - _portal.OpenedAt).TotalSeconds > _config.PortalSeconds)
            {
                ClosePortal();
                _charges = Math.Min(_charges + 1, _config.MaxCharges);
                Notification.Show("~c~The portal closed~s~ before you went through. Charge refunded.");
            }
        }

        /// <summary>T on the keyboard, or L3 (left stick click) on a controller.</summary>
        bool OpenPressed(Ped player)
        {
            var down = Game.IsKeyPressed(_key);
            var pressed = down && !_keyWasDown;
            _keyWasDown = down;
            if (_config.ControllerL3 && Game.LastInputMethod == InputMethod.GamePad)
            {
                // L3 is the horn in a car; while a portal is ready to open, it opens portals instead.
                if (player.IsInVehicle() && _charges > 0 && _portal == null)
                    Game.DisableControlThisFrame(GTA.Control.VehicleHorn);
                // FrontendLs is the physical L3 button whatever the game maps it to; the "disabled"
                // check also sees it while the horn is switched off.
                pressed |= Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 2, (int)GTA.Control.FrontendLs);
            }
            return pressed && Game.Player.CanControlCharacter && !Game.IsCutsceneActive;
        }

        // ---- Orbs: entangled power-ups ----

        void MaintainOrbs()
        {
            var player = Game.Player.Character.Position;
            foreach (var orb in _orbs.Where(o => o.Position.DistanceTo2D(player) > OrbForgetDistance).ToList())
                RemoveOrb(orb);
            var wanted = Game.Player.WantedLevel > 0 ? _config.OrbsWanted : _config.Orbs;
            if (_orbs.Count + 2 > Math.Max(wanted, 2))
                return;
            // One new pair per logic tick at most: partners on opposite sides of you, out of sight range.
            var angle = _rng.NextDouble() * Math.PI * 2;
            var a = FindOrbSpot(player, angle);
            var b = FindOrbSpot(player, angle + Math.PI);
            if (a == null || b == null)
                return;
            var pair = _nextPair;
            _nextPair = _nextPair % 9 + 1;
            AddOrb(a.Value, pair, 0);
            AddOrb(b.Value, pair, 1);
        }

        Vector3? FindOrbSpot(Vector3 player, double angle)
        {
            var distance = OrbMinDistance + (float)_rng.NextDouble() * (OrbMaxDistance - OrbMinDistance);
            var target = player + new Vector3((float)Math.Cos(angle), (float)Math.Sin(angle), 0f) * distance;
            var spot = World.GetNextPositionOnStreet(target);
            if (spot == Vector3.Zero || spot.DistanceTo2D(player) < OrbMinDistance * 0.7f || _orbs.Any(o => o.Position.DistanceTo2D(spot) < OrbGap))
                return null;
            return spot;
        }

        void AddOrb(Vector3 spot, int pair, int side)
        {
            var blip = World.CreateBlip(spot);
            blip.Sprite = BlipSprite.Standard;
            blip.Color = BlipColor.Purple;
            blip.Scale = 0.75f;
            blip.NumberLabel = pair; // partners show the same number on the map
            blip.IsShortRange = true;
            blip.Name = $"Quantum orb (pair {pair})";
            _orbs.Add(new Orb { Position = spot, Blip = blip, Pair = pair, Side = side, State = OrbState.Superposed });
        }

        void CollectOrbs(Ped player)
        {
            if (_charges >= _config.MaxCharges)
                return;
            var radius = player.IsInVehicle() ? OrbCollectRadius : OrbCollectRadiusOnFoot;
            var orb = _orbs.FirstOrDefault(o => o.Position.DistanceTo(player.Position) < radius + 1f);
            if (orb == null)
                return;
            if (orb.State == OrbState.Superposed)
                Measure(orb);
            RemoveOrb(orb);
            var gain = orb.State == OrbState.Gold ? OrbPairs.GoldCharges : OrbPairs.PlainCharges;
            _charges = Math.Min(_charges + gain, _config.MaxCharges);
            Audio.PlaySoundFrontend("PICK_UP", "HUD_FRONTEND_DEFAULT_SOUNDSET");
            GTA.UI.Screen.ShowSubtitle($"{(orb.State == OrbState.Gold ? "~y~GOLD orb +2" : "~p~Orb +1")}~s~: portal charges {_charges}/{_config.MaxCharges}. Press ~b~{Button}~s~ to open a portal.", 3000);
            if (!_taughtOrb)
            {
                _taughtOrb = true;
                Notification.Show($"~p~Quantum orbs come in entangled pairs~s~ (same number on the map). One of each pair is ~y~gold~s~ (+2 charges), the other plain (+1), and nothing decides which until you grab one. Press ~b~{Button}~s~ to open a portal.");
            }
        }

        /// <summary>The collapse: one shot from a graph-v1 job settles both orbs of the pair at once.</summary>
        void Measure(Orb orb)
        {
            var partner = _orbs.FirstOrDefault(o => o.Pair == orb.Pair && o != orb);
            var shot = _orbPool.Draw();
            if (shot == null)
            {
                // No quantum data at all: be upfront and give a plain orb.
                orb.State = OrbState.Plain;
                Notification.Show("~o~No quantum data~s~: this orb was counted as plain.");
                Log.Info($"orb pair {orb.Pair}: no quantum data, plain");
                return;
            }

            var slot = (orb.Pair - 1) % OrbPairs.PairsPerJob;
            var result = OrbPairs.Resolve(shot, slot, orb.Side);
            orb.State = OrbPairs.HereIsGold(result) ? OrbState.Gold : OrbState.Plain;
            var partnerState = OrbPairs.ThereIsGold(result) ? OrbState.Gold : OrbState.Plain;
            if (partner != null && partner.State == OrbState.Superposed)
            {
                partner.State = partnerState;
                if (partner.Blip != null && partner.Blip.Exists())
                {
                    partner.Blip.Color = partnerState == OrbState.Gold ? BlipColor.Yellow : BlipColor.White;
                    partner.Blip.Name = $"Quantum orb (pair {partner.Pair}, {(partnerState == OrbState.Gold ? "GOLD" : "plain")})";
                    if (partnerState == OrbState.Gold)
                    {
                        partner.Blip.IsFlashing = true;
                        partner.Blip.FlashTimeLeft = 6000;
                    }
                }
            }

            var bits = $"~y~|{shot.Bits.Substring(2 * slot, 2)}>~s~";
            var away = partner != null ? $" {partner.Position.DistanceTo2D(orb.Position):0} m away" : "";
            var proof = $"\n~c~{shot.SourceLabel} · {shot.JobLabel}";
            switch (result)
            {
                case OrbResult.GoldHere:
                    Notification.Show($"~y~GOLD~s~ orb! Pair {orb.Pair} collapsed {bits}\nIts entangled partner{away} just turned ~c~plain~s~.{proof}");
                    break;
                case OrbResult.GoldThere:
                    Notification.Show($"Plain orb. Pair {orb.Pair} collapsed {bits}\nSo its entangled partner{away} just turned ~y~GOLD~s~ (+2).{proof}");
                    break;
                case OrbResult.BothGold:
                    Notification.Show($"~y~BOTH GOLD~s~: pair {orb.Pair} collapsed {bits}\nChip noise broke the entanglement, in your favour.{proof}");
                    break;
                case OrbResult.BothPlain:
                    Notification.Show($"~c~Both plain~s~: pair {orb.Pair} collapsed {bits}\nChip noise broke the entanglement. Real qubits make mistakes.{proof}");
                    break;
            }
            Log.Info($"orb pair {orb.Pair} measured at side {orb.Side}: {result}, shot |{shot.Bits}> slot {slot} job {shot.JobId} shot {shot.Number}/{shot.Total} ({shot.Source}{(shot.Fresh ? ", new job" : "")})");
        }

        void RemoveOrb(Orb orb)
        {
            if (orb.Blip != null && orb.Blip.Exists())
                orb.Blip.Delete();
            _orbs.Remove(orb);
        }

        void DrawOrbs(Vector3 player)
        {
            var bob = 0.3f * (float)Math.Sin(Game.GameTime / 300.0);
            var flicker = (float)(0.7 + 0.3 * Math.Sin(Game.GameTime / 120.0));
            var full = _charges >= _config.MaxCharges;
            foreach (var orb in _orbs)
            {
                if (orb.Position.DistanceTo(player) > OrbDrawDistance)
                    continue;
                var color = full ? Color.FromArgb(90, 160, 160, 160)
                    : orb.State == OrbState.Gold ? GoldColor
                    : orb.State == OrbState.Plain ? PlainColor
                    : Color.FromArgb((int)(OrbColor.A * flicker), OrbColor);
                var size = orb.State == OrbState.Gold ? 1.5f : 1.1f;
                World.DrawMarker(MarkerType.DebugSphere, orb.Position + new Vector3(0f, 0f, 1.6f + bob), Vector3.Zero, Vector3.Zero,
                    new Vector3(size, size, size), color);
                World.DrawMarker(MarkerType.VerticalCylinder, orb.Position - new Vector3(0f, 0f, 0.9f), Vector3.Zero, Vector3.Zero,
                    new Vector3(OrbCollectRadius, OrbCollectRadius, 0.6f), Color.FromArgb(60, color));
            }
        }

        // ---- The Quantum Blur panel: the real blur-v1 image, on screen ----

        /// <summary>
        /// The 32x32 image blur-v1 returned, bottom-right of the screen: you are the green cell at the
        /// bottom, ahead is up, brightness is the chance of exiting there. After a portal, the measured
        /// exit blinks yellow.
        /// </summary>
        void DrawBlurPanel(DateTime now)
        {
            Wavefunction wave = null;
            Cell? landed = null;
            if (_portal != null)
                wave = _portal.Wave;
            else if (_last != null && now - _lastPortalAt < BlurPanelAfterTime)
            {
                wave = _last.Wave;
                landed = _last.Cell;
            }
            if (wave == null)
                return;
            if (wave != _panelWave)
            {
                _panelWave = wave;
                _panelMax = wave.Brightness.Max();
            }

            const float left = 0.79f, top = 0.36f, width = 0.17f;
            var height = width * GTA.UI.Screen.AspectRatio;
            float cw = width / wave.Size, ch = height / wave.Size;
            Rect(left + width / 2, top + height / 2, width + 0.006f, height + 0.006f * GTA.UI.Screen.AspectRatio, Color.FromArgb(200, 10, 5, 25));
            for (var y = 0; y < wave.Size; y++)
                for (var x = 0; x < wave.Size; x++)
                {
                    var b = wave.Brightness[y * wave.Size + x];
                    if (b == 0)
                        continue;
                    var v = (float)Math.Sqrt(b / (double)_panelMax);
                    var color = Color.FromArgb((int)(60 + 195 * v), (int)(110 + 145 * v), (int)(40 + 215 * v * v), 255);
                    Rect(left + (x + 0.5f) * cw, top + (wave.Size - 1 - y + 0.5f) * ch, cw, ch, color);
                }
            Rect(left + (Tunnels.OriginX + 0.5f) * cw, top + (wave.Size - 1 - Tunnels.OriginY + 0.5f) * ch, cw * 1.6f, ch * 1.6f, Color.FromArgb(255, 60, 255, 90));
            if (_portal != null)
                foreach (var exit in _portal.Exits)
                    Rect(left + (Tunnels.OriginX + exit.Cell.Dx + 0.5f) * cw, top + (wave.Size - 1 - (Tunnels.OriginY + exit.Cell.Dy) + 0.5f) * ch, cw * 2f, ch * 2f, Color.FromArgb(230, PairColors[exit.Pair]));
            if (landed != null && Game.GameTime / 200 % 2 == 0)
            {
                var c = landed.Value;
                Rect(left + (Tunnels.OriginX + c.Dx + 0.5f) * cw, top + (wave.Size - 1 - (Tunnels.OriginY + c.Dy) + 0.5f) * ch, cw * 2.2f, ch * 2.2f, Color.FromArgb(255, 255, 220, 40));
            }

            _panelTitle.Position = new PointF(left * GTA.UI.Screen.Width, (top - 0.045f) * GTA.UI.Screen.Height);
            _panelTitle.Caption = $"~p~QUANTUM BLUR~s~ blur-v1 | job {wave.ShortJobId}\n~c~green = you, up = ahead, bright = likely, coloured = the 4 exits";
            _panelTitle.Draw();
        }

        static void Rect(float x, float y, float w, float h, Color c) =>
            Function.Call(Hash.DRAW_RECT, x, y, w, h, (int)c.R, (int)c.G, (int)c.B, (int)c.A, false);

        // ---- The portal ----

        void OpenPortal(Ped player, DateTime now)
        {
            if (_portal != null)
                return;
            if (_charges <= 0)
            {
                Audio.PlaySoundFrontend("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                GTA.UI.Screen.ShowSubtitle("~c~No portal charges.~s~ Drive through a ~p~quantum orb~s~ (purple dots on the map).", 3000);
                return;
            }
            var wave = _pool.Peek(Kind);
            if (wave == null)
            {
                Audio.PlaySoundFrontend("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                GTA.UI.Screen.ShowSubtitle("~r~No quantum data yet~s~: waiting for Atlas. Check the API key in QuantumGetaway.ini.", 3000);
                return;
            }

            _charges--;
            var forward = Forward(player);
            var inCar = player.IsInVehicle();
            var ahead = inCar ? Math.Min(Math.Max(15f + player.CurrentVehicle.Speed * 0.8f, 15f), 45f) : 6f;
            var ground = player.Position + forward * ahead;
            var h = World.GetGroundHeight(new Vector3(ground.X, ground.Y, ground.Z + 5f));
            if (h > 0f && Math.Abs(h - ground.Z) < 6f)
                ground.Z = h;
            else
                ground.Z -= inCar ? 0.5f : 1f;
            _portal = new Portal { Ground = ground, Forward = forward, OpenedAt = now, Wave = wave };
            BuildExits(_portal);
            if (_portal.Exits.Count == 0)
            {
                ClosePortal();
                _charges++;
                GTA.UI.Screen.ShowSubtitle("~o~This wavefunction has no clear exits ahead.~s~ Try again in a moment.", 3000);
                return;
            }

            Audio.PlaySoundFrontend("FocusIn", "HintCamSounds");
            if (_fx.IsLoaded)
                World.CreateParticleEffectNonLooped(_fx, FxName, ground + new Vector3(0f, 0f, 1.5f), Vector3.Zero, 2f, InvertAxisFlags.None);
            if (!_taughtPortal)
            {
                _taughtPortal = true;
                Notification.Show("~p~The exit is in superposition.~s~ Four exit portals opened ahead (and on the map): two entangled pairs, 1A/1B and 2A/2B, with their odds. Drive in: Quantum Blur picks the pair, then the entangled twins pick A or B.");
            }
            Log.Info($"portal opened {ahead:0} m ahead, job {wave.JobId} ({wave.Source}), exits {string.Join(", ", _portal.Exits.Select(e => $"{e.Label} {e.Cell} {e.Chance:0.000}"))}");
        }

        /// <summary>
        /// The four exit portals: the two likeliest, well-separated twin pairs in the Quantum Blur
        /// image. Twin A is left of your path, twin B the mirror spot on the right. Shown odds: the
        /// pair's share of the blur probability, split evenly between its twins.
        /// </summary>
        void BuildExits(Portal portal)
        {
            portal.Options = portal.Wave.ExitOptions(ExitPairCount, ExitMinDy, ExitGap, _rng);
            var total = portal.Options.Sum(o => o.P);
            for (var i = 0; i < portal.Options.Count; i++)
            {
                var pair = portal.Options[i];
                var chance = pair.P / total / 2;
                AddExit(portal, pair.Left, i, $"{i + 1}A", chance);
                AddExit(portal, pair.Right, i, $"{i + 1}B", chance);
            }
        }

        void AddExit(Portal portal, Cell cell, int pair, string label, double chance)
        {
            var spot = CellToWorld(portal.Ground, portal.Forward, cell, _config.CellSize);
            var h = World.GetGroundHeight(new Vector3(spot.X, spot.Y, portal.Ground.Z + 50f));
            spot.Z = (h > 0f ? h : portal.Ground.Z) + 2.5f;
            var blip = World.CreateBlip(spot);
            blip.Sprite = BlipSprite.Standard;
            blip.Color = pair == 0 ? BlipColor.Blue : BlipColor.Pink;
            blip.Scale = 0.85f;
            blip.NumberLabel = pair + 1; // twins share a number
            blip.Name = $"Exit {label} ({GetawayHud.Percent(chance)})";
            portal.Exits.Add(new Exit { Centre = spot, Cell = cell, Pair = pair, Label = label, Chance = chance, Blip = blip });
        }

        void ClosePortal()
        {
            if (_portal == null)
                return;
            foreach (var exit in _portal.Exits)
                if (exit.Blip != null && exit.Blip.Exists())
                    exit.Blip.Delete();
            _portal = null;
        }

        /// <summary>
        /// Two measurements as you drive in: Quantum Blur picks pair 1 or 2 with the odds shown, then
        /// a graph-v1 entangled pair picks twin A or B. Chip noise can say both (a quantum copy of
        /// you comes out of the other twin) or neither (you come out between them).
        /// </summary>
        void GoThrough(Ped player, DateTime now)
        {
            var portal = _portal;
            ClosePortal();
            var wave = _pool.Take(Kind, portal.Wave, out var fresh);
            var origin = player.Position;
            _lastPortalAt = now;
            var blurProof = $"~c~blur-v1 {GetawayHud.Source(wave.Source)}~c~ · {(fresh ? "new" : "earlier")} job {wave.ShortJobId}";

            // Measurement 1 (Quantum Blur): which pair.
            var pick = Wavefunction.Choose(portal.Options, _rng);
            var pair = portal.Options[pick];
            var pairChance = pair.P / portal.Options.Sum(o => o.P);
            var n = pick + 1;

            // Measurement 2 (graph-v1): which twin.
            var exit = pair.Left;
            var label = $"{n}A";
            Cell? copyAt = null;
            string twinText, graphProof = "", twinLog;
            var shot = _orbPool.Draw();
            if (shot == null)
            {
                var right = _rng.Next(2) == 1;
                exit = right ? pair.Right : pair.Left;
                label = right ? $"{n}B" : $"{n}A";
                twinText = "~o~No graph-v1 data~s~: A or B was picked classically.";
                twinLog = "no graph-v1 data, twin picked classically";
            }
            else
            {
                var bits = $"~y~|{shot.Bits.Substring(0, 2)}>~s~";
                var twin = OrbPairs.ResolveTwins(shot);
                switch (twin)
                {
                    case TwinResult.Right:
                        exit = pair.Right;
                        label = $"{n}B";
                        twinText = $"Entangled twins collapsed {bits}: ~b~B~s~. Exit {n}A vanished.";
                        break;
                    case TwinResult.Left:
                        twinText = $"Entangled twins collapsed {bits}: ~b~A~s~. Exit {n}B vanished.";
                        break;
                    case TwinResult.Both:
                        copyAt = pair.Right;
                        twinText = $"Twins collapsed {bits}: ~y~BOTH~s~. Chip noise sent a ~p~quantum copy~s~ of you out of {n}B!";
                        break;
                    default:
                        exit = pair.Middle;
                        label = $"between {n}A and {n}B";
                        twinText = $"Twins collapsed {bits}: ~o~NEITHER~s~. Chip noise dropped you between them.";
                        break;
                }
                graphProof = $"\n~c~graph-v1 {shot.SourceLabel} · {shot.JobLabel}";
                twinLog = $"twins |{shot.Bits.Substring(0, 2)}> {twin} job {shot.JobId} shot {shot.Number}/{shot.Total} ({shot.Source}{(shot.Fresh ? ", new job" : "")})";
            }
            _last = new Landing(wave, new Cell(exit.Dx, exit.Dy, pairChance / 2), fresh);

            var target = CellToWorld(portal.Ground, portal.Forward, exit, _config.CellSize);
            var car = player.IsInVehicle() ? player.CurrentVehicle : null;
            var spot = Teleport(player, target);
            if (spot == null)
            {
                _lastMetres = 0;
                _charges = Math.Min(_charges + 1, _config.MaxCharges);
                Notification.Show($"~o~Exit {label} was blocked~s~: nowhere to come out, so you stayed put. Charge refunded.\n{blurProof}{graphProof}");
                Log.Info($"portal pair {n} ({pair.Side},{pair.Dy}) chance {pairChance:0.000}, {twinLog}: no safe spot near ({target.X:0},{target.Y:0}), refunded");
                return;
            }

            _lastMetres = origin.DistanceTo2D(spot.Value);
            _exitPortal = spot.Value + new Vector3(0f, 0f, 1.5f);
            _exitPortalUntil = now + ExitPortalTime;
            Effects(origin, spot.Value, car != null);
            var police = "";
            if (copyAt != null && car != null && SpawnCopy(car, CellToWorld(portal.Ground, portal.Forward, copyAt.Value, _config.CellSize)))
                police = Game.Player.WantedLevel > 0 ? "\nThe police are now searching around your copy." : "";
            else if (Game.Player.WantedLevel > 0 && _config.ShakePolice)
            {
                // The police keep searching the spot you vanished from.
                Game.Player.WantedCenterPosition = origin;
                police = "\nThe police are still searching where you went in.";
            }
            Notification.Show($"~p~EXIT {label}~s~, {_lastMetres:0} m.\nQuantum Blur picked pair {n} ({GetawayHud.Percent(pairChance)}). {twinText}{police}\n{blurProof}{graphProof}");
            Log.Info($"portal pair {n} ({pair.Side},{pair.Dy}) chance {pairChance:0.000} [options {string.Join(" ", portal.Options.Select(o => $"({o.Side},{o.Dy})={o.P:0.0000}"))}], {twinLog} -> exit {label} {exit}, {_lastMetres:0} m to ({spot.Value.X:0},{spot.Value.Y:0},{spot.Value.Z:0}), blur job {wave.JobId} ({wave.Source}{(fresh ? ", new job" : ", earlier job")}), wanted {Game.Player.WantedLevel}, charges left {_charges}");
        }

        /// <summary>Chip noise said "both": a copy of your car comes out of the other twin and drives off. While you're wanted, the police go after it.</summary>
        bool SpawnCopy(Vehicle car, Vector3 target)
        {
            var node = new OutputArgument();
            var heading = new OutputArgument();
            if (!Function.Call<bool>(Hash.GET_CLOSEST_VEHICLE_NODE_WITH_HEADING, target.X, target.Y, target.Z, node, heading, 1, 3f, 0))
                return false;
            var spot = node.GetResult<Vector3>();
            if (spot.DistanceTo2D(target) > 60f)
                return false;
            var roadHeading = heading.GetResult<float>();
            if (Math.Abs(WrapDegrees(roadHeading - car.Heading)) > 90f)
                roadHeading += 180f;
            var copy = World.CreateVehicle(car.Model, spot + new Vector3(0f, 0f, 0.5f), roadHeading);
            if (copy == null)
                return false;
            copy.Mods.PrimaryColor = car.Mods.PrimaryColor;
            copy.Mods.SecondaryColor = car.Mods.SecondaryColor;
            copy.PlaceOnGround();
            var driver = copy.CreateRandomPedOnSeat(VehicleSeat.Driver);
            if (driver != null)
                driver.Task.CruiseWithVehicle(copy, 40f, DrivingStyle.Rushed);
            copy.ForwardSpeed = car.Speed;
            var blip = copy.AddBlip();
            blip.Color = BlipColor.Purple;
            blip.Name = "Your quantum copy";
            if (_fx.IsLoaded)
                World.CreateParticleEffectNonLooped(_fx, FxName, spot, Vector3.Zero, 2.5f, InvertAxisFlags.None);
            if (Game.Player.WantedLevel > 0)
                Game.Player.WantedCenterPosition = spot;
            _copies.Add((copy, driver, DateTime.UtcNow + CopyLifetime));
            Log.Info($"quantum copy spawned at ({spot.X:0},{spot.Y:0},{spot.Z:0})");
            return true;
        }

        /// <summary>Copies fade back into the ordinary world after a while: the game may then clean them up.</summary>
        void ReleaseCopies(bool all)
        {
            foreach (var c in _copies.Where(c => all || DateTime.UtcNow >= c.Until).ToList())
            {
                if (c.Car != null && c.Car.Exists())
                {
                    c.Car.AttachedBlip?.Delete();
                    c.Car.MarkAsNoLongerNeeded();
                }
                if (c.Driver != null && c.Driver.Exists())
                    c.Driver.MarkAsNoLongerNeeded();
                _copies.Remove(c);
            }
        }

        /// <summary>Where you face: your car's nose, or the camera on foot. Flat, unit length.</summary>
        static Vector3 Forward(Ped player)
        {
            var f = player.IsInVehicle() ? player.CurrentVehicle.ForwardVector : GameplayCamera.ForwardVector;
            f.Z = 0f;
            return f.Length() < 0.01f ? player.ForwardVector : f.Normalized;
        }

        static Vector3 CellToWorld(Vector3 origin, Vector3 forward, Cell cell, float size)
        {
            var right = new Vector3(forward.Y, -forward.X, 0f);
            return origin + forward * (cell.Dy * size) + right * (cell.Dx * size);
        }

        /// <summary>Move you (and your car, keeping its speed) to a drivable/walkable spot near target. Null if there is none.</summary>
        static Vector3? Teleport(Ped player, Vector3 target)
        {
            var ground = World.GetGroundHeight(new Vector3(target.X, target.Y, target.Z + 50f));
            if (ground > 0f)
                target.Z = ground;
            Function.Call(Hash.REQUEST_COLLISION_AT_COORD, target.X, target.Y, target.Z);

            if (player.IsInVehicle())
            {
                var car = player.CurrentVehicle;
                if (car.IsAircraft)
                {
                    // Flying: keep your height above the ground and your velocity.
                    var up = car.HeightAboveGround;
                    var velocity = car.Velocity;
                    car.Position = new Vector3(target.X, target.Y, target.Z + Math.Max(up, 5f));
                    car.Velocity = velocity;
                    return car.Position;
                }
                var node = new OutputArgument();
                var heading = new OutputArgument();
                Vector3 spot;
                float roadHeading;
                if (Function.Call<bool>(Hash.GET_CLOSEST_VEHICLE_NODE_WITH_HEADING, target.X, target.Y, target.Z, node, heading, 1, 3f, 0)
                    && (spot = node.GetResult<Vector3>()).DistanceTo2D(target) < 40f)
                    roadHeading = heading.GetResult<float>();
                else
                {
                    spot = World.GetNextPositionOnStreet(target);
                    if (spot == Vector3.Zero || spot.DistanceTo2D(target) > 60f)
                        return null;
                    roadHeading = car.Heading;
                }
                // Keep driving the same way along the road you land on.
                if (Math.Abs(WrapDegrees(roadHeading - car.Heading)) > 90f)
                    roadHeading += 180f;
                var speed = car.Speed;
                car.Position = spot + new Vector3(0f, 0f, 0.5f);
                car.Heading = roadHeading;
                car.PlaceOnGround();
                car.ForwardSpeed = speed;
                return spot;
            }

            var safe = World.GetSafeCoordForPed(target, true, 16);
            if (safe == Vector3.Zero || safe.DistanceTo2D(target) > 40f)
            {
                safe = World.GetNextPositionOnStreet(target);
                if (safe == Vector3.Zero || safe.DistanceTo2D(target) > 60f)
                    return null;
            }
            player.Position = safe;
            return safe;
        }

        static float WrapDegrees(float d)
        {
            d %= 360f;
            return d > 180f ? d - 360f : d < -180f ? d + 360f : d;
        }

        void Effects(Vector3 from, Vector3 to, bool inVehicle)
        {
            var scale = inVehicle ? 2.5f : 1.2f;
            if (_fx.IsLoaded)
            {
                World.CreateParticleEffectNonLooped(_fx, FxName, from, Vector3.Zero, scale, InvertAxisFlags.None);
                World.CreateParticleEffectNonLooped(_fx, FxName, to, Vector3.Zero, scale, InvertAxisFlags.None);
            }
            Audio.PlaySoundFrontend("FocusOut", "HintCamSounds");
            Audio.PlaySoundFrontend("Hack_Success", "DLC_HEIST_BIOLAB_PREP_HACKING_SOUNDS");
            GameplayCamera.Shake(CameraShake.SmallExplosion, 0.4f);
            GTA.UI.Screen.StartEffect(ScreenEffect.SwitchShortNeutralIn);
            _flashEndsAt = DateTime.UtcNow + FlashTime;
        }

        /// <summary>Every frame: the open portal (swirling rings facing you), its ghost exits, and the exit portal you just left.</summary>
        void DrawPortal(DateTime now)
        {
            var t = Game.GameTime / 1000.0;
            if (_portal != null)
            {
                var centre = _portal.Ground + new Vector3(0f, 0f, 2.2f);
                var pulse = 0.5f * (float)Math.Sin(t * 6);
                DrawRing(centre, 7.5f + pulse, PortalOuter);
                DrawRing(centre, 5.5f - pulse, PortalInner);
                DrawRing(centre, 3.5f + pulse * 0.5f, Color.FromArgb(160, 255, 255, 255));

                var flicker = 0.8f + 0.2f * (float)Math.Sin(t * 9);
                foreach (var exit in _portal.Exits)
                {
                    var color = PairColors[exit.Pair];
                    DrawRing(exit.Centre, 6f - pulse, Color.FromArgb((int)(color.A * flicker), color));
                    DrawRing(exit.Centre, 4f + pulse * 0.5f, Color.FromArgb((int)(140 * flicker), 255, 255, 255));
                    var screen = GTA.UI.Screen.WorldToScreen(exit.Centre + new Vector3(0f, 0f, 4f));
                    if (screen != PointF.Empty)
                    {
                        _exitLabel.Position = screen;
                        _exitLabel.Caption = $"{exit.Label}\n{GetawayHud.Percent(exit.Chance)}";
                        _exitLabel.Color = Color.FromArgb(255, color);
                        _exitLabel.Draw();
                    }
                }
                var left = _config.PortalSeconds - (now - _portal.OpenedAt).TotalSeconds;
                GTA.UI.Screen.ShowSubtitle($"~p~Portal open~s~: drive through it ~c~({Math.Max(0, left):0}s)", 200);
            }
            if (now < _exitPortalUntil)
            {
                var fade = (float)((_exitPortalUntil - now).TotalSeconds / ExitPortalTime.TotalSeconds);
                DrawRing(_exitPortal, 7.5f * fade + 1f, Color.FromArgb((int)(220 * fade), PortalOuter));
                DrawRing(_exitPortal, 5f * fade + 0.5f, Color.FromArgb((int)(200 * fade), PortalInner));
            }
        }

        static void DrawRing(Vector3 centre, float size, Color color) =>
            World.DrawMarker(MarkerType.VerticleCircle, centre, Vector3.Zero, Vector3.Zero, new Vector3(size, size, size), color, faceCamera: true);

        void ShowStatusNoticeOnce()
        {
            var status = (AtlasStatus)_status;
            if (status == _announced || status == AtlasStatus.Checking || status == AtlasStatus.NoKey)
                return;
            _announced = status;
            switch (status)
            {
                case AtlasStatus.Connected:
                    Notification.Show($"~p~Quantum Getaway~s~ loaded. ~g~Atlas connected.~s~ You have {_charges} portal charge{(_charges == 1 ? "" : "s")}: press ~b~L3~s~ (controller) or ~b~{_key}~s~. Purple dots on the map are ~p~quantum orbs~s~ for more.");
                    break;
                case AtlasStatus.InvalidKey:
                    Notification.Show("~p~Quantum Getaway~s~: ~r~Atlas rejected the API key~s~. Check ApiKey in scripts\\QuantumGetaway.ini. Using cached results only.");
                    break;
                default:
                    Notification.Show("~p~Quantum Getaway~s~: ~o~can't reach Atlas~s~. Portals use cached real quantum results (OFFLINE) until it's back.");
                    break;
            }
        }

        void DrawHud(DateTime now)
        {
            var show = _portal != null || now - _loadedAt < StartupHudTime || now - _lastPortalAt < AfterPortalHudTime || Game.Player.WantedLevel > 0;
            if (!show)
                return;
            var orbSource = _orbPool.NextSource(out var chip);
            _hud.Caption = GetawayHud.Line((AtlasStatus)_status, Button, _charges, _config.MaxCharges, _portal != null, _last, _lastMetres, GetawayHud.Orbs(orbSource, chip));
            _hud.Draw();
        }

        void OnAborted(object sender, EventArgs e)
        {
            ClosePortal();
            ReleaseCopies(true);
            foreach (var orb in _orbs.ToList())
                RemoveOrb(orb);
            GTA.UI.Screen.StopEffect(ScreenEffect.SwitchShortNeutralIn);
            _pool.Dispose();
            _orbPool.Dispose();
            Log.Info("unloaded");
        }
    }
}
