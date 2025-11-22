using Microsoft.AspNetCore.HttpOverrides;

namespace Nexa.DrmLicenseServer.Configuration;

/// <summary>
/// Metody rozszerzające dla konfiguracji potoku aplikacji w Program.cs.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Dodaje security headers middleware.
    /// </summary>
    public static IApplicationBuilder UseNexaSecurityHeaders(
        this IApplicationBuilder app,
        IWebHostEnvironment environment)
    {
        app.Use(async (context, next) =>
        {
            // HSTS - wymusza HTTPS przez 1 rok (TYLKO dla HTTPS requests)
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

            // X-Permitted-Cross-Domain-Policies - blokuje cross-domain policy dla Flash/PDF
            context.Response.Headers.Append("X-Permitted-Cross-Domain-Policies", "none");

            await next();
        });

        return app;
    }

    /// <summary>
    /// Konfiguruje forwarded headers (dla działania za proxy).
    /// </summary>
    public static IApplicationBuilder UseNexaForwardedHeaders(this IApplicationBuilder app)
    {
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            // Ufa proxy z sieci Dockera
            KnownNetworks = { },  // Puste = ufaj wszystkim
            KnownProxies = { }
        });

        return app;
    }
}
