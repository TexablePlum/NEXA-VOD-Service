using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Nexa.Client.Services.Base;
using Nexa.Shared.Models;

namespace Nexa.Client.Services.Catalog
{
    /// <summary>
    /// Implementacja serwisu katalogu filmów
    /// </summary>
    public class CatalogService : BaseApiService, ICatalogService
    {
        private readonly string _baseUrl;

        public CatalogService(HttpClient httpClient, string baseUrl) : base(httpClient)
        {
            _baseUrl = baseUrl.TrimEnd('/');
        }

        /// <inheritdoc/>
        public async Task<CatalogResponse> GetCatalogAsync(int limit = 50, int offset = 0, string? search = null, CancellationToken ct = default)
        {
            // Walidacja parametrów
            if (limit < 1 || limit > 100)
                throw new ArgumentException("Limit musi być między 1 a 100", nameof(limit));

            if (offset < 0)
                throw new ArgumentException("Offset nie może być ujemny", nameof(offset));

            // Budowanie URL z query parameters
            var url = $"{_baseUrl}/api/catalog?limit={limit}&offset={offset}";

            if (!string.IsNullOrWhiteSpace(search))
            {
                url += $"&search={Uri.EscapeDataString(search)}";
            }

            return await GetAsync<CatalogResponse>(url, ct);
        }

        /// <inheritdoc/>
        public async Task<ContentMetadata> GetContentByIdAsync(string contentId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(contentId))
                throw new ArgumentException("Content ID nie może być pusty", nameof(contentId));

            var url = $"{_baseUrl}/api/catalog/{contentId}";
            return await GetAsync<ContentMetadata>(url, ct);
        }
    }
}
