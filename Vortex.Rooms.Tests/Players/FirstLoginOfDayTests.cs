using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Players.Configuration;
using Vortex.Players.Grains;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Players.Providers;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Progression.Achievements;
using Vortex.Progression.Configuration;
using Vortex.Progression.Grains;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Players;

/// <summary>
/// The login handler used to send <c>IsFirstLoginOfDay = true</c> to everybody, every time — the
/// client's daily-reward and first-visit surfaces read that message and nothing else.
///
/// <para>
/// The answer has to come from here rather than from the handler, because <c>MarkLoggedInAsync</c>
/// runs first in the handshake and overwrites <c>last_login_at</c>. By the time the composer is
/// built the previous value is gone; this is the one moment both exist.
/// </para>
/// </summary>
public sealed class FirstLoginOfDayTests
{
    private const int PlayerId = 501;

    [Fact]
    public async Task AnAccountThatHasNeverLoggedIn_CountsAsTheFirstOfTheDay()
    {
        Harness harness = await Harness.CreateAsync(lastLoginAt: null).ConfigureAwait(true);

        bool first = await harness
            .Grain.MarkLoggedInAsync(CancellationToken.None)
            .ConfigureAwait(true);

        first.Should().BeTrue();
    }

    [Fact]
    public async Task ALoginOnAnEarlierDay_MakesThisTheFirst()
    {
        Harness harness = await Harness
            .CreateAsync(DateTime.UtcNow.AddDays(-1))
            .ConfigureAwait(true);

        bool first = await harness
            .Grain.MarkLoggedInAsync(CancellationToken.None)
            .ConfigureAwait(true);

        first.Should().BeTrue();
    }

    [Fact]
    public async Task ASecondLoginTheSameDay_IsNotTheFirst()
    {
        // Midnight today, NOT "an hour ago": an hour before now falls on yesterday whenever the suite
        // runs between 00:00 and 01:00 UTC, which made this test fail for one hour out of every day.
        Harness harness = await Harness.CreateAsync(DateTime.UtcNow.Date).ConfigureAwait(true);

        bool first = await harness
            .Grain.MarkLoggedInAsync(CancellationToken.None)
            .ConfigureAwait(true);

        first.Should().BeFalse();
    }

    /// <summary>
    /// The comparison is on the calendar day, not on elapsed hours: logging in at 23:50 and again at
    /// 00:10 is twenty minutes apart and still two different days.
    /// </summary>
    [Fact]
    public async Task TwentyMinutesAcrossMidnight_IsStillANewDay()
    {
        DateTime justAfterMidnight = DateTime.UtcNow.Date.AddMinutes(10);
        Harness harness = await Harness
            .CreateAsync(justAfterMidnight.AddMinutes(-20))
            .ConfigureAwait(true);

        bool first = await harness
            .Grain.MarkLoggedInAsync(CancellationToken.None)
            .ConfigureAwait(true);

        // Yesterday 23:50 against a stamp taken now (today) -- a different date either way.
        first.Should().BeTrue();
    }

    [Fact]
    public async Task TheStampIsWritten_SoTheNextLoginSeesToday()
    {
        Harness harness = await Harness
            .CreateAsync(DateTime.UtcNow.AddDays(-3))
            .ConfigureAwait(true);

        await harness.Grain.MarkLoggedInAsync(CancellationToken.None).ConfigureAwait(true);

        bool second = await harness
            .Grain.MarkLoggedInAsync(CancellationToken.None)
            .ConfigureAwait(true);

        second.Should().BeFalse("the first call must have recorded today");
    }

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }

    private sealed class Harness
    {
        private Harness(PlayerGrain grain) => Grain = grain;

        public PlayerGrain Grain { get; }

        public static async Task<Harness> CreateAsync(DateTime? lastLoginAt)
        {
            DbContextOptions<VortexDbContext> options =
                new DbContextOptionsBuilder<VortexDbContext>()
                    .UseInMemoryDatabase($"first-login-{Guid.NewGuid():N}")
                    .Options;

            await using (VortexDbContext seed = new(options))
            {
                seed.Players.Add(
                    new PlayerEntity
                    {
                        Id = PlayerId,
                        Name = "tester",
                        Figure = "hd-1-1",
                        Gender = AvatarGenderType.Male,
                        PlayerStatus = PlayerStatusType.Offline,
                        PlayerPerks = PlayerPerkFlags.None,
                        LastLoginAt = lastLoginAt,
                    }
                );

                await seed.SaveChangesAsync().ConfigureAwait(true);
            }

            return new Harness(
                GrainActivationContext.CreateWithIntegerKey<PlayerGrain>(
                    PlayerId,
                    new TestDbContextFactory(options),
                    FakeProxy.Create<IGrainFactory>(_ => null),
                    FakeProxy.Create<IEventPublisher>(_ => null),
                    NullLogger<PlayerGrain>.Instance,
                    Options.Create(new ClubConfig()),
                    // Nothing here buys anything; the grain simply needs one to be built.
                    FakeProxy.Create<ICommerceJournal>(_ => Task.CompletedTask),
                    // No roles: the grain resolves staff-ness once at load and nothing here cares
                    // what the answer is.
                    FakeProxy.Create<IPermissionService>(_ => Task.FromResult(PermissionSet.Empty))
                )
            );
        }
    }
}
