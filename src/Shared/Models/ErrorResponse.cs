namespace Nexa.Shared.Models
{
    /// <summary>
    /// Format odpowiedzi błędu dla całego systemu NEXA.
    /// Wspólny dla Content Server i DRM Server.
    /// Zgodny z normą RFC 7807.
    /// </summary>
    public class ErrorResponse
    {
        /// <summary>
        /// Kod błędu.
        /// Przykład: "CONTENT_NOT_FOUND"
        /// </summary>
        public string ErrorCode { get; set; } = string.Empty;

        /// <summary>
        /// Komunikat dla użytkownika.
        /// Przykład: "Film nie został znaleziony"
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Szczegóły techniczne (opcjonalne i tylko w Development!).
        /// Przykład: Stack trace, inner exception
        /// </summary>
        public string? Details { get; set; }

        /// <summary>
        /// Timestamp błędu (strefa UTC).
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Ścieżka żądania który wywołał błąd.
        /// Przykład: "/api/catalog/invalid-id"
        /// </summary>
        public string? Path { get; set; }

        /// <summary>
        /// Opcjonalne: dodatkowe dane kontekstowe.
        /// Przykład: { "contentId": "abc123", "userId": "xyz" }
        /// </summary>
        public Dictionary<string, object>? Context { get; set; }
    }
}