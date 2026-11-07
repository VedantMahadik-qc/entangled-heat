using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace QuantumGetaway.Core.Atlas
{
    /// <summary>
    /// Pulls (bitstring, count) pairs out of an Atlas result.
    /// graph-v1 (checked against real jobs, 29 Oct 2026): result.output.measurements =
    /// [{bitstring, count, probability}] for the top 20 outcomes. labyrinth-v1 nests the same list
    /// under output.results.measurements with probability only, so counts are rebuilt as
    /// probability × output.metrics.num_shots. Other shapes are accepted defensively.
    /// </summary>
    public static class MeasurementParser
    {
        static readonly Regex Bits = new Regex("^[01]+$");
        static readonly string[] CountKeys = { "count", "counts", "shots", "frequency" };
        static readonly string[] ProbabilityKeys = { "probability", "prob", "p" };
        const int DefaultShots = 1000;

        public static List<KeyValuePair<string, int>> ParseCounts(JToken body, int numQubits)
        {
            var found = new List<KeyValuePair<string, int>>();
            var shots = ShotsOf(body);
            foreach (var token in body.SelectTokens("$..measurements").Concat(body.SelectTokens("$..counts")))
            {
                Collect(token, numQubits, shots, found);
                if (found.Count > 0)
                    break;
            }

            return found
                .GroupBy(p => p.Key)
                .Select(g => new KeyValuePair<string, int>(g.Key, g.Sum(p => p.Value)))
                .Where(p => p.Value > 0)
                .ToList();
        }

        /// <summary>Total shots of the job, used to turn probabilities back into counts.</summary>
        static int ShotsOf(JToken body)
        {
            var token = body.SelectTokens("$..num_shots").Concat(body.SelectTokens("$..shots"))
                .FirstOrDefault(t => t.Type == JTokenType.Integer && (int)t > 0);
            return token != null ? (int)token : DefaultShots;
        }

        static void Collect(JToken token, int numQubits, int shots, List<KeyValuePair<string, int>> found)
        {
            if (token is JArray array)
            {
                foreach (var item in array)
                    if (item is JObject entry && TryEntry(entry, numQubits, shots, out var pair))
                        found.Add(pair);
                return;
            }

            if (!(token is JObject obj))
                return;

            // {"010011": 37, ...}
            foreach (var prop in obj.Properties())
            {
                var key = Clean(prop.Name);
                if (key.Length == numQubits && Bits.IsMatch(key) && prop.Value.Type == JTokenType.Integer)
                    found.Add(new KeyValuePair<string, int>(key, (int)prop.Value));
            }
            if (found.Count > 0)
                return;

            // {"top": [{"bitstring": ..., "count": ...}], "dominant_bitstring": ...}
            foreach (var prop in obj.Properties())
                if (prop.Value is JArray || (prop.Value is JObject && prop.Name != "tomography"))
                {
                    Collect(prop.Value, numQubits, shots, found);
                    if (found.Count > 0)
                        return;
                }
        }

        static bool TryEntry(JObject entry, int numQubits, int shots, out KeyValuePair<string, int> pair)
        {
            pair = default;
            var bits = entry.Properties()
                .Where(p => p.Value.Type == JTokenType.String)
                .Select(p => Clean((string)p.Value))
                .FirstOrDefault(s => s.Length == numQubits && Bits.IsMatch(s));
            if (bits == null)
                return false;

            var count = CountKeys.Select(k => entry[k]).FirstOrDefault(t => t != null && t.Type == JTokenType.Integer);
            if (count != null)
            {
                pair = new KeyValuePair<string, int>(bits, (int)count);
                return true;
            }

            var probability = ProbabilityKeys.Select(k => entry[k]).FirstOrDefault(t => t != null && (t.Type == JTokenType.Float || t.Type == JTokenType.Integer));
            if (probability != null)
            {
                pair = new KeyValuePair<string, int>(bits, (int)Math.Round((double)probability * shots));
                return true;
            }
            return false;
        }

        static string Clean(string s) => s.Replace(" ", "").Trim();
    }
}
