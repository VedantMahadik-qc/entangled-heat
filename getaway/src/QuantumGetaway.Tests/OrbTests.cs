using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using QuantumGetaway.Core;
using QuantumGetaway.Core.Atlas;
using QuantumGetaway.Core.Orbs;
using QuantumGetaway.Core.Tunnel;
using Xunit;

namespace QuantumGetaway.Tests
{
    public class OrbPairTests
    {
        [Fact]
        public void ThreeAntiCorrelatedPairs()
        {
            var p = OrbPairs.PairGraph("fake", "marrakesh");
            Assert.Equal(6, (int)p["num_qubits"]);
            Assert.Equal("fake", (string)p["mode"]);
            Assert.Equal("marrakesh", (string)p["backend_name"]);
            var links = p["operations"].Where(o => (string)o["type"] == "relationship").ToList();
            Assert.Equal(3, links.Count);
            Assert.All(links, l => Assert.Equal(-1.0, (double)l["paulis"]["ZZ"]));
            Assert.Null(OrbPairs.PairGraph("emu", "marrakesh")["backend_name"]);
        }

        [Theory]
        [InlineData("100000", 0, 0, OrbResult.GoldHere)]
        [InlineData("100000", 0, 1, OrbResult.GoldThere)]
        [InlineData("000110", 1, 1, OrbResult.GoldHere)]
        [InlineData("000011", 2, 0, OrbResult.BothGold)]
        [InlineData("110000", 2, 1, OrbResult.BothPlain)]
        public void ResolvesFromTheGrabbedSide(string bits, int slot, int side, OrbResult expected) =>
            Assert.Equal(expected, OrbPairs.Resolve(new Shot(bits, "j", ShotSource.Simulator), slot, side));

        [Fact]
        public void RealNoisyChipResultIsMostlyAntiCorrelated()
        {
            // A real graph-v1 job on fake Marrakesh (2 Nov 2026), same circuit.
            var sample = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "orbs-fake-marrakesh.json")));
            var counts = MeasurementParser.ParseCounts(sample["result"], OrbPairs.Qubits);
            Assert.NotEmpty(counts);
            var total = counts.Sum(c => c.Value);
            for (var slot = 0; slot < OrbPairs.PairsPerJob; slot++)
            {
                var opposite = counts.Where(c => c.Key[2 * slot] != c.Key[2 * slot + 1]).Sum(c => c.Value);
                Assert.InRange(opposite / (double)total, 0.95, 1.0); // noise breaks a few pairs, never most
            }
        }
    }

    public class TwinTests
    {
        [Theory]
        [InlineData("10", TwinResult.Right)]
        [InlineData("01", TwinResult.Left)]
        [InlineData("11", TwinResult.Both)]
        [InlineData("00", TwinResult.Neither)]
        public void FirstPairOfTheShotPicksTheTwin(string bits, TwinResult expected) =>
            Assert.Equal(expected, OrbPairs.ResolveTwins(new Shot(bits + "0110", "j", ShotSource.NoisyChip)));

        [Fact]
        public void MirroredCellsFormOnePair()
        {
            var pixels = new byte[32 * 32];
            pixels[5 * 32 + 18] = 30; // (+3, 5)
            pixels[5 * 32 + 12] = 10; // (-3, 5): its twin
            pixels[9 * 32 + 15] = 60; // (0, 9): straight ahead, no twin
            var wave = new Wavefunction(TunnelKind.Leap, 32, pixels, "j", ShotSource.Simulator);
            var pairs = wave.ExitPairs(10);
            Assert.Equal(2, pairs.Count);
            Assert.Equal(0, pairs[0].Side); // 0.6 beats 0.4
            Assert.False(pairs[0].HasTwin);
            Assert.Equal(3, pairs[1].Side);
            Assert.Equal(5, pairs[1].Dy);
            Assert.Equal(0.4, pairs[1].P, 6);
            Assert.Equal(-3, pairs[1].Left.Dx);
            var pair = wave.PairOf(new Cell(-3, 5, 0.1));
            Assert.Equal(3, pair.Side);
            Assert.Equal(0.4, pair.P, 6);
        }

        [Fact]
        public void PortalShowsTwoSeparatedTwinPairsAhead()
        {
            var wave = new Wavefunction(TunnelKind.Leap, 32, Png.DecodeLuma(PngTests.Fixture("leap-sim-7c8eb7bb.png"), out _, out _), "j", ShotSource.Simulator);
            var options = wave.ExitOptions(2, 6, 4);
            Assert.Equal(2, options.Count);
            Assert.All(options, o => Assert.True(o.HasTwin && o.Dy >= 6));
            Assert.True(Math.Max(Math.Abs(options[0].Side - options[1].Side), Math.Abs(options[0].Dy - options[1].Dy)) >= 4);
            Assert.True(options[0].P >= options[1].P);
        }

        [Fact]
        public void EveryPortalDrawsADifferentSetOfExits()
        {
            var wave = new Wavefunction(TunnelKind.Leap, 32, Png.DecodeLuma(PngTests.Fixture("leap-sim-7c8eb7bb.png"), out _, out _), "j", ShotSource.Simulator);
            var rng = new Random(11);
            var sets = Enumerable.Range(0, 30)
                .Select(_ => wave.ExitOptions(2, 6, 4, rng))
                .Select(o => string.Join(";", o.Select(p => $"{p.Side},{p.Dy}")))
                .Distinct()
                .Count();
            Assert.True(sets > 15);
        }

        [Fact]
        public void ChoosingFollowsTheShownOdds()
        {
            var options = new[] { new ExitPair(3, 10, 0.03), new ExitPair(8, 20, 0.01) };
            var rng = new Random(7);
            var first = Enumerable.Range(0, 20000).Count(_ => Wavefunction.Choose(options, rng) == 0);
            Assert.InRange(first / 20000.0, 0.73, 0.77); // 0.03 / 0.04
        }

        [Fact]
        public void RealLeapPairsCoverTheWholeDistribution()
        {
            var wave = new Wavefunction(TunnelKind.Leap, 32, Png.DecodeLuma(PngTests.Fixture("leap-sim-7c8eb7bb.png"), out _, out _), "j", ShotSource.Simulator);
            var all = wave.ExitPairs(10000);
            Assert.Equal(1.0, all.Sum(p => p.P), 6);
            Assert.True(all.Count(p => p.HasTwin) > all.Count / 2);
        }
    }

    public class OrbPoolTests
    {
        sealed class FakeGraph : IAtlasClient
        {
            public int Jobs;
            public string LastMode;

            public Task<string> UploadAsync(byte[] data, string filename, string contentType, CancellationToken ct) =>
                throw new InvalidOperationException("graph-v1 needs no upload");

            public Task<AtlasJobResult> RunAsync(string engineId, JObject parameters, JObject inputFiles, TimeSpan timeout, CancellationToken ct)
            {
                Assert.Equal("graph-v1", engineId);
                Assert.Null(inputFiles);
                LastMode = (string)parameters["mode"];
                var n = Interlocked.Increment(ref Jobs);
                var body = JObject.Parse("{\"result\":{\"output\":{\"measurements\":[{\"bitstring\":\"100110\",\"count\":3},{\"bitstring\":\"011001\",\"count\":2}]}}}");
                return Task.FromResult(new AtlasJobResult { JobId = $"orbs-{n}", Body = body, Elapsed = TimeSpan.FromSeconds(4) });
            }

            public Task<byte[]> DownloadAsync(string url, CancellationToken ct) => throw new InvalidOperationException();
        }

        [Fact]
        public async Task EveryPairGetsANewJobAndOffNoisyChipByDefault()
        {
            var dir = Path.Combine(Path.GetTempPath(), "qg-orbs-" + Guid.NewGuid().ToString("N"));
            var atlas = new FakeGraph();
            var pool = new OrbPool(atlas, Config.Parse(""), dir);
            pool.Prefetch();
            await pool.WhenIdleAsync();
            Assert.Equal("fake", atlas.LastMode); // OrbMode defaults to the noisy IBM chip snapshot

            var first = pool.Draw();
            Assert.True(first.Fresh);
            Assert.Equal(6, first.Bits.Length);
            Assert.False(pool.Draw().Fresh); // another shot of the same job until the next one lands

            pool.Prefetch();
            await pool.WhenIdleAsync();
            Assert.True(pool.Draw().Fresh);
            Assert.Equal(2, atlas.Jobs);
        }

        [Fact]
        public void OrbModeIsParsed()
        {
            Assert.Equal("emu", Config.Parse("[Atlas]\nOrbMode=EMU\n").OrbMode);
            Assert.Equal("fake", Config.Parse("[Atlas]\nOrbMode=sim\n").OrbMode); // graph-v1 has no "sim"
        }
    }
}
