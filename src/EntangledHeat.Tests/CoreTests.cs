using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EntangledHeat.Core;
using EntangledHeat.Core.Atlas;
using EntangledHeat.Core.Heat;
using EntangledHeat.Core.Quantum;
using Newtonsoft.Json.Linq;
using Xunit;

namespace EntangledHeat.Tests
{
    public class ConfigTests
    {
        [Fact]
        public void ParsesValuesAndKeepsDefaults()
        {
            var c = Config.Parse("[Atlas]\r\nApiKey = abc123 \r\nUseIbm=false\r\n; comment\r\n[Gameplay]\r\nCollapseIntervalSeconds=12.5\r\n");
            Assert.Equal("abc123", c.ApiKey);
            Assert.False(c.UseIbm);
            Assert.Equal(12.5f, c.CollapseIntervalSeconds);
            Assert.Equal(4, c.IbmFromWantedLevel);
            Assert.True(c.EnableHeatSystem);
            Assert.Equal("F10", c.ToggleKey);
            Assert.True(c.PhoneTexts);
        }

        [Fact]
        public void EmptyKeyMeansNoKey() => Assert.False(Config.Parse("[Atlas]\nApiKey=\n").HasApiKey);

        [Fact]
        public void ExampleIniParses()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "config", "EntangledHeat.example.ini");
            var c = Config.Load(path);
            Assert.False(c.HasApiKey);
            Assert.Equal("https://api.mothquantum.com", c.ApiBase);
        }
    }

    public class HeatRequestTests
    {
        static List<double> Fractions(JObject p) =>
            p["operations"].Where(o => (string)o["type"] == "relationship").Select(o => (double)o["fraction"]).ToList();

        [Fact]
        public void LowStarsAreThreeLooselyLinkedPairs()
        {
            var p = HeatRequests.HeatGraph(1, "emu");
            Assert.Equal(6, (int)p["num_qubits"]);
            Assert.Equal("[[0,1],[2,3],[4,5]]", p["coupling_map"].ToString(Newtonsoft.Json.Formatting.None));
            Assert.Equal(new[] { 0.1, 0.1, 0.1 }, Fractions(p));
            Assert.Equal(6, p["operations"].Count(o => (string)o["type"] == "bloch" && (double)o["paulis"]["X"] == 1.0));
        }

        [Fact]
        public void MoreStarsTightenTheLinks()
        {
            Assert.Equal(new[] { 0.5, 0.5, 0.5 }, Fractions(HeatRequests.HeatGraph(3, "emu")));
            Assert.Equal(new[] { 1.0, 1.0, 1.0 }, Fractions(HeatRequests.HeatGraph(4, "emu")));
        }

        [Fact]
        public void FiveStarsChainTheWholeSquadInOrder()
        {
            var p = HeatRequests.HeatGraph(5, "qpu");
            Assert.Equal("[[0,1],[1,2],[2,3],[3,4],[4,5]]", p["coupling_map"].ToString(Newtonsoft.Json.Formatting.None));
            Assert.Equal(new[] { 1.0, 0.25, 1.0, 0.25, 1.0 }, Fractions(p));
            Assert.Equal("qpu", (string)p["mode"]);
        }

        [Fact]
        public void SearchGridHasAllAdjacentEdges()
        {
            var p = HeatRequests.SearchGrid(4, 4, "emu");
            Assert.Equal(24, ((JArray)p["level_data"]["coupling_map"]).Count);
            Assert.Equal(16, (int)p["level_data"]["num_qubits"]);
        }
    }

    public class MeasurementParserTests
    {
        [Fact]
        public void ParsesRealGraphResult()
        {
            // A real graph-v1 job saved by src/tools/atlas_probe.py.
            var samples = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "samples");
            var file = Directory.GetFiles(samples, "heat-graph-L1-*.json").OrderBy(f => f).Last();
            var body = (JObject)JObject.Parse(File.ReadAllText(file))["result"];
            var counts = MeasurementParser.ParseCounts(body, 6);
            Assert.Equal(20, counts.Count);
            Assert.All(counts, c => Assert.Equal(6, c.Key.Length));
            // Atlas returns only the top 20 outcomes; loose 1-star links spread shots widely (~470 of 1024).
            Assert.InRange(counts.Sum(c => c.Value), 400, 1024);
        }

        [Fact]
        public void ParsesRealLabyrinthResultAsShotCounts()
        {
            // A real labyrinth-v1 job: probabilities only, so counts come back as probability × num_shots (4096).
            var samples = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "samples");
            var file = Directory.GetFiles(samples, "search-grid-*.json").OrderBy(f => f).Last();
            var body = (JObject)JObject.Parse(File.ReadAllText(file))["result"];
            var counts = MeasurementParser.ParseCounts(body, 16);
            Assert.NotEmpty(counts);
            Assert.All(counts, c => Assert.Equal(16, c.Key.Length));
            var top = (JObject)body.SelectToken("$..results.measurements[0]");
            var expected = (int)Math.Round((double)top["probability"] * 4096);
            Assert.Equal(expected, counts.Single(c => c.Key == (string)top["bitstring"]).Value);
        }

        [Fact]
        public void ParsesListOfEntries()
        {
            var body = JObject.Parse(@"{""result"":{""output"":{""measurements"":{""top"":[
                {""bitstring"":""110011"",""count"":40,""probability"":0.04},
                {""bitstring"":""001100"",""count"":25}],""dominant_bitstring"":""110011"",""edge_agreement_score"":0.6}}}}");
            var counts = MeasurementParser.ParseCounts(body, 6);
            Assert.Equal(2, counts.Count);
            Assert.Contains(new KeyValuePair<string, int>("110011", 40), counts);
        }

        [Fact]
        public void ParsesCountsDictionary()
        {
            var body = JObject.Parse(@"{""output"":{""measurements"":{""counts"":{""000111"":7,""111000"":3},""dominant_bitstring"":""000111""}}}");
            var counts = MeasurementParser.ParseCounts(body, 6);
            Assert.Equal(10, counts.Sum(c => c.Value));
        }

        [Fact]
        public void FallsBackToProbabilities()
        {
            var body = JObject.Parse(@"{""output"":{""measurements"":[{""state"":""101010"",""probability"":0.5}]}}");
            Assert.Equal(500, MeasurementParser.ParseCounts(body, 6).Single().Value);
        }

        [Fact]
        public void IgnoresWrongLengthBitstrings()
        {
            var body = JObject.Parse(@"{""output"":{""measurements"":[{""bitstring"":""101"",""count"":5}]}}");
            Assert.Empty(MeasurementParser.ParseCounts(body, 6));
        }
    }

    public class ShotDeckTests
    {
        [Fact]
        public void HandsOutEveryShotExactlyOnce()
        {
            var deck = new ShotDeck(new Dictionary<string, int> { ["111111"] = 3, ["000000"] = 1 }, "job-1", ShotSource.Simulator, new Random(1));
            var drawn = new List<string>();
            Shot s;
            while ((s = deck.Draw()) != null)
                drawn.Add(s.Bits);
            Assert.Equal(3, drawn.Count(b => b == "111111"));
            Assert.Equal(1, drawn.Count(b => b == "000000"));
            Assert.Equal(0, deck.Remaining);
        }

        [Fact]
        public void RunningLowAtThreeQuartersUsed()
        {
            var deck = new ShotDeck(new Dictionary<string, int> { ["101010"] = 8 }, "j", ShotSource.Simulator, new Random(1));
            for (var i = 0; i < 5; i++) deck.Draw();
            Assert.False(deck.RunningLow);
            deck.Draw();
            Assert.True(deck.RunningLow);
        }

        [Fact]
        public void FirstShotFollowsTheMeasuredFrequencies()
        {
            // Each collapse uses a new job's first shot, so that shot must be an unbiased draw.
            var seeds = new Random(7);
            var counts = new Dictionary<string, int> { ["111111"] = 10, ["000000"] = 30, ["101010"] = 60 };
            var firsts = Enumerable.Range(0, 20000)
                .Select(_ => new ShotDeck(counts, "j", ShotSource.Simulator, new Random(seeds.Next())).Draw().Bits)
                .GroupBy(b => b).ToDictionary(g => g.Key, g => g.Count() / 20000.0);
            Assert.InRange(firsts["111111"], 0.09, 0.11);
            Assert.InRange(firsts["000000"], 0.28, 0.32);
            Assert.InRange(firsts["101010"], 0.58, 0.62);
        }

        [Fact]
        public void OnlyTheFirstLiveShotIsFresh()
        {
            var deck = new ShotDeck(new Dictionary<string, int> { ["101010"] = 3 }, "1a2b3c4d-9999", ShotSource.Simulator, new Random(1));
            var first = deck.Draw();
            var second = deck.Draw();
            Assert.True(first.Fresh);
            Assert.Equal("new job 1a2b3c4d", first.JobLabel);
            Assert.False(second.Fresh);
            Assert.Equal("earlier job 1a2b3c4d", second.JobLabel);

            var cached = new ShotDeck(new Dictionary<string, int> { ["101010"] = 1 }, "seed-1", ShotSource.OfflineCache, new Random(1)).Draw();
            Assert.False(cached.Fresh);
            Assert.Equal("job seed-1", cached.JobLabel);
        }
    }

    public class HeatCollapseTests
    {
        [Fact]
        public void BitsMapToOrders()
        {
            var orders = HeatCollapse.Orders(new Shot("100110", "j", ShotSource.Simulator), 8);
            Assert.Equal(new[]
            {
                UnitOrder.Pursue, UnitOrder.Intercept, UnitOrder.Intercept, UnitOrder.Pursue, UnitOrder.Pursue, UnitOrder.Intercept,
                UnitOrder.Pursue, UnitOrder.Intercept, // cars 7 and 8 reuse qubits 0 and 1
            }, orders);
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 0)]
        [InlineData(4, 5)]
        public void PartnersArePairs(int q, int partner) => Assert.Equal(partner, HeatCollapse.PartnerOf(q));

        [Fact]
        public void ChoiceRedrawsOutOfRangeValues()
        {
            // 3 options need 2 bits: "11" (=3) is rejected, then "10" (=2) is accepted.
            var shots = new Queue<Shot>(new[] { new Shot("110000", "j", ShotSource.Simulator), new Shot("100000", "j", ShotSource.Simulator) });
            Assert.Equal(2, HeatCollapse.CollapseChoice(() => shots.Count > 0 ? shots.Dequeue() : null, 3));
        }

        [Fact]
        public void ChoiceWithoutDataIsMinusOne() => Assert.Equal(-1, HeatCollapse.CollapseChoice(() => null, 3));
    }

    public class SearchGridTests
    {
        static SearchLayout Layout(string bits) => SearchGrid.Layout(new Shot(bits, "j", ShotSource.Simulator));

        [Fact]
        public void WholeGridSearchedMeansPerimeterCordon()
        {
            var layout = Layout(new string('1', 16));
            Assert.Equal(16, layout.SearchedCount);
            Assert.Equal(16, layout.Cordon.Count); // 4 sides × 4 blocks, no inner streets
            Assert.All(layout.Cordon, p => Assert.True(Math.Abs(p.X) == 2f || Math.Abs(p.Y) == 2f));
        }

        [Fact]
        public void NothingSearchedMeansNoCordon() => Assert.Empty(Layout(new string('0', 16)).Cordon);

        [Fact]
        public void OneSearchedBlockIsSealedOnFourSides()
        {
            // Block 5 = row 1, col 1; its centre is (-0.5, -0.5) from the grid centre.
            var layout = Layout("0000010000000000");
            Assert.Equal(4, layout.Cordon.Count);
            Assert.All(layout.Cordon, p => Assert.Equal(5, p.Block));
            Assert.Contains(layout.Cordon, p => p.X == -0.5f && p.Y == -1f);
            Assert.Contains(layout.Cordon, p => p.X == 0f && p.Y == -0.5f);
        }

        [Fact]
        public void NeighbouringSearchedBlocksShareNoRoadblock()
        {
            // Blocks 5 and 6 side by side: 6 streets around the pair, none between them.
            var layout = Layout("0000011000000000");
            Assert.Equal(6, layout.Cordon.Count);
            Assert.DoesNotContain(layout.Cordon, p => p.X == 0f && p.Y == -0.5f);
        }

        [Fact]
        public void NearestSortsByDistance()
        {
            var nearest = Layout(new string('1', 16)).Nearest(2f, 2f, 2);
            Assert.Equal(2, nearest.Count);
            Assert.All(nearest, p => Assert.True(p.X == 1.5f || p.Y == 1.5f));
        }

        [Fact]
        public void WrongBitCountThrows() =>
            Assert.Throws<ArgumentException>(() => Layout("101010"));
    }

    public class HudTextTests
    {
        [Fact]
        public void ShowsStatusStarsSourceJobAndBits()
        {
            var line = HudText.Line(AtlasStatus.Connected, 4, new Shot("010110", "1a2b3c4d-9999", ShotSource.IbmHardware));
            Assert.Contains("ATLAS ~g~OK", line);
            Assert.Contains("4 STARS", line);
            Assert.Contains("IBM HARDWARE", line);
            Assert.Contains("job 1a2b3c4d", line);
            Assert.Contains("|010110>", line);
            Assert.DoesNotContain("new job", line);
            Assert.Contains("new job 1a2b3c4d", HudText.Line(AtlasStatus.Connected, 4, new Shot("010110", "1a2b3c4d-9999", ShotSource.Simulator, fresh: true)));
        }

        [Fact]
        public void NoKeyAndNotWantedIsJustTheStatus() =>
            Assert.Equal("~p~ATLAS ~r~NO KEY", HudText.Line(AtlasStatus.NoKey, 0, null));

        [Fact]
        public void ShowsWhenQuantumPoliceAreOff()
        {
            var line = HudText.Line(AtlasStatus.Connected, 3, new Shot("000000", "j", ShotSource.Simulator), quantumOn: false, toggleKey: "F10");
            Assert.Contains("QUANTUM POLICE OFF (F10)", line);
            Assert.DoesNotContain("|000000>", line);
        }

        [Fact]
        public void ShowsWhenPausedForAStoryMission()
        {
            var line = HudText.Line(AtlasStatus.Connected, 2, new Shot("000000", "j", ShotSource.Simulator), missionPaused: true);
            Assert.Contains("PAUSED: STORY MISSION", line);
            Assert.DoesNotContain("|000000>", line);
            Assert.True(Config.Parse("").PauseDuringMissions);
            Assert.False(Config.Parse("[Gameplay]\nPauseDuringMissions=false\n").PauseDuringMissions);
        }

        [Fact]
        public void LiveShotOverridesStaleOfflineStatus()
        {
            Assert.Contains("OK", HudText.Line(AtlasStatus.Offline, 2, new Shot("000000", "j", ShotSource.Simulator)));
            Assert.Contains("OFFLINE", HudText.Line(AtlasStatus.Offline, 2, new Shot("000000", "j", ShotSource.OfflineCache)));
        }
    }

    public class StoryTests
    {
        static readonly DateTime T0 = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void OnceEventsTextOnlyOncePerSession()
        {
            var story = new Story(new Random(1));
            Assert.NotNull(story.Text(StoryEvent.FirstWanted, T0));
            Assert.Null(story.Text(StoryEvent.FirstWanted, T0.AddMinutes(5)));
        }

        [Fact]
        public void CooldownStopsTextSpamButNotEscapes()
        {
            var story = new Story(new Random(1));
            Assert.NotNull(story.Text(StoryEvent.Decoherence, T0));
            Assert.Null(story.Text(StoryEvent.Decoherence, T0.AddSeconds(5)));
            Assert.NotNull(story.Text(StoryEvent.Escaped, T0.AddSeconds(6)));
            Assert.NotNull(story.Text(StoryEvent.Decoherence, T0.AddSeconds(30)));
        }

        [Fact]
        public void OnceEventBlockedByCooldownIsSentLater()
        {
            var story = new Story(new Random(1));
            story.Text(StoryEvent.Decoherence, T0);
            Assert.Null(story.Text(StoryEvent.FiveStars, T0.AddSeconds(2)));
            Assert.NotNull(story.Text(StoryEvent.FiveStars, T0.AddSeconds(20)));
        }

        [Fact]
        public void TipsShowOnceAndOnlyForTaughtEvents()
        {
            var story = new Story();
            Assert.Contains("number", story.Tip(StoryEvent.Collapse));
            Assert.Null(story.Tip(StoryEvent.Collapse));
            Assert.Null(story.Tip(StoryEvent.Escaped));
        }
    }

    public class QuantumPoolTests : IDisposable
    {
        readonly string _dir = Path.Combine(Path.GetTempPath(), "eh-tests-" + Guid.NewGuid().ToString("N"));

        sealed class FakeAtlas : IAtlasClient
        {
            public int Calls;
            public bool Fail;
            public List<string> Modes = new List<string>();

            public Task<AtlasJobResult> RunAsync(string engineId, JObject parameters, TimeSpan timeout, CancellationToken ct)
            {
                Interlocked.Increment(ref Calls);
                lock (Modes) Modes.Add((string)parameters["mode"]);
                if (Fail)
                    throw new AtlasException("offline");
                var body = engineId == EngineIds.Labyrinth
                    ? JObject.Parse(@"{""result"":{""output"":{""metrics"":{""num_shots"":8},""results"":{""measurements"":[{""bitstring"":""0110011001100110"",""probability"":0.5}]}}}}")
                    : JObject.Parse(@"{""result"":{""output"":{""measurements"":{""top"":[{""bitstring"":""110011"",""count"":4}]}}}}");
                return Task.FromResult(new AtlasJobResult { JobId = "job-" + Calls, Body = body, Elapsed = TimeSpan.FromSeconds(1) });
            }
        }

        static Config Cfg(string extra = "") => Config.Parse("[Atlas]\nApiKey=x\n" + extra);

        [Fact]
        public async Task PrefetchFillsDeckAndCachesIt()
        {
            var atlas = new FakeAtlas();
            using (var pool = new QuantumPool(atlas, Cfg("UseIbm=false\n"), _dir))
            {
                Assert.Null(pool.Draw(2));
                pool.Prefetch(2);
                await pool.WhenIdleAsync();
                var shot = pool.Draw(2);
                Assert.Equal("110011", shot.Bits);
                Assert.Equal(ShotSource.Simulator, shot.Source);
            }
            Assert.True(File.Exists(Path.Combine(_dir, "heat-L2.json")));

            // A new session with Atlas down replays the cached real shots, labelled offline.
            using (var offline = new QuantumPool(new FakeAtlas { Fail = true }, Cfg("UseIbm=false\n"), _dir))
            {
                offline.Prefetch(2);
                await offline.WhenIdleAsync();
                var shot = offline.Draw(2);
                Assert.Equal(ShotSource.OfflineCache, shot.Source);
                Assert.True(offline.TryDequeueNotice(out var notice));
                Assert.Contains("OFFLINE", notice);
            }
        }

        [Fact]
        public async Task HighStarsAlsoRequestIbmOnce()
        {
            var atlas = new FakeAtlas();
            using (var pool = new QuantumPool(atlas, Cfg("IbmFromWantedLevel=4\n"), _dir))
            {
                pool.Prefetch(4);
                pool.Prefetch(4);
                await pool.WhenIdleAsync();
                Assert.Equal(1, atlas.Modes.Count(m => m == "qpu"));
                Assert.Equal(ShotSource.IbmHardware, pool.Draw(4).Source);
                Assert.Equal(1, pool.IbmArrivals);
                Assert.True(pool.TryDequeueNotice(out var notice));
                Assert.Contains("IBM", notice);
            }
        }

        [Fact]
        public async Task EachCollapseGetsItsOwnNewJob()
        {
            var atlas = new FakeAtlas();
            using (var pool = new QuantumPool(atlas, Cfg("UseIbm=false\n"), _dir))
            {
                pool.Prefetch(2);
                await pool.WhenIdleAsync();
                var first = pool.Draw(2);
                Assert.True(first.Fresh);
                Assert.Equal("job-1", first.JobId);

                // The next job hasn't been fetched yet: the collapse takes another shot from the last job, labelled as such.
                var earlier = pool.Draw(2);
                Assert.False(earlier.Fresh);
                Assert.Equal("job-1", earlier.JobId);

                pool.Prefetch(2);
                await pool.WhenIdleAsync();
                var second = pool.Draw(2);
                Assert.True(second.Fresh);
                Assert.Equal("job-2", second.JobId);
                Assert.Equal(2, pool.JobsCompleted);

                // One unused job waits per slot; prefetching again doesn't stack more.
                pool.Prefetch(2);
                pool.Prefetch(2);
                await pool.WhenIdleAsync();
                Assert.Equal(3, atlas.Calls);
            }
        }

        [Fact]
        public async Task WithoutFreshModeOneJobServesManyCollapses()
        {
            var atlas = new FakeAtlas();
            using (var pool = new QuantumPool(atlas, Cfg("UseIbm=false\nFreshJobPerCollapse=false\n"), _dir))
            {
                pool.Prefetch(2);
                await pool.WhenIdleAsync();
                Assert.True(pool.Draw(2).Fresh);
                pool.Prefetch(2);
                await pool.WhenIdleAsync();
                Assert.False(pool.Draw(2).Fresh); // 4 shots, 2 used: not running low, so no new job
                Assert.Equal(1, atlas.Calls);
            }
        }

        [Fact]
        public async Task FreshHardwareJobWinsThenTheNextOneStarts()
        {
            var atlas = new FakeAtlas();
            using (var pool = new QuantumPool(atlas, Cfg("IbmFromWantedLevel=4\n"), _dir))
            {
                pool.Prefetch(4);
                await pool.WhenIdleAsync();
                var hw = pool.Draw(4);
                Assert.Equal(ShotSource.IbmHardware, hw.Source);
                Assert.True(hw.Fresh);
                var sim = pool.Draw(4); // the waiting simulator job is still new
                Assert.Equal(ShotSource.Simulator, sim.Source);
                Assert.True(sim.Fresh);

                pool.Prefetch(4);
                await pool.WhenIdleAsync();
                Assert.Equal(2, atlas.Modes.Count(m => m == "qpu"));
                Assert.Equal(2, pool.IbmArrivals);
                Assert.True(pool.TryDequeueNotice(out _));
                Assert.False(pool.TryDequeueNotice(out _)); // hardware announced once, not every job
            }
        }

        [Fact]
        public async Task FailedJobsBackOffInsteadOfRetryingEveryTick()
        {
            var atlas = new FakeAtlas { Fail = true };
            using (var pool = new QuantumPool(atlas, Cfg("IbmFromWantedLevel=4\n"), _dir))
            {
                for (var tick = 0; tick < 5; tick++)
                    pool.Prefetch(4);
                await pool.WhenIdleAsync();
                Assert.Equal(1, atlas.Modes.Count(m => m == "qpu"));
                Assert.Equal(1, atlas.Modes.Count(m => m == "emu"));
            }
        }

        [Fact]
        public async Task SearchGridHasItsOwnDeckAndCache()
        {
            var atlas = new FakeAtlas();
            using (var pool = new QuantumPool(atlas, Cfg("UseIbm=false\n"), _dir))
            {
                Assert.Null(pool.DrawGrid());
                pool.PrefetchGrid(2);
                await pool.WhenIdleAsync();
                Assert.Null(pool.Draw(2)); // heat decks untouched
                var drawn = Enumerable.Range(0, 4).Select(_ => pool.DrawGrid()).ToList();
                Assert.All(drawn, s => Assert.Equal("0110011001100110", s.Bits));
                Assert.Null(pool.DrawGrid()); // 0.5 × 8 shots = 4 real shots, no more
            }
            Assert.True(File.Exists(Path.Combine(_dir, "grid.json")));
            Assert.False(File.Exists(Path.Combine(_dir, "heat-L2.json")));
        }

        [Fact]
        public void BundledCacheIsUsedWithoutAKey()
        {
            // The release ships real jobs in this format, so a judge's first run has quantum data.
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, "heat-L3.json"), @"{""JobId"":""seed-1"",""Source"":""IbmHardware"",""Counts"":{""111100"":2,""bad"":5}}");
            using (var pool = new QuantumPool(null, Cfg(), _dir))
            {
                var shot = pool.Draw(3);
                Assert.Equal("111100", shot.Bits);
                Assert.Equal(ShotSource.OfflineCache, shot.Source);
                Assert.Equal("seed-1", shot.JobId);
            }
        }

        [Fact]
        public void NoClientMeansNoCalls()
        {
            using (var pool = new QuantumPool(null, Cfg(), _dir))
            {
                pool.Prefetch(3);
                Assert.Null(pool.Draw(3));
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }
    }
}
