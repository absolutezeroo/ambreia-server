using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Vortex.Hosting.Tests.Architecture;

/// <summary>
/// Every way a player gets into a room goes through <c>RoomService</c>.
/// </summary>
/// <remarks>
/// <para>
/// The gates that decide who may enter — the room ban, the population cap, the password, the
/// doorbell, raid protection and the cancellable entry event — all live in <c>RoomService</c>, while
/// the state they protect lives in the room grain. Nothing stopped a caller from skipping the first
/// and calling the second, and two did. <c>FollowFriend</c> called
/// <c>SetActiveRoomAsync</c> straight off the friend's location, so a friend in a locked, passworded
/// or full room could be joined by anyone on their friend list — and without an entry payload, so
/// the server had an avatar standing in a room the client did not know it was in. A second handler,
/// since deleted, did the same off a pending request that a ringing doorbell leaves set.
/// </para>
/// <para>
/// Neither was visible in a build, a test or a grep for something wrong; both were visible only by
/// noticing that a handler reached the grain at all. So the convention gets named here, in the shape
/// <see cref="FurnitureMutationBoundaryTests" /> uses: one entry point, and a failing test when a
/// second one appears.
/// </para>
/// </remarks>
public sealed class RoomEntryBoundaryTests
{
    /// <summary>
    /// Files that may name <c>SetActiveRoomAsync</c>: the interface that declares it, the grain that
    /// implements it, and the service that is allowed to call it.
    /// </summary>
    private static readonly string[] EntryAllowList =
    [
        Path.Combine("Vortex.Primitives", "Players", "Grains", "IPlayerPresenceGrain.Room.cs"),
        Path.Combine("Vortex.Players", "Grains", "PlayerPresenceGrain.Room.cs"),
        Path.Combine("Vortex.Rooms", "RoomService.cs"),
    ];

    [Fact]
    public void OnlyRoomService_PutsAPlayerIntoARoom()
    {
        List<string> offenders = CallSitesOf("SetActiveRoomAsync", EntryAllowList);

        offenders
            .Should()
            .BeEmpty(
                "entering a room means passing the ban, the cap, the password, the doorbell, raid "
                    + "protection and the entry event, and all six live in RoomService. A caller that "
                    + "reaches the presence grain directly skips every one of them. A handler that "
                    + "wants to send a player somewhere answers RoomForward and lets the client "
                    + "connect, the way the navigator does"
            );
    }

    /// <summary>
    /// The pending request is what a ringing doorbell leaves behind, so the flag that says it was
    /// answered has exactly one writer: the end of <c>CompleteRoomEntryAsync</c>, after the player is
    /// actually in. It used to be set optimistically before any gate ran and never taken back on the
    /// doorbell path, which made "pending" and "admitted" the same state.
    /// </summary>
    [Fact]
    public void OnlyRoomService_ApprovesAPendingEntry()
    {
        List<string> offenders = CallSitesOf(
            "SetPendingRoomAsync",
            [
                Path.Combine(
                    "Vortex.Primitives",
                    "Players",
                    "Grains",
                    "IPlayerPresenceGrain.Room.cs"
                ),
                Path.Combine("Vortex.Players", "Grains", "PlayerPresenceGrain.Room.cs"),
                Path.Combine("Vortex.Rooms", "RoomService.cs"),
                Path.Combine("Vortex.Rooms", "RoomService.Doorbell.cs"),
                // Clears it on the doorbell timeout sweep; cannot grant, only revoke.
                Path.Combine("Vortex.Rooms", "Grains", "RoomGrain.Doorbell.cs"),
            ]
        );

        offenders
            .Should()
            .BeEmpty(
                "the flag is what says a player was admitted rather than merely left pending by a "
                    + "ringing doorbell, so anything that can set it is a door into every locked room "
                    + "in the hotel"
            );
    }

    /// <summary>
    /// Files outside <paramref name="allowList" /> that name <paramref name="member" /> in code
    /// rather than in a comment.
    /// </summary>
    private static List<string> CallSitesOf(string member, string[] allowList)
    {
        List<string> offenders = [];

        foreach (
            string project in Directory.EnumerateDirectories(RepositoryPaths.Root(), "Vortex.*")
        )
        {
            foreach (string file in RoomGrainConcurrencyTests.SourceFiles(project))
            {
                if (
                    file.Contains(
                        $"{Path.DirectorySeparatorChar}Vortex.Hosting.Tests{Path.DirectorySeparatorChar}",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || Array.Exists(
                        allowList,
                        allowed => file.EndsWith(allowed, StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    continue;
                }

                bool used = File.ReadAllLines(file)
                    .Any(line =>
                        line.Contains(member, StringComparison.Ordinal)
                        && !line.TrimStart().StartsWith("//", StringComparison.Ordinal)
                    );

                if (used)
                {
                    offenders.Add(Path.GetFileName(file));
                }
            }
        }

        return offenders;
    }
}
