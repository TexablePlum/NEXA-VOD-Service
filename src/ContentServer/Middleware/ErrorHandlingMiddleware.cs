using Nexa.Shared.Exceptions;
using Nexa.Shared.Models;
using System.Net;
using System.Text.Json;

namespace Nexa.ContentServer.Middleware
{
    /// <summary>
    /// Middleware do globalnej obsługi błędów w Content Server.
    /// Łapie wyjątki i zamienia je na standardowe ErrorResponse.
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
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            // Domyślne wartości
            var statusCode = (int)HttpStatusCode.InternalServerError;
            var errorCode = ErrorCode.INTERNAL_SERVER_ERROR;
            var message = "An unexpected error occurred";
            Dictionary<string, object>? errorContext = null;

            // Rozpoznaje typ wyjątku
            if (exception is NexaException nexaEx)
            {
                // customowe wyjątki
                statusCode = nexaEx.StatusCode;
                errorCode = nexaEx.ErrorCode;
                message = nexaEx.Message;
                errorContext = nexaEx.Context;

                // Loguje warningi
                _logger.LogWarning(exception,
                    "Business exception: {ErrorCode} - {Message}",
                    errorCode, message);
            }
            else
            {
                // Nieoczekiwane wyjątki - loguje błąd
                _logger.LogError(exception,
                    "Unhandled exception in Content Server");
            }

            // Tworzy ErrorResponse
            var errorResponse = new ErrorResponse
            {
                ErrorCode = errorCode,
                Message = message,
                Timestamp = DateTime.UtcNow,
                Path = context.Request.Path,
                Context = errorContext
            };

            // W Development ze szczegółami technicznymi
            if (_environment.IsDevelopment())
            {
                errorResponse.Details = exception.ToString();
            }

            // Ustawia response
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = statusCode;

            var json = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await context.Response.WriteAsync(json);
        }
    }
}