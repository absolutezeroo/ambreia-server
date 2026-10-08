namespace Vortex.WebApi.Http.Responses;

/// <summary>
/// The name the new-user step assigned. A name already taken is a 409 carrying
/// <see cref="ApiErrorResponse"/> — the site's own comment claimed it was a 200 with an error in the
/// body, which is not what this API has ever done.
/// </summary>
public sealed record NameSelectResponse(string Name);
