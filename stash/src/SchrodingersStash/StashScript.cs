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
using SchrodingersStash.Core;
using SchrodingersStash.Core.Atlas;
using SchrodingersStash.Core.Quantum;

namespace SchrodingersStash
{
    /// <summary>
    /// Schrödinger's Stash: press the start key and six cash stashes appear as ghost blips, in three
    /// entangled pairs on opposite sides of you. Each stash is cash or empty in superposition; partners
    /// are anti-correlated (ZZ=-1). Reaching one measures its pair with a new Atlas job: cash here means
    /// the partner across town is empty, an empty stash (and its alarm) means the partner is full.
    /// </summary>
    public class StashScript : Script
    {
        const string Sender = "Erwin";
        const float ObserveDistance = 20f;
        const float MarkerDistance = 150f;
        const float MinSpotDistance = 250f;
        const float MinSiteGap = 150f;
        static readonly TimeSpan LogicInterval = TimeSpan.FromMilliseconds(250);
        static readonly TimeSpan StartupHudTime = TimeSpan.FromSeconds(15);
        static readonly TimeSpan PulseTime = TimeSpan.FromMilliseconds(350);
        static readonly Color SuperposedColor = Color.FromArgb(120, 190, 90, 255);
        static readonly Color CashColor = Color.FromArgb(150, 60, 220, 90);

        enum SiteState { Superposed, Cash, Empty, Collected }

        sealed class Site
        {
            public Vector3 Position;
            public int Pair;
            public int Side;
            public SiteState State;
            public Blip Blip;
        }

        sealed class Run
        {
            public readonly List<Site> Sites = new List<Site>();
            public DateTime EndsAt;
            public int Cash;
            public Shot LastShot;
            public int LastPair = -1;
            public readonly HashSet<string> Jobs = new HashSet<string>();
        }

        readonly Config _config;
        readonly StashPool _pool;
        readonly Keys _startKey;
        readonly Random _rng = new Random();
        readonly TextElement _hud = new TextElement("", new PointF(8f, 26f), 0.32f, Color.White, GTA.UI.Font.ChaletLondon, Alignment.Left, true, true);
        readonly DateTime _loadedAt = DateTime.UtcNow;
        int _status = (int)AtlasStatus.NoKey; // written by the background key check
        AtlasStatus _announced = AtlasStatus.NoKey;
        Run _run;
        bool _noiseTexted;
        DateTime _nextLogic;
        DateTime _pulseEndsAt; // the collapse screen pulse; the effect never ends by itself
        bool _staleBlurCleared;

        public StashScript()
        {
            var dataDir = Path.Combine(BaseDirectory, "SchrodingersStash");
            Directory.CreateDirectory(dataDir);
            Log.Init(Path.Combine(dataDir, "SchrodingersStash.log"));

            _config = Config.Load(Path.Combine(BaseDirectory, "SchrodingersStash.ini"));
            if (!Enum.TryParse(_config.StartKey, true, out _startKey))
                _startKey = Keys.F9;
            var client = _config.HasApiKey ? new AtlasClient(_config.ApiBase, _config.ApiKey) : null;
            _pool = new StashPool(client, _config, Path.Combine(dataDir, "cache"));

            Log.Info($"loaded: api key {(_config.HasApiKey ? "set" : "MISSING")}, mode {_config.Mode} {_config.Backend}, start key {_startKey}");
            if (client != null)
                CheckKey(client);
            else
                Notification.Show("~p~Schrodinger's Stash~s~: ~r~no Atlas API key~s~ in scripts\\SchrodingersStash.ini. Using cached results only.");

            Interval = 0;
            Tick += OnTick;
            KeyDown += OnKeyDown;
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

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != _startKey)
                return;
            if (_run != null)
                End("~c~Stash run abandoned.~s~ The unopened stashes stay a mystery forever.");
            else if (Function.Call<bool>(Hash.GET_MISSION_FLAG))
                Notification.Show($"~p~Schrodinger's Stash~s~: finish this story mission first, then press {_startKey}.");
            else if (!Game.Player.Character.IsDead)
                Start();
        }

        void OnTick(object sender, EventArgs e)
        {
            if (!_staleBlurCleared)
            {
                // An older build left the collapse blur running; clear it once after loading.
                GTA.UI.Screen.StopEffect(ScreenEffect.SwitchHudIn);
                _staleBlurCleared = true;
            }
            if (DateTime.UtcNow >= _nextLogic)
            {
                _nextLogic = DateTime.UtcNow + LogicInterval;
                Logic();
            }
            if (_pulseEndsAt != DateTime.MinValue && DateTime.UtcNow >= _pulseEndsAt)
                StopPulse();
            DrawMarkers();
            DrawHud();
        }

        void Logic()
        {
            ShowStatusNoticeOnce();
            while (_pool.TryDequeueNotice(out var notice))
                Notification.Show(notice);
            // Keep one new job waiting, so every pair gets its own measurement without a wait.
            _pool.Prefetch();

            if (_run == null)
                return;

            var player = Game.Player.Character;
            if (player.IsDead || Function.Call<bool>(Hash.IS_PLAYER_BEING_ARRESTED, Game.Player, true))
            {
                End("~r~Stash run over.~s~ Hard to collect cash from the back of an ambulance.");
                return;
            }
            if (DateTime.UtcNow >= _run.EndsAt)
            {
                End("~c~Time's up.~s~ The unopened stashes decohered and vanished.");
                return;
            }

            foreach (var site in _run.Sites)
                if ((site.State == SiteState.Superposed || site.State == SiteState.Cash) && player.Position.DistanceTo(site.Position) < ObserveDistance)
                {
                    Open(site);
                    break;
                }

            if (_run != null && _run.Sites.All(s => s.State == SiteState.Empty || s.State == SiteState.Collected))
                End($"~g~Stash run complete:~s~ ${_run.Cash:N0} from {Stash.Pairs} entangled pairs, measured by {_run.Jobs.Count} Atlas job{(_run.Jobs.Count == 1 ? "" : "s")}.");
        }

        void Start()
        {
            _run = new Run { EndsAt = DateTime.UtcNow.AddMinutes(_config.RunMinutes) };
            var origin = Game.Player.Character.Position;
            var baseAngle = _rng.NextDouble() * Math.PI * 2;
            for (var pair = 0; pair < Stash.Pairs; pair++)
            {
                // Partners sit on opposite sides of you: measuring one settles the other across town.
                var angle = baseAngle + pair * (Math.PI * 2 / Stash.Pairs) + (_rng.NextDouble() - 0.5) * 0.5;
                var distance = 350f + (float)_rng.NextDouble() * 450f;
                for (var side = 0; side < 2; side++)
                {
                    var spot = FindSpot(origin, angle + side * Math.PI, distance);
                    var site = new Site { Position = spot, Pair = pair, Side = side, State = SiteState.Superposed };
                    site.Blip = World.CreateBlip(spot);
                    site.Blip.Color = BlipColor.Purple;
                    site.Blip.Alpha = 140;
                    site.Blip.Scale = 0.9f;
                    site.Blip.NumberLabel = pair + 1; // partners show the same number on the map
                    site.Blip.Name = $"Superposed stash (pair {pair + 1})";
                    _run.Sites.Add(site);
                }
            }

            Notification.Show($"~p~Stash run~s~: 6 stashes in 3 entangled pairs (same ~p~number~s~ = partners). One of each pair is full, and nobody knows which until you look. {_config.RunMinutes:0} min.");
            Text("Six bags, three pairs. Each pair is entangled: one full, one empty. Which one? Not even I know. The universe hasn't decided yet. Go look.");
            Log.Info("run started: " + string.Join(", ", _run.Sites.Select(s => $"p{s.Pair + 1}{(s.Side == 0 ? "a" : "b")} ({s.Position.X:0},{s.Position.Y:0}) {s.Position.DistanceTo(origin):0}m")));
        }

        /// <summary>A street spot near origin + direction × distance, away from you and the other stashes.</summary>
        Vector3 FindSpot(Vector3 origin, double angle, float distance)
        {
            var fallback = Vector3.Zero;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var a = angle + (attempt % 2 == 0 ? 1 : -1) * (attempt / 2) * 0.35;
                var target = origin + new Vector3((float)Math.Cos(a), (float)Math.Sin(a), 0f) * distance;
                var spot = World.GetNextPositionOnStreet(target);
                if (spot == Vector3.Zero)
                    continue;
                if (fallback == Vector3.Zero)
                    fallback = spot;
                var clear = _run.Sites.All(s => s.Position.DistanceTo(spot) > MinSiteGap);
                if (spot.DistanceTo(origin) > MinSpotDistance && spot.DistanceTo(target) < 300f && clear)
                    return spot;
            }
            return fallback != Vector3.Zero ? fallback : World.GetNextPositionOnStreet(origin + new Vector3(0f, distance, 0f));
        }

        void Open(Site site)
        {
            if (site.State == SiteState.Superposed)
                Measure(site);
            if (site.State == SiteState.Cash)
                Collect(site);
        }

        /// <summary>The collapse: one shot from a new Atlas job settles both stashes of the pair at once.</summary>
        void Measure(Site site)
        {
            var partner = _run.Sites.First(s => s.Pair == site.Pair && s != site);
            var shot = _pool.Draw();
            PairResult result;
            if (shot == null)
            {
                // No quantum data at all: be upfront and resolve this pair classically.
                result = _rng.Next(2) == 0 ? PairResult.CashHere : PairResult.CashThere;
                Notification.Show("~o~No quantum data~s~: this pair was resolved classically.");
            }
            else
            {
                result = Stash.Resolve(shot, site.Pair, site.Side);
                _run.LastShot = shot;
                _run.LastPair = site.Pair;
                _run.Jobs.Add(shot.JobId);
            }

            site.State = result == PairResult.CashHere || result == PairResult.Jackpot ? SiteState.Cash : SiteState.Empty;
            partner.State = result == PairResult.CashThere || result == PairResult.Jackpot ? SiteState.Cash : SiteState.Empty;

            Audio.PlaySoundFrontend("Hack_Success", "DLC_HEIST_BIOLAB_PREP_HACKING_SOUNDS");
            GTA.UI.Screen.StartEffect(ScreenEffect.SwitchHudIn);
            _pulseEndsAt = DateTime.UtcNow + PulseTime;

            var n = site.Pair + 1;
            var bits = shot != null ? $" ~y~|{shot.Bits.Substring(2 * site.Pair, 2)}>~s~" : "";
            var proof = shot != null ? $"\n~c~{shot.SourceLabel} · {shot.JobLabel}" : "";
            switch (result)
            {
                case PairResult.CashHere:
                    Notification.Show($"~g~CASH!~s~ Pair {n} collapsed{bits}\nIts partner across town just became ~r~empty~s~. You never went near it.{proof}");
                    break;
                case PairResult.CashThere:
                    Notification.Show($"~r~Empty~s~, and the alarm tripped. Pair {n} collapsed{bits}\nSo its partner is ~g~guaranteed full~s~. Route set.{proof}");
                    break;
                case PairResult.Jackpot:
                    Notification.Show($"~y~JACKPOT:~s~ both stashes in pair {n} are full{bits}\nNoise broke the entanglement. Go get the other one.{proof}");
                    NoiseText("Both full? That's not supposed to happen. Real qubits are noisy and you just found the noise. Don't tell anyone.");
                    break;
                case PairResult.Bust:
                    Notification.Show($"~r~BUST:~s~ both stashes in pair {n} are empty{bits}\nNoise broke the entanglement. And the alarm tripped.{proof}");
                    NoiseText("Both empty. That's hardware noise, kid. Real quantum chips make mistakes. Today it was yours.");
                    break;
            }
            Log.Info($"pair {n} measured at side {site.Side}: {result}, shot {(shot == null ? "none (classical)" : $"|{shot.Bits}> job {shot.JobId} ({shot.Source}{(shot.Fresh ? ", new job" : ", earlier job")})")}");

            if (site.State == SiteState.Empty)
                TripAlarm();
            Settle(site);
            Settle(partner);
        }

        /// <summary>Update a measured stash's blip: full ones turn green (with a route if far), empty ones vanish.</summary>
        void Settle(Site site)
        {
            if (site.State == SiteState.Empty)
            {
                RemoveBlip(site);
                return;
            }
            if (site.Blip == null)
                return;
            site.Blip.Color = BlipColor.Green;
            site.Blip.Alpha = 255;
            site.Blip.Name = $"Stash: CASH (pair {site.Pair + 1})";
            site.Blip.ShowRoute = Game.Player.Character.Position.DistanceTo(site.Position) > ObserveDistance;
        }

        static void RemoveBlip(Site site)
        {
            if (site.Blip != null && site.Blip.Exists())
                site.Blip.Delete();
            site.Blip = null;
        }

        void Collect(Site site)
        {
            site.State = SiteState.Collected;
            RemoveBlip(site);
            Game.Player.Money += _config.CashPerStash;
            _run.Cash += _config.CashPerStash;
            Audio.PlaySoundFrontend("PICK_UP", "HUD_FRONTEND_DEFAULT_SOUNDSET");
            Notification.Show($"~g~+${_config.CashPerStash:N0}~s~ collected (pair {site.Pair + 1}).");
            Log.Info($"collected pair {site.Pair + 1} side {site.Side}: total ${_run.Cash}");
        }

        void TripAlarm()
        {
            if (_config.AlarmStars > Game.Player.WantedLevel)
                Game.Player.WantedLevel = _config.AlarmStars;
        }

        void End(string message)
        {
            if (_run == null)
                return;
            foreach (var site in _run.Sites)
                RemoveBlip(site);
            Notification.Show(message);
            if (_run.Cash > 0)
                Text($"${_run.Cash:N0}. Not bad for money that didn't exist until you looked at it.");
            Log.Info($"run ended: ${_run.Cash}, jobs {string.Join(", ", _run.Jobs)}");
            _run = null;
        }

        void Text(string text)
        {
            if (!_config.PhoneTexts)
                return;
            Audio.PlaySoundFrontend("Text_Arrive_Tone", "Phone_SoundSet_Default");
            Notification.Show(NotificationIcon.HumanDefault, Sender, "Stash run", text);
        }

        void NoiseText(string text)
        {
            if (_noiseTexted)
                return;
            _noiseTexted = true;
            Text(text);
        }

        /// <summary>Every frame: a flickering purple column over superposed stashes nearby, green over full ones.</summary>
        void DrawMarkers()
        {
            if (_run == null)
                return;
            var player = Game.Player.Character.Position;
            var flicker = (int)(60 + 50 * Math.Sin(Game.GameTime / 150.0));
            foreach (var site in _run.Sites)
            {
                if (site.State != SiteState.Superposed && site.State != SiteState.Cash)
                    continue;
                if (site.Position.DistanceTo(player) > MarkerDistance)
                    continue;
                var color = site.State == SiteState.Cash ? CashColor : Color.FromArgb(flicker, SuperposedColor);
                World.DrawMarker(MarkerType.VerticalCylinder, site.Position - new Vector3(0f, 0f, 1f), Vector3.Zero, Vector3.Zero,
                    new Vector3(ObserveDistance * 0.3f, ObserveDistance * 0.3f, 3f), color);
                World.DrawMarker(MarkerType.UpsideDownCone, site.Position + new Vector3(0f, 0f, 3f), Vector3.Zero, Vector3.Zero,
                    new Vector3(1f, 1f, 1f), color, bobUpAndDown: true, faceCamera: true);
            }
        }

        void ShowStatusNoticeOnce()
        {
            var status = (AtlasStatus)_status;
            if (status == _announced || status == AtlasStatus.Checking || status == AtlasStatus.NoKey)
                return;
            _announced = status;
            switch (status)
            {
                case AtlasStatus.Connected:
                    Notification.Show($"~p~Schrodinger's Stash~s~ loaded. ~g~Atlas connected.~s~ Press ~b~{_startKey}~s~ to start a stash run.");
                    break;
                case AtlasStatus.InvalidKey:
                    Notification.Show("~p~Schrodinger's Stash~s~: ~r~Atlas rejected the API key~s~. Check ApiKey in scripts\\SchrodingersStash.ini. Using cached results only.");
                    break;
                default:
                    Notification.Show("~p~Schrodinger's Stash~s~: ~o~can't reach Atlas~s~. Replaying cached real quantum results (OFFLINE) until it's back.");
                    break;
            }
        }

        void DrawHud()
        {
            if (_run == null && DateTime.UtcNow - _loadedAt > StartupHudTime)
                return;
            var pairsLeft = _run == null ? 0 : _run.Sites.Where(s => s.State == SiteState.Superposed).Select(s => s.Pair).Distinct().Count();
            _hud.Caption = StashHud.Line((AtlasStatus)_status, _run != null, _run?.Cash ?? 0, pairsLeft, _run?.LastShot, _run?.LastPair ?? -1, _startKey.ToString());
            _hud.Draw();
        }

        void StopPulse()
        {
            if (_pulseEndsAt == DateTime.MinValue)
                return;
            GTA.UI.Screen.StopEffect(ScreenEffect.SwitchHudIn);
            _pulseEndsAt = DateTime.MinValue;
        }

        void OnAborted(object sender, EventArgs e)
        {
            StopPulse();
            if (_run != null)
                foreach (var site in _run.Sites)
                    RemoveBlip(site);
            _run = null;
            _pool.Dispose();
            Log.Info("unloaded");
        }
    }
}
