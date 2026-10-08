using Vortex.Primitives.Players;

namespace Vortex.WebApi.Http.Requests;

public sealed record NameSelectRequest(string? Name, int PlayerId)
{
    public bool IsValid => NameShape.IsWellFormed(Name) && PlayerId > 0;
}
