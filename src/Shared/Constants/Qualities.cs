using System;
using System.Collections.Generic;

namespace Nexa.Shared.Constants
{
    /// <summary>
    /// Stałe dla jakości wideo z hierarchią
    /// </summary>
    public static class Qualities
    {
        public const string Q480P = "480p";
        public const string Q720P = "720p";
        public const string Q1080P = "1080p";
        public const string Q1440P = "1440p";
        public const string Q2160P = "2160p";  // 4K

        // Hierarchia jakości (wyższa wartość = lepsza jakość)
        private static readonly Dictionary<string, int> QualityHierarchy = new()
        {
            [Q480P] = 1,
            [Q720P] = 2,
            [Q1080P] = 3,
            [Q1440P] = 4,
            [Q2160P] = 5
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
        /// Zwraca poziom hierarchii jakości (1-5)
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
