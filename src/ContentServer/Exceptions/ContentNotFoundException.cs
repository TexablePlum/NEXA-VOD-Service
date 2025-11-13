using Nexa.Shared.Exceptions;
using System.Reflection;

namespace ContentServer.Exceptions
{
    /// <summary>
    /// Wyjątek dla nie znalezionej treści (404).
    /// </summary>
    public class ContentNotFoundException : NexaException
    {
        public ContentNotFoundException(string contentId)
            : base(
                Nexa.Shared.Models.ErrorCode.CONTENT_NOT_FOUND,
                $"Content with ID '{contentId}' not found",
                404)
        {
            Context = new Dictionary<string, object>
            {
                ["contentId"] = contentId
            };
        }
    }
}
