using Microsoft.AspNetCore.Mvc;
using Nexa.DrmLicenseServer.Controllers.Base;
using Nexa.DrmLicenseServer.Services;
using Nexa.Shared.Models;

namespace Nexa.DrmLicenseServer.Controllers;

/// <summary>
/// Controller do zarządzania urządzeniami użytkownika (device keys).
/// Wymaga autentykacji JWT.
/// używa BaseAuthenticatedController.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DeviceController : BaseAuthenticatedController
{
    private readonly DeviceKeyService _deviceKeyService;

    public DeviceController(
        DeviceKeyService deviceKeyService,
        UserService userService,
        ILogger<DeviceController> logger)
        : base(userService, logger)
    {
        _deviceKeyService = deviceKeyService;
    }

    /// <summary>
    /// Pobiera wyzwanie (Nonce) dla urządzenia, wymagane do kryptograficznej weryfikacji.
    /// </summary>
    [HttpGet("challenge")]
    [ProducesResponseType(typeof(DeviceChallengeResponse), 200)]
    public async Task<ActionResult<DeviceChallengeResponse>> GetChallenge(
        [FromQuery] string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return BadRequest(new ErrorResponse { Message = "Device ID is required." });
        }

        var response = await _deviceKeyService.GenerateChallengeAsync(deviceId);
        return Ok(response);
    }

    /// <summary>
    /// Rejestruje nowe urządzenie z public keyem.
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(DeviceInfo), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    [ProducesResponseType(typeof(ErrorResponse), 401)]
    [ProducesResponseType(typeof(ErrorResponse), 403)]
    public async Task<ActionResult<DeviceInfo>> RegisterDevice(
        [FromBody] DeviceRegistrationRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();

        var device = await _deviceKeyService.RegisterDeviceAsync(
            userId,
            request.DeviceId,
            request.PublicKeyPem,
            request.Nonce,
            request.SignatureBase64,
            request.DeviceName,
            ct);

        return Ok(new DeviceInfo
        {
            DeviceId = device.DeviceId,
            DeviceName = device.DeviceName,
            RegisteredAt = device.RegisteredAt,
            LastUsedAt = device.LastUsedAt,
            IsActive = device.IsActive,
            IsCryptographicallyVerified = !string.IsNullOrEmpty(device.TpmAttestation) // Reused column holds signature
        });
    }

    /// <summary>
    /// Pobiera listę zarejestrowanych urządzeń użytkownika.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<DeviceInfo>), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 401)]
    public async Task<ActionResult<List<DeviceInfo>>> GetDevices(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var devices = await _deviceKeyService.GetUserDevicesAsync(userId, ct);

        return Ok(devices.Select(d => new DeviceInfo
        {
            DeviceId = d.DeviceId,
            DeviceName = d.DeviceName,
            RegisteredAt = d.RegisteredAt,
            LastUsedAt = d.LastUsedAt,
            IsActive = d.IsActive,
            IsCryptographicallyVerified = !string.IsNullOrEmpty(d.TpmAttestation) // Reused column holds signature
        }).ToList());
    }

    /// <summary>
    /// Usuwa (dezaktywuje) urządzenie.
    /// </summary>
    [HttpDelete("{deviceId}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ErrorResponse), 401)]
    [ProducesResponseType(typeof(ErrorResponse), 404)]
    public async Task<ActionResult> RemoveDevice(
        string deviceId,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await _deviceKeyService.RemoveDeviceAsync(userId, deviceId, ct);

        return Ok();
    }
}
