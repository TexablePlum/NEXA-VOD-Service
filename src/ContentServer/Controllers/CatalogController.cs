using Microsoft.AspNetCore.Mvc;
using Nexa.Shared.Models;
using Nexa.ContentServer.Services;

namespace Nexa.ContentServer.Controllers
{
    /// <summary>
    /// API endpoint do przeglądania katalogu filmów.
    /// Faza 1 (MVP): Publiczne endpointy bez autoryzacji.
    /// Faza 2: Dodać autoryzację i JWT walidację.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class CatalogController : ControllerBase
    {
        private readonly CatalogService _catalogService;
        private readonly ILogger<CatalogController> _logger;

        public CatalogController(CatalogService catalogService, ILogger<CatalogController> logger)
        {
            _catalogService = catalogService;
            _logger = logger;
        }

        /// <summary>
        /// GET /api/catalog
        /// Zwraca listę wszystkich dostępnych filmów.
        /// </summary>
        /// <param name="limit">Maksymalna liczba wyników (default: 50, max: 100)</param>
        /// <param name="offset">Offset dla paginacji (default: 0)</param>
        /// <param name="search">Opcjonalne: szukaj w tytułach</param>
        [HttpGet]
        [ProducesResponseType(typeof(CatalogResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
        public async Task<ActionResult<CatalogResponse>> GetCatalog(
            [FromQuery] int limit = 50,
            [FromQuery] int offset = 0,
            [FromQuery] string? search = null)
        {
            // Walidacja parametrów
            if (limit < 1) limit = 50;
            if (limit > 100) limit = 100;
            if (offset < 0) offset = 0;

            // Pobiera wszystkie filmy
            var allContent = await _catalogService.GetAllContentAsync();

            // Opcjonalne filtrowanie po tytule
            if (!string.IsNullOrWhiteSpace(search))
            {
                allContent = allContent
                    .Where(c => c.Title.Contains(search, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // Paginacja
            var total = allContent.Count;
            var items = allContent
                .Skip(offset)
                .Take(limit)
                .ToList();

            var response = new CatalogResponse
            {
                Total = total,
                Limit = limit,
                Offset = offset,
                Items = items
            };

            return Ok(response);
        }

        /// <summary>
        /// GET /api/catalog/{id}
        /// Zwraca szczegóły konkretnego filmu.
        /// </summary>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(ContentMetadata), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<ContentMetadata>> GetContentById(string id)
        {
            var content = await _catalogService.GetContentByIdAsync(id);
            return Ok(content);
        }
    }
}