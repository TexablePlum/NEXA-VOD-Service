namespace Nexa.Shared.Models
{
    /// <summary>
    /// Reprezentuje metadane pojedynczego filmu w systemie.
    /// </summary>
    public class ContentMetadata
    {
        /// <summary>
        /// Unikalny identyfikator filmu (UUID z bazy DRM Server)
        /// Przykład: "550e8400-e29b-41d4-a716-446655440000"
        /// </summary>
        public string ContentId { get; set; } = string.Empty;

        /// <summary>
        /// Tytuł filmu
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Opis
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Długość filmu w SEKUNDACH
        /// </summary>
        public int DurationSeconds { get; set; }

        /// <summary>
        /// Jaki plan subskrypcji jest wymagany?
        /// Wartości: "free", "basic", "pro"
        /// Content Server nie weryfikuje, ale DRM Server tak
        /// </summary>
        public string RequiredPlan { get; set; } = "free";

        /// <summary>
        /// Lista dostępnych jakości wideo
        /// Przykład: ["480p", "720p", "1080p", "4k"]
        /// </summary>
        public List<string> AvailableQualities { get; set; } = new();

        /// <summary>
        /// URL do manifestu MPEG-DASH
        /// Przykład: "/content/550e8400-e29b-41d4-a716-446655440000/manifest.mpd"
        /// </summary>
        public string ManifestUrl { get; set; } = string.Empty;

        /// <summary>
        /// URL do miniaturki filmu
        /// Przykład: "/content/550e8400-e29b-41d4-a716-446655440000/thumbnail.jpg"
        /// </summary>
        public string ThumbnailUrl { get; set; } = string.Empty;

        /// <summary>
        /// Opcjonalne: gatunki filmu
        /// Przykład: ["Action", "Thriller", "Sci-Fi"]
        /// </summary>
        public List<string>? Genres { get; set; }

        /// <summary>
        /// Opcjonalne: data premiery
        /// </summary>
        public DateTime? ReleaseDate { get; set; }
    }
}