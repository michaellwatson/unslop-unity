using System.Threading;
using System.Threading.Tasks;

namespace Unslop.UnityBridge.Editor.Api
{
    public interface IUnslopApiClient
    {
        string LastCorrelationId { get; }

        Task<CursorPage<ProjectDto>> ListProjectsAsync(string cursor = null, int? limit = null, CancellationToken cancellationToken = default);
        Task<ProjectDto> GetProjectAsync(string projectId, CancellationToken cancellationToken = default);
        Task<CursorPage<AssetSummaryDto>> ListProjectAssetsAsync(string projectId, string cursor = null, int? limit = null, CancellationToken cancellationToken = default);
        Task<AssetSummaryDto> GetAssetAsync(string assetId, CancellationToken cancellationToken = default);
        Task<CursorPage<AssetVersionSummaryDto>> ListAssetVersionsAsync(string assetId, string cursor = null, int? limit = null, CancellationToken cancellationToken = default);
        Task<AssetVersionDetailDto> GetAssetVersionAsync(string assetId, string versionId, CancellationToken cancellationToken = default);

        Task<DownloadGrantDto> CreateDownloadGrantAsync(string assetVersionId, DownloadGrantRequestDto request = null, string idempotencyKey = null, CancellationToken cancellationToken = default);
        Task<AssetUpdateCheckResponseDto> AssetUpdateChecksAsync(string projectId, AssetUpdateCheckRequestDto request, string idempotencyKey = null, CancellationToken cancellationToken = default);
        Task UpsertInstallReportAsync(string projectId, string assetId, InstallReportDto report, string idempotencyKey = null, CancellationToken cancellationToken = default);
        Task PinAssetAsync(string projectId, string assetId, PinRequestDto request, string idempotencyKey = null, CancellationToken cancellationToken = default);
        Task UnpinAssetAsync(string projectId, string assetId, CancellationToken cancellationToken = default);
        Task RecordCandidateDecisionAsync(string projectId, string assetId, CandidateDecisionDto decision, string idempotencyKey = null, CancellationToken cancellationToken = default);
        Task<DiagnosticUploadResultDto> UploadProjectDiagnosticsAsync(string projectId, DiagnosticUploadRequestDto request, string idempotencyKey = null, CancellationToken cancellationToken = default);
        Task<CursorPage<DiagnosticUploadResultDto>> ListProjectDiagnosticsAsync(string projectId, string engine = null, string kind = null, string cursor = null, int? limit = null, CancellationToken cancellationToken = default);
        Task<DiagnosticUploadResultDto> GetProjectDiagnosticAsync(string projectId, string sessionId, bool includeContent = true, CancellationToken cancellationToken = default);

        Task<CursorPage<PhysicalSpecRevisionDto>> ListPhysicalSpecRevisionsAsync(string assetId, string cursor = null, CancellationToken cancellationToken = default);
        Task<PhysicalSpecRevisionDto> CreatePhysicalSpecRevisionAsync(string assetId, PhysicalSpecCreateDto request, string ifMatch, string idempotencyKey = null, CancellationToken cancellationToken = default);
        Task<CursorPage<ScaleConfirmationDto>> ListScaleConfirmationsAsync(string assetId, string engine = "unity", string cursor = null, CancellationToken cancellationToken = default);
        Task<ScaleConfirmationDto> SubmitScaleConfirmationAsync(string assetId, ScaleConfirmationCreateDto request, string idempotencyKey = null, CancellationToken cancellationToken = default);

        Task<AssetSummaryDto> CreateProjectAssetAsync(string projectId, CreateAssetDto request, string idempotencyKey = null, CancellationToken cancellationToken = default);
        Task<AssetVersionSummaryDto> CreateDraftVersionAsync(string assetId, string idempotencyKey = null, CancellationToken cancellationToken = default);
        Task<UploadedFileDto> UploadVersionFileAsync(string assetId, string versionId, string relativePath, string role, string mediaType, byte[] contents, string idempotencyKey = null, CancellationToken cancellationToken = default);
        Task<AssetVersionSummaryDto> SubmitVersionAsync(string assetId, string versionId, object assetJson, object materialsJson, string idempotencyKey = null, CancellationToken cancellationToken = default);
        Task<AssetVersionSummaryDto> PublishVersionAsync(string assetId, string versionId, bool recommend = true, string idempotencyKey = null, CancellationToken cancellationToken = default);
    }
}
