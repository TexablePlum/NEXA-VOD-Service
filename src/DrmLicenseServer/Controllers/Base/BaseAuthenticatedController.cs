using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexa.DrmLicenseServer.Services;
using Nexa.Shared.Exceptions;
using Nexa.Shared.Models;
using System.Security.Claims;

namespace Nexa.DrmLicenseServer.Controllers.Base;

/// <summary>
/// Bazowy kontroler dla endpointów wymagających autentykacji.
/// Udostępnia helper method do pobierania zalogowanego użytkownika.
/// </summary>
[ApiController]
[Authorize]
public abstract class BaseAuthenticatedController : ControllerBase
{
    protected readonly UserService UserService;
    protected readonly ILogger Logger;

    protected BaseAuthenticatedController(UserService userService, ILogger logger)
    {
        UserService = userService;
        Logger = logger;
    }

    /// <summary>
    /// Pobiera zalogowanego użytkownika z JWT tokenu.
    /// Waliduje czy użytkownik istnieje i jest aktywny.
    /// </summary>
    protected async Task<User> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            Logger.LogWarning("JWT token missing userId claim");
            throw new UnauthorizedException("Token JWT nie zawiera identyfikatora użytkownika.");
        }

        var user = await UserService.GetUserByIdAsync(userId, ct);

        if (user == null || !user.IsActive)
        {
            throw new UnauthorizedException("Użytkownik nie istnieje lub został dezaktywowany.");
        }

        return user;
    }

    /// <summary>
    /// Pobiera userId z JWT tokenu (bez walidacji użytkownika).
    /// </summary>
    protected string GetCurrentUserId()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            Logger.LogWarning("JWT token missing userId claim");
            throw new UnauthorizedException("Token JWT nie zawiera identyfikatora użytkownika.");
        }

        return userId;
    }
}
