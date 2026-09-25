using System;
using System.Buffers.Text;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vortex.Database.Context;
using Vortex.Primitives.Authentication;
using Vortex.WebApi.Configuration;

namespace Vortex.WebApi.Services;

/// <remarks>
/// <para>
/// <b>The token is stateless.</b> It is <c>accountId:expiry</c>, signed with HMAC-SHA256 keyed by the
/// account's CURRENT password hash. That buys three things without a table or a configured secret:
/// nobody can forge one without reading the database (the hash carries a random salt); it expires
/// on its own; and it is single-use, because using it changes the hash that signed it — as does any
/// other password change in the meantime.
/// </para>
/// <para>
/// <b>The link is built from <see cref="WebApiConfig.PublicSiteUrl"/>, never from the request.</b>
/// The Host header is the caller's to write: taking the site's address from it would let anyone
/// request a reset for someone else with <c>Host: attacker.example</c> and have a valid token mailed
/// to them inside a link to the attacker's site. So no configured site URL means no mail.
/// </para>
/// </remarks>
public sealed class WebApiPasswordResetService(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    IAccountPasswordService passwords,
    IPasswordResetMailer mailer,
    IOptions<WebApiConfig> options,
    ILogger<WebApiPasswordResetService> logger
) : IWebApiPasswordResetService
{
    /// <summary>How long a mailed link works. habbo.com does not say; an hour is the usual figure.</summary>
    internal static readonly TimeSpan LIFETIME = TimeSpan.FromHours(1);

    private readonly WebApiConfig _config = options.Value;

    public async Task<bool> RequestAsync(string email, CancellationToken ct)
    {
        if (!_config.Smtp.IsConfigured || string.IsNullOrWhiteSpace(_config.PublicSiteUrl))
        {
            logger.LogWarning(
                "Password reset requested, but no mail server or PublicSiteUrl is configured"
            );
            return false;
        }

        string normalized = email.Trim().ToLowerInvariant();

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        var account = await db
            .PlayerAccounts.AsNoTracking()
            .Where(a => a.Email == normalized)
            .Select(a => new { a.Id, a.PasswordHash })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (account is null)
        {
            return true;
        }

        string token = CreateToken(
            account.Id,
            account.PasswordHash,
            DateTimeOffset.UtcNow + LIFETIME
        );
        string link = $"{_config.PublicSiteUrl.TrimEnd('/')}/reset-password/{token}";

        // Not awaited: how long the mail server takes would otherwise tell the caller whether the
        // address has an account, which the identical answer above is careful not to.
        _ = SendAsync(normalized, link, account.Id);

        return true;
    }

    public async Task<PasswordResetOutcome> ResetAsync(
        string token,
        string password,
        CancellationToken ct
    )
    {
        if (!TryReadToken(token, out int accountId, out DateTimeOffset expires, out _))
        {
            return PasswordResetOutcome.InvalidToken;
        }

        if (expires < DateTimeOffset.UtcNow)
        {
            return PasswordResetOutcome.InvalidToken;
        }

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        string? hash = await db
            .PlayerAccounts.AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => a.PasswordHash)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (hash is null || !IsSignedBy(token, hash))
        {
            return PasswordResetOutcome.InvalidToken;
        }

        PasswordChangeResult result = await passwords
            .ResetAsync(accountId, password, ct)
            .ConfigureAwait(false);

        if (result.Outcome == PasswordChangeOutcome.TooShort)
        {
            return PasswordResetOutcome.TooShort;
        }

        if (!result.Succeeded)
        {
            return PasswordResetOutcome.InvalidToken;
        }

        logger.LogInformation(
            "Account {AccountId} reset its password by mail; {Sessions} session(s) revoked",
            accountId,
            result.SessionsRevoked
        );

        return PasswordResetOutcome.Changed;
    }

    private async Task SendAsync(string to, string link, int accountId)
    {
        try
        {
            await mailer.SendAsync(to, link, CancellationToken.None).ConfigureAwait(false);
            logger.LogInformation("Password reset link sent for account {AccountId}", accountId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Password reset mail for account {AccountId} failed", accountId);
        }
    }

    internal static string CreateToken(int accountId, string passwordHash, DateTimeOffset expires)
    {
        byte[] payload = Encoding.UTF8.GetBytes(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{accountId}:{expires.ToUnixTimeSeconds()}"
            )
        );

        return $"{Base64Url.EncodeToString(payload)}.{Base64Url.EncodeToString(Sign(payload, passwordHash))}";
    }

    private static bool TryReadToken(
        string token,
        out int accountId,
        out DateTimeOffset expires,
        out byte[] payload
    )
    {
        accountId = 0;
        expires = default;
        payload = [];

        string[] parts = token.Split('.');

        if (parts.Length != 2)
        {
            return false;
        }

        try
        {
            payload = Base64Url.DecodeFromChars(parts[0]);
        }
        catch (FormatException)
        {
            return false;
        }

        string[] fields = Encoding.UTF8.GetString(payload).Split(':');

        if (
            fields.Length != 2
            || !int.TryParse(
                fields[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out accountId
            )
            || !long.TryParse(
                fields[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long unix
            )
        )
        {
            return false;
        }

        expires = DateTimeOffset.FromUnixTimeSeconds(unix);

        return true;
    }

    private static bool IsSignedBy(string token, string passwordHash)
    {
        if (!TryReadToken(token, out _, out _, out byte[] payload))
        {
            return false;
        }

        byte[] signature;

        try
        {
            signature = Base64Url.DecodeFromChars(token.AsSpan(token.IndexOf('.') + 1));
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(signature, Sign(payload, passwordHash));
    }

    private static byte[] Sign(byte[] payload, string passwordHash) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(passwordHash), payload);
}
