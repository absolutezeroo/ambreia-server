using FluentAssertions;
using Vortex.Rooms.Grains.Systems;
using Xunit;

namespace Vortex.Rooms.Tests.Pets;

/// <summary>
/// What a pet does on screen is the status string and nothing else, and the client is unforgiving
/// about its shape.
/// </summary>
/// <remarks>
/// <para>
/// It splits each segment on a space and only keeps the action when there are two pieces --
/// <c>if (pieces.length >= 2)</c> in this revision's parser. A bare <c>/lay/</c> or <c>/eat/</c> is
/// dropped on the floor, and the pet keeps standing: no lying down, no eating, no Zzz. Arcturus
/// never sends one either.
/// </para>
/// <para>
/// The posture name has to be one the pet's own asset declares. Decoding dog.nitro gives std, beg,
/// bnd, ded, eat, jmp, lay, pla, rdy, scr, sit, snf, spk, mv -- and no drk. An unknown posture
/// resolves to the default, which is standing, which is why drinking looked like nothing at all.
/// </para>
/// </remarks>
public sealed class PetStatusWireTests
{
    [Theory]
    [InlineData("/lay 0/")]
    [InlineData("/lay 1.25/")]
    [InlineData("/eat 0/")]
    public void AStatusAlwaysCarriesAValue(string status)
    {
        string body = status.Trim('/');

        body.Split(' ')
            .Should()
            .HaveCountGreaterThanOrEqualTo(
                2,
                "the client drops any segment it cannot split into an action and a value"
            );
    }

    [Fact]
    public void LayStatus_CarriesTheHeight()
    {
        PetAnimation.LayStatus(1.25).Should().Be("/lay 1.25/");
    }

    [Fact]
    public void EatStatus_CarriesTheHeight()
    {
        PetAnimation.EatStatus(0).Should().Be("/eat 0/");
    }

    [Fact]
    public void Heights_AreFormattedInvariantly()
    {
        PetAnimation
            .LayStatus(0.5)
            .Should()
            .Be("/lay 0.5/", "a comma would make the client's parseFloat stop at the whole part");
    }

    [Fact]
    public void DrinkingUsesTheEatingPosture()
    {
        PetAnimation
            .EatPosture.Should()
            .Be("eat", "no pet asset declares a drk posture -- the dog ships eat and not drk");
    }

    [Theory]
    [InlineData(PetAnimation.StandPosture)]
    [InlineData(PetAnimation.LayPosture)]
    [InlineData(PetAnimation.EatPosture)]
    public void EveryPostureWeSendIsOneTheAssetDeclares(string posture)
    {
        // From dog.nitro's visualization data, the id list the client resolves an animation from.
        string[] declared =
        [
            "std",
            "beg",
            "bnd",
            "ded",
            "eat",
            "jmp",
            "lay",
            "pla",
            "rdy",
            "scr",
            "sit",
            "snf",
            "spk",
            "mv",
        ];

        declared.Should().Contain(posture);
    }
}
