// Where a DTO lives, so that a file like this one does not become a drawer:
//
//   a shape a SERVICE returns          lives with its service interface
//                                      (AvatarInfo)
//   a shape only the ENDPOINT builds   lives here
//                                      (LoginResponse, SsoTicketResponse, HealthResponse …)
//
// The test is one question: could this record exist without an HTTP route? `AvatarInfo` is what
// listing an account's avatars means and would survive the website being deleted, so it belongs to
// the service. `LoginResponse` is "did that POST leave you with an avatar to pick" — a fact about
// one endpoint's answer, nothing else.

namespace Vortex.WebApi.Http;

/// <summary>
/// The refusal body every failing endpoint answers with: <c>{"error":"&lt;code&gt;"}</c>, and never
/// a message. The site turns the code into French in <c>src/lib/api.ts</c>, which is where the
/// hotel's own wording belongs; a server-side sentence would be a second copy of it in the wrong
/// language.
/// </summary>
/// <remarks>
/// It exists as a record rather than the anonymous object it used to be so the endpoints can DECLARE
/// it — <c>.Produces&lt;ApiErrorResponse&gt;(404)</c> — which is what puts the failure shape in the
/// OpenAPI document, and from there into the site's generated types. An anonymous object is invisible
/// to Swashbuckle.
/// </remarks>
public sealed record ApiErrorResponse(string Error);

/// <summary>
/// The success bodies. Records rather than the anonymous objects these used to be, for the same
/// reason as <see cref="ApiErrorResponse"/>: an anonymous type cannot be named in a handler's
/// declared return type, so every one of these responses was absent from the OpenAPI document and
/// the website had to describe it by hand.
/// </summary>
/// <remarks>
/// <c>{}</c> is a real answer here and not an oversight: logout and avatar selection both say
/// "done, nothing to tell you". It has a name so a handler can declare it.
/// </remarks>
public sealed record EmptyResponse
{
    public static readonly EmptyResponse Instance = new();
}

/// <summary>The liveness probe's answer.</summary>
public sealed record HelloResponse(string Status);

/// <summary>
/// The readiness probe's answer, at 200 when the hotel is healthy or degraded and 503 when the
/// database is unreachable.
/// </summary>
public sealed record HealthResponse(
    string Status,
    string Database,
    System.Collections.Generic.IReadOnlyCollection<string> DegradedServices
);

/// <summary>
/// <c>requiresOnboarding</c> is "this account owns no avatar yet", which is what sends the visitor
/// to the avatar creation rather than to the hotel.
/// </summary>
public sealed record LoginResponse(bool RequiresOnboarding);

/// <summary>The account id a registration created.</summary>
public sealed record RegistrationResponse(int Id);

/// <summary>The short-lived ticket the client trades for a connection.</summary>
public sealed record SsoTicketResponse(string SsoToken);

/// <summary>Whether a name is free, echoing the name that was asked about.</summary>
public sealed record NameCheckResponse(string Name, bool Valid);

/// <summary>
/// The name the new-user step assigned. A name already taken is a 409 carrying
/// <see cref="ApiErrorResponse"/> — the site's own comment claimed it was a 200 with an error in the
/// body, which is not what this API has ever done.
/// </summary>
public sealed record NameSelectResponse(string Name);
