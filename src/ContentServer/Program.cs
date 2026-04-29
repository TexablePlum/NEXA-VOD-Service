using AspNetCoreRateLimit;
using Nexa.ContentServer.HealthChecks;
using Nexa.ContentServer.Middleware;
using Nexa.ContentServer.Services;
using StackExchange.Redis;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

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

// Response Compression
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();

    // Kompresuje JSON (nie kompresuje video/audio/images)
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

// Output Caching
builder.Services.AddOutputCache(options =>
{
    // Polityka dla catalog API
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

// Autentykacja JWT
// Content Server wymaga autentykacji JWT dla dostępu do streamingu
// Używa tego samego JWT co DrmLicenseServer
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("JWT Secret not configured. Set Jwt:Secret in appsettings.json or environment variables.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "NexaDRMServer";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "NexaClient";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.FromMinutes(5)  // Tolerancja na różnice czasu między serwerami
        };

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();
                logger.LogWarning("JWT Authentication failed: {Error}", context.Exception.Message);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "NEXA Content Server API", Version = "v1" });

    // Dodaje JWT do Swagger
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("NexaClientPolicy", policy =>
    {
        policy
            .WithOrigins("https://nexa.player", "https://localhost")
            .AllowAnyMethod()
            .AllowAnyHeader();
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

    // Pobiera IOutputCacheStore dla inwalidacji cache
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

// Response Compression
app.UseResponseCompression();

// Output Caching
app.UseOutputCache();

// Middleware obsługi błędów
app.UseMiddleware<ErrorHandlingMiddleware>();

// Security Headers
app.Use(async (context, next) =>
{
    // HSTS - wymusza HTTPS przez 1 rok (tylko dla HTTPS requests)
    // Sprawdza również X-Forwarded-Proto dla reverse proxy (nginx)
    var forwardedProto = context.Request.Headers["X-Forwarded-Proto"].ToString();
    if (context.Request.IsHttps || forwardedProto.Equals("https", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
    }

    // X-Frame-Options - zapobiega clickjacking
    context.Response.Headers.Append("X-Frame-Options", "DENY");

    // X-Content-Type-Options - zapobiega MIME sniffing
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");

    // Referrer-Policy - kontroluje jakie referrer info jest wysyłane
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

    // Content-Security-Policy - ogranicza źródła zasobów
    // Relaxed dla Swagger UI (Development), strict dla API
    var path = context.Request.Path.Value ?? "";
    if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/_content", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase))
    {
        // Permisywny CSP dla Swagger UI (Development only)
        context.Response.Headers.Append("Content-Security-Policy",
            "default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; connect-src 'self'");
    }
    else
    {
        // Restrykcyjny CSP dla API
        context.Response.Headers.Append("Content-Security-Policy",
            "default-src 'none'; frame-ancestors 'none'; base-uri 'self'");
    }

    // X-Permitted-Cross-Domain-Policies
    context.Response.Headers.Append("X-Permitted-Cross-Domain-Policies", "none");

    await next();
});

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

// autentykacja JWT
app.UseAuthentication();
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