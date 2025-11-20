using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexa.Shared.Models;
using StackExchange.Redis;
using System.Text.Json;

namespace Nexa.DrmLicenseServer.Controllers;

/// <summary>
/// Administracyjny endpoint do zarządzania rejestracją treści w serwerze DRM.
/// Wymaga tokenu JWT z uprawnieniami administratora.
/// </summary>
[ApiController]
[Route("api/admin/content")]
[Authorize(Roles = "admin")]
public class ContentAdminController : ControllerBase
{
    private readonly IDatabase _redis;
    private readonly ILogger<ContentAdminController> _logger;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ContentAdminController(
        IDatabase redis,
        ILogger<ContentAdminController> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _redis = redis;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
        _configuration = configuration;
    }

    /// <summary>
    /// POST /api/admin/content/register
    /// Rejestruje treść w serwerze DRM, pobierając metadane z Content Server.
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RegisterContent([FromBody] ContentRegisterRequest request)
    {
        _logger.LogInformation("Registering content {ContentId} in DRM Server", request.ContentId);

        var contentServerUrl = _configuration["ContentServer:BaseUrl"] ?? "http://content-server:8080";
        var metadataUrl = $"{contentServerUrl}/api/catalog/{request.ContentId}";

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(metadataUrl);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to connect to Content Server at {Url}", metadataUrl);
            return StatusCode(503, new
            {
                error = "SERVICE_UNAVAILABLE",
                message = "Cannot connect to Content Server"
            });
        }

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Content {ContentId} not found on Content Server", request.ContentId);
                return NotFound(new
                {
                    error = "NOT_FOUND",
                    message = $"Content '{request.ContentId}' not found on Content Server"
                });
            }

            _logger.LogError("Content Server returned {StatusCode} for {ContentId}",
                response.StatusCode, request.ContentId);
            return StatusCode(500, new
            {
                error = "CONTENT_SERVER_ERROR",
                message = $"Content Server returned {response.StatusCode}"
            });
        }

        var jsonContent = await response.Content.ReadAsStringAsync();
        var metadata = JsonSerializer.Deserialize<ContentMetadata>(jsonContent, JsonOptions);

        if (metadata == null)
        {
            _logger.LogError("Failed to deserialize metadata for {ContentId}", request.ContentId);
            return StatusCode(500, new
            {
                error = "DESERIALIZATION_ERROR",
                message = "Failed to parse content metadata"
            });
        }

        var metaKey = $"content:meta:{request.ContentId}";
        var internalMeta = new ContentMetadataInternal
        {
            ContentId = metadata.ContentId,
            Title = metadata.Title,
            RequiredPlan = metadata.RequiredPlan,
            AvailableQualities = metadata.AvailableQualities
        };

        var serialized = JsonSerializer.Serialize(internalMeta);
        await _redis.StringSetAsync(metaKey, serialized, TimeSpan.FromDays(30));

        _logger.LogInformation(
            "Successfully registered content {ContentId} with plan {Plan} and qualities {Qualities}",
            request.ContentId, metadata.RequiredPlan, string.Join(", ", metadata.AvailableQualities));

        return Ok(new
        {
            message = "Content registered successfully",
            contentId = request.ContentId,
            requiredPlan = metadata.RequiredPlan,
            availableQualities = metadata.AvailableQualities
        });
    }
}

/// <summary>
/// Request model dla rejestracji contentu.
/// </summary>
public class ContentRegisterRequest
{
    public string ContentId { get; set; } = string.Empty;
}

/// <summary>
/// Internal model dla content metadata w Redis (DRM Server).
/// Minimalistyczna wersja - zawiera tylko to co potrzebne do autoryzacji.
/// </summary>
internal class ContentMetadataInternal
{
    public string ContentId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string RequiredPlan { get; set; } = "free";
    public List<string> AvailableQualities { get; set; } = new();
}
