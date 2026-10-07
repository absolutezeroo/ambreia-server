using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Main.Hosting;
using Vortex.Plugins;
using Vortex.Primitives.Console;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Permissions;

namespace Vortex.Main.Console;

/// <summary>
/// The operator command set, independent of where the command came from. <see
/// cref="ConsoleCommandService"/> feeds it stdin; the dashboard console feeds it a typed request
/// from an authenticated staff member. Output goes to the caller's sink rather than straight to
/// <c>System.Console</c>, which is what lets the same command answer both.
/// </summary>
public sealed class ConsoleCommandDispatcher(IServiceProvider services) : IConsoleCommandDispatcher
{
    private static readonly ConsoleCommandDescriptor[] CommandList =
    [
        new("help", "help", "List the available commands."),
        new(
            "quit",
            "quit",
            "Shut the emulator down gracefully.",
            Capabilities.Server.Control,
            ["exit"]
        ),
        new(
            "announce",
            "announce <minutes> [downtime]",
            "Warn every connected player that the hotel restarts in <minutes>.",
            // The same grant as `quit`: this is the warning for exactly what that command does, and
            // whoever may take the hotel down may say so.
            Capabilities.Server.Control
        ),
        new(
            "reload-plugins",
            "reload-plugins",
            "Reload every plugin from disk.",
            Capabilities.Server.ConfigManage
        ),
        new(
            "reload-plugin",
            "reload-plugin <key>",
            "Reload a single plugin by key.",
            Capabilities.Server.ConfigManage
        ),
        // `reload-fishing` was here. It reloaded a grain rather than an IReferenceDataProvider, which
        // is the only reason it was a separate word for the same idea; the grain is now a named
        // cache under `reload`, composed in Program.cs.
        //
        // The general form of that command, and of every reload button four domains grew
        // separately. Content written outside an admin service -- a SQL script, a restore -- is
        // invisible until the cache holding it is read again, and this is the way to do that
        // without restarting the hotel.
        new(
            "reload",
            "reload [provider]",
            "List the reloadable reference caches, or reload one by name.",
            Capabilities.Server.ConfigManage
        ),
    ];

    public IReadOnlyList<ConsoleCommandDescriptor> Commands => CommandList;

    public ConsoleCommandDescriptor? Find(string name)
    {
        string needle = name.Trim().ToLowerInvariant();

        return Array.Find(
            CommandList,
            c =>
                c.Name == needle
                || (c.Aliases is not null && c.Aliases.Contains(needle, StringComparer.Ordinal))
        );
    }

    public async Task<bool> ExecuteAsync(string input, Action<string> write, CancellationToken ct)
    {
        string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return true;
        }

        string cmd = parts[0].ToLowerInvariant();
        string[] args = [.. parts.Skip(1)];

        switch (cmd)
        {
            case "help":
                write("Available commands:");

                foreach (ConsoleCommandDescriptor descriptor in CommandList)
                {
                    write($"  {descriptor.Usage, -38} {descriptor.Description}");
                }

                return true;

            case "quit":
            case "exit":
                // Environment.Exit(0) used to cut the process here, which skipped host shutdown
                // entirely: no hosted service got to stop, and anything still buffered was lost.
                // Stopping the application lets the shutdown path run and the process exit 0 on
                // its own.
                write("Shutting down...");
                services.GetRequiredService<IHostApplicationLifetime>().StopApplication();

                return true;

            // The warning `quit` does not give, by hand. The hotel also warns on its own when
            // something stops the container (see ShutdownConfig), but that notice starts when the
            // deploy does; this one can go out before it, and it is the only way to warn about
            // anything the orchestrator is not driving.
            //
            // One send per invocation, because the client's alert does not tick — see
            // MaintenanceAnnouncer. A second warning is this command typed again with a smaller
            // number.
            case "announce":
            {
                const int DEFAULT_DOWNTIME_MINUTES = 2;

                if (args.Length == 0 || !int.TryParse(args[0], out int minutes) || minutes < 0)
                {
                    write("Usage: announce <minutes> [downtime]");

                    return true;
                }

                int downtime = DEFAULT_DOWNTIME_MINUTES;

                if (args.Length > 1 && (!int.TryParse(args[1], out downtime) || downtime < 0))
                {
                    write("Usage: announce <minutes> [downtime]");

                    return true;
                }

                int reached = await services
                    .GetRequiredService<IMaintenanceAnnouncer>()
                    .AnnounceAsync(minutes, downtime, ct)
                    .ConfigureAwait(false);

                write(
                    reached == 0
                        ? "Nobody is connected; nothing was announced."
                        : $"Warned {reached} player(s) of a restart in {minutes} minute(s), "
                            + $"back in about {downtime}."
                );

                return true;
            }

            case "reload":
            {
                IReferenceDataReloader reloader =
                    services.GetRequiredService<IReferenceDataReloader>();

                // With no argument it lists rather than doing anything: the names are type names,
                // nobody remembers twenty-nine of them, and a reload is not something to guess at.
                if (args.Length == 0)
                {
                    write("Reloadable reference caches:");

                    foreach (string name in reloader.Providers)
                    {
                        write($"  {name}");
                    }

                    write("Usage: reload <provider>");

                    return true;
                }

                ReloadOutcome outcome = await reloader
                    .ReloadAsync(args[0], ct)
                    .ConfigureAwait(false);

                write(
                    outcome.Reloaded
                        ? $"{outcome.Provider} reloaded in {outcome.ElapsedMs}ms."
                        : $"{outcome.Provider} not reloaded: {outcome.Error}"
                );

                return true;
            }

            case "reload-plugins":
                try
                {
                    PluginManager pluginMgr = services.GetRequiredService<PluginManager>();
                    await pluginMgr.LoadAllAsync(true, ct).ConfigureAwait(false);
                    write("Plugins reloaded.");
                }
                catch (Exception ex)
                {
                    write($"Reload failed: {ex.Message}");
                }

                return true;

            case "reload-plugin":
            {
                if (args.Length == 0)
                {
                    write("Usage: reload-plugin <key>");

                    return true;
                }

                try
                {
                    PluginManager pluginMgr = services.GetRequiredService<PluginManager>();
                    await pluginMgr.ReloadAsync(args[0], ct).ConfigureAwait(false);
                    write($"Plugin '{args[0]}' reloaded.");
                }
                catch (Exception ex)
                {
                    write($"Reload failed for '{args[0]}': {ex.Message}");
                }

                return true;
            }

            default:
                write($"Unknown command: {cmd}");

                return false;
        }
    }
}
