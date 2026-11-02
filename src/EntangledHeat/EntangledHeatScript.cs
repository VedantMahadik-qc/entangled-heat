using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EntangledHeat.Core;
using EntangledHeat.Core.Atlas;
using EntangledHeat.Core.Quantum;
using GTA;
using GTA.Native;
using GTA.UI;

namespace EntangledHeat
{
    /// <summary>
    /// Entry point. Ticks every frame to draw the HUD and entanglement threads; game logic runs every
    /// 250 ms and only reads ready quantum shots. All Atlas network work happens on background tasks.
    /// </summary>
    public class EntangledHeatScript : Script
    {
        static readonly TimeSpan LogicInterval = TimeSpan.FromMilliseconds(250);
        static readonly TimeSpan StartupHudTime = TimeSpan.FromSeconds(15);

        readonly Config _config;
        readonly bool _hasClient;
        readonly QuantumPool _pool;
        readonly HeatDirector _heat;
        readonly Hideouts _hideouts;
        readonly SearchCordon _search;
        readonly Story _story = new Story();
        readonly Keys _toggleKey;
        readonly TextElement _hud = new TextElement("", new PointF(8f, 6f), 0.32f, Color.White, GTA.UI.Font.ChaletLondon, Alignment.Left, true, true);
        readonly DateTime _loadedAt = DateTime.UtcNow;
        int _status = (int)AtlasStatus.NoKey; // written by the background key check
        AtlasStatus _announced = AtlasStatus.NoKey;
        bool _quantumOn = true;
        bool _missionPaused;
        int _lastWanted;
        int _ibmArrivalsSeen;
        DateTime _nextLogic;
        DateTime _nextCollapse;
        bool _staleBlurCleared;

        public EntangledHeatScript()
        {
            var dataDir = Path.Combine(BaseDirectory, "EntangledHeat");
            Directory.CreateDirectory(dataDir);
            Log.Init(Path.Combine(dataDir, "EntangledHeat.log"));

            _config = Config.Load(Path.Combine(BaseDirectory, "EntangledHeat.ini"));
            if (!Enum.TryParse(_config.ToggleKey, true, out _toggleKey))
                _toggleKey = Keys.F10;
            var client = _config.HasApiKey ? new AtlasClient(_config.ApiBase, _config.ApiKey) : null;
            _hasClient = client != null;
            _pool = new QuantumPool(client, _config, Path.Combine(dataDir, "cache"));
            _heat = new HeatDirector(_pool, OnStory);
            _hideouts = new Hideouts(_pool, OnStory);
            _search = new SearchCordon(_pool, OnStory);

            Log.Info($"loaded: api key {(_config.HasApiKey ? "set" : "MISSING")}, IBM from {_config.IbmFromWantedLevel} stars: {_config.UseIbm}, toggle {_toggleKey}");
            if (client != null)
                CheckKey(client);
            else
                Notification.Show("~p~Entangled Heat~s~: ~r~no Atlas API key~s~ in scripts\\EntangledHeat.ini. Using cached results only.");

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
            if (e.KeyCode != _toggleKey)
                return;
            _quantumOn = !_quantumOn;
            if (!_quantumOn)
                ReleaseAll("quantum police switched off");
            _nextCollapse = DateTime.MinValue; // collapse straight away when switched back on
            Notification.Show(_quantumOn
                ? _missionPaused
                    ? "~p~Quantum police ON~s~, but paused until this story mission ends."
                    : "~p~Quantum police ON~s~: cars are entangled qubits again."
                : $"~c~Quantum police OFF~s~: normal GTA police. Press {_toggleKey} to switch back.");
            Log.Info($"quantum police {(_quantumOn ? "on" : "off")}");
        }

        void OnTick(object sender, EventArgs e)
        {
            if (!_staleBlurCleared)
            {
                // An older build left the collapse blur running; clear it once after loading.
                GTA.UI.Screen.StopEffect(ScreenEffect.SwitchHudIn);
                _staleBlurCleared = true;
            }
            var wanted = Game.Player.WantedLevel;
            if (DateTime.UtcNow >= _nextLogic)
            {
                _nextLogic = DateTime.UtcNow + LogicInterval;
                Logic(wanted);
            }
            if (_quantumOn)
                _heat.Draw();
            DrawHud(wanted);
        }

        void Logic(int wanted)
        {
            ShowStatusNoticeOnce();
            while (_pool.TryDequeueNotice(out var notice))
                Notification.Show(notice);
            if (_pool.IbmArrivals != _ibmArrivalsSeen)
            {
                _ibmArrivalsSeen = _pool.IbmArrivals;
                OnStory(StoryEvent.IbmArrived);
            }

            UpdateMissionPause();
            if (!_quantumOn || _missionPaused)
            {
                _lastWanted = wanted;
                return;
            }

            if (wanted > 0)
            {
                if (_lastWanted == 0)
                    OnStory(StoryEvent.FirstWanted);
                if (wanted >= 5)
                    OnStory(StoryEvent.FiveStars);
                if (_hasClient && _config.UseIbm && wanted >= _config.IbmFromWantedLevel)
                    OnStory(StoryEvent.IbmRequested);

                _pool.Prefetch(wanted);
                if (wanted < QuantumPool.MaxWantedLevel)
                    _pool.Prefetch(wanted + 1);

                if (_config.EnableHeatSystem)
                {
                    if (wanted != _lastWanted || DateTime.UtcNow >= _nextCollapse)
                    {
                        _heat.Collapse(wanted);
                        _nextCollapse = DateTime.UtcNow.AddSeconds(_config.CollapseIntervalSeconds);
                    }
                    _heat.Update();
                }

                if (_config.EnableSearchGrid)
                    _search.Update(wanted);

                if (_config.EnableSuperposedHideouts)
                    _hideouts.Update(wanted);
            }
            else
            {
                // Not wanted: keep a job ready for the first collapse so it never falls back to the cache.
                _pool.Prefetch(1);
                _pool.Prefetch(2);
                _pool.PrefetchGrid(1);
            }

            if (wanted == 0 && _lastWanted > 0)
            {
                ReleaseAll("wanted level cleared");
                var player = Game.Player.Character;
                if (!player.IsDead && !Function.Call<bool>(Hash.IS_PLAYER_BEING_ARRESTED, Game.Player, true))
                    OnStory(StoryEvent.Escaped);
            }

            _lastWanted = wanted;
        }

        /// <summary>
        /// Story missions script their own police (the prologue chase, heists...). While the game's mission
        /// flag is set, hand everything back to vanilla AI so the mod never breaks a mission.
        /// </summary>
        void UpdateMissionPause()
        {
            var onMission = _config.PauseDuringMissions && Function.Call<bool>(Hash.GET_MISSION_FLAG);
            if (onMission == _missionPaused)
                return;
            _missionPaused = onMission;
            Log.Info($"story mission {(onMission ? "started: quantum police paused" : "ended: quantum police resumed")}");
            if (!_quantumOn)
                return;
            if (onMission)
            {
                ReleaseAll("story mission");
                Notification.Show("~p~Quantum police paused~s~ for this story mission. They're back when it ends.");
            }
            else
            {
                _nextCollapse = DateTime.MinValue;
                Notification.Show("~p~Quantum police back~s~: mission over, the cops are entangled again.");
            }
        }

        /// <summary>Story beats from any feature: a phone text from Uncle Shor and/or a one-time help tip.</summary>
        void OnStory(StoryEvent e)
        {
            if (_config.Tips)
            {
                var tip = _story.Tip(e);
                if (tip != null)
                    GTA.UI.Screen.ShowHelpText(tip, 7000, false, false); // not System.Windows.Forms.Screen
            }
            if (_config.PhoneTexts)
            {
                var text = _story.Text(e, DateTime.UtcNow);
                if (text != null)
                {
                    Audio.PlaySoundFrontend("Text_Arrive_Tone", "Phone_SoundSet_Default");
                    Notification.Show(NotificationIcon.HumanDefault, Story.Sender, "Re: your heat", text);
                }
            }
        }

        void ReleaseAll(string reason)
        {
            _heat.Release();
            _search.End(reason);
            _hideouts.Clear();
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
                    Notification.Show($"~p~Entangled Heat~s~ loaded. ~g~Atlas connected.~s~ Police are now a quantum graph state. ({_toggleKey}: on/off)");
                    break;
                case AtlasStatus.InvalidKey:
                    Notification.Show("~p~Entangled Heat~s~: ~r~Atlas rejected the API key~s~. Check ApiKey in scripts\\EntangledHeat.ini. Using cached results only.");
                    break;
                default:
                    Notification.Show("~p~Entangled Heat~s~: ~o~can't reach Atlas~s~. Replaying cached real quantum results (OFFLINE) until it's back.");
                    break;
            }
        }

        void DrawHud(int wanted)
        {
            if (wanted == 0 && DateTime.UtcNow - _loadedAt > StartupHudTime)
                return;
            _hud.Caption = HudText.Line((AtlasStatus)_status, wanted, _heat.LastShot ?? _search.LastShot, _quantumOn, _toggleKey.ToString(), _missionPaused);
            _hud.Draw();
        }

        void OnAborted(object sender, EventArgs e)
        {
            ReleaseAll("script unloaded");
            _pool.Dispose();
            Log.Info("unloaded");
        }
    }
}
