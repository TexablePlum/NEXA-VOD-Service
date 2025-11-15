using ContentServer.Exceptions;
using Nexa.Shared.Exceptions;

namespace Nexa.ContentServer.Services
{
    /// <summary>
    /// Serwis do obsługi streamingu: manifesty, segmenty, thumbnails.
    /// Waliduje parametry i sprawdza dostępność plików.
    /// </summary>
    public class StreamingService
    {
        private readonly string _basePath;
        private readonly ILogger<StreamingService> _logger;

        public StreamingService(string basePath, ILogger<StreamingService> logger)
        {
            _basePath = basePath;
            _logger = logger;
        }

        /// <summary>
        /// Pobiera ścieżkę do manifestu MPEG-DASH.
        /// Rzuca wyjątek jeśli manifest nie istnieje.
        /// </summary>
        public string GetManifestPath(string contentId)
        {
            if (string.IsNullOrWhiteSpace(contentId) || contentId.Contains(".."))
            {
                throw new ValidationException("Invalid content ID");
            }

            var manifestPath = Path.Combine(_basePath, contentId, "manifest.mpd");

            var fullBasePath = Path.GetFullPath(_basePath);
            var fullManifestPath = Path.GetFullPath(manifestPath);

            if (!fullManifestPath.StartsWith(fullBasePath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Path traversal attempt blocked for manifest: {ContentId}", contentId);
                throw new ValidationException("Invalid content ID");
            }

            if (!File.Exists(manifestPath))
            {
                _logger.LogWarning("Manifest not found: {ContentId}", contentId);
                throw new ManifestNotFoundException(contentId);
            }

            return manifestPath;
        }

        /// <summary>
        /// Pobiera ścieżkę do segmentu wideo.
        /// Rzuca wyjątek jeśli segment nie istnieje lub jest niebezpieczny.
        /// </summary>
        public string GetSegmentPath(string contentId, string quality, string segmentName)
        {
            if (string.IsNullOrWhiteSpace(contentId))
            {
                throw new ValidationException("Content ID cannot be empty");
            }

            if (string.IsNullOrWhiteSpace(quality))
            {
                throw new ValidationException("Quality parameter cannot be empty");
            }

            if (string.IsNullOrWhiteSpace(segmentName))
            {
                throw new ValidationException("Segment name cannot be empty");
            }

            //
            // KRYTYCZNE !!!
            //
            // 1. Sprawdza, czy nazwa segmentu nie zawiera ".." (próba wyjścia z folderu)
            // 2. Sprawdza, czy kończy się na .m4s (blokuje dostęp do .json, .key, .mpd itp.)
            //
            if (segmentName.Contains("..") || !segmentName.EndsWith(".m4s"))
            {
                _logger.LogWarning("Potential path traversal attack blocked: {SegmentName}", segmentName);
                throw new ValidationException("Invalid segment name");
            }

            var segmentPath = Path.Combine(
                _basePath,
                contentId,
                quality,
                segmentName
            );

            // Finalna ścieżka ma być w folderze _basePath
            var fullBasePath = Path.GetFullPath(_basePath);
            var fullSegmentPath = Path.GetFullPath(segmentPath);

            if (!fullSegmentPath.StartsWith(fullBasePath))
            {
                _logger.LogWarning("Path traversal attack detected: {SegmentPath}", segmentPath);
                throw new ValidationException("Invalid segment path");
            }


            if (!File.Exists(segmentPath))
            {
                _logger.LogWarning(
                    "Segment not found: {ContentId}/{Quality}/{SegmentName}",
                    contentId, quality, segmentName);

                throw new SegmentNotFoundException(contentId, quality, segmentName);
            }

            return segmentPath;
        }

        /// <summary>
        /// Dostarcza poprawny typ MIME dla segmentu.
        /// </summary>
        public string GetMimeTypeForSegment(string segmentName)
        {
            if (segmentName.StartsWith("init_video") || segmentName.StartsWith("video_"))
            {
                return "video/mp4";
            }

            if (segmentName.StartsWith("init_audio") || segmentName.StartsWith("audio_"))
            {
                return "audio/mp4";
            }

            // Domyślnie
            return "application/octet-stream";
        }

        /// <summary>
        /// Pobiera ścieżkę do miniaturki filmu.
        /// Rzuca wyjątek jeśli miniaturka nie istnieje.
        /// </summary>
        public string GetThumbnailPath(string contentId)
        {
            if (string.IsNullOrWhiteSpace(contentId) || contentId.Contains(".."))
            {
                throw new ValidationException("Invalid content ID");
            }

            var thumbnailPath = Path.Combine(_basePath, contentId, "thumbnail.jpg");

            var fullBasePath = Path.GetFullPath(_basePath);
            var fullThumbnailPath = Path.GetFullPath(thumbnailPath);

            if (!fullThumbnailPath.StartsWith(fullBasePath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Path traversal attempt blocked for thumbnail: {ContentId}", contentId);
                throw new ValidationException("Invalid content ID");
            }

            if (!File.Exists(thumbnailPath))
            {
                _logger.LogWarning("Thumbnail not found: {ContentId}", contentId);
                throw new ThumbnailNotFoundException(contentId);
            }

            return thumbnailPath;
        }
    }
}