using AspNetCoreRateLimit;
using Nexa.DrmLicenseServer.Configuration;
using Nexa.DrmLicenseServer.Data;
using Nexa.DrmLicenseServer.Middleware;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Kontrolery
builder.Services.AddControllers();

// HttpClient dla komunikacji z Content Server
builder.Services.AddHttpClient();

// Konfiguracja serwisów przez extension methods
builder.Services.AddNexaDatabase(builder.Configuration);
builder.Services.AddNexaRedis(builder.Configuration);
builder.Services.AddNexaAuthentication(builder.Configuration);
builder.Services.AddNexaRateLimiting(builder.Configuration);
builder.Services.AddNexaServices();
builder.Services.AddNexaHealthChecks();
builder.Services.AddNexaCors();
builder.Services.AddNexaSwagger();

var app = builder.Build();

// Auto-migracje bazy danych przy starcie
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<NexaDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        logger.LogInformation("Applying database migrations...");
        dbContext.Database.Migrate();
        logger.LogInformation("Database migrations applied successfully");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error applying database migrations");
        throw;
    }
}

// Middleware obsługi błędów
app.UseMiddleware<ErrorHandlingMiddleware>();

// Forwarded Headers - aplikacja rozumie że jest za proxy
app.UseNexaForwardedHeaders();

// IP Whitelist Middleware - ogranicza dostęp do admin endpoints
app.UseMiddleware<IpWhitelistMiddleware>();

// Security Headers
app.UseNexaSecurityHeaders(app.Environment);

// HTTPS enforcement - warunkowo włączony (wyłączony w produkcji za proxy)
if (!app.Environment.IsProduction() || builder.Configuration.GetValue<bool>("Security:EnforceHttpsRedirection", false))
{
    app.UseHttpsRedirection();
}

// Rate Limiting
app.UseIpRateLimiting();

// Swagger tylko w Development
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// CORS
app.UseCors("NexaClientPolicy");

// Autentykacja i Autoryzacja
app.UseAuthentication();
app.UseMiddleware<UserRateLimitingMiddleware>(); // Rate limiting per-user
app.UseAuthorization();

// Health check endpoint
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";

        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds
            }),
            totalDuration = report.TotalDuration.TotalMilliseconds
        });

        await context.Response.WriteAsync(result);
    }
});

app.MapControllers();

app.Run();
