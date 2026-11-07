using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using QuantumGetaway.Core;
using QuantumGetaway.Core.Atlas;
using QuantumGetaway.Core.Tunnel;
using Xunit;

namespace QuantumGetaway.Tests
{
    public class PngTests
    {
        [Fact]
        public void GreyImageRoundTrips()
        {
            var pixels = Enumerable.Range(0, 32 * 32).Select(i => (byte)(i * 7)).ToArray();
            var png = Png.EncodeGray(32, 32, pixels);
            var back = Png.DecodeLuma(png, out var w, out var h);
            Assert.Equal(32, w);
            Assert.Equal(32, h);
            Assert.Equal(pixels, back);
        }

        [Fact]
        public void InputImageIsOneBrightPixelAtTheOrigin()
        {
            var luma = Png.DecodeLuma(Tunnels.InputPng(), out var w, out _);
            Assert.Equal(Tunnels.Size, w);
            Assert.Equal(255, luma[Tunnels.OriginY * w + Tunnels.OriginX]);
            Assert.Equal(255, luma.Sum(b => b));
        }

        [Fact]
        public void DecodesRealAtlasOutput()
        {
            // blur-v1 returns 8-bit RGB with filtered rows; this is a real Hop result from 7 Nov 2026.
            var luma = Png.DecodeLuma(Fixture("hop-sim-28312ddb.png"), out var w, out var h);
            Assert.Equal(32, w);
            Assert.Equal(32, h);
            Assert.Equal(255, luma[Tunnels.OriginX]); // the origin is the brightest cell (p 15%)
            Assert.Equal(134, luma[Tunnels.OriginX + 1]); // a neighbour, matching the Python probe
        }

        [Fact]
        public void RejectsNonPng() => Assert.Throws<FormatException>(() => Png.DecodeLuma(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, out _, out _));

        internal static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
    }

    public class WavefunctionTests
    {
        static Wavefunction Real(TunnelKind kind, string file) =>
            new Wavefunction(kind, 32, Png.DecodeLuma(PngTests.Fixture(file), out _, out _), "job-" + file, ShotSource.Simulator);

        [Fact]
        public void HopMatchesTheProbe()
        {
            var hop = Real(TunnelKind.Hop, "hop-sim-28312ddb.png");
            Assert.InRange(hop.StayProbability, 0.14, 0.16);
            Assert.InRange(hop.MeanDistance(), 5.0, 7.0);
            // The far interference echoes along the forward axis exist (3, 7 and 15 cells ahead).
            Assert.True(hop.P(0, 3) > 0);
            Assert.True(hop.P(0, 7) > 0);
            Assert.True(hop.P(0, 15) > 0);
        }

        [Fact]
        public void LeapNeverStaysAndGoesFar()
        {
            var leap = Real(TunnelKind.Leap, "leap-sim-7c8eb7bb.png");
            Assert.Equal(0, leap.StayProbability);
            Assert.InRange(leap.MeanDistance(), 18.0, 22.0);
        }

        [Fact]
        public void NoisyChipLeapIsUsableToo()
        {
            var leap = Real(TunnelKind.Leap, "leap-fake-marrakesh-56bf17fa.png");
            Assert.True(leap.MeanDistance() > 15);
        }

        [Fact]
        public void SamplingFollowsTheProbabilities()
        {
            var hop = Real(TunnelKind.Hop, "hop-sim-28312ddb.png");
            var rng = new Random(1);
            const int n = 40000;
            var stays = 0;
            var forward1 = 0;
            for (var i = 0; i < n; i++)
            {
                var c = hop.Sample(rng);
                Assert.True(c.P > 0); // never lands on a dark cell
                if (c.IsStay) stays++;
                if (c.Dx == 0 && c.Dy == 1) forward1++;
            }
            Assert.InRange(stays / (double)n, hop.StayProbability - 0.01, hop.StayProbability + 0.01);
            Assert.InRange(forward1 / (double)n, hop.P(0, 1) - 0.01, hop.P(0, 1) + 0.01);
        }

        [Fact]
        public void TopIsSortedAndCellsAreRelativeToYou()
        {
            var pixels = new byte[32 * 32];
            pixels[0 * 32 + 15] = 10; // you
            pixels[5 * 32 + 18] = 30; // 3 right, 5 ahead
            var wave = new Wavefunction(TunnelKind.Hop, 32, pixels, "j", ShotSource.Simulator);
            var top = wave.Top(5);
            Assert.Equal(2, top.Count);
            Assert.Equal(3, top[0].Dx);
            Assert.Equal(5, top[0].Dy);
            Assert.Equal(0.75, top[0].P, 6);
            Assert.True(top[1].IsStay);
            Assert.Equal("(+3,+5)", top[0].ToString());
        }

        [Fact]
        public void DarkImageIsRejected() =>
            Assert.Throws<ArgumentException>(() => new Wavefunction(TunnelKind.Hop, 32, new byte[32 * 32], "j", ShotSource.Simulator));
    }

    public class TunnelsTests
    {
        [Fact]
        public void SimParamsHaveNoMachine()
        {
            var p = Tunnels.Params(TunnelKind.Hop, "sim", "marrakesh");
            Assert.Equal(0.4, (double)p["strength"]);
            Assert.Equal(0.5, (double)p["reach"]);
            Assert.Equal(32, (int)p["size"]);
            Assert.Null(p["machine"]);
        }

        [Fact]
        public void FakeAndQpuPickTheChip()
        {
            Assert.Equal("fake_fez", (string)Tunnels.Params(TunnelKind.Leap, "fake", "Fez")["machine"]);
            Assert.Equal("ibm_marrakesh", (string)Tunnels.Params(TunnelKind.Leap, "qpu", "marrakesh")["machine"]);
            Assert.Equal("least_busy", (string)Tunnels.Params(TunnelKind.Leap, "qpu", "")["machine"]);
        }

        [Fact]
        public void FindsTheOutputUrl()
        {
            var body = JObject.Parse("{\"outputs\":[{\"slot\":\"result\",\"url\":\"https://x/y.png\"}]}");
            Assert.Equal("https://x/y.png", Tunnels.OutputUrl(body));
            Assert.Throws<FormatException>(() => Tunnels.OutputUrl(new JObject()));
        }
    }

    public class ConfigTests
    {
        [Fact]
        public void ParsesAndClamps()
        {
            var c = Config.Parse("[Atlas]\nApiKey= abc \nMode=FAKE\n[Gameplay]\nTunnelKey=G\nControllerL3=false\nMaxCharges=50\nStartCharges=7\nCellSize=1\nPortalSeconds=8\n");
            Assert.Equal("abc", c.ApiKey);
            Assert.Equal("fake", c.Mode);
            Assert.Equal("G", c.TunnelKey);
            Assert.False(c.ControllerL3);
            Assert.Equal(9, c.MaxCharges);
            Assert.Equal(7, c.StartCharges);
            Assert.Equal(2f, c.CellSize);
            Assert.Equal(8f, c.PortalSeconds);
        }

        [Fact]
        public void StartChargesNeverExceedTheMaximum() =>
            Assert.Equal(3, Config.Parse("[Gameplay]\nStartCharges=5\n").StartCharges);

        [Fact]
        public void BorrowsEntangledHeatsKeyWhenItsOwnIsEmpty()
        {
            var dir = Path.Combine(Path.GetTempPath(), "qg-cfg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "QuantumGetaway.ini"), "[Atlas]\nApiKey=\nMode=fake\n");
            File.WriteAllText(Path.Combine(dir, "EntangledHeat.ini"), "[Atlas]\nApiKey=eh-key\nUseIbm=true\n");
            var c = Config.Load(Path.Combine(dir, "QuantumGetaway.ini"), Path.Combine(dir, "EntangledHeat.ini"));
            Assert.Equal("eh-key", c.ApiKey);
            Assert.Equal("fake", c.Mode); // everything else still comes from its own ini

            File.WriteAllText(Path.Combine(dir, "QuantumGetaway.ini"), "[Atlas]\nApiKey=own-key\n");
            Assert.Equal("own-key", Config.Load(Path.Combine(dir, "QuantumGetaway.ini"), Path.Combine(dir, "EntangledHeat.ini")).ApiKey);
        }

        [Fact]
        public void UnknownModeKeepsTheDefault() => Assert.Equal("sim", Config.Parse("[Atlas]\nMode=warp\n").Mode);
    }

    public class HudTests
    {
        [Fact]
        public void ShowsTheLastMeasurement()
        {
            var pixels = new byte[32 * 32];
            pixels[17 * 32 + 18] = 255;
            var wave = new Wavefunction(TunnelKind.Leap, 32, pixels, "1a2b3c4d-ffff", ShotSource.NoisyChip);
            var line = GetawayHud.Line(AtlasStatus.Connected, "L3", 2, 3, false, new Landing(wave, new Cell(3, 17, 0.018), true), 142f);
            Assert.Contains("PORTALS 2/3", line);
            Assert.Contains("L3: open", line);
            Assert.Contains("142m", line);
            Assert.Contains("NOISY CHIP", line);
            Assert.Contains("new job 1a2b3c4d", line);
            Assert.Contains("exit (+3,+17) p 1.8%", line);
        }

        [Fact]
        public void TellsYouToFindAnOrbWhenEmpty()
        {
            var line = GetawayHud.Line(AtlasStatus.Connected, "T", 0, 3, false, null, 0);
            Assert.Contains("find an orb", line);
            Assert.DoesNotContain("T: open", line);
        }
    }

    public class PoolTests
    {
        sealed class FakeAtlas : IAtlasClient
        {
            public int Uploads;
            public int Jobs;
            public bool Fail;
            public string LastMachine;
            public readonly List<string> Machines = new List<string>();
            public byte[] Output = PngTests.Fixture("hop-sim-28312ddb.png");

            public Task<string> UploadAsync(byte[] data, string filename, string contentType, CancellationToken ct)
            {
                Interlocked.Increment(ref Uploads);
                return Task.FromResult("asset-1");
            }

            public Task<AtlasJobResult> RunAsync(string engineId, JObject parameters, JObject inputFiles, TimeSpan timeout, CancellationToken ct)
            {
                Assert.Equal("blur-v1", engineId);
                Assert.Equal("asset-1", (string)inputFiles["image"]);
                LastMachine = (string)parameters["machine"];
                lock (Machines)
                    Machines.Add(LastMachine);
                if (Fail)
                    throw new AtlasException("boom");
                var n = Interlocked.Increment(ref Jobs);
                var body = JObject.Parse("{\"outputs\":[{\"slot\":\"result\",\"url\":\"https://storage/out.png\"}]}");
                return Task.FromResult(new AtlasJobResult { JobId = $"job-{n}", Body = body, Elapsed = TimeSpan.FromSeconds(4) });
            }

            public Task<byte[]> DownloadAsync(string url, CancellationToken ct) => Task.FromResult(Output);
        }

        static string TempDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "qg-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact]
        public async Task EveryTunnelGetsItsOwnNewJob()
        {
            var atlas = new FakeAtlas();
            var pool = new TunnelPool(atlas, Config.Parse(""), TempDir());
            pool.Prefetch();
            await pool.WhenIdleAsync();
            Assert.Equal(2, atlas.Jobs); // one Hop, one Leap
            Assert.Equal(1, atlas.Uploads); // the input image is uploaded once and reused

            var rng = new Random(3);
            var first = pool.Measure(TunnelKind.Hop, rng);
            Assert.True(first.Fresh);
            var again = pool.Measure(TunnelKind.Hop, rng);
            Assert.False(again.Fresh); // no new job yet: same wavefunction, labelled "earlier job"
            Assert.Equal(first.Wave.JobId, again.Wave.JobId);

            pool.Prefetch();
            await pool.WhenIdleAsync();
            var third = pool.Measure(TunnelKind.Hop, rng);
            Assert.True(third.Fresh);
            Assert.NotEqual(first.Wave.JobId, third.Wave.JobId);
            Assert.Equal(1, atlas.Uploads);
        }

        [Fact]
        public async Task FetchesOnlyTheKindsTheGameUses()
        {
            var atlas = new FakeAtlas();
            var pool = new TunnelPool(atlas, Config.Parse(""), TempDir(), TunnelKind.Leap);
            pool.Prefetch();
            await pool.WhenIdleAsync();
            Assert.Equal(1, atlas.Jobs);
            Assert.NotNull(pool.Peek(TunnelKind.Leap));
            Assert.Null(pool.Peek(TunnelKind.Hop));
        }

        [Fact]
        public async Task MeasuresWhatThePlayerWasShown()
        {
            var atlas = new FakeAtlas();
            var pool = new TunnelPool(atlas, Config.Parse(""), TempDir());
            pool.Prefetch();
            await pool.WhenIdleAsync();
            var shown = pool.Peek(TunnelKind.Hop);
            pool.Measure(TunnelKind.Hop, new Random(1));
            pool.Prefetch(); // a newer job lands while the player is still holding the key
            await pool.WhenIdleAsync();
            var landing = pool.Measure(TunnelKind.Hop, new Random(1), shown);
            Assert.Same(shown, landing.Wave);
            Assert.False(landing.Fresh);
        }

        [Fact]
        public async Task OfflineUsesTheCacheFromAnEarlierSession()
        {
            var dir = TempDir();
            var atlas = new FakeAtlas();
            var online = new TunnelPool(atlas, Config.Parse(""), dir);
            online.Prefetch();
            await online.WhenIdleAsync();

            var offline = new TunnelPool(null, Config.Parse(""), dir);
            var landing = offline.Measure(TunnelKind.Leap, new Random(1));
            Assert.NotNull(landing);
            Assert.Equal(ShotSource.OfflineCache, landing.Wave.Source);
            Assert.Equal("job-2", landing.Wave.JobId); // Hop was job-1, Leap job-2
        }

        [Fact]
        public void NoDataMeansNoTunnel()
        {
            var pool = new TunnelPool(null, Config.Parse(""), TempDir());
            Assert.Null(pool.Peek(TunnelKind.Hop));
            Assert.Null(pool.Measure(TunnelKind.Hop, new Random(1)));
        }

        [Fact]
        public async Task NoisyChipFailureFallsBackToTheSimulator()
        {
            var atlas = new FakeAtlas { Fail = true };
            var pool = new TunnelPool(atlas, Config.Parse("[Atlas]\nMode=fake\n"), TempDir());
            pool.Prefetch();
            await pool.WhenIdleAsync();
            Assert.Equal("fake_marrakesh", atlas.Machines[0]);
            Assert.Null(atlas.Machines[1]); // the Leap job started after the failure already uses sim
            Assert.True(pool.TryDequeueNotice(out var notice));
            Assert.False(pool.TryDequeueNotice(out _)); // announced once
            Assert.Contains("fake mode unavailable", notice);

            atlas.Fail = false;
            pool.Prefetch();
            await pool.WhenIdleAsync();
            Assert.Null(atlas.LastMachine); // sim
            Assert.Equal(ShotSource.Simulator, pool.Peek(TunnelKind.Hop).Source);
        }
    }
}

namespace QuantumGetaway.Tests
{
    /// <summary>Real Atlas calls (2 blur-v1 jobs). Runs only with QG_LIVE=1 and MOTH_API_KEY set.</summary>
    public class LiveAtlasTests
    {
        [Fact]
        public async Task RealJobsBecomeWavefunctions()
        {
            var key = Environment.GetEnvironmentVariable("MOTH_API_KEY");
            if (Environment.GetEnvironmentVariable("QG_LIVE") != "1" || string.IsNullOrWhiteSpace(key))
                return;
            var dir = Path.Combine(Path.GetTempPath(), "qg-live-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            Log.Init(Path.Combine(dir, "QuantumGetaway.log"));
            using (var pool = new TunnelPool(new AtlasClient("https://api.mothquantum.com", key), Config.Parse(""), dir))
            {
                pool.Prefetch();
                await pool.WhenIdleAsync();
                var hop = pool.Peek(TunnelKind.Hop);
                var leap = pool.Peek(TunnelKind.Leap);
                Assert.NotNull(hop);
                Assert.NotNull(leap);
                Assert.InRange(hop.StayProbability, 0.10, 0.20);
                Assert.True(leap.MeanDistance() > 15);
                Assert.True(pool.Measure(TunnelKind.Hop, new Random()).Fresh);
                Console.WriteLine($"live: hop job {hop.JobId} stay {hop.StayProbability:P1}, leap job {leap.JobId} mean {leap.MeanDistance():0.0} cells");
            }
            Console.WriteLine(File.ReadAllText(Path.Combine(dir, "QuantumGetaway.log")));
        }

        /// <summary>A real IBM hardware job (queues vary). Runs only with QG_LIVE_QPU=1.</summary>
        [Fact]
        public async Task RealIbmHardwareOrbJob()
        {
            var key = Environment.GetEnvironmentVariable("MOTH_API_KEY");
            if (Environment.GetEnvironmentVariable("QG_LIVE_QPU") != "1" || string.IsNullOrWhiteSpace(key))
                return;
            var dir = Path.Combine(Path.GetTempPath(), "qg-live-qpu-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            Log.Init(Path.Combine(dir, "QuantumGetaway.log"));
            using (var pool = new QuantumGetaway.Core.Orbs.OrbPool(new AtlasClient("https://api.mothquantum.com", key), Config.Parse(""), dir))
            {
                pool.Prefetch();
                await pool.WhenIdleAsync();
                var shot = pool.Draw();
                Assert.Equal(ShotSource.IbmHardware, shot.Source);
                Console.WriteLine($"live: IBM hardware job {shot.JobId} first shot |{shot.Bits}>");
            }
            Console.WriteLine(File.ReadAllText(Path.Combine(dir, "QuantumGetaway.log")));
        }

        [Fact]
        public async Task RealOrbPairJob()
        {
            var key = Environment.GetEnvironmentVariable("MOTH_API_KEY");
            if (Environment.GetEnvironmentVariable("QG_LIVE") != "1" || string.IsNullOrWhiteSpace(key))
                return;
            var dir = Path.Combine(Path.GetTempPath(), "qg-live-orbs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            Log.Init(Path.Combine(dir, "QuantumGetaway.log"));
            using (var pool = new QuantumGetaway.Core.Orbs.OrbPool(new AtlasClient("https://api.mothquantum.com", key), Config.Parse(""), dir))
            {
                pool.Prefetch();
                await pool.WhenIdleAsync();
                var shot = pool.Draw();
                Assert.NotNull(shot);
                Assert.True(shot.Fresh);
                Assert.Equal(ShotSource.NoisyChip, shot.Source);
                Console.WriteLine($"live: orb pairs job {shot.JobId} first shot |{shot.Bits}>");
            }
            Console.WriteLine(File.ReadAllText(Path.Combine(dir, "QuantumGetaway.log")));
        }
    }
}
