using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Console;

/// <summary>
/// Runs operator commands and writes their output to a caller-supplied sink.
/// <para>
/// The emulator's stdin loop is the only front-end today. A command is a typed lookup rather than a
/// blind string, and declares a <see cref="ConsoleCommandDescriptor.RequiredCapability"/> that a
/// front-end with an untrusted caller would check.
/// </para>
/// </summary>
public interface IConsoleCommandDispatcher
{
    /// <summary>Every command this dispatcher understands, in <c>help</c> order.</summary>
    IReadOnlyList<ConsoleCommandDescriptor> Commands { get; }

    /// <summary>
    /// Resolves a command word (or alias) to its descriptor.
    /// </summary>
    /// <returns><see langword="null"/> when no command answers to <paramref name="name"/>.</returns>
    ConsoleCommandDescriptor? Find(string name);

    /// <summary>
    /// Parses and runs <paramref name="input"/>, writing every line the command produces to
    /// <paramref name="write"/>.
    /// </summary>
    /// <returns>
    ///     <see langword="false"/> when the input named no known command — the caller decides
    ///     whether that is worth reporting.
    /// </returns>
    Task<bool> ExecuteAsync(string input, Action<string> write, CancellationToken ct);
}
