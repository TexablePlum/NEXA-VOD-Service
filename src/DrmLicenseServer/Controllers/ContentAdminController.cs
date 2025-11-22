using Microsoft.AspNetCore.Mvc;
using Nexa.DrmLicenseServer.Controllers.Base;
using Nexa.DrmLicenseServer.Services;
using Nexa.DrmLicenseServer.Services.License;
using Nexa.Shared.Models;
using System.Text.Json;

namespace Nexa.DrmLicenseServer.Controllers;

/// <summary>
/// Administracyjny endpoint do zarządzania rejestracją treści w serwerze DRM.
/// Wymaga tokenu JWT z uprawnieniami administratora.
/// używa LicenseService i CacheInvalidationService dla spójności.
/// </summary>
[ApiController]
[Route("api/admin/content")]
public class ContentAdminController : AdminBaseController
{
    private readonly LicenseService _licenseService;
    private readonly CacheInvalidationService _cacheInvalidation;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ContentAdminController(
        LicenseService licenseService,
        CacheInvalidationService cacheInvalidation,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ContentAdminController> logger)
        : base(logger)
    {
        _licenseService = licenseService;
        _cacheInvalidation = cacheInvalidation;
        _httpClient = httpClientFactory.CreateClient();
        _configuration = configuration;
    }

    /// <summary>
    /// POST /api/admin/content/register
    /// Rejestruje treść w serwerze DRM wraz z CEK-ami w jednej atomic operacji.
    /// Pobiera metadane z Content Server (includings ReleaseDate) i importuje wszystkie CEK-i.
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(ContentRegisterResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ContentRegisterResponse>> RegisterContent(
        [FromBody] ContentRegisterRequest request,
        CancellationToken ct)
    {
        LogSecurityAudit(
            "Content Registration Request",
            $"ContentId: {request.ContentId}, CEKs count: {request.Ceks.Count}",
            new { request.ContentId, CeksCount = request.Ceks.Count }
        );

        var contentServerUrl = _configuration["ContentServer:BaseUrl"] ?? "http://content-server:8080";
        var metadataUrl = $"{contentServerUrl}/api/catalog/{request.ContentId}";

        // 1. Pobiera metadane z Content Server
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(metadataUrl, ct);
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Failed to connect to Content Server at {Url}", metadataUrl);
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ErrorResponse
                {
                    ErrorCode = "SERVICE_UNAVAILABLE",
                    Message = "Cannot connect to Content Server"
                }
            );
        }

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Logger.LogWarning("Content {ContentId} not found on Content Server", request.ContentId);
                return NotFound(new ErrorResponse
                {
                    ErrorCode = "NOT_FOUND",
                    Message = $"Content '{request.ContentId}' not found on Content Server"
                });
            }

            Logger.LogError(
                "Content Server returned error {StatusCode} for ContentId: {ContentId}",
                response.StatusCode,
                request.ContentId
            );

            return StatusCode(
                (int)response.StatusCode,
                new ErrorResponse
                {
                    ErrorCode = "CONTENT_SERVER_ERROR",
                    Message = $"Content Server returned error: {response.StatusCode}"
                }
            );
        }

        // 2. Deserializuje metadata
        var contentJson = await response.Content.ReadAsStringAsync(ct);
        var contentMetadata = JsonSerializer.Deserialize<ContentMetadata>(contentJson, JsonOptions);

        if (contentMetadata == null)
        {
            Logger.LogError("Failed to deserialize content metadata for ContentId: {ContentId}", request.ContentId);
            return BadRequest(new ErrorResponse
            {
                ErrorCode = "INVALID_METADATA",
                Message = "Invalid content metadata format"
            });
        }

        // 3. Importuje wszystkie CEK-i
        var importedQualities = new List<string>();
        foreach (var cekData in request.Ceks)
        {
            try
            {
                await _licenseService.ImportCekAsync(
                    request.ContentId,
                    cekData.Quality,
                    cekData.Cek,
                    cekData.KeyId,
                    ct
                );

                importedQualities.Add(cekData.Quality);
                Logger.LogDebug("CEK imported for quality {Quality}", cekData.Quality);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to import CEK for quality {Quality}", cekData.Quality);
                // Kontynuuje z innymi jakośćmi - częściowy sukces jest akceptowalny
            }
        }

        if (importedQualities.Count == 0)
        {
            Logger.LogError("Failed to import any CEK for ContentId: {ContentId}", request.ContentId);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new ErrorResponse
                {
                    ErrorCode = "CEK_IMPORT_FAILED",
                    Message = "Failed to import any CEK. Check server logs for details."
                }
            );
        }

        // 4. Zapisuje metadata contentu (RequiredPlan + ReleaseDate) do Redis
        await _licenseService.ImportContentMetadataAsync(
            request.ContentId,
            contentMetadata.RequiredPlan,
            contentMetadata.ReleaseDate,
            ct
        );

        // 5. Inwaliduje quality cache
        await _cacheInvalidation.InvalidateQualityCacheAsync(request.ContentId, ct);

        Logger.LogInformation(
            "Content registered successfully - ContentId: {ContentId}, RequiredPlan: {RequiredPlan}, ReleaseDate: {ReleaseDate}, CEKs: {ImportedCount}/{TotalCount}",
            request.ContentId,
            contentMetadata.RequiredPlan,
            contentMetadata.ReleaseDate?.ToString("yyyy-MM-dd") ?? "none",
            importedQualities.Count,
            request.Ceks.Count
        );

        return Ok(new ContentRegisterResponse
        {
            Success = true,
            Message = $"Content registered successfully with {importedQualities.Count} CEK(s)",
            ContentId = request.ContentId,
            RequiredPlan = contentMetadata.RequiredPlan,
            ReleaseDate = contentMetadata.ReleaseDate,
            RegisteredAt = DateTime.UtcNow,
            ImportedQualities = importedQualities,
            TotalCeksImported = importedQualities.Count
        });
    }
}
