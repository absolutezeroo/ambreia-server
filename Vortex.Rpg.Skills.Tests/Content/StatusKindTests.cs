using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Kinds;
using Vortex.Rpg.Content.Validation;
using Xunit;

namespace Vortex.Rpg.Skills.Tests.Content;

/// <summary>The rules of a status (spec M1b §5.3): one test per rule.</summary>
public sealed class StatusKindTests
{
    private static readonly StatusDefinition Burning = new()
    {
        Id = "status.burning",
        Kind = "status",
        Type = StatusType.Dot,
        DurationMs = 6000,
        PeriodMs = 2000,
        Stacking = StatusStacking.Stack,
        MaxStacks = 3,
        Coefficient = 0.2,
    };

    private static readonly StatusDefinition Frozen = new()
    {
        Id = "status.frozen",
        Kind = "status",
        Type = StatusType.Slow,
        DurationMs = 5000,
        Stacking = StatusStacking.Replace,
        SlowPercent = 60,
    };

    private static string[] Codes(StatusDefinition status) =>
        [
            .. new StatusKind()
                .Check(
                    status,
                    new ContentCheck(
                        new Dictionary<string, ContentDefinition> { [status.Id] = status },
                        _ => false
                    )
                )
                .Select(f => f.Code),
        ];

    [Fact]
    public void ValidStatuses_HaveNoFinding()
    {
        Codes(Burning).Should().BeEmpty();
        Codes(Frozen).Should().BeEmpty();
    }

    [Fact]
    public void ADot_WithoutAPeriod_IsRefused() =>
        Codes(Burning with { PeriodMs = 0 }).Should().Equal("PeriodicWithoutPeriod");

    [Fact]
    public void AHot_WithoutAPeriod_IsRefused() =>
        Codes(Burning with { Type = StatusType.Hot, PeriodMs = 0 })
            .Should()
            .Equal("PeriodicWithoutPeriod");

    [Fact]
    public void AStack_WithoutAMaximum_IsRefused() =>
        Codes(Burning with { MaxStacks = 0 }).Should().Equal("StackWithoutMax");

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    public void ASlow_OutsideOneToNinety_IsRefused(int percent) =>
        Codes(Frozen with { SlowPercent = percent }).Should().Equal("BadSlow");

    [Fact]
    public void ANonPositiveDuration_IsRefused() =>
        Codes(Frozen with { DurationMs = 0 }).Should().Equal("BadDuration");

    [Fact]
    public void ANegativeValue_IsRefused() =>
        Codes(Burning with { Coefficient = -1 }).Should().Equal("NegativeValue");

    [Fact]
    public void AShield_WithoutAnAmount_IsRefused() =>
        Codes(Frozen with { Type = StatusType.Shield, SlowPercent = 0 })
            .Should()
            .Equal("ShieldWithoutAmount");
}
