using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace Vortex.Primitives.Console;

/// <summary>
///     Holds the last N console lines and fans new ones out to every attached viewer.
///     <para>
///     One process needs this: the emulator, where a logger provider feeds it every line it logs so
///     the dashboard can follow along. A second used to — a supervisor that ran the emulator as a
///     child process and carried its stdout, so its control page kept a console while the emulator
///     was down. It was removed: under Docker the container runtime already restarts the process and
///     already keeps its logs, and it was wired into no deployment here.
///     </para>
///     <para>
///     Each viewer gets its own bounded channel that drops its oldest line when full, so a browser
///     tab that stops reading slows down nothing but itself — the emulator's logging is never
///     back-pressured by a stalled HTTP response.
///     </para>
/// </summary>
public sealed partial class ServerConsoleFeed(int capacity)
{
    private readonly object _gate = new();
    private readonly LinkedList<string> _history = new();
    private readonly List<Channel<string>> _viewers = [];

    /// <summary>Replays what is already buffered, then follows new lines. Dispose to detach.</summary>
    public ServerConsoleSubscription Subscribe()
    {
        Channel<string> channel = Channel.CreateBounded<string>(
            new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.DropOldest }
        );

        string[] backlog;

        lock (_gate)
        {
            backlog = [.. _history];
            _viewers.Add(channel);
        }

        return new ServerConsoleSubscription(this, channel, backlog);
    }

    public void Publish(string line)
    {
        string clean = StripAnsi(line);

        lock (_gate)
        {
            _history.AddLast(clean);

            while (_history.Count > capacity)
            {
                _history.RemoveFirst();
            }

            foreach (Channel<string> viewer in _viewers)
            {
                // Bounded + DropOldest: this never blocks and never fails.
                viewer.Writer.TryWrite(clean);
            }
        }
    }

    internal void Detach(Channel<string> viewer)
    {
        lock (_gate)
        {
            _viewers.Remove(viewer);
        }

        viewer.Writer.TryComplete();
    }

    /// <summary>
    ///     The console logger emits colour, which is noise once the line is going to a browser rather
    ///     than a terminal.
    /// </summary>
    private static string StripAnsi(string line) => AnsiEscapeRegex().Replace(line, string.Empty);

    [GeneratedRegex(@"\x1B\[[0-9;]*[a-zA-Z]")]
    private static partial Regex AnsiEscapeRegex();
}

/// <summary>One attached viewer: the lines buffered before it arrived, then everything after.</summary>
public sealed class ServerConsoleSubscription(
    ServerConsoleFeed feed,
    Channel<string> channel,
    IReadOnlyList<string> backlog
) : IDisposable
{
    private bool _disposed;

    public IReadOnlyList<string> Backlog => backlog;

    public ChannelReader<string> Reader => channel.Reader;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        feed.Detach(channel);
    }
}
