using Microsoft.AspNetCore.Mvc;

namespace Nexa.DrmLicenseServer.Controllers.Base;

/// <summary>
/// Bazowy kontroler dla endpointów administracyjnych.
/// Wymaga autoryzacji JWT z rolą 'admin'.
/// Automatycznie loguje security audit dla wszystkich operacji.
/// </summary>
[ApiController]
[Microsoft.AspNetCore.Authorization.Authorize(Roles = "admin")]
public abstract class AdminBaseController : ControllerBase
{
    protected readonly ILogger Logger;

    protected AdminBaseController(ILogger logger)
    {
        Logger = logger;
    }

    /// <summary>
    /// Loguje security audit event dla akcji administracyjnej.
    /// </summary>
    protected void LogSecurityAudit(string action, string details, object? additionalData = null)
    {
        Logger.LogInformation(
            "SECURITY AUDIT: {Action} - {Details} | IP: {RemoteIp} | User: {User} | Data: {@AdditionalData}",
            action,
            details,
            HttpContext.Connection.RemoteIpAddress,
            User.Identity?.Name ?? "anonymous",
            additionalData
        );
    }

    /// <summary>
    /// Loguje security warning dla podejrzanej aktywności.
    /// </summary>
    protected void LogSecurityWarning(string action, string reason, object? additionalData = null)
    {
        Logger.LogWarning(
            "SECURITY WARNING: {Action} - {Reason} | IP: {RemoteIp} | User: {User} | Data: {@AdditionalData}",
            action,
            reason,
            HttpContext.Connection.RemoteIpAddress,
            User.Identity?.Name ?? "anonymous",
            additionalData
        );
    }

    /// <summary>
    /// Pobiera nazwę użytkownika z JWT (dla audit trail).
    /// </summary>
    protected string GetAdminUser()
    {
        return User.Identity?.Name ?? "admin";
    }
}
