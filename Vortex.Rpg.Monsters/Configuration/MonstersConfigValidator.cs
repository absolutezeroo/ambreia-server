using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Vortex.Rpg.Monsters.Configuration;

/// <summary>Refuses the start on a monsters setting that could not work (spec M1c §10), each problem
/// naming its key.</summary>
public sealed class MonstersConfigValidator : IValidateOptions<MonstersConfig>
{
    public ValidateOptionsResult Validate(string? name, MonstersConfig options)
    {
        IReadOnlyList<string> problems = options.Problems();

        return problems.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(problems);
    }
}
