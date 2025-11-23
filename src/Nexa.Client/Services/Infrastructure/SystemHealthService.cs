using Nexa.Client.Configuration;
using Nexa.Shared.Models;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Nexa.Client.Services.Infrastructure
{
    public enum SystemHealthStatus
    {
        Healthy,            // Wszystko działa
        GatewayUnreachable, // Nginx (lub brak internetu)
        DrmUnreachable,     // Backend licencji 
        ContentUnreachable  // Backend contentu 
    }

    public interface ISystemHealthService
    {
        Task<SystemHealthStatus> CheckHealthAsync();
    }

    public class SystemHealthService : ISystemHealthService
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public SystemHealthService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<SystemHealthStatus> CheckHealthAsync()
        {
            // Używa klienta skonfigurowanego w App.xaml.cs
            var client = _httpClientFactory.CreateClient("NexaGateway");

            // 1. Sprawdza Gateway (Nginx) - endpoint /health
            if (!await IsEndpointHealthy(client, "/health"))
            {
                return SystemHealthStatus.GatewayUnreachable;
            }

            // 2. Sprawdza DRM Server (przez Nginx) - endpoint /health/drm
            if (!await IsEndpointHealthy(client, "/health/drm"))
            {
                return SystemHealthStatus.DrmUnreachable;
            }

            // 3. Sprawdza Content Server (przez Nginx) - endpoint /health/content
            if (!await IsEndpointHealthy(client, "/health/content"))
            {
                return SystemHealthStatus.ContentUnreachable;
            }

            return SystemHealthStatus.Healthy;
        }

        private async Task<bool> IsEndpointHealthy(HttpClient client, string endpoint)
        {
            try
            {
                // Pobiera JSON i deserializuje do modelu z Shared
                var result = await client.GetFromJsonAsync<HealthResponse>(endpoint);

                // Sprawdza czy status w JSON to "Healthy"
                // (HealthChecki .NET zwracają "Healthy", "Degraded" lub "Unhealthy")
                return result != null && result.Status == "Healthy";
            }
            catch
            {
                // Błąd sieci, timeout, 404, 500 lub błąd parsowania JSON
                return false;
            }
        }
    }
}