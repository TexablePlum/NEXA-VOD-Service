using Nexa.Shared.Exceptions;

namespace ContentServer.Exceptions
{
    /// <summary>
    /// Wyjątek dla niedostępnego storage'u (503).
    /// </summary>
    public class StorageUnavailableException : NexaException
    {
        public StorageUnavailableException(string storagePath)
            : base(
                Nexa.Shared.Models.ErrorCode.STORAGE_UNAVAILABLE,
                $"Storage is unavailable at path: {storagePath}",
                503)
        {
            Context = new Dictionary<string, object>
            {
                ["storagePath"] = storagePath
            };
        }
    }
}
