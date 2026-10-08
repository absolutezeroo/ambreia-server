using System.Net.Mail;

namespace Vortex.WebApi.Http.Validation;

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
