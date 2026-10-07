namespace Vortex.Primitives.Moderation.WordFilter;

/// <summary>One journaled hit, before the writer stamps it and turns it into a row.</summary>
public sealed record WordFilterHit(
    int PlayerId,
    WordFilterSource Source,
    int WordId,
    WordFilterAction Action,
    string OriginalText
);

/// <summary>
/// Where the word filter sends what it caught. The audit pipeline implements it; the filter itself
/// lives with the permissions and must not reach into the pipeline's channel.
/// </summary>
public interface IWordFilterJournal
{
    /// <summary>Queues the hit. Never blocks the line being filtered; a saturated journal drops it.</summary>
    void Record(WordFilterHit hit);
}
