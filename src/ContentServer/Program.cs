using AspNetCoreRateLimit;
using Nexa.ContentServer.HealthChecks;
using Nexa.ContentServer.Middleware;
using Nexa.ContentServer.Services;
using StackExchange.Redis;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using Microsoft.AspNetCore.OutputCaching;

var builder = WebApplication.CreateBuilder(args);

// Kontrolery
builder.Services.AddControllers();

// Rate Limiting
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
builder.Services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
builder.Services.AddSingleton<IProcessingStrategy, AsyncKeyLockProcessingStrategy>();
builder.Services.AddInMemoryRateLimiting();

// Response Compression (Gzip/Brotli dla JSON)
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();

    // Kompresuje tylko JSON (nie kompresuje video/audio/images z definicji są już skompresowane)
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "application/json",
        "application/dash+xml"  // MPD manifest
    });
});

// Konfiguruje poziomy kompresji
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});

builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});

// Output Caching (cache gotowych HTTP responses)
builder.Services.AddOutputCache(options =>
{
    // Polityka dla catalog API - cache z możliwością iwalidacji
    options.AddPolicy("CatalogCache", builder =>
    {
        builder
            .Expire(TimeSpan.FromMinutes(5))  // 5 minut domyślnie
            .SetVaryByQuery("limit", "offset", "search")  // Różne cache w zależności od parametru zapytania
            .Tag("catalog");  // Tag do inwalidacji
    });

    // Polityka dla pojedynczego contentu
    options.AddPolicy("ContentCache", builder =>
    {
        builder
            .Expire(TimeSpan.FromMinutes(5))
            .Tag("catalog");  // Inwalidacja razem z katalogiem
    });

    // Polityka dla segmentów inicjalizacyjnych (małe, często używane)
    options.AddPolicy("InitSegmentCache", builder =>
    {
        builder
            .Expire(TimeSpan.FromHours(24))  // 24h
            .SetVaryByQuery("*");
    });

    // Limit rozmiaru cache (100 MB max)
    options.SizeLimit = 100 * 1024 * 1024;
});

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("NexaClientPolicy", policy =>
    {
        policy
            .AllowAnyOrigin()       // Zezwalaj z dowolnego origin (MVP - później zmienimy)
            .AllowAnyMethod()       // Zezwalaj GET, POST, PUT, DELETE, etc.
            .AllowAnyHeader();      // Zezwalaj dowolne headers (Authorization, Content-Type, etc.)
    });
});

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck("self", () =>
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("ContentServer is running"))
    .AddCheck<StorageHealthCheck>("storage")
    .AddCheck<RedisHealthCheck>("redis");

// Pobiera ścieżkę z appsettings.json
var storagePath = builder.Configuration["ContentStorage:BasePath"] ?? "./content/storage";

// Rejestracja StackExchange.Redis
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(sp =>
{
    var redisConfig = builder.Configuration["Redis:ConnectionString"];

    if (string.IsNullOrEmpty(redisConfig))
    {
        throw new InvalidOperationException("Redis ConnectionString not found in configuration (Redis:ConnectionString)");
    }

    var configOptions = ConfigurationOptions.Parse(redisConfig);
    configOptions.ConnectTimeout = 5000;
    configOptions.SyncTimeout = 5000;
    configOptions.AbortOnConnectFail = true;

    return ConnectionMultiplexer.Connect(configOptions);
});

// Rejestracja CatalogService
builder.Services.AddSingleton<CatalogService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<CatalogService>>();
    var redisMultiplexer = sp.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>();
    var redisDb = redisMultiplexer.GetDatabase();

    var cacheDurationSeconds = builder.Configuration.GetValue<int?>("ContentStorage:CacheDurationSeconds") ?? 3600;
    var cacheDuration = TimeSpan.FromSeconds(cacheDurationSeconds);

    // Pobierz IOutputCacheStore dla invalidacji cache
    var outputCacheStore = sp.GetService<IOutputCacheStore>();

    var service = new CatalogService(storagePath, logger, redisDb, cacheDuration, outputCacheStore);

    var lifetime = sp.GetRequiredService<IHostApplicationLifetime>();
    lifetime.ApplicationStopping.Register(() => service.Dispose());

    return service;
});

// Rejestracja StreamingService
builder.Services.AddSingleton<StreamingService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<StreamingService>>();
    return new StreamingService(storagePath, logger);
});

var app = builder.Build();

// Response Compression - MUSI BYĆ NA POCZĄTKU PIPELINE
app.UseResponseCompression();

// Output Caching - zaraz po compression
app.UseOutputCache();

// Middleware obsługi błędów
app.UseMiddleware<ErrorHandlingMiddleware>();

// Rate Limiting
app.UseIpRateLimiting();

// Swagger tylko w Development
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Dla Dockera lepiej nie używać
// app.UseHttpsRedirection();

// CORS
app.UseCors("NexaClientPolicy");

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