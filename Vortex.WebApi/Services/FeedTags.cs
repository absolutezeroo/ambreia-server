using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Vortex.Database.Entities.Feed;

namespace Vortex.WebApi.Services;

/// <summary>
/// Pulling the hashtags out of a post's body.
/// </summary>
/// <remarks>
/// <para>
/// The pattern is duplicated on the website, in <c>components/feed/FeedBody.svelte</c>, because
/// the page has to know where to break the text to turn the tags into links. They are kept in step
/// by hand, and a mismatch is visible rather than silent: a tag that renders as plain text, or a
/// link that leads to a filter finding nothing.
/// </para>
/// <para>
/// Lowercased on the way in because that is the search key; the body keeps the casing the author
/// typed, so <c>#McBurgerFC</c> reads as they wrote it and still finds <c>#mcburgerfc</c>.
/// </para>
/// </remarks>
internal static partial class FeedTags
{
    /// <summary>
    /// A <c>#</c> then letters, digits or underscore, in any script — <c>\p{L}</c> rather than
    /// <c>\w</c> so <c>#accentué</c> and a non-Latin tag are whole tags rather than truncated ones.
    /// </summary>
    [GeneratedRegex(@"#([\p{L}\p{N}_]+)", RegexOptions.CultureInvariant)]
    public static partial Regex Pattern();

    /// <summary>Each distinct tag in the body, lowercased, in the order it first appears.</summary>
    /// <remarks>
    /// Over-long tags are dropped rather than truncated: a truncated tag is a different tag, and
    /// silently filing a post under one nobody typed is worse than filing it under none.
    /// </remarks>
    public static IReadOnlyList<string> Extract(string? body) =>
        Pattern()
            .Matches(body ?? string.Empty)
            .Select(m => m.Groups[1].Value.ToLowerInvariant())
            .Where(t => t.Length <= WebFeedTagEntity.TagLength)
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
