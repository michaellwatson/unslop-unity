using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unslop.UnityBridge.Editor.Diagnostics;
using UnityEngine.Networking;

namespace Unslop.UnityBridge.Editor.Api
{
    public sealed class UnslopApiClient : IUnslopApiClient
    {
        readonly string _baseUrl;
        readonly Func<string> _apiKeyProvider;

        static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        public string LastCorrelationId { get; private set; }

        public UnslopApiClient(string baseUrl, Func<string> apiKeyProvider)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new ArgumentException("Base URL is required.", nameof(baseUrl));
            }

            _baseUrl = baseUrl.TrimEnd('/');
            _apiKeyProvider = apiKeyProvider ?? throw new ArgumentNullException(nameof(apiKeyProvider));
        }

        public Task<CursorPage<ProjectDto>> ListProjectsAsync(string cursor = null, int? limit = null, CancellationToken cancellationToken = default)
            => GetAsync<CursorPage<ProjectDto>>(BuildPath("/projects", cursor, limit), cancellationToken);

        public Task<ProjectDto> GetProjectAsync(string projectId, CancellationToken cancellationToken = default)
            => GetAsync<ProjectDto>($"/projects/{Esc(projectId)}", cancellationToken);

        public Task<CursorPage<AssetSummaryDto>> ListProjectAssetsAsync(string projectId, string cursor = null, int? limit = null, CancellationToken cancellationToken = default)
            => GetAsync<CursorPage<AssetSummaryDto>>(BuildPath($"/projects/{Esc(projectId)}/assets", cursor, limit), cancellationToken);

        public Task<AssetSummaryDto> GetAssetAsync(string assetId, CancellationToken cancellationToken = default)
            => GetAsync<AssetSummaryDto>($"/assets/{Esc(assetId)}", cancellationToken);

        public Task<CursorPage<AssetVersionSummaryDto>> ListAssetVersionsAsync(string assetId, string cursor = null, int? limit = null, CancellationToken cancellationToken = default)
            => GetAsync<CursorPage<AssetVersionSummaryDto>>(BuildPath($"/assets/{Esc(assetId)}/versions", cursor, limit), cancellationToken);

        public Task<AssetVersionDetailDto> GetAssetVersionAsync(string assetId, string versionId, CancellationToken cancellationToken = default)
            => GetAsync<AssetVersionDetailDto>($"/assets/{Esc(assetId)}/versions/{Esc(versionId)}", cancellationToken);

        public Task<DownloadGrantDto> CreateDownloadGrantAsync(string assetVersionId, DownloadGrantRequestDto request = null, string idempotencyKey = null, CancellationToken cancellationToken = default)
            => SendAsync<DownloadGrantDto>("POST", $"/asset-versions/{Esc(assetVersionId)}/download-grants", request ?? new DownloadGrantRequestDto(), idempotencyKey, null, cancellationToken);

        public Task<AssetUpdateCheckResponseDto> AssetUpdateChecksAsync(string projectId, AssetUpdateCheckRequestDto request, string idempotencyKey = null, CancellationToken cancellationToken = default)
            => SendAsync<AssetUpdateCheckResponseDto>("POST", $"/projects/{Esc(projectId)}/asset-update-checks", request ?? throw new ArgumentNullException(nameof(request)), idempotencyKey, null, cancellationToken);

        public Task UpsertInstallReportAsync(string projectId, string assetId, InstallReportDto report, string idempotencyKey = null, CancellationToken cancellationToken = default)
            => SendAsync<object>("PUT", $"/projects/{Esc(projectId)}/asset-installations/{Esc(assetId)}", report ?? throw new ArgumentNullException(nameof(report)), idempotencyKey, null, cancellationToken);

        public Task PinAssetAsync(string projectId, string assetId, PinRequestDto request, string idempotencyKey = null, CancellationToken cancellationToken = default)
            => SendAsync<object>("PUT", $"/projects/{Esc(projectId)}/asset-installations/{Esc(assetId)}/pin", request ?? throw new ArgumentNullException(nameof(request)), idempotencyKey, null, cancellationToken);

        public Task UnpinAssetAsync(string projectId, string assetId, CancellationToken cancellationToken = default)
            => SendAsync<object>("DELETE", $"/projects/{Esc(projectId)}/asset-installations/{Esc(assetId)}/pin", null, null, null, cancellationToken);

        public Task RecordCandidateDecisionAsync(string projectId, string assetId, CandidateDecisionDto decision, string idempotencyKey = null, CancellationToken cancellationToken = default)
            => SendAsync<object>("POST", $"/projects/{Esc(projectId)}/asset-installations/{Esc(assetId)}/candidate-decisions", decision ?? throw new ArgumentNullException(nameof(decision)), idempotencyKey, null, cancellationToken);

        public Task<CursorPage<PhysicalSpecRevisionDto>> ListPhysicalSpecRevisionsAsync(string assetId, string cursor = null, CancellationToken cancellationToken = default)
            => GetAsync<CursorPage<PhysicalSpecRevisionDto>>(BuildPath($"/assets/{Esc(assetId)}/physical-spec-revisions", cursor, null), cancellationToken);

        public Task<PhysicalSpecRevisionDto> CreatePhysicalSpecRevisionAsync(string assetId, PhysicalSpecCreateDto request, string ifMatch, string idempotencyKey = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(ifMatch))
            {
                throw new ArgumentException("If-Match ETag is required for physical-spec creation.", nameof(ifMatch));
            }

            return SendAsync<PhysicalSpecRevisionDto>(
                "POST",
                $"/assets/{Esc(assetId)}/physical-spec-revisions",
                request ?? throw new ArgumentNullException(nameof(request)),
                idempotencyKey,
                ifMatch,
                cancellationToken);
        }

        public Task<CursorPage<ScaleConfirmationDto>> ListScaleConfirmationsAsync(string assetId, string engine = "unity", string cursor = null, CancellationToken cancellationToken = default)
        {
            var path = BuildPath($"/assets/{Esc(assetId)}/scale-confirmations", cursor, null);
            path += (path.Contains("?") ? "&" : "?") + "engine=" + Esc(string.IsNullOrWhiteSpace(engine) ? "unity" : engine);
            return GetAsync<CursorPage<ScaleConfirmationDto>>(path, cancellationToken);
        }

        public Task<ScaleConfirmationDto> SubmitScaleConfirmationAsync(string assetId, ScaleConfirmationCreateDto request, string idempotencyKey = null, CancellationToken cancellationToken = default)
            => SendAsync<ScaleConfirmationDto>("POST", $"/assets/{Esc(assetId)}/scale-confirmations", request ?? throw new ArgumentNullException(nameof(request)), idempotencyKey, null, cancellationToken);

        Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
            => SendAsync<T>("GET", path, null, null, null, cancellationToken);

        async Task<T> SendAsync<T>(string method, string path, object body, string idempotencyKey, string ifMatch, CancellationToken cancellationToken)
        {
            var correlationId = Guid.NewGuid().ToString("N");
            LastCorrelationId = correlationId;

            var apiKey = _apiKeyProvider();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new UnslopApiException(401, "Bridge API key is not configured.", correlationId, null);
            }

            using var request = new UnityWebRequest(_baseUrl + path, method);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Accept", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey.Trim());
            request.SetRequestHeader("X-Correlation-ID", correlationId);

            var mutating = !string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
                           && !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase);
            if (mutating)
            {
                request.SetRequestHeader(
                    "Idempotency-Key",
                    string.IsNullOrWhiteSpace(idempotencyKey) ? Guid.NewGuid().ToString("N") : idempotencyKey);
            }

            if (!string.IsNullOrEmpty(ifMatch))
            {
                request.SetRequestHeader("If-Match", ifMatch);
            }

            if (body != null && mutating && !string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(body, JsonSettings));
                request.uploadHandler = new UploadHandlerRaw(bytes);
                request.SetRequestHeader("Content-Type", "application/json");
            }

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    request.Abort();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                await Task.Yield();
            }

            var responseCorrelation = request.GetResponseHeader("X-Correlation-ID") ?? correlationId;
            LastCorrelationId = responseCorrelation;
            var status = (int)request.responseCode;
            var text = request.downloadHandler?.text ?? string.Empty;
            var success = request.result == UnityWebRequest.Result.Success && status >= 200 && status < 300;

            if (!success)
            {
                var message = ExtractErrorMessage(text, request.error, status);
                BridgeLog.Warn($"API {method} {path} failed status={status} correlation={responseCorrelation}: {message}");
                throw new UnslopApiException(status, message, responseCorrelation, BridgeLog.Redact(text));
            }

            if (typeof(T) == typeof(object) || string.IsNullOrWhiteSpace(text))
            {
                return default;
            }

            try
            {
                return JsonConvert.DeserializeObject<T>(text, JsonSettings);
            }
            catch (Exception ex)
            {
                throw new UnslopApiException(status, "Failed to deserialize API response: " + ex.Message, responseCorrelation, BridgeLog.Redact(text));
            }
        }

        static string ExtractErrorMessage(string responseBody, string transportError, int status)
        {
            if (!string.IsNullOrWhiteSpace(responseBody))
            {
                try
                {
                    var err = JsonConvert.DeserializeObject<ApiErrorDto>(responseBody, JsonSettings);
                    if (!string.IsNullOrWhiteSpace(err?.message))
                    {
                        return err.message;
                    }

                    if (!string.IsNullOrWhiteSpace(err?.error))
                    {
                        return err.error;
                    }
                }
                catch
                {
                    // fall through
                }
            }

            if (!string.IsNullOrWhiteSpace(transportError))
            {
                return transportError;
            }

            return status == 0 ? "Network error" : $"HTTP {status}";
        }

        static string Esc(string value) => Uri.EscapeDataString(value ?? string.Empty);

        static string BuildPath(string path, string cursor, int? limit)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(cursor))
            {
                parts.Add("cursor=" + Esc(cursor));
            }

            if (limit.HasValue)
            {
                parts.Add("limit=" + limit.Value);
            }

            return parts.Count == 0 ? path : path + "?" + string.Join("&", parts);
        }
    }
}
