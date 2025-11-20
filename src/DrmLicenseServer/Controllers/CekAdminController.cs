using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexa.DrmLicenseServer.Services;
using Nexa.Shared.Models;
using StackExchange.Redis;
using System.Text.Json;

namespace Nexa.DrmLicenseServer.Controllers;

/// <summary>
/// Kontroler administracyjny do bezpiecznego importu CEK
/// - Wymaga JWT z rol¹=admin
/// - White lista IP (localhost + sieæ Dockera)
/// - Limitowanie zapytañ: globalny limit przez AspNetCoreRateLimit
/// - Rejestrowanie audytu (bez jawnego CEK)
/// - Szyfrowanie CEK przed zapisaniem
/// </summary>
[ApiController]
[Route("api/admin/cek")]
[Authorize(Roles = "admin")]
public class CekAdminController : ControllerBase
{
    private readonly ILogger<CekAdminController> _logger;
    private readonly IDatabase _redis;
    private readonly CekEncryptionService _cekEncryption;

    public CekAdminController(
        ILogger<CekAdminController> logger,
        IConnectionMultiplexer redis,
        CekEncryptionService cekEncryption)
    {
        _logger = logger;
        _redis = redis.GetDatabase();
        _cekEncryption = cekEncryption;
    }

    /// <summary>
    /// Import CEK dla jakoœci treœci – bezpieczny endpoint dla pipeline’u uploadu
    /// </summary>
    /// <param name="request">¯¹danie importu CEK z contentId, quality, cek, keyId</param>
    /// <returns>OdpowiedŸ sukcesu lub b³¹d</returns>
    [HttpPost("import")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ImportCek([FromBody] CekImportRequest request)
    {
        _logger.LogInformation(
            "SECURITY AUDIT: CEK import request - ContentId: {ContentId}, Quality: {Quality}, IP: {RemoteIp}, User: {User}",
            request.ContentId,
            request.Quality,
            HttpContext.Connection.RemoteIpAddress,
            User.Identity?.Name ?? "anonymous"
        );

        try
        {
            byte[] cekBytes;
            try
            {
                cekBytes = Convert.FromHexString(request.Cek);
            }
            catch (FormatException)
            {
                _logger.LogWarning(
                    "SECURITY: Invalid CEK format for ContentId: {ContentId}, Quality: {Quality}",
                    request.ContentId,
                    request.Quality
                );
                return BadRequest(new { error = "Invalid CEK format. Must be 32 hex characters." });
            }

            if (cekBytes.Length != 16)
            {
                _logger.LogWarning(
                    "SECURITY: Invalid CEK length ({Length} bytes) for ContentId: {ContentId}, Quality: {Quality}",
                    cekBytes.Length,
                    request.ContentId,
                    request.Quality
                );
                return BadRequest(new { error = $"Invalid CEK length. Expected 16 bytes (128-bit), got {cekBytes.Length} bytes." });
            }

            if (request.KeyId.Length != 32 || !IsHexString(request.KeyId))
            {
                return BadRequest(new { error = "Invalid KeyId format. Must be 32 hex characters (GUID without dashes)." });
            }

            var encryptedCek = _cekEncryption.Encrypt(request.Cek);

            var cekKey = $"cek:{request.ContentId}:{request.Quality}";
            var cekValue = JsonSerializer.Serialize(new
            {
                EncryptedKey = encryptedCek,
                KeyId = request.KeyId,
                ImportedAt = DateTime.UtcNow,
                ImportedBy = User.Identity?.Name ?? "admin"
            });

            await _redis.StringSetAsync(cekKey, cekValue);

            var qualitiesSetKey = $"content:qualities:{request.ContentId}";
            await _redis.SetAddAsync(qualitiesSetKey, request.Quality);

            var cachePattern = $"content:qualities:{request.ContentId}:*";
            try
            {
                var endpoints = _redis.Multiplexer.GetEndPoints();
                var server = _redis.Multiplexer.GetServer(endpoints[0]);
                var keysToDelete = server.Keys(pattern: cachePattern).ToArray();
                if (keysToDelete.Length > 0)
                {
                    await _redis.KeyDeleteAsync(keysToDelete);
                    _logger.LogInformation(
                        "Cache invalidated for ContentId: {ContentId} ({Count} keys)",
                        request.ContentId,
                        keysToDelete.Length
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to invalidate cache for ContentId: {ContentId}", request.ContentId);
            }

            _logger.LogInformation(
                "SUCCESS: CEK imported for ContentId: {ContentId}, Quality: {Quality}",
                request.ContentId,
                request.Quality
            );

            return Ok(new
            {
                success = true,
                message = "CEK imported successfully",
                contentId = request.ContentId,
                quality = request.Quality,
                keyId = request.KeyId,
                importedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "ERROR: Failed to import CEK for ContentId: {ContentId}, Quality: {Quality}",
                request.ContentId,
                request.Quality
            );

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { error = "Internal server error during CEK import" }
            );
        }
    }

    /// <summary>
    /// Sprawdzenie istnienia CEK dla danej jakoœci treœci (diagnostyczny endpoint administracyjny)
    /// </summary>
    [HttpGet("verify/{contentId}/{quality}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VerifyCek(string contentId, string quality)
    {
        var cekKey = $"cek:{contentId}:{quality}";
        var exists = await _redis.KeyExistsAsync(cekKey);

        if (!exists)
        {
            return NotFound(new
            {
                exists = false,
                contentId,
                quality
            });
        }

        var cekData = await _redis.StringGetAsync(cekKey);
        var cekInfo = JsonSerializer.Deserialize<JsonElement>(cekData!);

        return Ok(new
        {
            exists = true,
            contentId,
            quality,
            keyId = cekInfo.GetProperty("KeyId").GetString(),
            importedAt = cekInfo.TryGetProperty("ImportedAt", out var importedAt)
                ? importedAt.GetDateTime()
                : (DateTime?)null,
            importedBy = cekInfo.TryGetProperty("ImportedBy", out var importedBy)
                ? importedBy.GetString()
                : null
        });
    }

    private static bool IsHexString(string input)
    {
        return input.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));
    }
}
