using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nexa.Shared.Exceptions
{
    /// <summary>
    /// Wyjątek dla wewnętrznych błędów serwera (500).
    /// Używany gdy coś poszło nie tak po stronie serwera.
    /// </summary>
    public class InternalServerException : NexaException
    {
        public InternalServerException(string message, Exception? innerException = null)
            : base(Models.ErrorCode.INTERNAL_SERVER_ERROR, message, 500, innerException)
        {
        }
    }
}
