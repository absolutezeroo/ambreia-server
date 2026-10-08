using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Vortex.Hosting.Tests.Architecture;

/// <summary>
/// Everything the client needs on room entry is in the burst the room builds
/// (<c>RoomGrain.Entry.cs</c>), because the client asks for nothing.
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
/// any known client, so it never ran: the replayed avatar effects, the
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
        // Occupants' worn effects.
        "AvatarEffectMessageComposer",
        // Occupants' hand items.
        "CarryObjectMessageComposer",
        // Pins chooserDisabled, freeFurniMovementsEnabled and invisibleFurni on the client's room
        // object instead of leaving them to whatever the client defaults to.
        "HanditemConfigurationMessageComposer",
    ];

    /// <summary>
    /// The entry burst in the order an official room entry was captured sending it.
    /// </summary>
    /// <remarks>
    /// The room's shell first: ready, the door tile, the paint, the geometry, the visualisation settings. Then who is here, then what is
    /// here, then whose room it is. Then the furniture configuration and the
    /// rights. The official burst ends with the room's rating, which the RPG does not send.
    /// </remarks>
    private static readonly string[] CapturedOrder =
    [
        "RoomReadyMessageComposer",
        "RoomEntryTileMessageComposer",
        "RoomPropertyMessageComposer",
        "HeightMapMessageComposer",
        "FloorHeightMapMessageComposer",
        "RoomVisualizationSettingsMessageComposer",
        "UsersMessageComposer",
        "ObjectsMessageComposer",
        "ItemsMessageComposer",
        "RoomEntryInfoMessageComposer",
        "HanditemConfigurationMessageComposer",
        "YouAreControllerMessageComposer",
        "YouAreOwnerMessageComposer",
    ];

    [Fact]
    public void TheEntryBurst_CarriesTheWholePayload()
    {
        string source = EntryMethodSource();

        string[] missing = RequiredOnEntry
            .Where(composer => !source.Contains($"new {composer}", StringComparison.Ordinal))
            .ToArray();

        missing
            .Should()
            .BeEmpty(
                "the client requests nothing after OpenConnection, so anything not in this push "
                    + "never reaches the player walking in. A composer moved out of the burst is a "
                    + "feature that still builds, still passes its own wire tests and is dead on "
                    + "arrival"
            );
    }

    /// <summary>
    /// The burst goes out in the order Sulake sends it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A source-order check, and honest about being one: it reads where each composer is built in
    /// <c>BuildEntryBurstAsync</c> rather than watching a socket. That is enough for what it
    /// guards, because the burst goes out in the order it is built, and the order is a decision taken
    /// against a captured entry rather than an accident of how the code grew — it drifted once
    /// already, with the rating second and the paint arriving after the geometry it paints.
    /// </para>
    /// <para>
    /// The replay set (effects, hand items) is deliberately absent: those are ours, the
    /// capture's room was empty, and there is no official order to hold them to.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheEntryBurst_GoesOutInTheCapturedOrder()
    {
        string source = EntryMethodSource();

        int previous = -1;
        string previousName = "the start of the method";

        foreach (string name in CapturedOrder)
        {
            int at = source.IndexOf(name, StringComparison.Ordinal);

            at.Should().BeGreaterThan(-1, "{0} is part of the entry burst", name);
            at.Should()
                .BeGreaterThan(
                    previous,
                    "an official entry sends {0} after {1}",
                    name,
                    previousName
                );

            previous = at;
            previousName = name;
        }
    }

    /// <summary>Just <c>BuildEntryBurstAsync</c>, the last method of the file, so the door above it
    /// cannot skew an index.</summary>
    private static string EntryMethodSource()
    {
        string source = File.ReadAllText(
            Path.Combine(RepositoryPaths.Root(), "Vortex.Rooms", "Grains", "RoomGrain.Entry.cs")
        );

        int start = source.IndexOf(
            "private async Task<ImmutableArray<IComposer>> BuildEntryBurstAsync",
            StringComparison.Ordinal
        );

        start.Should().BeGreaterThan(-1);

        return source[start..];
    }
}
