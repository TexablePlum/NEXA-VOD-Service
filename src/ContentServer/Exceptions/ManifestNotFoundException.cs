using Nexa.Shared.Exceptions;

namespace ContentServer.Exceptions
{
    /// <summary>
    /// Wyjątek dla nie znalezionego manifestu (404).
    /// </summary>
    public class ManifestNotFoundException : NexaException
    {
        public ManifestNotFoundException(string contentId)
            : base(
                Nexa.Shared.Models.ErrorCode.MANIFEST_NOT_FOUND,
                $"Manifest for content '{contentId}' not found",
                404)
        {
            Context = new Dictionary<string, object>
            {
                ["contentId"] = contentId
            };
        }
    }
}
