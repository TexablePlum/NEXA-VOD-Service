using Nexa.Shared.Exceptions;

namespace ContentServer.Exceptions
{
    /// <summary>
    /// Wyjątek dla nie znalezionej miniaturki (404).
    /// </summary>
    public class ThumbnailNotFoundException : NexaException
    {
        public ThumbnailNotFoundException(string contentId)
            : base(
                Nexa.Shared.Models.ErrorCode.THUMBNAIL_NOT_FOUND,
                $"Thumbnail for content '{contentId}' not found",
                404)
        {
            Context = new Dictionary<string, object>
            {
                ["contentId"] = contentId
            };
        }
    }
}
