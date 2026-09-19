using System.Collections.Generic;
using FluentAssertions;
using Vortex.Progression.Grains;
using Xunit;

namespace Vortex.Players.Tests.Badges;

/// <summary>
///     The five (slot, code) pairs SetActivatedBadges carries come straight off the wire, and the
///     grain clears every equipped slot before it reads them — so anything this rejects has to be
///     rejected without throwing, or the clear is all that happens.
/// </summary>
public sealed class ActivatedBadgeSlotsTests
{
    private static List<(int SlotId, string BadgeCode)> Slots(params (int, string)[] pairs) =>
        [.. pairs];

    [Fact]
    public void NormalizeSlots_KeepsTheFilledSlots()
    {
        Dictionary<string, int> result = PlayerBadgeGrain.NormalizeSlots(
            Slots(
                (1, "ACH_HabbiconCollected2"),
                (2, "ACH_AllTimeHotelPresence5"),
                (3, ""),
                (4, ""),
                (5, "")
            )
        );

        result
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, int>
                {
                    ["ACH_HabbiconCollected2"] = 1,
                    ["ACH_AllTimeHotelPresence5"] = 2,
                }
            );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(int.MaxValue)]
    public void NormalizeSlots_DropsASlotTheEditorCannotRender(int slot)
    {
        PlayerBadgeGrain.NormalizeSlots(Slots((slot, "ADM"))).Should().BeEmpty();
    }

    [Fact]
    public void NormalizeSlots_SurvivesTheSameBadgeInTwoSlots()
    {
        // ToDictionary threw here, and the throw landed after the slots had already been cleared:
        // one crafted packet unequipped everything the player was wearing.
        PlayerBadgeGrain
            .NormalizeSlots(Slots((1, "ADM"), (2, "ADM")))
            .Should()
            .BeEquivalentTo(new Dictionary<string, int> { ["ADM"] = 1 });
    }

    [Fact]
    public void NormalizeSlots_SurvivesTwoBadgesInTheSameSlot()
    {
        PlayerBadgeGrain
            .NormalizeSlots(Slots((3, "ADM"), (3, "ACH_Login5")))
            .Should()
            .BeEquivalentTo(new Dictionary<string, int> { ["ADM"] = 3 });
    }

    [Fact]
    public void NormalizeSlots_IsEmpty_WhenEveryBadgeIsTakenOff()
    {
        PlayerBadgeGrain
            .NormalizeSlots(Slots((1, ""), (2, ""), (3, ""), (4, ""), (5, "")))
            .Should()
            .BeEmpty();
    }
}
