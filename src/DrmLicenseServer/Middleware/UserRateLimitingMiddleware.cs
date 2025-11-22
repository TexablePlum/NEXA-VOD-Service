using StackExchange.Redis;
using System.Security.Claims;
using System.Text.Json;

namespace Nexa.DrmLicenseServer.Middleware;

/// <summary>
/// Middleware do rate limiting per-user dla endpointów licencji.
/// </summary>
public class UserRateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<UserRateLimitingMiddleware> _logger;
    private readonly IConfiguration _configuration;

    public UserRateLimitingMiddleware(
        RequestDelegate next,
        ILogger<UserRateLimitingMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context, IDatabase redisDb)
    {
        // Sprawdza czy rate limiting jest włączony
        var enabled = _configuration.GetValue<bool>("UserRateLimiting:Enabled", true);

        if (!enabled)
        {
            await _next(context);
            return;
        }

        // Sprawdza czy to endpoint licencji
        var path = context.Request.Path.Value?.ToLower() ?? "";
        var isLicenseEndpoint = path.StartsWith("/api/license/") && !path.Contains("/qualities");

        if (!isLicenseEndpoint)
        {
            await _next(context);
            return;
        }

        // Pobiera userId z JWT claims
        var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User?.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            // Jeśli brak userId - pomija rate limiting (błąd unauthorized później)
            await _next(context);
            return;
        }

        // Sprawdza rate limit
        var limit = _configuration.GetValue<int>("UserRateLimiting:LicenseEndpoints:Limit", 10);
        var period = _configuration.GetValue<string>("UserRateLimiting:LicenseEndpoints:Period", "1m");

        var periodSeconds = ParsePeriodToSeconds(period);
        var rateLimitKey = $"ratelimit:user:{userId}:{period}";

        try
        {
            var count = await redisDb.StringIncrementAsync(rateLimitKey);

            if (count == 1)
            {
                await redisDb.KeyExpireAsync(rateLimitKey, TimeSpan.FromSeconds(periodSeconds));
            }

            // Sprawdza czy przekroczono limit
            if (count > limit)
            {
                _logger.LogWarning(
                    "User {UserId} exceeded rate limit: {Count}/{Limit} in {Period}",
                    userId, count, limit, period);

                context.Response.StatusCode = 429;
                context.Response.ContentType = "application/json";

                var ttl = await redisDb.KeyTimeToLiveAsync(rateLimitKey);
                var retryAfter = ttl?.TotalSeconds ?? periodSeconds;

                context.Response.Headers["Retry-After"] = ((int)retryAfter).ToString();
                context.Response.Headers["X-RateLimit-Limit"] = limit.ToString();
                context.Response.Headers["X-RateLimit-Remaining"] = "0";
                context.Response.Headers["X-RateLimit-Reset"] = DateTimeOffset.UtcNow.AddSeconds(retryAfter).ToUnixTimeSeconds().ToString();

                var errorResponse = new
                {
                    errorCode = "RATE_LIMIT_EXCEEDED",
                    message = $"Przekroczono limit requestów: {limit} requestów na {period}. Spróbuj ponownie za {(int)retryAfter} sekund.",
                    timestamp = DateTime.UtcNow,
                    path = context.Request.Path.Value,
                    context = new
                    {
                        limit,
                        period,
                        retryAfter = (int)retryAfter
                    }
                };

                var json = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await context.Response.WriteAsync(json);
                return;
            }

            // Dodaje nagłówek z informacją o rate limit
            context.Response.OnStarting(() =>
            {
                context.Response.Headers["X-RateLimit-Limit"] = limit.ToString();
                context.Response.Headers["X-RateLimit-Remaining"] = Math.Max(0, limit - (int)count).ToString();

                return Task.CompletedTask;
            });

            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in UserRateLimitingMiddleware");
            // W przypadku błędu Redis, nie blokuje requestu
            await _next(context);
        }
    }

    private int ParsePeriodToSeconds(string period)
    {
        // walidacja formatu period
        if (string.IsNullOrWhiteSpace(period))
        {
            _logger.LogError("Period format is null or empty, using default 60s");
            return 60;
        }

        // Walidacja formatu: musi kończyć się na s/m/h
        if (!period.EndsWith("s") && !period.EndsWith("m") && !period.EndsWith("h"))
        {
            _logger.LogError("Invalid period format: {Period}. Must end with 's', 'm', or 'h'. Using default 60s", period);
            return 60;
        }

        // Parse wartości numerycznej
        var numericPart = period[..^1]; // Wszystko oprócz ostatniego znaku
        if (!int.TryParse(numericPart, out var value) || value <= 0)
        {
            _logger.LogError("Invalid period format: {Period}. Numeric part must be positive integer. Using default 60s", period);
            return 60;
        }

        // Konwertuje do sekund z clampingiem 1s - 24h
        int seconds = period[^1] switch
        {
            's' => value,
            'm' => value * 60,
            'h' => value * 3600,
            _ => 60
        };

        var clamped = Math.Max(1, Math.Min(seconds, 86400)); // 1s - 24h
        if (clamped != seconds)
        {
            _logger.LogWarning("Period {Period} ({Seconds}s) exceeds limits, clamped to {Clamped}s", period, seconds, clamped);
        }

        return clamped;
    }
}
