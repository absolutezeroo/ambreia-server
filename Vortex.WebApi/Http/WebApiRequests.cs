using System.Net.Mail;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Avatar;
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

public sealed record RegisterRequest(string? Email, string? Password, string? PasswordRepeated)
{
    public bool IsValid =>
        EmailShape.IsWellFormed(Email)
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
    //
    // A look past the column width used to reach the database and come back as a 500.
    public bool IsValid =>
        (string.IsNullOrWhiteSpace(Name) || NameShape.IsWellFormed(Name))
        && (Figure is null || Figure.Length <= FigureString.MaxLength);
}

public sealed record SelectAvatarRequest(string? UniqueId)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(UniqueId);
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

/// <summary>
/// An address the account can be found by again: one mailbox, nothing around it, within the 254
/// characters SMTP allows (the column is wider). Anything else used to be stored as typed.
/// </summary>
internal static class EmailShape
{
    private const int MAX_LENGTH = 254;

    public static bool IsWellFormed(string? email)
    {
        string? trimmed = email?.Trim();

        return !string.IsNullOrEmpty(trimmed)
            && trimmed.Length <= MAX_LENGTH
            && MailAddress.TryCreate(trimmed, out MailAddress? address)
            && address.Address == trimmed;
    }
}
