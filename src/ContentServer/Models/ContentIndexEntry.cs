namespace Nexa.ContentServer.Models
{
    /// <summary>
    /// Lekki wpis w indeksie zawiera tylko dane potrzebne do filtrowania i paginacji
    /// </summary>
    public class ContentIndexEntry
    {
        public string ContentId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public List<string>? Genres { get; set; } = new();
        public DateTime? ReleaseDate { get; set; }
        public string RequiredPlan { get; set; } = "free";

        // Timestamp ostatniej modyfikacji
        public DateTime LastModified { get; set; }
    }
}