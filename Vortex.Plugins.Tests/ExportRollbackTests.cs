using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Plugins.Exports;
using Xunit;

namespace Vortex.Plugins.Tests;

/// <summary>
/// Export binds used to be irreversible: the registry never removed an entry, and swapping in a new
/// instance disposed the old one immediately. A plugin that failed to start therefore left its
/// half-built object published globally, with the working one already destroyed — the worst of both
/// outcomes. These tests pin the journaled behaviour that fixes it.
/// </summary>
// CA2000: every Greeter created here is deliberately handed to the export registry, which takes
// ownership and decides whether to dispose it (superseded/rolled back) or keep it (restored). That
// hand-off is exactly what these tests assert on, so disposing them locally would defeat the tests.
#pragma warning disable CA2000
public sealed class ExportRollbackTests
{
    public interface IGreeter
    {
        string Greet();
    }

    [Fact]
    public async Task ARolledBackBind_RestoresThePreviousInstance()
    {
        ExportRegistry registry = new(NullLogger.Instance);
        ExportJournal journal = new(NullLogger.Instance);

        Greeter original = new("original");
        await registry.SwapAsync<IGreeter>("greeter", original);

        Greeter replacement = new("replacement");
        await registry.SwapJournaledAsync(typeof(IGreeter), "greeter", replacement, journal);

        registry.GetOrCreate<IGreeter>("greeter").Current.Greet().Should().Be("replacement");

        await journal.RollbackAsync("plugin");

        registry.GetOrCreate<IGreeter>("greeter").Current.Greet().Should().Be("original");
    }

    [Fact]
    public async Task ARolledBackBind_LeavesTheRestoredInstanceUsable()
    {
        ExportRegistry registry = new(NullLogger.Instance);
        ExportJournal journal = new(NullLogger.Instance);

        Greeter original = new("original");
        await registry.SwapAsync<IGreeter>("greeter", original);

        await registry.SwapJournaledAsync(
            typeof(IGreeter),
            "greeter",
            new Greeter("replacement"),
            journal
        );
        await journal.RollbackAsync("plugin");

        // The whole point of deferring disposal: restoring an already-disposed instance would be a
        // second outage rather than a recovery.
        original.Disposed.Should().BeFalse();
    }

    [Fact]
    public async Task ARolledBackBind_DisposesTheFailedActivationsInstance()
    {
        ExportRegistry registry = new(NullLogger.Instance);
        ExportJournal journal = new(NullLogger.Instance);

        await registry.SwapAsync<IGreeter>("greeter", new Greeter("original"));

        Greeter replacement = new("replacement");
        await registry.SwapJournaledAsync(typeof(IGreeter), "greeter", replacement, journal);
        await journal.RollbackAsync("plugin");

        replacement.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task ACommittedBind_DisposesTheInstanceItReplaced()
    {
        ExportRegistry registry = new(NullLogger.Instance);
        ExportJournal journal = new(NullLogger.Instance);

        Greeter original = new("original");
        await registry.SwapAsync<IGreeter>("greeter", original);

        Greeter replacement = new("replacement");
        await registry.SwapJournaledAsync(typeof(IGreeter), "greeter", replacement, journal);
        await journal.CommitAsync();

        original.Disposed.Should().BeTrue("the superseded instance is dead once the swap commits");
        replacement.Disposed.Should().BeFalse();
        registry.GetOrCreate<IGreeter>("greeter").Current.Greet().Should().Be("replacement");
    }

    [Fact]
    public async Task RollingBackAFirstEverBind_LeavesTheExportUnbound()
    {
        ExportRegistry registry = new(NullLogger.Instance);
        ExportJournal journal = new(NullLogger.Instance);

        await registry.SwapJournaledAsync(
            typeof(IGreeter),
            "greeter",
            new Greeter("only"),
            journal
        );
        await journal.RollbackAsync("plugin");

        // There was nothing to restore, so the slot must go back to "never bound" rather than keep
        // pointing at the failed activation's object.
        FluentActions
            .Invoking(() => registry.GetOrCreate<IGreeter>("greeter").Current)
            .Should()
            .Throw<System.InvalidOperationException>();
    }

    [Fact]
    public async Task MultipleBinds_AreRolledBackInReverseOrder()
    {
        ExportRegistry registry = new(NullLogger.Instance);
        ExportJournal journal = new(NullLogger.Instance);

        await registry.SwapAsync<IGreeter>("a", new Greeter("a-original"));
        await registry.SwapAsync<IGreeter>("b", new Greeter("b-original"));

        await registry.SwapJournaledAsync(typeof(IGreeter), "a", new Greeter("a-new"), journal);
        await registry.SwapJournaledAsync(typeof(IGreeter), "b", new Greeter("b-new"), journal);

        journal.Count.Should().Be(2);

        await journal.RollbackAsync("plugin");

        registry.GetOrCreate<IGreeter>("a").Current.Greet().Should().Be("a-original");
        registry.GetOrCreate<IGreeter>("b").Current.Greet().Should().Be("b-original");
    }

    [Fact]
    public async Task Subscribers_AreNotifiedOfTheRestoredInstance()
    {
        ExportRegistry registry = new(NullLogger.Instance);
        ExportJournal journal = new(NullLogger.Instance);

        await registry.SwapAsync<IGreeter>("greeter", new Greeter("original"));

        string? seen = null;
        registry.GetOrCreate<IGreeter>("greeter").Subscribe(g => seen = g.Greet());

        await registry.SwapJournaledAsync(
            typeof(IGreeter),
            "greeter",
            new Greeter("replacement"),
            journal
        );
        seen.Should().Be("replacement");

        await journal.RollbackAsync("plugin");

        // Consumers that cached the value on swap must be told to drop the failed plugin's object.
        seen.Should().Be("original");
    }

    private sealed class Greeter(string name) : IGreeter, System.IDisposable
    {
        public bool Disposed { get; private set; }

        public string Greet() => name;

        public void Dispose() => Disposed = true;
    }
}

#pragma warning restore CA2000
