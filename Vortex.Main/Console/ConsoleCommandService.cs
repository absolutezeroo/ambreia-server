using System;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Console;

namespace Vortex.Main.Console;

/// <summary>
/// The stdin front-end onto <see cref="IConsoleCommandDispatcher"/>: reads lines from the operator's
/// terminal and prints whatever the command writes. The commands themselves live in the dispatcher,
/// which takes an output sink rather than writing to the terminal itself.
/// </summary>
public class ConsoleCommandService(IConsoleCommandDispatcher dispatcher)
{
    private readonly IConsoleCommandDispatcher _dispatcher = dispatcher;
    private readonly CancellationTokenSource _cts = new();

    private Task? _loopTask;

    public bool IsRunning => _loopTask is { IsCompleted: false };

    public void Enable()
    {
        System.Console.WriteLine("Console command service started. Type 'help' for commands.");

        if (IsRunning)
        {
            throw new InvalidOperationException("Already running.");
        }

        _loopTask = Task.Run(() => LoopAsync(_cts.Token));
    }

    public async Task DisableAsync()
    {
        if (!IsRunning)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);

        if (_loopTask is not null)
#pragma warning disable VSTHRD003
        {
            await _loopTask.ConfigureAwait(false);
        }
#pragma warning restore VSTHRD003

        _cts.Dispose();
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            string? input = await Task.Run(System.Console.ReadLine, ct).ConfigureAwait(false);

            // `null` is end of stream, NOT an empty line, and the difference is the whole reason
            // this branch exists. A container started without a TTY has stdin on /dev/null, so
            // ReadLine returns null immediately and keeps doing so for the life of the process.
            // Folding that into the IsNullOrWhiteSpace check below spins this loop as fast as the
            // thread pool can schedule it: several hundred percent CPU, on an idle hotel, logging
            // nothing at all. Measured at 225-370% across six .NET TP Workers on a 4-core VPS.
            if (input is null)
            {
                System.Console.WriteLine(
                    "Console input is closed; console commands are disabled for this process."
                );

                return;
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            // A command that throws (a grain call timing out, say) is reported to whoever typed
            // it; letting it escape would fault this loop and silently end the console.
            try
            {
                await _dispatcher
                    .ExecuteAsync(input, System.Console.WriteLine, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                System.Console.WriteLine($"Command failed: {ex.Message}");
            }
        }
    }
}
