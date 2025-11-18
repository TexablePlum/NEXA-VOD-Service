namespace Nexa.Shared.Exceptions;

/// <summary>
/// Wyjątek rzucany gdy użytkownik jest authenticated ale nie ma uprawnień do zasobu.
/// HTTP 403 Forbidden.
/// Przykład: użytkownik z planem "free" próbuje odtworzyć content "pro".
/// </summary>
public class ForbiddenException : NexaException
{
    public ForbiddenException(string message, Dictionary<string, object>? context = null)
        : base(Models.ErrorCode.FORBIDDEN, message, 403)
    {
        if (context != null)
        {
            Context = context;
        }
    }
}
