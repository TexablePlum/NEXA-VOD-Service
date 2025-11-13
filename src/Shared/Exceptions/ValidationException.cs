using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nexa.Shared.Exceptions
{
    /// <summary>
    /// Wyjątek dla błędów walidacji danych wejściowych (400).
    /// </summary>
    public class ValidationException : NexaException
    {
        public ValidationException(string message, Dictionary<string, object>? validationErrors = null)
            : base(Models.ErrorCode.VALIDATION_ERROR, message, 400)
        {
            if (validationErrors != null)
            {
                Context = validationErrors;
            }
        }
    }
}
