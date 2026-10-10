using Vortex.Database.Entities.Rpg;

namespace Vortex.WebApi.Http.Requests;

/// <summary>XP to grant a character from the admin route; the grain applies its own caps. The key
/// is the caller's: a request replayed with it grants nothing more.</summary>
public sealed record GrantXpRequest(int Amount, string? GrantKey)
{
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(GrantKey)
        && GrantKey.Length <= RpgXpGrantEntity.GrantKeyLength
        && Amount >= 1;
}
