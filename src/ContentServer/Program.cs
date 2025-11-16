using AspNetCoreRateLimit;
using Nexa.ContentServer.HealthChecks;
using Nexa.ContentServer.Middleware;
using Nexa.ContentServer.Services;
using StackExchange.Redis;

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

    var service = new CatalogService(storagePath, logger, redisDb, cacheDuration);

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