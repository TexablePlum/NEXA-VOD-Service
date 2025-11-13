using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nexa.Shared.Constants
{
    /// <summary>
    /// Stałe dla planów subskrypcji
    /// </summary>
    public static class Plans
    {
        public const string FREE = "free";
        public const string BASIC = "basic";
        public const string PRO = "pro";

        // Metoda walidacyjna
        public static bool IsValid(string plan)
        {
            return plan == FREE || plan == BASIC || plan == PRO;
        }
    }
}
