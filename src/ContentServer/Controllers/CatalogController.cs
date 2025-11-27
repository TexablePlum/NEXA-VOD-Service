using Microsoft.AspNetCore.Mvc;
using Nexa.Shared.Models;
using Nexa.ContentServer.Services;

namespace Nexa.ContentServer.Controllers
{
    /// <summary>
    /// API endpoint do przeglądania katalogu filmów.
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
        /// Zwraca listę dostępnych filmów z paginacją i opcjonalnym wyszukiwaniem.
        /// Ładuje tylko potrzebne filmy (limit), nie wszystkie.
        /// Cache przez 5 minut, inwalidacja przy zmianach w storage.
        /// </summary>
        /// <param name="limit">Maksymalna liczba wyników (default: 50, max: 100)</param>
        /// <param name="offset">Offset dla paginacji (default: 0)</param>
        /// <param name="search">Opcjonalne: szuka w tytułach</param>
        [HttpGet]
        [Microsoft.AspNetCore.OutputCaching.OutputCache(PolicyName = "CatalogCache")]
        [ProducesResponseType(typeof(CatalogResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
        public async Task<ActionResult<CatalogResponse>> GetCatalog(
            [FromQuery] int limit = 50,
            [FromQuery] int offset = 0,
            [FromQuery] string? search = null,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("GET /api/catalog - limit={Limit}, offset={Offset}, search={Search}", limit, offset, search);

            // Walidacja parametrów
            if (limit < 1) limit = 50;
            if (limit > 100) limit = 100;
            if (offset < 0) offset = 0;

            // Pobiera total count (nie ładuje wszystkich obiektów jeśli brak search)
            var total = await _catalogService.GetTotalCountAsync(search, cancellationToken);

            // Pobiera tylko potrzebne filmy (limit) z paginacją i searchem
            var items = await _catalogService.GetAllContentAsync(limit, offset, search, cancellationToken);

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
        /// Cache przez 5 minut, inwalidacja przy zmianach w storage.
        /// </summary>
        [HttpGet("{id}")]
        [Microsoft.AspNetCore.OutputCaching.OutputCache(PolicyName = "ContentCache")]
        [ProducesResponseType(typeof(ContentMetadata), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<ContentMetadata>> GetContentById(string id, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("GET /api/catalog/{Id}", id);
            var content = await _catalogService.GetContentByIdAsync(id, cancellationToken);
            return Ok(content);
        }

        /// <summary>
        /// GET /api/catalog/{id}/thumbnail.jpg
        /// Zwraca miniaturkę filmu.
        /// Publiczny endpoint - nie wymaga autoryzacji.
        /// Cache przez 1 godzinę.
        /// </summary>
        [HttpGet("{id}/thumbnail.jpg")]
        [Microsoft.AspNetCore.OutputCaching.OutputCache(Duration = 3600)] // 1 godzina
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public IActionResult GetThumbnail(string id)
        {
            _logger.LogInformation("GET /api/catalog/{Id}/thumbnail.jpg", id);
            var thumbnailPath = _catalogService.GetThumbnailPath(id);

            return PhysicalFile(
                Path.GetFullPath(thumbnailPath),
                "image/jpeg",
                enableRangeProcessing: true
            );
        }
    }
}