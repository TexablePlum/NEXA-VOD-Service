using System;
using System.Collections.Generic;

namespace Nexa.Shared.Constants
{
    /// <summary>
    /// Stałe dla jakości wideo z hierarchią (144p - 8K).
    /// Wspiera pełny zakres standardowych rozdzielczości.
    /// </summary>
    public static class Qualities
    {
        // Niskie jakości (mobile, słabe połączenia)
        public const string Q144P = "144p";
        public const string Q240P = "240p";
        public const string Q360P = "360p";
        public const string Q480P = "480p";   // SD

        // Standardowe jakości
        public const string Q720P = "720p";   // HD
        public const string Q1080P = "1080p"; // Full HD

        // Wysokie jakości (premium)
        public const string Q1440P = "1440p"; // 2K
        public const string Q2160P = "2160p"; // 4K / UHD
        public const string Q4320P = "4320p"; // 8K

        // Hierarchia jakości (wyższa wartość = lepsza jakość)
        private static readonly Dictionary<string, int> QualityHierarchy = new()
        {
            [Q144P] = 1,
            [Q240P] = 2,
            [Q360P] = 3,
            [Q480P] = 4,
            [Q720P] = 5,
            [Q1080P] = 6,
            [Q1440P] = 7,
            [Q2160P] = 8,
            [Q4320P] = 9
        };

        /// <summary>
        /// Sprawdza czy jakość jest poprawna
        /// </summary>
        public static bool IsValid(string quality)
        {
            return QualityHierarchy.ContainsKey(quality);
        }

        /// <summary>
        /// Sprawdza czy maxQuality pozwala na dostęp do requestedQuality
        /// Przykład: maxQuality=1080p pozwala na 480p, 720p, 1080p ale nie 2160p
        /// </summary>
        public static bool IsQualitySufficient(string maxQuality, string requestedQuality)
        {
            if (!QualityHierarchy.ContainsKey(maxQuality) || !QualityHierarchy.ContainsKey(requestedQuality))
                return false;

            return QualityHierarchy[maxQuality] >= QualityHierarchy[requestedQuality];
        }

        /// <summary>
        /// Zwraca poziom hierarchii jakości (1-9, gdzie 1=144p, 9=8K)
        /// </summary>
        public static int GetLevel(string quality)
        {
            return QualityHierarchy.TryGetValue(quality, out var level) ? level : 0;
        }

        /// <summary>
        /// Zwraca listę wszystkich dostępnych jakości dla danego max
        /// </summary>
        public static List<string> GetAvailableQualities(string maxQuality)
        {
            var result = new List<string>();
            if (!QualityHierarchy.ContainsKey(maxQuality))
                return result;

            var maxLevel = QualityHierarchy[maxQuality];

            foreach (var kvp in QualityHierarchy)
            {
                if (kvp.Value <= maxLevel)
                    result.Add(kvp.Key);
            }

            result.Sort((a, b) => QualityHierarchy[a].CompareTo(QualityHierarchy[b]));
            return result;
        }
    }
}
