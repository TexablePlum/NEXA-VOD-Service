using Microsoft.EntityFrameworkCore;
using Nexa.DrmLicenseServer.Data;

namespace Nexa.DrmLicenseServer.Services;

/// <summary>
/// Background service do czyszczenia wygasłych licencji z bazy danych.
/// Uruchamia się co określony czas (domyślnie 6 godzin) i usuwa licencje wygasłe > 7 dni temu.
/// </summary>
public class LicenseCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LicenseCleanupService> _logger;
    private readonly TimeSpan _cleanupInterval;
    private readonly TimeSpan _retentionPeriod;

    public LicenseCleanupService(
        IServiceProvider serviceProvider,
        ILogger<LicenseCleanupService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        // Domyślnie: cleanup co 6 godzin, usuwa licencje wygasłe > 7 dni temu
        var intervalHours = configuration.GetValue<int>("License:CleanupIntervalHours", 6);
        var retentionDays = configuration.GetValue<int>("License:RetentionDays", 7);

        _cleanupInterval = TimeSpan.FromHours(intervalHours);
        _retentionPeriod = TimeSpan.FromDays(retentionDays);

        _logger.LogInformation(
            "LicenseCleanupService initialized: interval={IntervalHours}h, retention={RetentionDays}d",
            intervalHours, retentionDays);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LicenseCleanupService started");

        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupExpiredLicensesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during license cleanup");
            }

            // Czeka do następnego cyklu
            await Task.Delay(_cleanupInterval, stoppingToken);
        }

        _logger.LogInformation("LicenseCleanupService stopped");
    }

    private async Task CleanupExpiredLicensesAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NexaDbContext>();

        var cutoffDate = DateTime.UtcNow - _retentionPeriod;

        _logger.LogInformation("Starting database cleanup. Removing data older than {CutoffDate}", cutoffDate);

        try
        {
            // Usuwa wygasłe licencje starsze niż określony okres przechowywania
            var deletedLicensesCount = await dbContext.IssuedLicenses
                .Where(l => l.ExpiresAt < cutoffDate)
                .ExecuteDeleteAsync(ct);

            if (deletedLicensesCount > 0)
            {
                _logger.LogInformation("Cleaned up {DeletedCount} expired licenses", deletedLicensesCount);
            }
            else
            {
                _logger.LogDebug("No expired licenses to clean up");
            }

            // Usuwa wygasłe/revoked refresh tokeny
            var refreshTokensCutoff = DateTime.UtcNow;
            var deletedTokensCount = await dbContext.RefreshTokens
                .Where(rt => rt.ExpiresAt < refreshTokensCutoff || rt.IsRevoked)
                .ExecuteDeleteAsync(ct);

            if (deletedTokensCount > 0)
            {
                _logger.LogInformation("Cleaned up {DeletedCount} expired/revoked refresh tokens", deletedTokensCount);
            }

            var deviceKeyCutoff = DateTime.UtcNow.AddDays(-90);
            var deletedDeviceKeysCount = await dbContext.UserDeviceKeys
                .Where(d => !d.IsActive || d.LastUsedAt < deviceKeyCutoff)
                .ExecuteDeleteAsync(ct);

            if (deletedDeviceKeysCount > 0)
            {
                _logger.LogInformation("Cleaned up {DeletedCount} inactive/old device keys", deletedDeviceKeysCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean up database");
            throw;
        }
    }
}
