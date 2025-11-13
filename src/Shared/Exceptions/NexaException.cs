
namespace Nexa.Shared.Exceptions
{
    /// <summary>
    /// Bazowa klasa dla wszystkich wyjątków w systemie NEXA.
    /// Zawiera ErrorCode i HTTP status code.
    /// </summary>
    public abstract class NexaException : Exception
    {
        /// <summary>
        /// Kod błędu (z klasy ErrorCode).
        /// </summary>
        public string ErrorCode { get; }

        /// <summary>
        /// Kod statusu HTTP do zwrócenia.
        /// </summary>
        public int StatusCode { get; }

        /// <summary>
        /// Opcjonalne: dodatkowy kontekst.
        /// </summary>
        public Dictionary<string, object>? Context { get; set; }

        protected NexaException(
            string errorCode,
            string message,
            int statusCode = 500,
            Exception? innerException = null)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
            StatusCode = statusCode;
        }
    }
}