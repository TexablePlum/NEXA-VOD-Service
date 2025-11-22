using Microsoft.AspNetCore.Mvc;
using Nexa.DrmLicenseServer.Controllers.Base;
using Nexa.DrmLicenseServer.Services;
using Nexa.Shared.Models;
using Nexa.Shared.Exceptions;
using System.ComponentModel.DataAnnotations;

namespace Nexa.DrmLicenseServer.Controllers;

/// <summary>
/// Controller do pobierania licencji DRM (CEK).
/// Wymaga autentykacji JWT.
/// używa BaseAuthenticatedController.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class LicenseController : BaseAuthenticatedController
{
    private readonly LicenseService _licenseService;

    public LicenseController(
        LicenseService licenseService,
        UserService userService,
        ILogger<LicenseController> logger)
        : base(userService, logger)
    {
        _licenseService = licenseService;
    }

    /// <summary>
    /// Pobiera licencje (CEK) dla wszystkich dostępnych jakości contentu.
    /// Zwraca klucze zaszyfrowane public keyem urządzenia.
    /// </summary>
    [HttpGet("{contentId}")]
    [ProducesResponseType(typeof(MultiQualityLicenseResponse), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    [ProducesResponseType(typeof(ErrorResponse), 401)]
    [ProducesResponseType(typeof(ErrorResponse), 403)]
    [ProducesResponseType(typeof(ErrorResponse), 404)]
    public async Task<ActionResult<MultiQualityLicenseResponse>> GetLicenses(
        string contentId,
        [FromQuery][Required(ErrorMessage = "Device ID is required. Please register your device first at POST /api/device/register")] string deviceId,
        CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(ct);
        var licenses = await _licenseService.GetAllLicensesAsync(contentId, user, deviceId, ct);

        return Ok(licenses);
    }

    /// <summary>
    /// Aktualizuje heartbeat dla licencji contentu.
    /// </summary>
    [HttpPost("{contentId}/heartbeat")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ErrorResponse), 401)]
    [ProducesResponseType(typeof(ErrorResponse), 404)]
    public async Task<ActionResult> Heartbeat(
        string contentId,
        CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(ct);
        await _licenseService.HeartbeatAsync(contentId, user, ct);

        return Ok();
    }

    /// <summary>
    /// Usuwa (revoke) licencje dla contentu - zwalnia slot concurrent stream.
    /// </summary>
    [HttpDelete("{contentId}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ErrorResponse), 401)]
    [ProducesResponseType(typeof(ErrorResponse), 404)]
    public async Task<ActionResult> RevokeLicense(
        string contentId,
        CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(ct);
        await _licenseService.RevokeLicenseAsync(contentId, user, ct);

        return Ok();
    }
}
