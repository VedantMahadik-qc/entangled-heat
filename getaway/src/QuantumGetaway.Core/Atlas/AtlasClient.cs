using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace QuantumGetaway.Core.Atlas
{
    public sealed class AtlasJobResult
    {
        public string JobId { get; set; }
        public JObject Body { get; set; }
        public TimeSpan Elapsed { get; set; }
    }

    public enum AtlasStatus { NoKey, Checking, Connected, InvalidKey, Offline }

    public sealed class AtlasException : Exception
    {
        public AtlasException(string message, int statusCode = 0) : base(message) => StatusCode = statusCode;

        /// <summary>The HTTP status, or 0 if the job itself failed.</summary>
        public int StatusCode { get; }
    }

    public interface IAtlasClient
    {
        /// <summary>Upload a file as an Atlas asset and return its id.</summary>
        Task<string> UploadAsync(byte[] data, string filename, string contentType, CancellationToken ct);

        /// <summary>Submit a job, poll until it finishes and return its result body.</summary>
        Task<AtlasJobResult> RunAsync(string engineId, JObject parameters, JObject inputFiles, TimeSpan timeout, CancellationToken ct);

        /// <summary>Fetch a job output from its presigned URL.</summary>
        Task<byte[]> DownloadAsync(string url, CancellationToken ct);
    }

    /// <summary>Moth Atlas wire contract: assets → process → poll status → result → output file.</summary>
    public sealed class AtlasClient : IAtlasClient, IDisposable
    {
        readonly HttpClient _http;
        readonly HttpClient _storage; // presigned URLs: never send them our API key

        static AtlasClient()
        {
            // .NET Framework 4.8 inside GTA may default to old TLS versions.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        public AtlasClient(string apiBase, string apiKey)
        {
            _http = new HttpClient { BaseAddress = new Uri(apiBase.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(60) };
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("QuantumGetaway/0.1");
            _storage = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            _storage.DefaultRequestHeaders.UserAgent.ParseAdd("QuantumGetaway/0.1");
        }

        public async Task<string> UploadAsync(byte[] data, string filename, string contentType, CancellationToken ct)
        {
            var asset = await SendAsync(HttpMethod.Post, "api/v1/assets",
                new JObject { ["filename"] = filename, ["content_type"] = contentType, ["size_bytes"] = data.Length }, ct).ConfigureAwait(false);
            var id = (string)asset["asset_id"] ?? throw new AtlasException("assets: no asset_id in response");
            var upload = asset["upload"] as JObject ?? throw new AtlasException("assets: no upload URL in response");

            using (var put = new HttpRequestMessage(new HttpMethod((string)upload["method"] ?? "PUT"), (string)upload["url"]))
            {
                put.Content = new ByteArrayContent(data);
                put.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                if (upload["headers"] is JObject headers)
                    foreach (var h in headers)
                        if (!h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                            put.Headers.TryAddWithoutValidation(h.Key, (string)h.Value);
                using (var response = await _storage.SendAsync(put, ct).ConfigureAwait(false))
                    if (!response.IsSuccessStatusCode)
                        throw new AtlasException($"asset upload -> {(int)response.StatusCode}", (int)response.StatusCode);
            }

            await SendAsync(HttpMethod.Post, $"api/v1/assets/{id}/complete", new JObject(), ct).ConfigureAwait(false);
            return id;
        }

        public async Task<AtlasJobResult> RunAsync(string engineId, JObject parameters, JObject inputFiles, TimeSpan timeout, CancellationToken ct)
        {
            var clock = Stopwatch.StartNew();
            var body = new JObject { ["params"] = parameters };
            if (inputFiles != null)
                body["input_files"] = inputFiles;
            var submitted = await SendAsync(HttpMethod.Post, $"api/v1/engines/{engineId}/process", body, ct).ConfigureAwait(false);
            var jobId = (string)submitted["job_id"] ?? throw new AtlasException($"{engineId}: no job_id in response");

            // Hardware jobs queue for minutes to hours; poll them gently.
            var machine = (string)parameters["machine"] ?? "";
            var hardware = machine.StartsWith("ibm_") || machine == "least_busy" || (string)parameters["mode"] == "qpu";
            var poll = hardware ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(1);
            while (true)
            {
                var status = await SendAsync(HttpMethod.Get, $"api/v1/jobs/{jobId}/status", null, ct).ConfigureAwait(false);
                var state = (string)status["status"];
                if (state == "completed")
                    break;
                if (state == "failed" || state == "cancelled")
                    throw new AtlasException($"job {jobId} {state}: {status["error"]}");
                if (clock.Elapsed > timeout)
                    throw new AtlasException($"job {jobId} still {state} after {timeout.TotalMinutes:0} min");
                await Task.Delay(poll, ct).ConfigureAwait(false);
            }

            var result = await SendAsync(HttpMethod.Get, $"api/v1/jobs/{jobId}/result", null, ct).ConfigureAwait(false);
            return new AtlasJobResult { JobId = jobId, Body = result, Elapsed = clock.Elapsed };
        }

        public async Task<byte[]> DownloadAsync(string url, CancellationToken ct)
        {
            using (var response = await _storage.GetAsync(url, ct).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                    throw new AtlasException($"output download -> {(int)response.StatusCode}", (int)response.StatusCode);
                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }

        /// <summary>Checks the key with GET /api/v1/me. Never throws; run it off the game thread.</summary>
        public async Task<AtlasStatus> CheckAsync(CancellationToken ct)
        {
            try
            {
                using (var response = await _http.GetAsync("api/v1/me", ct).ConfigureAwait(false))
                {
                    if (response.IsSuccessStatusCode)
                        return AtlasStatus.Connected;
                    var code = (int)response.StatusCode;
                    return code == 401 || code == 403 ? AtlasStatus.InvalidKey : AtlasStatus.Offline;
                }
            }
            catch (Exception)
            {
                return AtlasStatus.Offline;
            }
        }

        async Task<JObject> SendAsync(HttpMethod method, string path, JObject json, CancellationToken ct)
        {
            using (var request = new HttpRequestMessage(method, path))
            {
                if (json != null)
                    request.Content = new StringContent(json.ToString(), Encoding.UTF8, "application/json");
                using (var response = await _http.SendAsync(request, ct).ConfigureAwait(false))
                {
                    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        throw new AtlasException($"{method} {path} -> {(int)response.StatusCode}: {Truncate(text, 300)}", (int)response.StatusCode);
                    return text.Length == 0 ? new JObject() : JObject.Parse(text);
                }
            }
        }

        static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);

        public void Dispose()
        {
            _http.Dispose();
            _storage.Dispose();
        }
    }
}
