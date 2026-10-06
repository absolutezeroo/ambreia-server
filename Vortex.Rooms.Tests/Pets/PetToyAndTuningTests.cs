using System.Linq;
using System.Reflection;
using FluentAssertions;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Rooms.Configuration;
using Vortex.Rooms.Grains.Systems;
using Vortex.Rooms.Object.Logic.Furniture.Floor;
using Xunit;

namespace Vortex.Rooms.Tests.Pets;

/// <summary>
/// Toys, and the numbers behind them. Habbo's own guides name toys as what cheers a pet up; none of
/// the four in the shipped catalogue carried a logic, so no pet could ever reach one.
/// </summary>
public sealed class PetToyAndTuningTests
{
    [Theory]
    [InlineData(100, false)]
    [InlineData(51, false)]
    [InlineData(50, true)]
    [InlineData(0, true)]
    public void IsBored_CrossingTheThreshold_SendsThePetToPlay(int happiness, bool expected)
    {
        RoomPetRuntime
            .IsBored(PetWith(type: 0, happiness), boredHappinessThreshold: 50)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void TheToyLogicCarriesBothArcturusNames()
    {
        string[] keys =
        [
            .. typeof(FurniturePetToyLogic)
                .GetCustomAttributes<RoomObjectLogicAttribute>(false)
                .Select(a => a.Key),
        ];

        keys.Should().Contain("pet_toy");
        keys.Should().Contain("pet_trampoline");
    }

    [Fact]
    public void PlayStatus_CarriesAPostureTheAssetDeclares()
    {
        PetAnimation.PlayStatus(0).Should().Be("/pla 0/");
    }

    /// <summary>
    /// Boredom is why a pet must go to a toy; the whim is why it sometimes goes anyway. Without the
    /// second, a pet only ever played when it was miserable, which reads as a machine.
    /// </summary>
    [Fact]
    public void APetSometimesPlaysOnAWhim()
    {
        PetConfig config = new();

        config.ToyPlayChancePercent.Should().BeInRange(1, 99);
    }

    /// <summary>
    /// The cooldown is what stops a pet pacing back and forth across a ball from topping its mood up
    /// for free, and it has to outlast the bout itself.
    /// </summary>
    [Fact]
    public void TheToyCooldownOutlastsTheBout()
    {
        PetConfig config = new();

        config.ToyPlayCooldownMs.Should().BeGreaterThan(config.ToyPlayDurationMs);
    }

    /// <summary>
    /// The tunables fall back to the compiled defaults, so a hotel that has never touched the
    /// dashboard behaves exactly as it did before they became editable -- and a cleared key reads
    /// the default rather than zero, which would freeze the pet's mood.
    /// </summary>
    [Fact]
    public void Tuning_FallsBackToTheCompiledDefaults()
    {
        PetConfig defaults = new();
        PetTuning tuning = PetTuning.FromDefaults(defaults);

        tuning.HappinessDecayPerMinute.Should().Be(defaults.HappinessDecayPerMinute);
        tuning.ToyHappinessReward.Should().Be(defaults.ToyHappinessReward);
        tuning.BoredHappinessThreshold.Should().Be(defaults.BoredHappinessThreshold);
    }

    [Fact]
    public void EveryTuningKeyIsNamespaced()
    {
        string[] keys =
        [
            PetTuning.HappinessDecayPerMinuteKey,
            PetTuning.HappinessRestGainPerMinuteKey,
            PetTuning.RespectHappinessRewardKey,
            PetTuning.CommandHappinessRewardKey,
            PetTuning.CommandObeyBaseChancePercentKey,
            PetTuning.CommandObeyChancePerLevelPercentKey,
            PetTuning.CommandObeyMaxChancePercentKey,
            PetTuning.ToyHappinessRewardKey,
            PetTuning.BoredHappinessThresholdKey,
            PetTuning.ToyPlayDurationMsKey,
            PetTuning.ToyPlayCooldownMsKey,
            PetTuning.ToyPlayChancePercentKey,
            PetTuning.WanderIdleMinMsKey,
            PetTuning.WanderIdleMaxMsKey,
            PetTuning.VocalIntervalMsKey,
        ];

        keys.Should().OnlyContain(k => k.StartsWith("pets."), "the dashboard lists them together");
        keys.Should().OnlyHaveUniqueItems("two knobs sharing a key would overwrite each other");
    }

    private static PetSnapshot PetWith(int type, int happiness) =>
        new()
        {
            PetId = 1,
            OwnerId = new PlayerId(1),
            RoomId = 10,
            Name = "Rex",
            Type = type,
            Race = 0,
            Color = "FFFFFF",
            Gender = AvatarGenderType.Male,
            Level = 1,
            Experience = 0,
            Energy = 100,
            Nutrition = 100,
            Happiness = happiness,
            Respect = 0,
            X = 0,
            Y = 0,
            Z = 0,
            Direction = Rotation.South,
        };
}
