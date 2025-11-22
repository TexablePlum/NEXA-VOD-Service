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

        /// <summary>
        /// Brak lub nieprawidłowy token JWT (401).
        /// </summary>
        public const string UNAUTHORIZED = "UNAUTHORIZED";

        /// <summary>
        /// Użytkownik authenticated ale nie ma uprawnień (403).
        /// </summary>
        public const string FORBIDDEN = "FORBIDDEN";

        /// <summary>
        /// Użytkownik o podanym email już istnieje (409).
        /// </summary>
        public const string USER_ALREADY_EXISTS = "USER_ALREADY_EXISTS";

        /// <summary>
        /// Nieprawidłowe dane logowania (401).
        /// </summary>
        public const string INVALID_CREDENTIALS = "INVALID_CREDENTIALS";

        /// <summary>
        /// Nieprawidłowy refresh token (401).
        /// </summary>
        public const string INVALID_REFRESH_TOKEN = "INVALID_REFRESH_TOKEN";

        /// <summary>
        /// Licencja (CEK) dla contentu nie znaleziona (404).
        /// </summary>
        public const string LICENSE_NOT_FOUND = "LICENSE_NOT_FOUND";

        /// <summary>
        /// Plan subskrypcji niewystarczający do odtworzenia contentu (403).
        /// </summary>
        public const string INSUFFICIENT_PLAN = "INSUFFICIENT_PLAN";

        /// <summary>
        /// Content nie został jeszcze wypuszczony - premiera w przyszłości (403).
        /// </summary>
        public const string CONTENT_NOT_RELEASED = "CONTENT_NOT_RELEASED";
    }
}