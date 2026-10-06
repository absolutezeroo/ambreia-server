using Vortex.Primitives.Feed;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums;

namespace Vortex.WebApi.Http;

/// <summary>
/// Strongly typed request bodies for the web API POST endpoints. Each record exposes a
/// <c>IsValid</c> predicate so the endpoint can reject malformed payloads with a clean 400 before
/// any service work happens, replacing the hand-rolled null/whitespace checks of the old listener.
/// </summary>
/// <remarks>
/// Every one of these belongs to exactly one route, which is what keeps the file a vocabulary rather
/// than a drawer: a request body has no meaning away from the endpoint that reads it, so there is
/// nowhere else for it to live. The question only bites on the ANSWERS — see the note at the top of
/// <c>WebApiResponses.cs</c> for the rule that decides between here and a service interface.
///
/// <para>
/// `IsValid` is shape, never policy. It asks whether the endpoint can proceed at all — a field
/// present, a name that could be a habbo name — and never whether the caller is allowed: that answer
/// needs the database and belongs to the service, which refuses again at the sink.
/// </para>
/// </remarks>
public sealed record LoginRequest(string? Email, string? Password, string? Code = null)
{
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password);
}

/// <summary>
/// A password change for the signed-in account. The current password is required even though the
/// caller holds a session cookie: a stolen cookie must not be enough to take the account.
/// </summary>
public sealed record ChangePasswordRequest(
    string? CurrentPassword,
    string? NewPassword,
    string? Code
)
{
    public bool IsValid =>
        !string.IsNullOrEmpty(CurrentPassword) && !string.IsNullOrEmpty(NewPassword);
}

/// <summary>habbo.com's <c>forgotPassword/send</c> body: <c>{ email }</c>.</summary>
public sealed record ForgotPasswordRequest(string? Email)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(Email);
}

/// <summary>habbo.com's <c>forgotPassword/changePassword</c> body: <c>{ token, password }</c>.</summary>
public sealed record ResetPasswordRequest(string? Token, string? Password)
{
    public bool IsValid => !string.IsNullOrEmpty(Token) && !string.IsNullOrEmpty(Password);
}

public sealed record RegisterRequest(string? Email, string? Password, string? PasswordRepeated)
{
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Email)
        && !string.IsNullOrWhiteSpace(Password)
        // The confirmation field is optional (some clients omit it), but when supplied it
        // must match the password.
        && (PasswordRepeated is null || Password == PasswordRepeated);
}

/// <remarks>
/// <see cref="Name"/> is optional: the client posts this route with an empty name right after
/// registering, and <c>WebApiPlayerService.CreateAvatarAsync</c> assigns a placeholder that the
/// client's onboarding step replaces. Everything else about the request is unchanged.
/// </remarks>
public sealed record CreateAvatarRequest(string? Name, string? Figure, string? Gender)
{
    // Blank stays valid — that is the registration path, which wants the placeholder. A name that IS
    // supplied has to be one the player could have picked in-game; WebApiPlayerService refuses it
    // again at the sink, this is only so the caller gets a 400 rather than a 409 about a name clash
    // that is not what went wrong.
    public bool IsValid => string.IsNullOrWhiteSpace(Name) || NameShape.IsWellFormed(Name);
}

public sealed record SelectAvatarRequest(string? UniqueId)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(UniqueId);
}

/// <summary>
/// Changes the address the account signs in with. The current password is required even though the
/// caller holds a session cookie — moving the address is how an account is taken for good — and the
/// code comes along when the account has a second factor.
/// </summary>
public sealed record ChangeEmailRequest(string? CurrentPassword, string? Email, string? Code)
{
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(CurrentPassword) && !string.IsNullOrWhiteSpace(Email);
}

/// <summary>
/// Throws or lifts the account's safety lock. The password is required in BOTH directions: a thief
/// who could lock the account without it would have a way to grief its owner.
/// </summary>
public sealed record SafetyLockRequest(bool? Locked, string? CurrentPassword, string? Code)
{
    public bool IsValid => Locked is not null && !string.IsNullOrWhiteSpace(CurrentPassword);
}

/// <summary>
/// Which choice the caller is taking in a feed poll.
/// </summary>
/// <remarks>
/// The choice id alone, with the poll named by the route. The service checks the two agree, because
/// on its own this is a number a browser sends and it would otherwise let a vote land on any poll
/// in the hotel. Re-sending the same choice is a no-op and sending a different one MOVES the vote,
/// so a retry can never turn one player into two voices.
/// </remarks>
public sealed record FeedVoteRequest(int ChoiceId);

/// <summary>
/// Whether the caller wants this reaction on this post, or not.
/// </summary>
/// <remarks>
/// The WANTED STATE, not a toggle: a toggle sent twice — a flaky connection retrying, a double
/// tap — lands on the opposite of what the reader pressed, and they cannot tell which of the two
/// happened. <see cref="Kind"/> separates a repost from a favourite, which are independent — the
/// unique index behind them spans the kind too.
/// </remarks>
public sealed record FeedReactionRequest(FeedReactionKind Kind, bool On);

/// <summary>
/// Sets, or replaces, the account's two security questions. The password is required because these
/// are a way INTO the account: a thief holding a session who could quietly install questions of
/// their own would own the recovery path as well as the session.
/// </summary>
public sealed record SafetyQuestionsSaveRequest(
    int? Question1,
    string? Answer1,
    int? Question2,
    string? Answer2,
    string? CurrentPassword,
    string? Code
)
{
    public bool IsValid =>
        Question1 is not null
        && Question2 is not null
        && !string.IsNullOrWhiteSpace(Answer1)
        && !string.IsNullOrWhiteSpace(Answer2)
        && !string.IsNullOrWhiteSpace(CurrentPassword);
}

/// <summary>Removes the account's security questions, against the current password.</summary>
public sealed record SafetyQuestionsDisableRequest(string? CurrentPassword, string? Code)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(CurrentPassword);
}

/// <summary>
/// Answers the challenge. No password, and that is the point: the visitor is proving they own a
/// session the server is not sure about, and is asked for the one secret someone holding the
/// password alone would not have.
/// </summary>
/// <param name="Trust">
/// habbo.com's own radio pair — "débloquer juste pour cette fois" against "enregistrer cette
/// connexion comme étant de confiance". False trusts this SESSION only; true also remembers the
/// place, so the next sign-in from it is not challenged.
/// </param>
public sealed record SafetyQuestionsUnlockRequest(string? Answer1, string? Answer2, bool? Trust)
{
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Answer1) && !string.IsNullOrWhiteSpace(Answer2);
}

/// <summary>
/// Saves the selected avatar's preferences. Absent means unchanged, which is what lets the site post
/// the one field it can save out of a form habbo.com fills with seven.
/// </summary>
public sealed record SavePreferencesRequest(bool? ProfileVisible)
{
    public bool IsValid => ProfileVisible is not null;
}

/// <summary>
/// Confirms a second factor. <see cref="Secret"/> is the one
/// <c>/api/user/twofactor/startregistration</c> handed out and stored nowhere: the account gets it
/// only if <see cref="Code"/> proves an authenticator already holds it.
/// </summary>
public sealed record TwoFactorEnableRequest(string? Secret, string? Code)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(Secret) && !string.IsNullOrWhiteSpace(Code);
}

/// <summary>
/// Removes a second factor. The code is required — the caller holds a session cookie, and a
/// hijacked session must not be able to take the factor off.
/// </summary>
public sealed record TwoFactorDisableRequest(string? Code)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(Code);
}

public sealed record NameRequest(string? Name)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(Name);
}

public sealed record NameSelectRequest(string? Name, int PlayerId)
{
    public bool IsValid => NameShape.IsWellFormed(Name) && PlayerId > 0;
}

/// <summary>
/// The player-name shape rule, borrowed from the one place that owns it so the HTTP routes and the
/// in-game rename cannot drift apart. Uniqueness is still storage's call, not this.
/// </summary>
internal static class NameShape
{
    public static bool IsWellFormed(string? name) =>
        NameChangePolicy.Validate(
            name,
            NameChangePolicy.DEFAULT_MIN_LENGTH,
            NameChangePolicy.DEFAULT_MAX_LENGTH
        ) == NameChangeResultCode.Ok;
}

public sealed record SaveFigureRequest(string? FigureString, string? Gender, int PlayerId)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(FigureString) && PlayerId > 0;
}

/// <summary>
/// A bug report written by a player. Everything but <see cref="Message" /> is context the client
/// collects on its own, because a report that says only "it's broken" costs more to chase than it
/// saves — and a player will not think to mention which room they were in.
/// </summary>
/// <remarks>
/// The limits are deliberate and enforced here rather than trusted from the client. The audit
/// payload is a JSON column and the reports arrive over an authenticated but public route, so an
/// unbounded field is an unbounded row: <see cref="MAX_MESSAGE" /> is generous for a description
/// and small enough that a thousand of them cost nothing, and <see cref="MAX_CONTEXT" /> caps each
/// piece of collected context.
/// </remarks>
public sealed record SubmitReportRequest(
    string? Message,
    string? Page,
    string? ClientVersion,
    int? RoomId,
    string? Console
)
{
    public const int MAX_MESSAGE = 2000;
    public const int MAX_CONTEXT = 500;

    /// <summary>The tail of the browser console, when the client was able to capture one.</summary>
    public const int MAX_CONSOLE = 4000;

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Message)
        && Message.Length <= MAX_MESSAGE
        && (Page is null || Page.Length <= MAX_CONTEXT)
        && (ClientVersion is null || ClientVersion.Length <= MAX_CONTEXT)
        && (Console is null || Console.Length <= MAX_CONSOLE);
}

/// <summary>
/// Buying one thing from the shop. A product code, and deliberately nothing else.
/// </summary>
/// <remarks>
/// There is no amount field and no price field, and that absence is the point: the server reads both
/// off the product row and snapshots them onto the order. A request body that could carry a price is
/// a request body somebody will eventually edit, and no amount of server-side checking makes a field
/// that should not exist safe.
/// </remarks>
public sealed record StartOrderRequest(string? ProductCode)
{
    /// <summary>Generous for a content id, and short enough that the lookup is never the attack.</summary>
    public const int MAX_CODE = 64;

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(ProductCode) && ProductCode.Length <= MAX_CODE;
}

/// <summary>A prepaid code, off the shop's second tab.</summary>
public sealed record RedeemVoucherRequest(string? Code)
{
    public const int MAX_CODE = 64;

    public bool IsValid => !string.IsNullOrWhiteSpace(Code) && Code.Length <= MAX_CODE;
}
