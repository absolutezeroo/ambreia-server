using FluentAssertions;
using Vortex.Rpg.Combat.Configuration;
using Xunit;

namespace Vortex.Rpg.Combat.Tests;

public sealed class CombatConfigTests
{
    [Fact]
    public void TheKoDefaults_AreTheSpecs()
    {
        CombatConfig config = new();

        (
            config.AutoReleaseSeconds,
            config.MaxKoSeconds,
            config.ResurrectOfferSeconds,
            config.ThreatReportStep
        )
            .Should()
            .Be((60, 600, 30, 5));
    }
}
