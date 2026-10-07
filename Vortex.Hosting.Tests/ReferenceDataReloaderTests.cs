using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Main;
using Vortex.Primitives.Hosting;
using Xunit;

namespace Vortex.Hosting.Tests;

/// <summary>
/// The runtime half of reference-data loading, and specifically its error policy.
/// </summary>
/// <remarks>
/// The whole reason this class exists separately from <c>VortexEmulator</c>'s startup loop is that
/// the two must fail in opposite ways: a cache that will not load at boot has to stop the hotel, and
/// one that will not reload at runtime must not. That is behaviour, not structure, so it is checked
/// rather than described.
/// </remarks>
public sealed class ReferenceDataReloaderTests
{
    private sealed class FakeProvider(Action? onReload = null) : IReferenceDataProvider
    {
        public int Reloads { get; private set; }

        public int LoadStage => 0;

        public Task ReloadAsync(CancellationToken ct)
        {
            Reloads += 1;
            onReload?.Invoke();

            return Task.CompletedTask;
        }
    }

    private sealed class BrokenProvider : IReferenceDataProvider
    {
        public int LoadStage => 0;

        public Task ReloadAsync(CancellationToken ct) =>
            throw new InvalidOperationException("the database said no");
    }

    private static ReferenceDataReloader Reloader(
        IEnumerable<IReferenceDataProvider> providers,
        IReadOnlyDictionary<string, Func<CancellationToken, Task>>? extra = null
    ) => new(providers, NullLogger<ReferenceDataReloader>.Instance, extra);

    [Fact]
    public async Task Reloading_a_provider_by_name_calls_it()
    {
        FakeProvider provider = new();
        ReferenceDataReloader reloader = Reloader([provider]);

        ReloadOutcome outcome = await reloader.ReloadAsync(
            nameof(FakeProvider),
            CancellationToken.None
        );

        outcome.Reloaded.Should().BeTrue();
        outcome.Provider.Should().Be(nameof(FakeProvider));
        outcome.Error.Should().BeNull();
        provider.Reloads.Should().Be(1);
    }

    [Fact]
    public async Task The_name_is_case_insensitive_and_comes_back_spelled_properly()
    {
        // The names are type names. An operator typing them at a console will not get the case
        // right, and being told "no such cache" for a capitalisation would be its own small cruelty.
        ReloadOutcome outcome = await Reloader([new FakeProvider()])
            .ReloadAsync("fakeprovider", CancellationToken.None);

        outcome.Reloaded.Should().BeTrue();
        outcome.Provider.Should().Be(nameof(FakeProvider));
    }

    [Fact]
    public async Task An_unknown_name_is_an_answer_and_not_an_exception()
    {
        ReloadOutcome outcome = await Reloader([new FakeProvider()])
            .ReloadAsync("NoSuchThing", CancellationToken.None);

        outcome.Reloaded.Should().BeFalse();
        outcome.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_failing_provider_is_reported_and_never_thrown()
    {
        // The runtime policy in one assertion. The hotel is already up and the cache still holds
        // what it had; taking the process down for it would be worse than the stale data.
        ReloadOutcome outcome = await Reloader([new BrokenProvider()])
            .ReloadAsync(nameof(BrokenProvider), CancellationToken.None);

        outcome.Reloaded.Should().BeFalse();
        outcome.Error.Should().Contain("the database said no");
    }

    [Fact]
    public async Task A_cancellation_is_not_swallowed_as_a_failed_reload()
    {
        // Shutting down mid-reload is not the provider failing, and reporting it as one would put a
        // false error in front of an operator who pressed nothing.
        ReferenceDataReloader reloader = Reloader([
            new FakeProvider(() => throw new OperationCanceledException()),
        ]);

        Func<Task> reload = () =>
            reloader.ReloadAsync(nameof(FakeProvider), CancellationToken.None);

        await reload.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task A_grain_held_cache_is_reloadable_under_the_same_word()
    {
        // A cache held by a grain rather than by a provider singleton is passed in as a named
        // cache, so `reload` stays one word for the whole idea. The key is only a sample name.
        int calls = 0;
        ReferenceDataReloader reloader = Reloader(
            [],
            new Dictionary<string, Func<CancellationToken, Task>>
            {
                ["FishingDefinitions"] = _ =>
                {
                    calls += 1;

                    return Task.CompletedTask;
                },
            }
        );

        reloader.Providers.Should().Contain("FishingDefinitions");

        ReloadOutcome outcome = await reloader.ReloadAsync(
            "FishingDefinitions",
            CancellationToken.None
        );

        outcome.Reloaded.Should().BeTrue();
        calls.Should().Be(1);
    }

    [Fact]
    public void Providers_are_listed_in_a_stable_order()
    {
        // Read by a human choosing one of the names.
        Reloader([new BrokenProvider(), new FakeProvider()])
            .Providers.Should()
            .ContainInOrder(nameof(BrokenProvider), nameof(FakeProvider));
    }

    /// <summary>
    /// Two closed generics of one open type are two separate caches and must answer to two names.
    /// </summary>
    /// <remarks>
    /// <c>Type.Name</c> renders both <c>GenericProvider&lt;NormalTag&gt;</c> and
    /// <c>GenericProvider&lt;OtherTag&gt;</c> as <c>GenericProvider`1</c>; named that way they
    /// would collide in the name table, and one cache could not be reloaded by any name while
    /// `reload` still listed something that looked like it.
    /// </remarks>
    [Fact]
    public async Task Two_closed_generics_of_one_type_get_one_name_each()
    {
        GenericProvider<NormalTag> normal = new();
        GenericProvider<OtherTag> other = new();

        ReferenceDataReloader reloader = Reloader([normal, other]);

        reloader
            .Providers.Should()
            .BeEquivalentTo(["GenericProvider<NormalTag>", "GenericProvider<OtherTag>"]);

        // And each name reaches its own cache, rather than both landing on whichever won.
        ReloadOutcome outcome = await reloader.ReloadAsync(
            "GenericProvider<NormalTag>",
            CancellationToken.None
        );

        outcome.Reloaded.Should().BeTrue();
        normal.Reloads.Should().Be(1);
        other.Reloads.Should().Be(0);
    }

    [Fact]
    public void A_non_generic_provider_keeps_its_plain_type_name()
    {
        // Nobody should have to type angle brackets for a provider that is not generic.
        Reloader([new FakeProvider()]).Providers.Should().Equal(nameof(FakeProvider));
    }

    private sealed class NormalTag;

    private sealed class OtherTag;

    private sealed class GenericProvider<TTag> : IReferenceDataProvider
    {
        public int Reloads { get; private set; }

        public int LoadStage => 0;

        public Task ReloadAsync(CancellationToken ct)
        {
            Reloads += 1;

            return Task.CompletedTask;
        }
    }
}
