namespace Nexa.Shared.Exceptions;

/// <summary>
/// Wyjątek rzucany gdy żądanie wymaga autentykacji (brak/nieprawidłowy token).
/// HTTP 401 Unauthorized.
/// </summary>
public class UnauthorizedException : NexaException
{
    public UnauthorizedException(string message, Dictionary<string, object>? context = null)
        : base(Models.ErrorCode.UNAUTHORIZED, message, 401)
    {
        if (context != null)
        {
            Context = context;
        }
    }
}
