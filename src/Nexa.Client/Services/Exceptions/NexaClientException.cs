using Nexa.Shared.Models;
using System;

namespace Nexa.Client.Services.Exceptions
{
    /// <summary>
    /// Jednolity wyjątek dla całej aplikacji klienckiej.
    /// Wrapuje ErrorResponse z backendu lub generuje go lokalnie.
    /// </summary>
    public class NexaClientException : Exception
    {
        public ErrorResponse Error { get; }
        public int StatusCode { get; }

        public NexaClientException(ErrorResponse error, int statusCode = 0)
            : base(error.Message)
        {
            Error = error;
            StatusCode = statusCode;
        }

        // Helpery do szybkiego sprawdzania typu błędu
        public bool IsValidation => Error.ErrorCode == ErrorCode.VALIDATION_ERROR;
        public bool IsNetwork => Error.ErrorCode == "NETWORK_ERROR";
        public bool IsAuth => Error.ErrorCode == ErrorCode.UNAUTHORIZED || Error.ErrorCode == ErrorCode.FORBIDDEN;
    }
}