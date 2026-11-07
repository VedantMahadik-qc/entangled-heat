using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using QuantumGetaway.Core.Atlas;

namespace QuantumGetaway.Core.Tunnel
{
    /// <summary>A measured landing: the cell, the wavefunction it came from, and whether that job was new.</summary>
    public sealed class Landing
    {
        public Landing(Wavefunction wave, Cell cell, bool fresh)
        {
            Wave = wave;
            Cell = cell;
            Fresh = fresh;
        }

        public Wavefunction Wave { get; }
        public Cell Cell { get; }

        /// <summary>True if this tunnel used a job nobody had measured yet.</summary>
        public bool Fresh { get; }
    }

    /// <summary>
    /// One finished blur-v1 job waits ready for each tunnel kind. Tunnelling measures it and the next
    /// job starts on the next Prefetch, so every tunnel is normally measured from its own new job.
    /// All Atlas calls run on background tasks; the game thread only calls Prefetch / Peek / Measure /
    /// TryDequeueNotice, which never block.
    /// </summary>
    public sealed class TunnelPool : IDisposable
    {
        static readonly TimeSpan JobTimeout = TimeSpan.FromMinutes(2);
        static readonly TimeSpan HardwareTimeout = TimeSpan.FromMinutes(30);
        static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(30);
        static readonly TimeSpan ModeFallbackTime = TimeSpan.FromMinutes(5);
        readonly TunnelKind[] _kinds;
        readonly IAtlasClient _client;
        readonly Config _config;
        readonly string _cacheDir;
        readonly object _gate = new object();
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        readonly ConcurrentQueue<string> _notices = new ConcurrentQueue<string>();
        readonly List<Task> _inFlight = new List<Task>();
        readonly Wavefunction[] _ready = new Wavefunction[2];
        readonly Wavefunction[] _last = new Wavefunction[2];
        readonly Wavefunction[] _offline = new Wavefunction[2];
        readonly bool[] _busy = new bool[2];
        readonly DateTime[] _retryAfter = new DateTime[2];
        readonly byte[] _input = Tunnels.InputPng();

        Task<string> _asset; // the input image is the same every time, so it's uploaded once and reused
        DateTime _simulatorUntil; // the configured mode failed: use the plain simulator until then
        bool _fallbackAnnounced;
        int _jobsCompleted;

        /// <param name="client">Null when there is no API key: only cached results are used.</param>
        /// <param name="kinds">The kinds the game uses; only these are fetched. Default: both.</param>
        public TunnelPool(IAtlasClient client, Config config, string cacheDir, params TunnelKind[] kinds)
        {
            _kinds = kinds != null && kinds.Length > 0 ? kinds : new[] { TunnelKind.Hop, TunnelKind.Leap };
            _client = client;
            _config = config;
            _cacheDir = cacheDir;
            Directory.CreateDirectory(cacheDir);
            foreach (var kind in _kinds)
                _offline[(int)kind] = LoadCache(kind);
        }

        /// <summary>Atlas jobs completed this session.</summary>
        public int JobsCompleted => Volatile.Read(ref _jobsCompleted);

        /// <summary>Start a job for each kind that has none waiting. Cheap to call every tick.</summary>
        public void Prefetch()
        {
            if (_client == null)
                return;
            var now = DateTime.UtcNow;
            lock (_gate)
            {
                foreach (var kind in _kinds)
                {
                    var k = (int)kind;
                    if (_ready[k] != null || _busy[k] || now < _retryAfter[k])
                        continue;
                    _busy[k] = true;
                    var mode = now < _simulatorUntil ? "sim" : _config.Mode;
                    Track(FetchAsync(kind, mode));
                }
            }
        }

        /// <summary>The wavefunction the next tunnel of this kind will measure (for the preview), or null.</summary>
        public Wavefunction Peek(TunnelKind kind)
        {
            var k = (int)kind;
            lock (_gate)
                return _ready[k] ?? _last[k] ?? (_config.OfflineFallback ? _offline[k] : null);
        }

        /// <summary>
        /// The measurement. Pass the wavefunction the player was shown so they land on what they saw,
        /// even if a newer job arrived in between. Returns null when there is no quantum data at all.
        /// </summary>
        public Landing Measure(TunnelKind kind, Random rng, Wavefunction shown = null)
        {
            var wave = Take(kind, shown, out var fresh);
            return wave == null ? null : new Landing(wave, wave.Sample(rng), fresh);
        }

        /// <summary>
        /// Use up the wavefunction for one tunnel without sampling a cell (the caller measures among
        /// its own options). fresh is true if it was a new job; the next job then starts on Prefetch.
        /// </summary>
        public Wavefunction Take(TunnelKind kind, Wavefunction shown, out bool fresh)
        {
            var k = (int)kind;
            lock (_gate)
            {
                var wave = shown ?? Peek(kind);
                fresh = wave != null && wave == _ready[k];
                if (fresh)
                {
                    _last[k] = wave;
                    _ready[k] = null;
                }
                return wave;
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

        async Task FetchAsync(TunnelKind kind, string mode)
        {
            var k = (int)kind;
            var tag = $"{Tunnels.Engine} {Tunnels.Name(kind).ToLowerInvariant()} {mode}";
            try
            {
                var asset = await InputAsset().ConfigureAwait(false);
                var inputs = new Newtonsoft.Json.Linq.JObject { ["image"] = asset };
                var timeout = mode == "qpu" ? HardwareTimeout : JobTimeout;
                AtlasJobResult result;
                try
                {
                    result = await _client.RunAsync(Tunnels.Engine, Tunnels.Params(kind, mode, _config.Backend), inputs, timeout, _cts.Token).ConfigureAwait(false);
                }
                catch (AtlasException ex) when (ex.StatusCode >= 400 && ex.StatusCode < 500)
                {
                    // The uploaded image may have expired: upload it again next time.
                    lock (_gate)
                        _asset = null;
                    throw;
                }
                var png = await _client.DownloadAsync(Tunnels.OutputUrl(result.Body), _cts.Token).ConfigureAwait(false);
                var luma = Png.DecodeLuma(png, out var w, out var h);
                if (w != Tunnels.Size || h != Tunnels.Size)
                    throw new FormatException($"job {result.JobId}: expected a {Tunnels.Size}x{Tunnels.Size} image, got {w}x{h}");
                var wave = new Wavefunction(kind, Tunnels.Size, luma, result.JobId, Tunnels.SourceOf(mode));

                lock (_gate)
                    _ready[k] = wave;
                SaveCache(wave);
                var jobs = Interlocked.Increment(ref _jobsCompleted);
                Log.Info($"{tag}: job {result.JobId}, stay {wave.StayProbability:P0}, mean jump {wave.MeanDistance():0.0} cells, {result.Elapsed.TotalSeconds:0.0}s (#{jobs} this session)");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log.Error($"{tag}: {ex.Message}");
                bool nothingLive, hasOffline;
                lock (_gate)
                {
                    if (mode != "sim")
                    {
                        // Noisy chip or hardware unavailable: switch to the plain simulator for a while, straight away.
                        _simulatorUntil = DateTime.UtcNow + ModeFallbackTime;
                        if (!_fallbackAnnounced)
                            _notices.Enqueue($"~o~Atlas {mode} mode unavailable~s~: using the exact simulator for now.");
                        _fallbackAnnounced = true;
                    }
                    else
                        _retryAfter[k] = DateTime.UtcNow + FailureBackoff;
                    nothingLive = _ready[k] == null && _last[k] == null;
                    hasOffline = _offline[k] != null;
                }
                if (mode == "sim" && nothingLive && kind == _kinds[0])
                    _notices.Enqueue(hasOffline
                        ? "~o~Atlas unreachable~s~: portals use cached real quantum results (OFFLINE)."
                        : "~r~Atlas unreachable~s~ and no cached results: portals are offline.");
            }
            finally
            {
                lock (_gate)
                    _busy[k] = false;
            }
        }

        Task<string> InputAsset()
        {
            lock (_gate)
            {
                if (_asset == null || _asset.IsFaulted || _asset.IsCanceled)
                    _asset = _client.UploadAsync(_input, "tunnel-origin.png", "image/png", _cts.Token);
                return _asset;
            }
        }

        void Track(Task task)
        {
            _inFlight.RemoveAll(t => t.IsCompleted);
            _inFlight.Add(task);
        }

        /// <summary>On-disk format of a cached job. tools/getaway_seed.py writes the same shape.</summary>
        sealed class CacheFile
        {
            public string JobId { get; set; }
            public string Source { get; set; }
            public string Kind { get; set; }
            public int Size { get; set; }
            public int[] Pixels { get; set; }
        }

        string CachePath(TunnelKind kind) => Path.Combine(_cacheDir, Tunnels.Name(kind).ToLowerInvariant() + ".json");

        void SaveCache(Wavefunction wave)
        {
            try
            {
                var pixels = new int[wave.Brightness.Length];
                for (var i = 0; i < pixels.Length; i++)
                    pixels[i] = wave.Brightness[i];
                var file = new CacheFile { JobId = wave.JobId, Source = wave.Source.ToString(), Kind = wave.Kind.ToString(), Size = wave.Size, Pixels = pixels };
                File.WriteAllText(CachePath(wave.Kind), JsonConvert.SerializeObject(file));
            }
            catch (Exception ex)
            {
                Log.Error($"cache write: {ex.Message}");
            }
        }

        Wavefunction LoadCache(TunnelKind kind)
        {
            try
            {
                var path = CachePath(kind);
                if (!File.Exists(path))
                    return null;
                var file = JsonConvert.DeserializeObject<CacheFile>(File.ReadAllText(path));
                if (file?.Pixels == null || file.Size != Tunnels.Size || file.Pixels.Length != Tunnels.Size * Tunnels.Size)
                    return null;
                var luma = new byte[file.Pixels.Length];
                for (var i = 0; i < luma.Length; i++)
                    luma[i] = (byte)Math.Min(Math.Max(file.Pixels[i], 0), 255);
                return new Wavefunction(kind, file.Size, luma, file.JobId, ShotSource.OfflineCache);
            }
            catch (Exception ex)
            {
                Log.Error($"cache read ({kind}): {ex.Message}");
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
