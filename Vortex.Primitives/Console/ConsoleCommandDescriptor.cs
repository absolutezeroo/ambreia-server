using System.Collections.Generic;

namespace Vortex.Primitives.Console;

/// <summary>
/// One operator command: the word that invokes it, how to call it, and the capability it declares.
/// </summary>
/// <param name="Name">The lowercase word that selects this command.</param>
/// <param name="Usage">A one-line usage string, shown by <c>help</c>.</param>
/// <param name="Description">What the command does, shown by <c>help</c>.</param>
/// <param name="RequiredCapability">
///     The capability this command declares, or <see langword="null"/> for none. Declared, not
///     enforced: the stdin loop is the only front-end and its operator runs every command.
/// </param>
/// <param name="Aliases">Alternate words that select the same command.</param>
public sealed record ConsoleCommandDescriptor(
    string Name,
    string Usage,
    string Description,
    string? RequiredCapability = null,
    IReadOnlyList<string>? Aliases = null
);
