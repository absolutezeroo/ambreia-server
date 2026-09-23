using System;
using System.Collections.Generic;
using Vortex.Primitives.Moderation.WordFilter;

namespace Vortex.Dashboard.API.Api.Hotel.Contracts;

/// <summary>One word on the hotel-wide list. A null replacement means the configured default.</summary>
public sealed record WordFilterWordRow(
    int Id,
    string Word,
    WordFilterAction Action,
    WordFilterMatch Match,
    string? Replacement,
    string? CreatedBy,
    DateTime CreatedAt
);

/// <summary>The whole list — it is small, and the page filters it locally.</summary>
public sealed record WordFilterWordList(IReadOnlyList<WordFilterWordRow> Items);

/// <summary>
/// One journaled hit. <see cref="Word"/> is read through soft deletes, so a word removed since
/// still shows what it caught; it is null only for a row that never existed.
/// </summary>
public sealed record WordFilterHitRow(
    int Id,
    int PlayerId,
    string? PlayerName,
    string? AvatarUrl,
    WordFilterSource Source,
    int WordId,
    string? Word,
    WordFilterAction Action,
    string OriginalText,
    DateTime OccurredAt
);

/// <summary>One page of the journal, newest first.</summary>
public sealed record WordFilterHitPage(
    int Page,
    int Limit,
    int Total,
    IReadOnlyList<WordFilterHitRow> Items
);

/// <summary>What the filter would do with a sentence. Journals nothing.</summary>
public sealed record WordFilterTestResult(string Text, bool Refused, bool Changed);

/// <summary>
/// The word being written in the drawer, tried beside the saved list before it is saved.
/// <see cref="Id"/> is the word being edited, which the draft stands in for; null for a new one.
/// </summary>
public sealed record WordFilterDraft(
    int? Id,
    string Word,
    WordFilterAction Action,
    WordFilterMatch Match,
    string? Replacement
);
