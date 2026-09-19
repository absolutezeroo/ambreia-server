using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Camera;

/// <summary>
/// The answer to <c>PhotoCompetition</c>.
/// </summary>
/// <remarks>
/// Both fields are always written — unlike the publish status, the client reads the string
/// unconditionally, so an <c>ok</c> with no reason still has to carry an empty one. The reason is a
/// code, not a sentence: the client matches <c>too-many-submits</c> by name and has its own wording
/// for it, and falls back to a generic failure for anything it does not recognise.
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record CompetitionStatusMessageComposer : IComposer
{
    [Id(0)]
    public required bool Ok { get; init; }

    /// <summary>A code the client knows, or empty. Never a message meant to be read as it is.</summary>
    [Id(1)]
    public required string ErrorReason { get; init; }
}
