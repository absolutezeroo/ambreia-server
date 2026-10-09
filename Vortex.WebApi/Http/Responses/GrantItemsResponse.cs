namespace Vortex.WebApi.Http.Responses;

/// <summary>What the grant came to: <c>ok</c> or <c>duplicate_grant</c>, and how much went to the
/// bag and to parcels.</summary>
public sealed record GrantItemsResponse(string Result, int ToBag, int ToParcels);
