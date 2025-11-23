using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using Nexa.Client.Services.ErrorHandling;
using Nexa.Client.Services.Exceptions;
using Nexa.Shared.Models;
using System.Net.Http.Headers;
using System;

namespace Nexa.Client.Services.Base
{
    public abstract class BaseApiService
    {
        protected readonly HttpClient _httpClient;
        protected readonly ClientErrorHandler _errorHandler;
        protected readonly JsonSerializerOptions _jsonOptions;

        protected BaseApiService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _errorHandler = new ClientErrorHandler();
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }

        protected async Task<T> GetAsync<T>(string uri, CancellationToken ct = default)
        {
            try
            {
                var response = await _httpClient.GetAsync(uri, ct);
                await _errorHandler.ThrowIfErrorAsync(response);

                var content = await response.Content.ReadAsStringAsync(ct);
                return JsonSerializer.Deserialize<T>(content, _jsonOptions)!;
            }
            catch (HttpRequestException ex)
            {
                throw CreateNetworkException(ex);
            }
        }

        protected async Task<TResponse> PostAsync<TRequest, TResponse>(string uri, TRequest request, CancellationToken ct = default)
        {
            try
            {
                var json = JsonSerializer.Serialize(request, _jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(uri, content, ct);
                await _errorHandler.ThrowIfErrorAsync(response);

                if (typeof(TResponse) == typeof(bool) && response.IsSuccessStatusCode)
                    return (TResponse)(object)true;

                var responseContent = await response.Content.ReadAsStringAsync(ct);

                // Obsługa pustych odpowiedzi
                if (string.IsNullOrWhiteSpace(responseContent)) return default!;

                return JsonSerializer.Deserialize<TResponse>(responseContent, _jsonOptions)!;
            }
            catch (HttpRequestException ex)
            {
                throw CreateNetworkException(ex);
            }
        }

        private NexaClientException CreateNetworkException(Exception ex)
        {
            return new NexaClientException(new ErrorResponse
            {
                ErrorCode = "NETWORK_ERROR",
                Message = "Nie można połączyć się z serwerem. Sprawdź połączenie internetowe.",
                Details = ex.Message,
                Timestamp = DateTime.UtcNow
            }, 0);
        }

        // Metoda do ustawiania tokena
        public void SetAccessToken(string token)
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        }
    }
}