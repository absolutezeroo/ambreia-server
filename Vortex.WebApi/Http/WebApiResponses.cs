// Where a DTO lives, because the API has forty of them across six files and the rule had never been
// written down — which is how a file like this one becomes a drawer:
//
//   a shape a SERVICE returns          lives with its service interface
//                                      (ProfileUser, RoomSummary, PlayerPurse, ArticleFeed …)
//   a shape only the ENDPOINT builds   lives here
//                                      (LoginResponse, SsoTicketResponse, HealthResponse …)
//
// The test is one question: could this record exist without an HTTP route? `PlayerProfile` is what
// reading a profile means and would survive the website being deleted, so it belongs to the service.
// `LoginResponse` is "did that POST leave you with an avatar to pick" — a fact about one endpoint's
// answer, nothing else.
//
// One record bends it on purpose. `TwoFactorEnrolmentResponse` mirrors `MfaEnrolment`, which the
// service already returns, rather than exposing it: that type is shared with the dashboard, and a
// field the dashboard needs one day would otherwise land in the website's generated types by
// accident. A copy of two strings is cheaper than that coupling.

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
/// <c>{}</c> is a real answer here and not an oversight: logout, avatar selection and the new-user
/// room step all say "done, nothing to tell you". It has a name so a handler can declare it.
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

/// <summary>How many sessions the password change ended — the caller's own included.</summary>
public sealed record PasswordChangeResponse(int SessionsRevoked);

/// <summary>
/// The address the link went to, echoed as habbo.com's site shows it back ("un e-mail a été envoyé
/// à …"). The same answer whether or not an account uses it.
/// </summary>
public sealed record ForgotPasswordResponse(string Email);

/// <summary>The account id a registration created.</summary>
public sealed record RegistrationResponse(int Id);

/// <summary>The single-use ticket the client trades for a connection.</summary>
public sealed record SsoTicketResponse(string SsoToken);

/// <summary>Whether a name is free, echoing the name that was asked about.</summary>
public sealed record NameCheckResponse(string Name, bool Valid);

/// <summary>
/// The preferences this hotel stores for the selected avatar. habbo.com's own privacy form carries
/// six more — online status, follow, friend requests, the newsletter, the GDPR export — and every
/// one of them would need a surface this emulator does not have: hiding an online status means
/// hiding it on the game socket too, and a newsletter means an outbound mail path. One field is what
/// can be honoured, so one field is what is offered.
/// </summary>
public sealed record PlayerPreferencesResponse(bool ProfileVisible);

/// <summary>
/// The address the account signs in with. <paramref name="Verified"/> is always false and says so
/// rather than being left out: this hotel has no way to send to an address, so none has ever been
/// confirmed, and the website shows that state instead of a "resend verification" button that would
/// do nothing.
/// </summary>
public sealed record AccountEmailResponse(string Email, bool Verified);

/// <summary>
/// Whether the account's safety lock is on. While it is, the account cannot spend — no catalog
/// purchase, no marketplace — and the server enforces that, not only the client.
/// </summary>
public sealed record SafetyLockResponse(bool Locked);

/// <summary>
/// The account's protection, as both the settings page and the challenge dialog need it.
/// </summary>
/// <param name="Configured">Whether the account has security questions at all.</param>
/// <param name="Question1">
/// The first question's NUMBER, 1 to 9, or 0 when there are none. Only the number travels: the
/// wording is <c>IDENTITY_SAFETYQUESTION_&lt;n&gt;</c> in whichever language the reader is using, so
/// freezing a sentence here would pin the account to one language.
/// </param>
/// <param name="Question2">The second question's number, or 0.</param>
/// <param name="Trusted">
/// Whether THIS session has cleared the challenge. Always true when there are no questions. The
/// site reads it to know a risky action will be refused before it tries.
/// </param>
/// <param name="Locked">
/// Whether the safety lock is on — the same flag <c>/api/user/safetylock</c> reports, repeated here
/// so the page can say in one call what answering would lift.
/// </param>
public sealed record SafetyQuestionsResponse(
    bool Configured,
    int Question1,
    int Question2,
    bool Trusted,
    bool Locked
);

/// <summary>How many trusted locations were forgotten.</summary>
public sealed record TrustedLocationsResetResponse(int Forgotten);

/// <summary>Whether the account has a confirmed second factor.</summary>
public sealed record TwoFactorStatusResponse(bool Enabled);

/// <summary>
/// A secret and the <c>otpauth://</c> URI an authenticator reads, neither of them stored yet —
/// enrolment is two steps precisely so that someone who walks away from the dialog has not locked
/// themselves out of their own account.
/// </summary>
public sealed record TwoFactorEnrolmentResponse(string Secret, string Uri);

/// <summary>
/// The name the new-user step assigned. A name already taken is a 409 carrying
/// <see cref="ApiErrorResponse"/> — the site's own comment claimed it was a 200 with an error in the
/// body, which is not what this API has ever done.
/// </summary>
public sealed record NameSelectResponse(string Name);

/// <summary>
/// How many notifications the read swept up. Answered rather than an empty 200 so the page can
/// zero its own counter without refetching the list it is already showing.
/// </summary>
public sealed record NotificationsReadResponse(int Read);
