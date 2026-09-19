using System;
using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Protocol.Messages.Outgoing.Room.Pets;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Pets;

/// <summary>
///     Pinned to a capture of the live hotel (2026-09-19): opening the training panel of the level 8
///     dog "filou" answered GetPetCommands with these bytes. Two arrays, each its own length prefix
///     -- every command the pet's species can ever learn, then the ones it has learned so far.
///
///     The second array is the first thirteen entries of the first, which is what says the full list
///     is ordered by unlock level rather than by id. <c>PetCommandProvider</c> loads its rows
///     <c>OrderBy(LevelRequired)</c>, so that property holds here by construction.
/// </summary>
public sealed class PetCommandsWireTests
{
    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    private static readonly ImmutableArray<int> AllCommands =
    [
        46,
        0,
        1,
        14,
        43,
        2,
        3,
        4,
        17,
        5,
        6,
        7,
        8,
        9,
        10,
        11,
        12,
        13,
        15,
        16,
        24,
        25,
        26,
    ];

    [Fact]
    public void TrainingPanel_MatchesTheLiveHotelBytes()
    {
        PetCommandsMessageComposer composer = new()
        {
            PetId = 353_056,
            AllCommandIds = AllCommands,
            EnabledCommandIds = [.. AllCommands[..13]],
        };

        byte[] bytes = Revision
            .Serializers[typeof(PetCommandsMessageComposer)]
            .Serialize(composer)
            .ToArray();

        // Body only: the captured hotel is a later build and announces this on 1607 where
        // WIN63-202607011411 binds its own id.
        Convert
            .ToHexString(bytes.AsSpan(6))
            .Should()
            .Be(
                "00056320000000170000002E00000000000000010000000E0000002B00000002"
                    + "00000003000000040000001100000005000000060000000700000008000000090000000A"
                    + "0000000B0000000C0000000D0000000F0000001000000018000000190000001A0000000D"
                    + "0000002E00000000000000010000000E0000002B000000020000000300000004"
                    + "0000001100000005000000060000000700000008"
            );
    }
}
