using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Vortex.Hosting.Tests.Architecture;

/// <summary>
/// Everything the client needs on room entry is pushed by <c>RoomService</c>, because the client
/// asks for nothing.
/// </summary>
/// <remarks>
/// <para>
/// An official capture of a room entry settles what used to be an open question: after
/// <c>OpenConnection</c> the client sends nothing that would fetch the room's contents — it receives
/// the whole burst unprompted and its only outgoing messages are for the interstitial, the group
/// badges, the achievements and the camera. There is no request to answer.
/// </para>
/// <para>
/// Half of the payload nevertheless lived in a handler for a client message that does not exist in
/// any known client, so it never ran: the click-user environment, the replayed avatar effects, the
/// replayed hand items and the bots' dances were built, tested and never sent. The handler is gone
/// and its payload moved into the entry push; this test is what stops the split from reappearing,
/// since nothing else notices — each of these composers still compiles, still has unit tests, and is
/// simply never reached.
/// </para>
/// </remarks>
public sealed class RoomEntryPayloadTests
{
    /// <summary>
    /// Composers that carry state absent from the <c>Users</c> payload, so each one has to be sent
    /// explicitly to the arriving player or the thing it describes is invisible to them.
    /// </summary>
    private static readonly string[] RequiredOnEntry =
    [
        // Without it the client assumes no click-user box exists, opens its own context menu and
        // never sends WiredClickUser -- the trigger becomes unreachable.
        "WiredEnvironmentMessageComposer",
        // Occupants' worn effects.
        "AvatarEffectMessageComposer",
        // Occupants' hand items.
        "CarryObjectMessageComposer",
        // Occupants' dances, players and bots alike.
        "DanceMessageComposer",
    ];

    [Fact]
    public void RoomService_SendsTheWholeEntryPayload()
    {
        string source = File.ReadAllText(
            Path.Combine(RepositoryPaths.Root(), "Vortex.Rooms", "RoomService.cs")
        );

        string[] missing = RequiredOnEntry
            .Where(composer => !source.Contains($"new {composer}", StringComparison.Ordinal))
            .ToArray();

        missing
            .Should()
            .BeEmpty(
                "the client requests nothing after OpenConnection, so anything not in this push "
                    + "never reaches the player walking in. A composer moved out of RoomService is a "
                    + "feature that still builds, still passes its own wire tests and is dead on "
                    + "arrival"
            );
    }

    /// <summary>
    /// Bots dance too, and their dance is as absent from the <c>Users</c> payload as a player's.
    /// The replay used to filter on the player snapshot alone, so a room entered while a bot was
    /// dancing showed it standing still until it changed dance.
    /// </summary>
    [Fact]
    public void TheDanceReplay_CoversBotsAsWellAsPlayers()
    {
        string source = File.ReadAllText(
            Path.Combine(RepositoryPaths.Root(), "Vortex.Rooms", "RoomService.cs")
        );

        source
            .Should()
            .Contain(
                "OfType<RoomBotAvatarSnapshot>()",
                "a bot's dance reaches the arriving client through the same replay as a player's, "
                    + "and through nothing else"
            );
    }
}
