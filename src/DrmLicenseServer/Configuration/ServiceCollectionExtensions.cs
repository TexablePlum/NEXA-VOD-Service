using AspNetCoreRateLimit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Nexa.DrmLicenseServer.Data;
using Nexa.DrmLicenseServer.HealthChecks;
using Nexa.DrmLicenseServer.Repositories;
using Nexa.DrmLicenseServer.Services;
using Nexa.DrmLicenseServer.Services.Auth;
using Nexa.DrmLicenseServer.Services.License;
using StackExchange.Redis;
using System.Text;

namespace Nexa.DrmLicenseServer.Configuration;

/// <summary>
/// Metody rozszerzające dla konfiguracji serwisów w Program.cs.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Rejestruje bazę danych PostgreSQL + Entity Framework.
    /// </summary>
    public static IServiceCollection AddNexaDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<NexaDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MaxBatchSize(100);
                npgsqlOptions.CommandTimeout(30);
            });
        });

        return services;
    }

    /// <summary>
    /// Rejestruje Redis connection.
    /// </summary>
    public static IServiceCollection AddNexaRedis(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var redisConfig = configuration["Redis:ConnectionString"];

            if (string.IsNullOrEmpty(redisConfig))
            {
                throw new InvalidOperationException("Redis ConnectionString not found in configuration");
            }

            var configOptions = ConfigurationOptions.Parse(redisConfig);
            configOptions.ConnectTimeout = 5000;
            configOptions.SyncTimeout = 5000;
            configOptions.AbortOnConnectFail = true;

            return ConnectionMultiplexer.Connect(configOptions);
        });

        // Rejestracja IDatabase z Redis
        services.AddSingleton(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            return redis.GetDatabase();
        });

        return services;
    }

    /// <summary>
    /// Rejestruje JWT authentication.
    /// </summary>
    public static IServiceCollection AddNexaAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtSecret = configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("JWT Secret not configured");
        var jwtIssuer = configuration["Jwt:Issuer"] ?? "NexaDrmServer";
        var jwtAudience = configuration["Jwt:Audience"] ?? "NexaClient";

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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
                    ClockSkew = TimeSpan.FromMinutes(5),
                    RoleClaimType = System.Security.Claims.ClaimTypes.Role
                };

                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILogger<Program>>();

                        logger.LogWarning("JWT authentication failed: {Error}", context.Exception.Message);
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILogger<Program>>();

                        var userId = context.Principal?.FindFirst("sub")?.Value;
                        logger.LogDebug("JWT token validated for user: {UserId}", userId);
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();

        return services;
    }

    /// <summary>
    /// Rejestruje rate limiting (AspNetCoreRateLimit).
    /// </summary>
    public static IServiceCollection AddNexaRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.Configure<IpRateLimitOptions>(configuration.GetSection("IpRateLimiting"));
        services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
        services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
        services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
        services.AddSingleton<IProcessingStrategy, AsyncKeyLockProcessingStrategy>();
        services.AddInMemoryRateLimiting();

        return services;
    }

    /// <summary>
    /// Rejestruje wszystkie serwisy aplikacji.
    /// </summary>
    public static IServiceCollection AddNexaServices(this IServiceCollection services)
    {
        // Repositories
        services.AddScoped<IssuedLicenseRepository>();

        // Core Services
        services.AddScoped<UserService>();
        services.AddScoped<AuthService>();
        services.AddScoped<DeviceKeyService>();
        services.AddSingleton<AuditService>();
        services.AddSingleton<CekEncryptionService>();
        services.AddSingleton<CekPublicKeyEncryptionService>();

        // License Services
        services.AddScoped<LicenseService>();
        services.AddScoped<LicenseValidationService>();
        services.AddScoped<ContentMetadataService>();
        services.AddScoped<QualityService>();
        services.AddScoped<CekManager>();
        services.AddScoped<ConcurrentStreamManager>();
        services.AddScoped<CacheInvalidationService>();

        // Auth Services
        services.AddScoped<TokenService>();
        services.AddScoped<LoginRateLimiter>();

        // Background Services
        services.AddHostedService<LicenseCleanupService>();

        return services;
    }

    /// <summary>
    /// Rejestruje health checks.
    /// </summary>
    public static IServiceCollection AddNexaHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck("self", () =>
                Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("DRMServer is running"))
            .AddCheck<RedisHealthCheck>("redis");

        return services;
    }

    /// <summary>
    /// Rejestruje CORS policy.
    /// </summary>
    public static IServiceCollection AddNexaCors(this IServiceCollection services)
    {
        services.AddCors(options =>
        {
            options.AddPolicy("NexaClientPolicy", policy =>
            {
                policy
                    .WithOrigins("https://nexa.player", "https://localhost")
                    .AllowAnyMethod()
                    .AllowAnyHeader();
            });
        });

        return services;
    }

    /// <summary>
    /// Rejestruje Swagger.
    /// </summary>
    public static IServiceCollection AddNexaSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new() { Title = "NEXA DRM Server API", Version = "v1" });

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

        return services;
    }
}
