namespace Nexa.Shared.Exceptions
{
    /// <summary>
    /// Wyjątek dla nie znalezionych zasobów (404).
    /// </summary>
    public class NotFoundException : NexaException
    {
        public NotFoundException(string message, string? resourceId = null)
            : base(Models.ErrorCode.NOT_FOUND, message, 404)
        {
            if (resourceId != null)
            {
                Context = new Dictionary<string, object>
                {
                    ["resourceId"] = resourceId
                };
            }
        }
    }
}