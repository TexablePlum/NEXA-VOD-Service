namespace Nexa.Shared.Models
{
    /// <summary>
    /// Odpowiedź API dla endpointu GET /api/catalog
    /// Zawiera listę filmów + informacje o paginacji
    /// </summary>
    public class CatalogResponse
    {
        /// <summary>
        /// Całkowita liczba filmów w systemie (przed filtrowaniem)
        /// </summary>
        public int Total { get; set; }

        /// <summary>
        /// Ile filmów zwracamy w tym żądaniu
        /// (użytkownik może poprosić o maks 50)
        /// </summary>
        public int Limit { get; set; }

        /// <summary>
        /// Od którego filmu zaczyna (dla stronicowania)
        /// Przykład: offset=0 (pierwsza strona), offset=50 (druga strona)
        /// </summary>
        public int Offset { get; set; }

        /// <summary>
        /// Lista filmów na tej "stronie" wyników
        /// </summary>
        public List<ContentMetadata> Items { get; set; } = new();
    }
}