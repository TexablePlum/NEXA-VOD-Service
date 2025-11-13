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
        /// </summary>
        [HttpGet("{contentId}/manifest.mpd")]
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
        /// Zwraca zaszyfrowany segment wideo lub audio
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
            
            _logger.LogInformation("Serving segment {SegmentName} with type {MimeType}", segmentName, mimeType);

            return PhysicalFile(
                Path.GetFullPath(segmentPath),
                mimeType,
                enableRangeProcessing: true
            );
        }

        /// <summary>
        /// GET /content/{id}/thumbnail.jpg
        /// Zwraca miniaturkę filmu.
        /// </summary>
        [HttpGet("{contentId}/thumbnail.jpg")]
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