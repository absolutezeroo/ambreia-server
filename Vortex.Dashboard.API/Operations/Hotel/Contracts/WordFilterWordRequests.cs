using Vortex.Dashboard.API.Hosting;
using Vortex.Primitives.Moderation.WordFilter;

namespace Vortex.Dashboard.API.Operations.Hotel.Contracts;

/// <summary>A word for the hotel-wide list. A blank replacement means the configured default.</summary>
public sealed record AddWordFilterWordRequest(
    string Word,
    WordFilterAction Action,
    WordFilterMatch Match,
    string? Replacement,
    string Reason
) : IReasonedRequest;

/// <summary>An edit of a listed word — its spelling, its action, how it matches, its replacement.</summary>
public sealed record UpdateWordFilterWordRequest(
    int Id,
    string Word,
    WordFilterAction Action,
    WordFilterMatch Match,
    string? Replacement,
    string Reason
) : IReasonedRequest;

/// <summary>Taking a word off the list. Its journal rows stay, and still show it.</summary>
public sealed record RemoveWordFilterWordRequest(int Id, string Reason) : IReasonedRequest;
