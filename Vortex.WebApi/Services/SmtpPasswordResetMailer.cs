using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Vortex.WebApi.Configuration;

namespace Vortex.WebApi.Services;

/// <summary>
/// The forgotten-password mail, over <see cref="WebApiConfig.Smtp"/>. Plain text and French, the
/// hotel's language; the link is the whole message.
/// </summary>
/// <remarks>
/// ponytail: <c>System.Net.Mail.SmtpClient</c> rather than a mail library — one mail type, sent
/// rarely. Its ceiling is STARTTLS only (no implicit TLS on 465) and one connection per mail; move to
/// MailKit if either ever matters.
/// </remarks>
internal sealed class SmtpPasswordResetMailer(IOptions<WebApiConfig> options) : IPasswordResetMailer
{
    private readonly WebApiConfig _config = options.Value;

    public async Task SendAsync(string to, string link, CancellationToken ct)
    {
        WebApiConfig.SmtpOptions smtp = _config.Smtp;
        string hotel = string.IsNullOrWhiteSpace(_config.SiteName) ? "l'hôtel" : _config.SiteName;

        using SmtpClient client = new(smtp.Host, smtp.Port) { EnableSsl = smtp.EnableSsl };

        if (!string.IsNullOrEmpty(smtp.Username))
        {
            client.Credentials = new NetworkCredential(smtp.Username, smtp.Password);
        }

        using MailMessage message = new(smtp.From, to)
        {
            Subject = $"{hotel} : réinitialise ton mot de passe",
            Body =
                $"Quelqu'un a demandé à réinitialiser le mot de passe de ton compte {hotel}.\n\n"
                + $"Pour en choisir un nouveau, ouvre ce lien (valable une heure) :\n{link}\n\n"
                + "Si ce n'est pas toi, ignore ce message : ton mot de passe ne change pas.",
        };

        await client.SendMailAsync(message, ct).ConfigureAwait(false);
    }
}
