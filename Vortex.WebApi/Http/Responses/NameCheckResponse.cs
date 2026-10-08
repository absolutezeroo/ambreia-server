namespace Vortex.WebApi.Http.Responses;

/// <summary>Whether a name is free, echoing the name that was asked about.</summary>
public sealed record NameCheckResponse(string Name, bool Valid);
