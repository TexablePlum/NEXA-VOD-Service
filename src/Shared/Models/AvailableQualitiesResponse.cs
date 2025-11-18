using System;
using System.Collections.Generic;

namespace Nexa.Shared.Models
{
    /// <summary>
    /// Response zwracający dostępne jakości dla contentu.
    /// </summary>
    public class AvailableQualitiesResponse
    {
        /// <summary>
        /// ID contentu
        /// </summary>
        public string ContentId { get; set; } = string.Empty;

        /// <summary>
        /// Plan użytkownika
        /// </summary>
        public string UserPlan { get; set; } = string.Empty;

        /// <summary>
        /// Maksymalna jakość dostępna dla planu użytkownika
        /// </summary>
        public string MaxQuality { get; set; } = string.Empty;

        /// <summary>
        /// Lista dostępnych jakości posortowana od najniższej do najwyższej.
        /// Zawiera tylko jakości które faktycznie istnieją dla tego contentu
        /// i są dozwolone dla planu użytkownika.
        /// </summary>
        public List<string> Qualities { get; set; } = new();

        /// <summary>
        /// Liczba dostępnych jakości
        /// </summary>
        public int Count => Qualities.Count;
    }
}
