using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Main.Console;
using Vortex.Primitives.Console;
using Xunit;

namespace Vortex.Hosting.Tests;

/// <summary>
/// The stdin loop has to tell an empty line apart from end of stream.
/// <para>
/// A container started without a TTY has stdin on <c>/dev/null</c>, where
/// <c>Console.ReadLine()</c> returns <see langword="null"/> immediately and keeps doing so. Folding
/// that into a "nothing was typed, read again" branch spun the loop as fast as the thread pool
/// could schedule it — measured at 225-370% CPU across six workers on an idle 4-core hotel, with
/// no log line to say so. The production emulator ran that way for hours.
/// </para>
/// </summary>
public sealed class ConsoleCommandServiceTests
{
    [Fact]
    public async Task Loop_StopsWhenStdinIsAtEndOfStream()
    {
        TextReader previous = System.Console.In;

        // An exhausted reader is exactly what a container's /dev/null stdin looks like to
        // Console.ReadLine(): the first call already returns null.
        System.Console.SetIn(new StringReader(string.Empty));

        try
        {
            ConsoleCommandService service = new(new NoCommands());

            service.Enable();

            await WaitUntilStoppedAsync(service, TimeSpan.FromSeconds(5));

            // The real assertion is that the wait above returned at all. Before the end-of-stream
            // guard the loop never completed, so this is the spin regression in one line.
            service.IsRunning.Should().BeFalse();
        }
        finally
        {
            System.Console.SetIn(previous);
        }
    }

    [Fact]
    public async Task ACommandThatThrows_DoesNotEndTheConsole()
    {
        TextReader previous = System.Console.In;
        System.Console.SetIn(new StringReader("boom\nnext\n"));

        try
        {
            ThrowsOnce dispatcher = new();
            ConsoleCommandService service = new(dispatcher);

            service.Enable();

            await WaitUntilStoppedAsync(service, TimeSpan.FromSeconds(5));

            dispatcher.Received.Should().Equal("boom", "next");
        }
        finally
        {
            System.Console.SetIn(previous);
        }
    }

    private static async Task WaitUntilStoppedAsync(ConsoleCommandService service, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;

        while (service.IsRunning && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25).ConfigureAwait(false);
        }
    }

    private sealed class ThrowsOnce : IConsoleCommandDispatcher
    {
        public List<string> Received { get; } = [];

        public IReadOnlyList<ConsoleCommandDescriptor> Commands => [];

        public ConsoleCommandDescriptor? Find(string name) => null;

        public Task<bool> ExecuteAsync(string input, Action<string> write, CancellationToken ct)
        {
            Received.Add(input);

            return Received.Count == 1
                ? throw new InvalidOperationException("grain call timed out")
                : Task.FromResult(true);
        }
    }

    private sealed class NoCommands : IConsoleCommandDispatcher
    {
        public IReadOnlyList<ConsoleCommandDescriptor> Commands => [];

        public ConsoleCommandDescriptor? Find(string name) => null;

        public Task<bool> ExecuteAsync(string input, Action<string> write, CancellationToken ct) =>
            Task.FromResult(false);
    }
}
