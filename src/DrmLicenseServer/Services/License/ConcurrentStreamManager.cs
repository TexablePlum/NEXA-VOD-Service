using Nexa.DrmLicenseServer.Repositories;
using Nexa.Shared.Exceptions;

namespace Nexa.DrmLicenseServer.Services.License;

/// <summary>
/// Serwis do zarządzania limitami jednoczesnych streamów dla użytkowników.
/// free/basic: max 1 stream, pro: max 2 streamy jednocześnie.
/// </summary>
public class ConcurrentStreamManager
{
    private readonly IssuedLicenseRepository _licenseRepository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConcurrentStreamManager> _logger;

    public ConcurrentStreamManager(
        IssuedLicenseRepository licenseRepository,
        IConfiguration configuration,
        ILogger<ConcurrentStreamManager> logger)
    {
        _licenseRepository = licenseRepository;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Sprawdza limit concurrent streams dla użytkownika.
    /// free/basic: max 1 stream, pro: max 2 streamy jednocześnie.
    /// Liczy unikalne content ID (jeden content = jeden stream, niezależnie od liczby jakości).
    /// Stream aktywny tylko jeśli LastHeartbeat < 2 minuty temu (heartbeat mechanism).
    /// Ta metoda MUSI być wywoływana w ramach Serializable transaction!
    /// </summary>
    public async Task CheckLimitAsync(
        string userId,
        string contentId,
        string userPlan,
        CancellationToken ct = default)
    {
        // Pobiera limit dla planu użytkownika
        var limit = _configuration.GetValue<int>($"License:ConcurrentStreamLimits:{userPlan}", 1);

        // Heartbeat timeout - stream jest aktywny tylko jeśli ostatni heartbeat < 2 minuty temu
        var heartbeatTimeoutMinutes = _configuration.GetValue<int>("License:HeartbeatTimeoutMinutes", 2);
        var heartbeatCutoff = DateTime.UtcNow.AddMinutes(-heartbeatTimeoutMinutes);

        // Pobiera wszystkie faktycznie aktywne licencje użytkownika z bazy danych
        var activeLicenses = await _licenseRepository.GetActiveLicensesWithHeartbeatAsync(
            userId, heartbeatCutoff, contentId, ct);

        // Liczy unikalne content ID - każdy content to jeden stream niezależnie od liczby jakości
        var uniqueContentIds = activeLicenses
            .Select(l => l.ContentId)
            .Distinct()
            .ToList();

        var activeStreams = uniqueContentIds.Count;

        // Dla logowania - pokazuje wszystkie jakości i czas ostatniego heartbeatu
        var activeStreamsList = activeLicenses
            .GroupBy(l => l.ContentId)
            .Select(g =>
            {
                var lastHeartbeat = g.Max(l => l.LastHeartbeat);
                var secondsAgo = (int)(DateTime.UtcNow - lastHeartbeat).TotalSeconds;
                return $"{g.Key} ({string.Join(", ", g.Select(l => l.Quality))}, heartbeat {secondsAgo}s ago)";
            })
            .ToList();

        // Sprawdza czy przekroczono limit
        if (activeStreams >= limit)
        {
            _logger.LogWarning(
                "Concurrent stream limit exceeded for user {UserId} (plan: {Plan}). Active: {Active}/{Limit}. Streams: {Streams}",
                userId, userPlan, activeStreams, limit, string.Join("; ", activeStreamsList));

            throw new ForbiddenException(
                $"Osiągnięto limit jednoczesnych streamów dla Twojego planu ({userPlan}). " +
                $"Maksymalna liczba równoczesnych streamów: {limit}. " +
                $"Aktualnie aktywne streamy: {activeStreams}. " +
                "Zamknij jeden z aktywnych streamów i spróbuj ponownie.",
                new Dictionary<string, object>
                {
                    ["userPlan"] = userPlan,
                    ["limit"] = limit,
                    ["activeStreams"] = activeStreams
                }
            );
        }
    }
}
