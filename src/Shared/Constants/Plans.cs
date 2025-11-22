using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nexa.Shared.Constants
{
    /// <summary>
    /// Stałe dla planów subskrypcji z hierarchią uprawnień
    /// </summary>
    public static class Plans
    {
        public const string FREE = "free";
        public const string BASIC = "basic";
        public const string PRO = "pro";

        // Hierarchia planów (wyższy poziom = więcej uprawnień)
        private static readonly Dictionary<string, int> PlanHierarchy = new()
        {
            [FREE] = 1,
            [BASIC] = 2,
            [PRO] = 3
        };

        // Maksymalna jakość dla każdego planu
        // L1 (z TPM): FREE=720p, BASIC=1080p, PRO=8K
        // L3 (bez TPM): wszystkie plany ograniczone do max 720p
        private static readonly Dictionary<string, string> MaxQualityForPlan = new()
        {
            [FREE] = Qualities.Q720P,    // HD
            [BASIC] = Qualities.Q1080P,  // Full HD
            [PRO] = Qualities.Q4320P     // 8K (max możliwa jakość)
        };

        /// <summary>
        /// Sprawdza czy plan jest poprawny
        /// </summary>
        public static bool IsValid(string plan)
        {
            return PlanHierarchy.ContainsKey(plan);
        }

        /// <summary>
        /// Sprawdza czy userPlan ma wystarczające uprawnienia dla requiredPlan
        /// Przykład: basic ma dostęp do free i basic, ale nie do pro
        /// </summary>
        public static bool HasSufficientPlan(string userPlan, string requiredPlan)
        {
            if (!PlanHierarchy.ContainsKey(userPlan) || !PlanHierarchy.ContainsKey(requiredPlan))
                return false;

            return PlanHierarchy[userPlan] >= PlanHierarchy[requiredPlan];
        }

        /// <summary>
        /// Zwraca maksymalną jakość dostępną dla danego planu
        /// </summary>
        public static string GetMaxQuality(string plan)
        {
            return MaxQualityForPlan.TryGetValue(plan, out var quality)
                ? quality
                : Qualities.Q480P;  // Fallback na najniższą jakość
        }

        /// <summary>
        /// Sprawdza czy dany plan może uzyskać dostęp do określonej jakości
        /// </summary>
        public static bool CanAccessQuality(string userPlan, string requestedQuality)
        {
            if (!PlanHierarchy.ContainsKey(userPlan))
                return false;

            var maxQuality = GetMaxQuality(userPlan);
            return Qualities.IsQualitySufficient(maxQuality, requestedQuality);
        }

        /// <summary>
        /// Zwraca poziom hierarchii planu (1-3)
        /// </summary>
        public static int GetLevel(string plan)
        {
            return PlanHierarchy.TryGetValue(plan, out var level) ? level : 0;
        }
    }
}
