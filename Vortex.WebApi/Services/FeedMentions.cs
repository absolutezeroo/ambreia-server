using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Vortex.WebApi.Services;

/// <summary>
/// Pulling the <c>@name</c>s out of a post's body.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately the twin of <see cref="FeedTags"/>, down to the duplicated pattern on the website
/// (<c>components/feed/FeedBody.svelte</c>) that decides where to break the text into links. The
/// two are kept in step by hand, and a mismatch is visible rather than silent: a name that renders
/// as plain text, or a link to a profile that is not there.
/// </para>
/// <para>
/// Unlike a tag, a mention is NOT stored. Nothing joins a post to the people named in it — the
/// page turns every <c>@word</c> into a profile link on its own, and the server resolves the names
/// only to decide who to notify. What that costs is a link to a profile that does not exist, which
/// lands on the site's own empty-results page; what it saves is a table and its two indexes.
/// </para>
/// </remarks>
internal static partial class FeedMentions
{
    /// <summary>
    /// At most this many people are pinged by one post. A mention is a notification, so an
    /// unbounded list is a megaphone: one post naming fifty players rings fifty bells for free.
    /// Extra names past the cap still render as links — they are simply not told.
    /// </summary>
    public const int MaximumPerPost = 10;

    /// <summary>
    /// An <c>@</c> then letters, digits, underscore or hyphen — the characters a Habbo name is
    /// made of. The hyphen is the one difference from a hashtag's pattern, and it is there because
    /// names like <c>-Prodiigy-</c> are ordinary.
    /// </summary>
    [GeneratedRegex(@"@([\p{L}\p{N}_-]+)", RegexOptions.CultureInvariant)]
    public static partial Regex Pattern();

    /// <summary>
    /// Each distinct name in the body, in the order it first appears, capped at
    /// <see cref="MaximumPerPost"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The DEDUPLICATION here ignores case — <c>@Chloe</c> twice in one post is one person however
    /// it was capitalised the second time — but the name is handed on exactly as typed. Whether
    /// <c>@chloe</c> then finds Chloe is the database's business: every name lookup in this hotel
    /// is a plain <c>==</c> (see <c>WebApiPlayerService.NameAvailableAsync</c>, whose comment notes
    /// that whispering, gifting and friend requests all resolve by exact string), so the answer
    /// comes from the column's collation. MySQL's default is case-insensitive and mentions behave
    /// accordingly; the in-memory provider the tests run on is not, which is why no test here
    /// asserts it.
    /// </para>
    /// <para>
    /// Lowercasing on the way in — which is what <see cref="FeedTags"/> does — would be wrong for
    /// the same reason it is right there: a tag is a search key the hotel owns, a name is somebody's
    /// identity and the capitals are theirs.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> Extract(string? body) =>
        Pattern()
            .Matches(body ?? string.Empty)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumPerPost)
            .ToList();
}
