namespace Nexa.Shared.Models
{
    /// <summary>
    /// Kody błędów używane w całym systemie NEXA.
    /// Każdy kod ma unikalną nazwę i można go mapować na HTTP status.
    /// </summary>
    public static class ErrorCode
    {
        // ===== BŁEDY GLOBALNE (Content i DRM serwer) =====

        /// <summary>
        /// Wewnętrzny błąd serwera (500).
        /// </summary>
        public const string INTERNAL_SERVER_ERROR = "INTERNAL_SERVER_ERROR";

        /// <summary>
        /// Zasób nie znaleziony (404).
        /// </summary>
        public const string NOT_FOUND = "NOT_FOUND";

        /// <summary>
        /// Błąd walidacji danych wejściowych (400).
        /// </summary>
        public const string VALIDATION_ERROR = "VALIDATION_ERROR";

        /// <summary>
        /// Usługa niedostępna (503).
        /// </summary>
        public const string SERVICE_UNAVAILABLE = "SERVICE_UNAVAILABLE";

        // ===== TYLKO CONTENT SERWER =====

        public const string CONTENT_NOT_FOUND = "CONTENT_NOT_FOUND";
        public const string MANIFEST_NOT_FOUND = "MANIFEST_NOT_FOUND";
        public const string SEGMENT_NOT_FOUND = "SEGMENT_NOT_FOUND";
        public const string THUMBNAIL_NOT_FOUND = "THUMBNAIL_NOT_FOUND";
        public const string STORAGE_UNAVAILABLE = "STORAGE_UNAVAILABLE";

        // ===== TYLKO DRM SERWER =====

        // TODO: Dodać po implementacji DRM-a
    }
}