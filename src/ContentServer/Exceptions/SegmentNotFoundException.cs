using Nexa.Shared.Exceptions;

namespace ContentServer.Exceptions
{
    /// <summary>
    /// Wyjątek dla nie znalezionego segmentu wideo (404).
    /// </summary>
    public class SegmentNotFoundException : NexaException
    {
        public SegmentNotFoundException(string contentId, string quality, string segmentName)
            : base(
                Nexa.Shared.Models.ErrorCode.SEGMENT_NOT_FOUND,
                $"Segment {segmentName} not found for content '{contentId}' in quality '{quality}'",
                404)
        {
            Context = new Dictionary<string, object>
            {
                ["contentId"] = contentId,
                ["quality"] = quality,
                ["segmentName"] = segmentName
            };
        }
    }

}
