using Nexa.Shared.Models;

namespace Nexa.Client.Services.Catalog
{
    /// <summary>
    /// Serwis do pobierania katalogu filmów z Content Server
    /// </summary>
    public interface ICatalogService
    {
        /// <summary>
        /// Pobiera katalog filmów z paginacją
        /// </summary>
        /// <param name="limit">Ile filmów pobrać (max 100)</param>
        /// <param name="offset">Od którego filmu zacząć</param>
        /// <param name="search">Opcjonalne wyszukiwanie w tytułach</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Odpowiedź z listą filmów i info o paginacji</returns>
        Task<CatalogResponse> GetCatalogAsync(int limit = 50, int offset = 0, string? search = null, CancellationToken ct = default);

        /// <summary>
        /// Pobiera szczegóły konkretnego filmu
        /// </summary>
        /// <param name="contentId">ID filmu</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Metadane filmu</returns>
        Task<ContentMetadata> GetContentByIdAsync(string contentId, CancellationToken ct = default);
    }
}
