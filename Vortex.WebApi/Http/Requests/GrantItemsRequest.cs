using Vortex.Database.Entities.Rpg;

namespace Vortex.WebApi.Http.Requests;

/// <summary>An item to grant a character from the admin route (spec Objets §10). The key is the
/// caller's: a request replayed with it grants nothing more.</summary>
public sealed record GrantItemsRequest(string? GrantKey, string? DefinitionId, int Quantity)
{
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(GrantKey)
        && GrantKey.Length <= RpgItemGrantEntity.GrantKeyLength
        && !string.IsNullOrWhiteSpace(DefinitionId)
        && Quantity >= 1;
}
