using Microsoft.AspNetCore.Mvc;
using Nexa.ContentServer.Services;
using Nexa.Shared.Models;

namespace Nexa.ContentServer.Controllers
{
    /// <summary>
    /// Controller do serwowania plików wideo: manifesty, segmenty, thumbnails.
    /// Faza 1 (MVP): Publiczne endpointy - serwuje pliki z dysku.
    /// Faza 2: Dodać walidację JWT.
    /// </summary>
    [ApiController]
    [Route("content")]
    public class StreamingController : ControllerBase
    {
        private readonly StreamingService _streamingService;
        private readonly ILogger<StreamingController> _logger;

        public StreamingController(
            StreamingService streamingService,
            ILogger<StreamingController> logger)
        {
            _streamingService = streamingService;
            _logger = logger;
        }

        /// <summary>
        /// GET /content/{id}/manifest.mpd
        /// Zwraca manifest MPEG-DASH dla filmu.
        /// Manifesty mogą się zmieniać, więc krótki cache.
        /// </summary>
        [HttpGet("{contentId}/manifest.mpd")]
        [Microsoft.AspNetCore.OutputCaching.OutputCache(Duration = 300)] // 5 minut
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public IActionResult GetManifest(string contentId)
        {
            var manifestPath = _streamingService.GetManifestPath(contentId);

            return PhysicalFile(
                Path.GetFullPath(manifestPath),
                "application/dash+xml",
                enableRangeProcessing: true
            );
        }

        /// <summary>
        /// GET /content/{id}/{quality}/segment_{n}.m4s
        /// Zwraca zaszyfrowany segment wideo lub audio.
        /// Output Cache: Tylko dla init segments (małe, ~1-2KB) - 24h cache.
        /// Duże segmenty wideo NIE są cacheowane.
        /// </summary>
        [HttpGet("{contentId}/{quality}/{segmentName}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public IActionResult GetSegment(string contentId, string quality, string segmentName)
        {
            var segmentPath = _streamingService.GetSegmentPath(contentId, quality, segmentName);
            var mimeType = _streamingService.GetMimeTypeForSegment(segmentName);

            // Log tylko init segments
            if (segmentName.StartsWith("init_"))
            {
                _logger.LogDebug("Serving init segment {SegmentName} with type {MimeType}", segmentName, mimeType);
            }

            // Output Cache tylko dla init segments
            if (segmentName.StartsWith("init_"))
            {
                HttpContext.Response.Headers.CacheControl = "public, max-age=86400, immutable";
            }

            return PhysicalFile(
                Path.GetFullPath(segmentPath),
                mimeType,
                enableRangeProcessing: true
            );
        }

        /// <summary>
        /// GET /content/{id}/thumbnail.jpg
        /// Zwraca miniaturkę filmu.
        /// Output Cache: 1h.
        /// </summary>
        [HttpGet("{contentId}/thumbnail.jpg")]
        [Microsoft.AspNetCore.OutputCaching.OutputCache(Duration = 3600)] // 1 godzina
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public IActionResult GetThumbnail(string contentId)
        {
            var thumbnailPath = _streamingService.GetThumbnailPath(contentId);

            return PhysicalFile(
                Path.GetFullPath(thumbnailPath),
                "image/jpeg",
                enableRangeProcessing: true
            );
        }
    }
}