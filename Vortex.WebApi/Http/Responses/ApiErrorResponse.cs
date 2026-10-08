namespace Vortex.WebApi.Http.Responses;

/// <summary>
/// The refusal body every failing endpoint answers with: <c>{"error":"&lt;code&gt;"}</c>, and never
/// a message. The site turns the code into French in <c>src/lib/api.ts</c>, which is where the
/// hotel's own wording belongs; a server-side sentence would be a second copy of it in the wrong
/// language.
/// </summary>
/// <remarks>
/// <para>
/// It exists as a record rather than the anonymous object it used to be so the endpoints can DECLARE
/// it — <c>.Produces&lt;ApiErrorResponse&gt;(404)</c> — which is what puts the failure shape in the
/// OpenAPI document, and from there into the site's generated types. An anonymous object is invisible
/// to Swashbuckle.
/// </para>
/// <para>
/// Where a DTO lives: a shape only the ENDPOINT builds lives in this folder; a shape a SERVICE
/// returns lives with its service interface. The test is one question: could the
/// record exist without an HTTP route?
/// </para>
/// </remarks>
public sealed record ApiErrorResponse(string Error);
