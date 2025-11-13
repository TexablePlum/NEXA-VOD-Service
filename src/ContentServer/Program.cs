using Nexa.ContentServer.HealthChecks;
using Nexa.ContentServer.Middleware;
using Nexa.ContentServer.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Kontrolery
builder.Services.AddControllers();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("NexaClientPolicy", policy =>
    {
        policy
            .AllowAnyOrigin()       // Zezwalaj z dowolnego origin (MVP - póŸniej zmienimy)
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

// Pobiera œcie¿kê z appsettings.json
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
    configOptions.AbortOnConnectFail = false;

    return ConnectionMultiplexer.Connect(configOptions);
});

// Rejestracja CatalogService
builder.Services.AddSingleton<CatalogService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<CatalogService>>();

    // Pobiera bazê Redis
    var redisMultiplexer = sp.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>();
    var redisDb = redisMultiplexer.GetDatabase();

    // Pobiera czas trwania cache z konfiguracji (domyœlnie 1h)
    var cacheDurationSeconds = builder.Configuration.GetValue<int?>("ContentStorage:CacheDurationSeconds") ?? 3600;
    var cacheDuration = TimeSpan.FromSeconds(cacheDurationSeconds);

    return new CatalogService(storagePath, logger, redisDb, cacheDuration);
});

// Rejestracja StreamingService
builder.Services.AddSingleton<StreamingService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<StreamingService>>();
    return new StreamingService(storagePath, logger);
});

var app = builder.Build();

// Middleware obs³ugi b³êdów
app.UseMiddleware<ErrorHandlingMiddleware>();

// Swagger tylko w Development
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

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