using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SchrodingersStash.Core;
using SchrodingersStash.Core.Atlas;
using SchrodingersStash.Core.Quantum;
using Xunit;

namespace SchrodingersStash.Tests
{
    public class ConfigTests
    {
        [Fact]
        public void DefaultsToTheNoisyChip()
        {
            var c = Config.Parse("");
            Assert.Equal("fake", c.Mode);
            Assert.Equal("marrakesh", c.Backend);
            Assert.Equal("F9", c.StartKey);
            Assert.False(c.HasApiKey);
        }

        [Fact]
        public void ParsesAndClampsValues()
        {
            var c = Config.Parse("[Atlas]\r\nApiKey = k1 \r\nMode=EMU\r\n[Gameplay]\r\nAlarmStars=9\r\nRunMinutes=0.5\r\nCashPerStash=2500\r\n");
            Assert.Equal("k1", c.ApiKey);
            Assert.Equal("emu", c.Mode);
            Assert.Equal(5, c.AlarmStars);
            Assert.Equal(2f, c.RunMinutes);
            Assert.Equal(2500, c.CashPerStash);
        }

        [Fact]
        public void UnknownModeKeepsDefault() => Assert.Equal("fake", Config.Parse("[Atlas]\nMode=warp\n").Mode);

        [Fact]
        public void ExampleIniParses()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "config", "SchrodingersStash.example.ini");
            var c = Config.Load(path);
            Assert.True(File.Exists(path));
            Assert.False(c.HasApiKey);
            Assert.Equal("fake", c.Mode);
        }
    }

    public class StashRequestTests
    {
        [Fact]
        public void ThreeAntiCorrelatedPairs()
        {
            var p = Stash.PairGraph("emu", "marrakesh");
            Assert.Equal(6, (int)p["num_qubits"]);
            Assert.Equal("[[0,1],[2,3],[4,5]]", p["coupling_map"].ToString(Newtonsoft.Json.Formatting.None));
            var links = p["operations"].Where(o => (string)o["type"] == "relationship").ToList();
            Assert.Equal(3, links.Count);
            Assert.All(links, l => Assert.Equal(-1.0, (double)l["paulis"]["ZZ"]));
            Assert.Equal(6, p["operations"].Count(o => (string)o["type"] == "bloch" && (double)o["paulis"]["X"] == 1.0));
            Assert.Null(p["backend_name"]); // the perfect simulator takes no chip
        }

        [Fact]
        public void NoisyChipNamesTheBackend()
        {
            var p = Stash.PairGraph("fake", "fez");
            Assert.Equal("fake", (string)p["mode"]);
            Assert.Equal("fez", (string)p["backend_name"]);
        }
    }

    public class ResolveTests
    {
        static Shot S(string bits) => new Shot(bits, "j", ShotSource.Simulator);

        [Theory]
        [InlineData("100000", 0, 0, PairResult.CashHere)]
        [InlineData("100000", 0, 1, PairResult.CashThere)]
        [InlineData("000100", 1, 0, PairResult.CashThere)]
        [InlineData("000100", 1, 1, PairResult.CashHere)]
        [InlineData("000011", 2, 0, PairResult.Jackpot)]
        [InlineData("111100", 2, 1, PairResult.Bust)]
        public void ReadsOnlyThatPairsBits(string bits, int pair, int side, PairResult expected) =>
            Assert.Equal(expected, Stash.Resolve(S(bits), pair, side));

        [Fact]
        public void RealNoisyChipJobIsMostlyOpposite()
        {
            // A real fake-marrakesh job saved by the probe (2 Nov 2026).
            var samples = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "samples");
            var file = Directory.GetFiles(samples, "stash-pairs-fake-marrakesh-*.json").OrderBy(f => f).Last();
            var body = (JObject)JObject.Parse(File.ReadAllText(file))["result"];
            var counts = MeasurementParser.ParseCounts(body, Stash.Qubits);
            var total = counts.Sum(c => c.Value);
            Assert.InRange(total, 900, 1024);
            for (var pair = 0; pair < Stash.Pairs; pair++)
            {
                var opposite = counts.Where(c => c.Key[2 * pair] != c.Key[2 * pair + 1]).Sum(c => c.Value);
                Assert.InRange((double)opposite / total, 0.95, 1.0);
            }
        }
    }

    public class HudTests
    {
        [Fact]
        public void IdleLineShowsTheStartKey() =>
            Assert.Equal("~p~STASH~s~ | ~p~ATLAS ~g~OK ~s~| ~c~F9: start a stash run", StashHud.Line(AtlasStatus.Connected, false, 0, 0, null));

        [Fact]
        public void RunLineShowsCashPairsSourceJobAndPairBits()
        {
            var line = StashHud.Line(AtlasStatus.Connected, true, 20000, 1, new Shot("011001", "1a2b3c4d-99", ShotSource.NoisyChip, fresh: true), lastPair: 1);
            Assert.Contains("$20,000", line);
            Assert.Contains("1 PAIR LEFT", line);
            Assert.Contains("NOISY CHIP", line);
            Assert.Contains("new job 1a2b3c4d", line);
            Assert.Contains("|10>", line);
        }
    }

    public class StashPoolTests : IDisposable
    {
        readonly string _dir = Path.Combine(Path.GetTempPath(), "stash-tests-" + Guid.NewGuid().ToString("N"));

        sealed class FakeAtlas : IAtlasClient
        {
            public int Calls;
            public HashSet<string> FailModes = new HashSet<string>();
            public List<string> Modes = new List<string>();

            public Task<AtlasJobResult> RunAsync(string engineId, JObject parameters, TimeSpan timeout, CancellationToken ct)
            {
                Interlocked.Increment(ref Calls);
                var mode = (string)parameters["mode"];
                lock (Modes) Modes.Add(mode);
                if (FailModes.Contains(mode))
                    throw new AtlasException(mode + " down");
                var body = JObject.Parse(@"{""result"":{""output"":{""measurements"":[{""bitstring"":""100110"",""count"":4}]}}}");
                return Task.FromResult(new AtlasJobResult { JobId = "job-" + Calls, Body = body, Elapsed = TimeSpan.FromSeconds(1) });
            }
        }

        static Config Cfg(string extra = "") => Config.Parse("[Atlas]\nApiKey=x\n" + extra);

        [Fact]
        public async Task EveryPairGetsItsOwnNewJob()
        {
            var atlas = new FakeAtlas();
            using (var pool = new StashPool(atlas, Cfg(), _dir))
            {
                Assert.Null(pool.Draw());
                pool.Prefetch();
                await pool.WhenIdleAsync();
                var first = pool.Draw();
                Assert.True(first.Fresh);
                Assert.Equal(ShotSource.NoisyChip, first.Source);
                Assert.Equal("job-1", first.JobId);

                var earlier = pool.Draw(); // next job not fetched yet: another shot of the same job
                Assert.False(earlier.Fresh);
                Assert.Equal("job-1", earlier.JobId);

                pool.Prefetch();
                pool.Prefetch();
                await pool.WhenIdleAsync();
                Assert.Equal("job-2", pool.Draw().JobId);
                Assert.Equal(2, atlas.Calls); // one waiting job at a time, never stacked
            }
            Assert.True(File.Exists(Path.Combine(_dir, "stash.json")));
        }

        [Fact]
        public async Task NoisyChipFailureFallsBackToTheSimulatorOnce()
        {
            var atlas = new FakeAtlas { FailModes = { "fake" } };
            using (var pool = new StashPool(atlas, Cfg(), _dir))
            {
                pool.Prefetch();
                await pool.WhenIdleAsync();
                pool.Prefetch(); // straight to emu, no 30 s wait
                await pool.WhenIdleAsync();
                var shot = pool.Draw();
                Assert.Equal(ShotSource.Simulator, shot.Source);
                Assert.Equal(new[] { "fake", "emu" }, atlas.Modes);

                Assert.True(pool.TryDequeueNotice(out var notice));
                Assert.Contains("fake mode unavailable", notice);
                Assert.False(pool.TryDequeueNotice(out _));
            }
        }

        [Fact]
        public async Task SimulatorFailureBacksOffAndUsesTheCache()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, "stash.json"), @"{""JobId"":""seed-1"",""Source"":""NoisyChip"",""Counts"":{""011010"":2,""bad"":5}}");
            var atlas = new FakeAtlas { FailModes = { "emu" } };
            using (var pool = new StashPool(atlas, Cfg("Mode=emu\n"), _dir))
            {
                for (var tick = 0; tick < 5; tick++)
                    pool.Prefetch();
                await pool.WhenIdleAsync();
                Assert.Equal(1, atlas.Calls);
                var shot = pool.Draw();
                Assert.Equal(ShotSource.OfflineCache, shot.Source);
                Assert.Equal("011010", shot.Bits);
                Assert.True(pool.TryDequeueNotice(out var notice));
                Assert.Contains("OFFLINE", notice);
            }
        }

        [Fact]
        public void NoKeyMeansNoCallsAndNoData()
        {
            using (var pool = new StashPool(null, Cfg(), _dir))
            {
                pool.Prefetch();
                Assert.Null(pool.Draw());
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }
    }
}
