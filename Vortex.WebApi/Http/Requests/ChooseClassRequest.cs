namespace Vortex.WebApi.Http.Requests;

/// <summary>The class an avatar without one takes, once (spec Personnage §8).</summary>
public sealed record ChooseClassRequest(string? ClassId)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(ClassId);
}
