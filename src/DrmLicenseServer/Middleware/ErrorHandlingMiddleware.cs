using Nexa.Shared.Exceptions;
using Nexa.Shared.Models;
using System.Text.Json;

namespace Nexa.DrmLicenseServer.Middleware;

/// <summary>
/// Middleware do globalnej obsługi błędów.
/// Konwertuje wyjątki na standardowe ErrorResponse zgodne z RFC 7807.
/// </summary>
public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ErrorHandlingMiddleware(
        RequestDelegate next,
        ILogger<ErrorHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (NexaException nex)
        {
            _logger.LogWarning(nex, "Nexa exception occurred: {ErrorCode} - {Message}",
                nex.ErrorCode, nex.Message);

            await HandleNexaExceptionAsync(context, nex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception occurred");

            await HandleGenericExceptionAsync(context, ex);
        }
    }

    private async Task HandleNexaExceptionAsync(HttpContext context, NexaException exception)
    {
        context.Response.StatusCode = exception.StatusCode;
        context.Response.ContentType = "application/json";

        var errorResponse = new ErrorResponse
        {
            ErrorCode = exception.ErrorCode,
            Message = exception.Message,
            Timestamp = DateTime.UtcNow,
            Path = context.Request.Path,
            Context = exception.Context
        };

        // W Development dodaje szczegóły
        if (_environment.IsDevelopment())
        {
            errorResponse.Details = exception.StackTrace;
        }

        var json = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }

    private async Task HandleGenericExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";

        var errorResponse = new ErrorResponse
        {
            ErrorCode = ErrorCode.INTERNAL_SERVER_ERROR,
            Message = "Wystąpił wewnętrzny błąd serwera.",
            Timestamp = DateTime.UtcNow,
            Path = context.Request.Path
        };

        // W Development dodaje szczegóły
        if (_environment.IsDevelopment())
        {
            errorResponse.Details = exception.ToString();
        }

        var json = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}
