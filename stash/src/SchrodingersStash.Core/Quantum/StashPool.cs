using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SchrodingersStash.Core.Atlas;

namespace SchrodingersStash.Core.Quantum
{
    /// <summary>
    /// Pre-fetched real shots for the stash pairs. One finished Atlas job waits ready; opening a pair
    /// takes it, and the next job starts on the next Prefetch, so every pair is measured by its own
    /// new job. All Atlas calls run on background tasks; the game thread only calls Prefetch / Draw /
    /// TryDequeueNotice, which never block.
    /// </summary>
    public sealed class StashPool : IDisposable
    {
        static readonly TimeSpan JobTimeout = TimeSpan.FromMinutes(2);
        static readonly TimeSpan HardwareTimeout = TimeSpan.FromMinutes(30);
        static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(30);
        static readonly TimeSpan ModeFallbackTime = TimeSpan.FromMinutes(5);
        const string CacheName = "stash.json";

        readonly IAtlasClient _client;
        readonly Config _config;
        readonly string _cacheDir;
        readonly object _gate = new object();
        readonly Random _rng = new Random();
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        readonly ConcurrentQueue<string> _notices = new ConcurrentQueue<string>();
        readonly List<Task> _inFlight = new List<Task>();

        ShotDeck _ready;
        ShotDeck _last;
        ShotDeck _offline;
        bool _busy;
        DateTime _retryAfter;
        DateTime _simulatorUntil; // the configured mode failed: use the plain simulator until then
        bool _fallbackAnnounced;
        int _jobsCompleted;

        /// <param name="client">Null when there is no API key: only cached results are used.</param>
        public StashPool(IAtlasClient client, Config config, string cacheDir)
        {
            _client = client;
            _config = config;
            _cacheDir = cacheDir;
            Directory.CreateDirectory(cacheDir);
            _offline = LoadCache();
        }

        /// <summary>Atlas jobs completed this session.</summary>
        public int JobsCompleted => Volatile.Read(ref _jobsCompleted);

        /// <summary>Start a job if none is waiting. Cheap to call every tick.</summary>
        public void Prefetch()
        {
            if (_client == null)
                return;
            var now = DateTime.UtcNow;
            lock (_gate)
            {
                if (_ready != null || _busy || now < _retryAfter)
                    return;
                _busy = true;
                var mode = now < _simulatorUntil ? "emu" : _config.Mode;
                Track(FetchAsync(mode));
            }
        }

        /// <summary>Next real shot: a new job if one is ready, else another shot of the last job, else the cache.</summary>
        public Shot Draw()
        {
            lock (_gate)
            {
                if (_ready != null)
                {
                    _last = _ready;
                    _ready = null;
                }
                var shot = _last?.Draw();
                if (shot != null || !_config.OfflineFallback)
                    return shot;
                shot = _offline?.Draw();
                if (shot == null && _offline != null)
                {
                    // Replaying the cache again is still real data, just reused while offline.
                    _offline = LoadCache();
                    shot = _offline?.Draw();
                }
                return shot;
            }
        }

        /// <summary>Messages for the game, e.g. "noisy chip unavailable". Call from the game thread.</summary>
        public bool TryDequeueNotice(out string notice) => _notices.TryDequeue(out notice);

        /// <summary>For tests: wait for all background jobs started so far.</summary>
        public Task WhenIdleAsync()
        {
            lock (_gate)
                return Task.WhenAll(_inFlight.ToArray());
        }

        async Task FetchAsync(string mode)
        {
            var source = Stash.SourceOf(mode);
            try
            {
                var request = Stash.PairGraph(mode, _config.Backend);
                var timeout = mode == "qpu" ? HardwareTimeout : JobTimeout;
                var result = await _client.RunAsync(Stash.Engine, request, timeout, _cts.Token).ConfigureAwait(false);
                var counts = MeasurementParser.ParseCounts(result.Body, Stash.Qubits);
                if (counts.Count == 0)
                    throw new AtlasException($"job {result.JobId}: no measurements found in result");

                var deck = new ShotDeck(counts, result.JobId, source, NewRng());
                lock (_gate)
                    _ready = deck;
                SaveCache(source, result.JobId, counts);
                var jobs = Interlocked.Increment(ref _jobsCompleted);
                Log.Info($"{Stash.Engine} {mode}: job {result.JobId}, {deck.Total} shots, {result.Elapsed.TotalSeconds:0.0}s (#{jobs} this session)");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log.Error($"{Stash.Engine} {mode}: {ex.Message}");
                bool nothingLive, hasOffline;
                lock (_gate)
                {
                    if (mode != "emu")
                    {
                        // Noisy chip or hardware unavailable: switch to the plain simulator for a while, straight away.
                        _simulatorUntil = DateTime.UtcNow + ModeFallbackTime;
                        if (!_fallbackAnnounced)
                            _notices.Enqueue($"~o~Atlas {mode} mode unavailable~s~: using the perfect simulator for now.");
                        _fallbackAnnounced = true;
                    }
                    else
                        _retryAfter = DateTime.UtcNow + FailureBackoff;
                    nothingLive = _ready == null && _last == null;
                    hasOffline = _offline != null;
                }
                if (mode == "emu" && nothingLive)
                    _notices.Enqueue(hasOffline
                        ? "~o~Atlas unreachable~s~: replaying cached real quantum results (OFFLINE)."
                        : "~r~Atlas unreachable~s~ and no cached results: stashes can't be measured yet.");
            }
            finally
            {
                lock (_gate)
                    _busy = false;
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

        /// <summary>On-disk format of a cached job. tools/stash_seed.py writes the same shape.</summary>
        sealed class CacheFile
        {
            public string JobId { get; set; }
            public string Source { get; set; }
            public Dictionary<string, int> Counts { get; set; }
        }

        string CachePath => Path.Combine(_cacheDir, CacheName);

        void SaveCache(ShotSource source, string jobId, List<KeyValuePair<string, int>> counts)
        {
            try
            {
                var file = new CacheFile { JobId = jobId, Source = source.ToString(), Counts = counts.ToDictionary(p => p.Key, p => p.Value) };
                File.WriteAllText(CachePath, JsonConvert.SerializeObject(file, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Log.Error($"cache write: {ex.Message}");
            }
        }

        ShotDeck LoadCache()
        {
            try
            {
                if (!File.Exists(CachePath))
                    return null;
                var file = JsonConvert.DeserializeObject<CacheFile>(File.ReadAllText(CachePath));
                if (file?.Counts == null || file.Counts.Count == 0)
                    return null;
                var counts = file.Counts.Where(p => p.Key.Length == Stash.Qubits && p.Value > 0).ToList();
                return counts.Count == 0 ? null : new ShotDeck(counts, file.JobId, ShotSource.OfflineCache, NewRng());
            }
            catch (Exception ex)
            {
                Log.Error($"cache read: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            (_client as IDisposable)?.Dispose();
        }
    }
}
