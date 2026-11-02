using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EntangledHeat.Core.Atlas;
using EntangledHeat.Core.Heat;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EntangledHeat.Core.Quantum
{
    /// <summary>
    /// Pre-fetched real shots: heat-graph jobs per wanted level (graph-v1) and search-grid jobs
    /// (labyrinth-v1). With FreshJobPerCollapse every collapse is measured from its own new Atlas
    /// job: one job waits ready per slot, the next collapse takes it and the next job starts.
    /// All Atlas calls run on background tasks; the game thread only calls Prefetch / Draw /
    /// TryDequeueNotice, which never block.
    /// </summary>
    public sealed class QuantumPool : IDisposable
    {
        public const int MaxWantedLevel = 5;
        static readonly TimeSpan SimTimeout = TimeSpan.FromMinutes(2);
        static readonly TimeSpan IbmTimeout = TimeSpan.FromHours(3);
        static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(30);
        static readonly TimeSpan IbmFailureBackoff = TimeSpan.FromMinutes(5);

        /// <summary>One kind of job: its request, its decks and its fetch state.</summary>
        sealed class Slot
        {
            public string Name;   // cache file name and log tag, e.g. "heat-L3"
            public string Label;  // for the HUD, e.g. "3-star heat"
            public string Engine;
            public int Qubits;
            public Func<string, JObject> Request;
            public ShotDeck ReadySim;  // finished job no collapse has used yet
            public ShotDeck ReadyIbm;
            public ShotDeck Last;      // job behind the latest collapse; its other shots fill in until the next job lands
            public ShotDeck Offline;
            public bool SimBusy;
            public bool IbmBusy;
            public bool IbmArrived;
            public DateTime RetryAfter;
            public DateTime IbmRetryAfter;
        }

        readonly IAtlasClient _client;
        readonly Config _config;
        readonly string _cacheDir;
        readonly object _gate = new object();
        readonly Random _rng = new Random();
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        readonly ConcurrentQueue<string> _notices = new ConcurrentQueue<string>();
        readonly List<Task> _inFlight = new List<Task>();

        readonly Slot[] _heat = new Slot[MaxWantedLevel + 1];
        readonly Slot _grid;
        int _ibmArrivals;
        int _jobsCompleted;

        /// <summary>How many IBM hardware decks have arrived this session. The game polls it to react.</summary>
        public int IbmArrivals => Volatile.Read(ref _ibmArrivals);

        /// <summary>Atlas jobs completed this session (simulator and hardware).</summary>
        public int JobsCompleted => Volatile.Read(ref _jobsCompleted);

        /// <param name="client">Null when there is no API key: only cached results are used.</param>
        public QuantumPool(IAtlasClient client, Config config, string cacheDir)
        {
            _client = client;
            _config = config;
            _cacheDir = cacheDir;
            Directory.CreateDirectory(cacheDir);

            for (var level = 1; level <= MaxWantedLevel; level++)
            {
                var l = level;
                _heat[level] = NewSlot($"heat-L{l}", $"{l}-star heat", EngineIds.Graph, HeatRequests.Units, mode => HeatRequests.HeatGraph(l, mode));
            }
            _grid = NewSlot("grid", "search grid", EngineIds.Labyrinth, SearchGrid.Blocks, mode => HeatRequests.SearchGrid(SearchGrid.Rows, SearchGrid.Cols, mode));
        }

        Slot NewSlot(string name, string label, string engine, int qubits, Func<string, JObject> request)
        {
            var slot = new Slot { Name = name, Label = label, Engine = engine, Qubits = qubits, Request = request };
            slot.Offline = LoadCache(slot);
            return slot;
        }

        /// <summary>Start any heat-graph jobs this wanted level needs. Cheap to call every tick.</summary>
        public void Prefetch(int wantedLevel)
        {
            var level = Clamp(wantedLevel);
            Prefetch(_heat[level], level);
        }

        /// <summary>Start a search-grid job if the deck is low. IBM follows the same star threshold as heat.</summary>
        public void PrefetchGrid(int wantedLevel) => Prefetch(_grid, Clamp(wantedLevel));

        /// <summary>Next real heat shot for this wanted level, or null if there is no quantum data at all.</summary>
        public Shot Draw(int wantedLevel) => Draw(_heat[Clamp(wantedLevel)]);

        /// <summary>Next real search-grid shot (16 bits), or null if there is no quantum data at all.</summary>
        public Shot DrawGrid() => Draw(_grid);

        /// <summary>Messages for the HUD, e.g. "IBM hardware result arrived". Call from the game thread.</summary>
        public bool TryDequeueNotice(out string notice) => _notices.TryDequeue(out notice);

        /// <summary>For tests: wait for all background jobs started so far.</summary>
        public Task WhenIdleAsync()
        {
            lock (_gate)
                return Task.WhenAll(_inFlight.ToArray());
        }

        void Prefetch(Slot slot, int level)
        {
            if (_client == null)
                return;

            var now = DateTime.UtcNow;
            lock (_gate)
            {
                // Fresh mode keeps one unused job waiting; otherwise refill only as the current job runs low.
                var needSim = slot.ReadySim == null &&
                    (_config.FreshJobPerCollapse || slot.Last == null || slot.Last.RunningLow);
                if (needSim && !slot.SimBusy && now >= slot.RetryAfter)
                {
                    slot.SimBusy = true;
                    Track(FetchAsync(slot, "emu", ShotSource.Simulator));
                }

                // Hardware: at most one job in flight per slot. Without fresh mode, one per session.
                var needIbm = _config.UseIbm && level >= _config.IbmFromWantedLevel && slot.ReadyIbm == null &&
                    (_config.FreshJobPerCollapse || !slot.IbmArrived);
                if (needIbm && !slot.IbmBusy && now >= slot.IbmRetryAfter)
                {
                    slot.IbmBusy = true;
                    Track(FetchAsync(slot, "qpu", ShotSource.IbmHardware));
                }
            }
        }

        Shot Draw(Slot slot)
        {
            lock (_gate)
            {
                // An unused job wins (hardware first); otherwise reuse the job behind the last collapse.
                var ready = slot.ReadyIbm ?? slot.ReadySim;
                if (ready != null)
                {
                    if (ready == slot.ReadyIbm)
                        slot.ReadyIbm = null;
                    else
                        slot.ReadySim = null;
                    slot.Last = ready;
                }
                var shot = slot.Last?.Draw();
                if (shot != null)
                    return shot;
                if (!_config.OfflineFallback)
                    return null;
                shot = slot.Offline?.Draw();
                if (shot == null && slot.Offline != null)
                {
                    // Replaying the cache again is still real data, just reused while offline.
                    slot.Offline = LoadCache(slot);
                    shot = slot.Offline?.Draw();
                }
                return shot;
            }
        }

        async Task FetchAsync(Slot slot, string mode, ShotSource source)
        {
            try
            {
                var request = slot.Request(mode);
                var timeout = source == ShotSource.IbmHardware ? IbmTimeout : SimTimeout;
                var result = await _client.RunAsync(slot.Engine, request, timeout, _cts.Token).ConfigureAwait(false);
                var counts = MeasurementParser.ParseCounts(result.Body, slot.Qubits);
                if (counts.Count == 0)
                    throw new AtlasException($"job {result.JobId}: no measurements found in result");

                var deck = new ShotDeck(counts, result.JobId, source, NewRng());
                bool firstIbm;
                lock (_gate)
                {
                    if (source == ShotSource.IbmHardware)
                        slot.ReadyIbm = deck;
                    else
                        slot.ReadySim = deck;
                    firstIbm = source == ShotSource.IbmHardware && !slot.IbmArrived;
                    if (source == ShotSource.IbmHardware)
                        slot.IbmArrived = true;
                }
                SaveCache(slot, source, result.JobId, counts);
                var jobs = Interlocked.Increment(ref _jobsCompleted);
                Log.Info($"{slot.Engine} {mode} {slot.Name}: job {result.JobId}, {deck.Total} shots, {result.Elapsed.TotalSeconds:0.0}s (#{jobs} this session)");
                if (source == ShotSource.IbmHardware)
                {
                    Interlocked.Increment(ref _ibmArrivals);
                    // Announce hardware once per slot; later hardware jobs show in each collapse notice.
                    if (firstIbm)
                        _notices.Enqueue($"~p~Atlas~s~: {slot.Label} now measured on ~y~IBM quantum hardware~s~ (job {deck.JobId.Substring(0, Math.Min(8, deck.JobId.Length))})");
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log.Error($"{slot.Engine} {mode} {slot.Name}: {ex.Message}");
                bool noLive, hasOffline;
                lock (_gate)
                {
                    // Back off so a dead network or gated QPU access never hits the API every tick.
                    if (source == ShotSource.IbmHardware)
                        slot.IbmRetryAfter = DateTime.UtcNow + IbmFailureBackoff;
                    else
                        slot.RetryAfter = DateTime.UtcNow + FailureBackoff;
                    noLive = slot.Last == null && slot.ReadySim == null && slot.ReadyIbm == null;
                    hasOffline = slot.Offline != null;
                }
                if (source == ShotSource.Simulator && noLive)
                    _notices.Enqueue(hasOffline
                        ? $"~o~Atlas unreachable~s~: replaying cached quantum results for the {slot.Label} (OFFLINE)"
                        : $"~r~Atlas unreachable~s~ and no cached {slot.Label} results yet: police use normal AI");
            }
            finally
            {
                lock (_gate)
                {
                    if (source == ShotSource.IbmHardware)
                        slot.IbmBusy = false;
                    else
                        slot.SimBusy = false;
                }
            }
        }

        /// <summary>A Random per deck. System.Random isn't thread-safe and decks are built on background threads.</summary>
        Random NewRng()
        {
            lock (_gate)
                return new Random(_rng.Next());
        }

        void Track(Task task)
        {
            _inFlight.RemoveAll(t => t.IsCompleted);
            _inFlight.Add(task);
        }

        /// <summary>On-disk format of a cached job. src/tools/atlas_probe.py writes the same shape for the seed cache.</summary>
        sealed class CacheFile
        {
            public string JobId { get; set; }
            public string Source { get; set; }
            public Dictionary<string, int> Counts { get; set; }
        }

        string CachePath(Slot slot) => Path.Combine(_cacheDir, slot.Name + ".json");

        void SaveCache(Slot slot, ShotSource source, string jobId, List<KeyValuePair<string, int>> counts)
        {
            try
            {
                var file = new CacheFile { JobId = jobId, Source = source.ToString(), Counts = counts.ToDictionary(p => p.Key, p => p.Value) };
                File.WriteAllText(CachePath(slot), JsonConvert.SerializeObject(file, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Log.Error($"cache write {slot.Name}: {ex.Message}");
            }
        }

        ShotDeck LoadCache(Slot slot)
        {
            try
            {
                var path = CachePath(slot);
                if (!File.Exists(path))
                    return null;
                var file = JsonConvert.DeserializeObject<CacheFile>(File.ReadAllText(path));
                if (file?.Counts == null || file.Counts.Count == 0)
                    return null;
                var counts = file.Counts.Where(p => p.Key.Length == slot.Qubits && p.Value > 0).ToList();
                return counts.Count == 0 ? null : new ShotDeck(counts, file.JobId, ShotSource.OfflineCache, NewRng());
            }
            catch (Exception ex)
            {
                Log.Error($"cache read {slot.Name}: {ex.Message}");
                return null;
            }
        }

        static int Clamp(int level) => Math.Min(Math.Max(level, 1), MaxWantedLevel);

        public void Dispose()
        {
            _cts.Cancel();
            (_client as IDisposable)?.Dispose();
        }
    }
}
