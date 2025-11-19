using AspNetCoreRateLimit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Nexa.DrmLicenseServer.Data;
using Nexa.DrmLicenseServer.HealthChecks;
using Nexa.DrmLicenseServer.Middleware;
using Nexa.DrmLicenseServer.Services;
using StackExchange.Redis;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Kontrolery
builder.Services.AddControllers();

// Database (PostgreSQL + Entity Framework Core)
builder.Services.AddDbContext<NexaDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        // Ustawienia
        npgsqlOptions.MaxBatchSize(100);
        npgsqlOptions.CommandTimeout(30);
    });
});

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
builder.Services.AddSwaggerGen(c =>
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

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("NexaClientPolicy", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

// JWT Authentication
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("JWT Secret not configured");
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
            ClockSkew = TimeSpan.Zero // Brak tolerancji na czas (strict expiration)
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

builder.Services.AddAuthorization();

// Redis
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var redisConfig = builder.Configuration["Redis:ConnectionString"];

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
builder.Services.AddSingleton(sp =>
{
    var redis = sp.GetRequiredService<IConnectionMultiplexer>();
    return redis.GetDatabase();
});

// Rejestracja serwisów
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<LicenseService>();
builder.Services.AddScoped<DeviceKeyService>();
builder.Services.AddSingleton<AuditService>();
builder.Services.AddSingleton<CekEncryptionService>();
builder.Services.AddSingleton<CekPublicKeyEncryptionService>();

// Background services
builder.Services.AddHostedService<LicenseCleanupService>(); // Czyszczenie wygasłych licencji

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck("self", () =>
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("DRMServer is running"))
    .AddCheck<RedisHealthCheck>("redis");

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
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// HTTPS enforcement - warunkowo włączony (wyłączony w production za proxy)
// W Docker/Kubernetes proxy (nginx, traefik) kończy HTTPS i wysyła HTTP do kontenera
// UseHttpsRedirection powodowałoby redirect loop
// Włączyć tylko jeśli nie ma proxy (Development lub Security:EnforceHttpsRedirection=true)
if (!app.Environment.IsProduction() || builder.Configuration.GetValue<bool>("Security:EnforceHttpsRedirection", false))
{
    app.UseHttpsRedirection();

    // HSTS (HTTP Strict Transport Security) - tylko w Development
    if (app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }
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
