using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nexa.Shared.Exceptions
{
    /// <summary>
    /// Wyjątek dla niedostępnej usługi (503).
    /// Używany gdy serwis jest tymczasowo niedostępny (np. przeciążenie).
    /// </summary>
    public class ServiceUnavailableException : NexaException
    {
        public ServiceUnavailableException(string message, string? serviceName = null)
            : base(Models.ErrorCode.SERVICE_UNAVAILABLE, message, 503)
        {
            if (serviceName != null)
            {
                Context = new Dictionary<string, object>
                {
                    ["serviceName"] = serviceName
                };
            }
        }
    }
}
