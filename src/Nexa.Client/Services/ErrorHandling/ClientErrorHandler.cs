using Nexa.Client.Services.Exceptions;
using Nexa.Shared.Models;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using System.Linq;

namespace Nexa.Client.Services.ErrorHandling
{
    public class ClientErrorHandler
    {
        private readonly JsonSerializerOptions _jsonOptions;

        public ClientErrorHandler()
        {
            _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        }

        public async Task ThrowIfErrorAsync(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode) return;

            var statusCode = (int)response.StatusCode;
            string content = await response.Content.ReadAsStringAsync();
            ErrorResponse errorResponse = null;

            // Próba deserializacji jako standardowy ErrorResponse (NEXA Format)
            try
            {
                errorResponse = JsonSerializer.Deserialize<ErrorResponse>(content, _jsonOptions);
            }
            catch {  }

            // Jeśli to nie ErrorResponse, sprawdza czy to Validation ProblemDetails (ASP.NET Default)
            if (errorResponse == null || string.IsNullOrEmpty(errorResponse.ErrorCode))
            {
                errorResponse = TryParseProblemDetails(content, statusCode);
            }

            // Fallback - jeśli nie udało się nic sparsować
            if (errorResponse == null)
            {
                errorResponse = new ErrorResponse
                {
                    ErrorCode = MapStatusCodeToErrorCode(statusCode),
                    Message = !string.IsNullOrWhiteSpace(content) ? content : response.ReasonPhrase ?? "Unknown Error",
                    Timestamp = DateTime.UtcNow
                };
            }

            // Obsługa Rate Limiting (dodanie Retry-After z nagłówków jeśli brakuje w kontekście)
            if (statusCode == 429 && response.Headers.RetryAfter?.Delta != null)
            {
                errorResponse.Context ??= new Dictionary<string, object>();
                if (!errorResponse.Context.ContainsKey("retryAfter"))
                {
                    errorResponse.Context["retryAfter"] = response.Headers.RetryAfter.Delta.Value.TotalSeconds;
                }
            }

            throw new NexaClientException(errorResponse, statusCode);
        }

        private ErrorResponse? TryParseProblemDetails(string json, int statusCode)
        {
            try
            {
                // Próbuje czytać jako surowy JSON żeby wyciągnąć błędy walidacji
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                // Sprawdza czy to struktura ProblemDetails (ma pole "errors" lub "title")
                if (root.TryGetProperty("errors", out var errorsElement))
                {
                    var validationErrors = new Dictionary<string, object>();

                    foreach (var property in errorsElement.EnumerateObject())
                    {
                        // Konwersja tablicy stringów na pojedynczy string lub listę
                        var messages = property.Value.EnumerateArray().Select(x => x.GetString()).ToList();
                        validationErrors[property.Name] = messages.Count == 1 ? messages[0] : messages;
                    }

                    return new ErrorResponse
                    {
                        ErrorCode = ErrorCode.VALIDATION_ERROR, //
                        Message = "Błąd walidacji danych.",
                        Context = validationErrors,
                        Timestamp = DateTime.UtcNow
                    };
                }
            }
            catch
            {
                return null;
            }
            return null;
        }

        private string MapStatusCodeToErrorCode(int statusCode)
        {
            return statusCode switch
            {
                401 => ErrorCode.UNAUTHORIZED, //
                403 => ErrorCode.FORBIDDEN,
                404 => ErrorCode.NOT_FOUND,
                500 => ErrorCode.INTERNAL_SERVER_ERROR,
                503 => ErrorCode.SERVICE_UNAVAILABLE,
                _ => "HTTP_ERROR"
            };
        }
    }
}