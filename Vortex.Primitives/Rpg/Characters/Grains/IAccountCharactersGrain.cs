using System.Threading.Tasks;
using Orleans;

namespace Vortex.Primitives.Rpg.Characters.Grains;

/// <summary>
/// The characters of one account (spec F3a §5.3), keyed by account id: the character screen's
/// list, creation and class choice. Each call answers the account's socket itself, through
/// <c>IAccountSessionGrain</c>; one activation per account is what serializes two creates.
/// </summary>
public interface IAccountCharactersGrain : IGrainWithIntegerKey
{
    /// <summary>The character list, then the class list; an old avatar without a character is
    /// first given the starter class.</summary>
    public Task OpenAsync();

    /// <summary>The character list.</summary>
    public Task ListAsync();

    /// <summary>A new avatar in the starter class: a result, then the list when it worked.</summary>
    public Task CreateAsync(string name, string figure, string gender);

    /// <summary>Leaves the starter class for <paramref name="classId"/>: a result, then the list
    /// when it worked.</summary>
    public Task ChooseClassAsync(int playerId, string classId);

    /// <summary>Whether <paramref name="playerId"/> is a live avatar of this account; when not,
    /// the socket is told <c>character.not_owned</c> for entering the world.</summary>
    public Task<bool> OwnsAsync(int playerId);
}
