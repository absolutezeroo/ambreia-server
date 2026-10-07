using System;
using System.Collections.Generic;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Authentication;

namespace Vortex.Players.Authentication;

/// <summary>
/// Validates the authentication options at startup.
/// <para>
/// Outside Development a placeholder <see cref="AuthenticationConfig.IpHashSecret" /> makes the
/// hashed IPs recorded in auth events trivially reversible — the hash is only as secret as its key,
/// and the defaults are in the repository. This carries the check that used to live inline in
/// <c>AuthenticationModule.ConfigureServices</c>, so every sensitive option is now validated the
/// same way (<c>ValidateOnStart</c>) instead of one module hand-rolling its own fail-fast.
/// </para>
/// </summary>
public sealed class AuthenticationConfigValidator(IHostEnvironment environment)
    : IValidateOptions<AuthenticationConfig>
{
    /// <summary>Secrets shipped in the repo; useless as HMAC keys because everyone has them.</summary>
    internal static readonly string[] PLACEHOLDER_IP_HASH_SECRETS =
    [
        "local-dev-ip-hash-secret",
        "replace-with-a-development-secret",
        "replace-with-a-production-secret",
    ];

    public ValidateOptionsResult Validate(string? name, AuthenticationConfig options)
    {
        List<string> failures = [];

        if (!environment.IsDevelopment())
        {
            if (
                string.IsNullOrWhiteSpace(options.IpHashSecret)
                || Array.IndexOf(PLACEHOLDER_IP_HASH_SECRETS, options.IpHashSecret) >= 0
            )
            {
                failures.Add(
                    $"'{AuthenticationConfig.SECTION_NAME}:{nameof(AuthenticationConfig.IpHashSecret)}' "
                        + "is unset or still a placeholder default. The IP addresses hashed into auth "
                        + "events would be reversible by anyone with the repository. Set a real "
                        + "secret via the VORTEX__Vortex__Authentication__IpHashSecret environment "
                        + "variable or user-secrets before running outside Development."
                );
            }
        }

        // Neither bound set is the one combination that lets an observed ticket be replayed for as
        // long as somebody keeps using it: the sliding branch pushes the expiry forward on EVERY
        // successful use, so the TTL never runs out, and the absolute cap that would stop it is off
        // by default. Each option is individually reasonable -- single-use is deliberately off for
        // CMS integrations that reuse one ticket across reconnects -- which is exactly why nothing
        // noticed that turning both off leaves no bound at all (SEC-15).
        //
        // Development is exempt so a fresh clone still runs; anywhere else this is a refusal to
        // start rather than a warning, because the failure it prevents is silent account takeover.
        if (
            !environment.IsDevelopment()
            && !options.TicketSingleUse
            && options.TicketAbsoluteLifetimeSeconds is null
        )
        {
            failures.Add(
                $"'{AuthenticationConfig.SECTION_NAME}' bounds ticket replay in two ways and both are "
                    + $"off: '{nameof(AuthenticationConfig.TicketSingleUse)}' is false and "
                    + $"'{nameof(AuthenticationConfig.TicketAbsoluteLifetimeSeconds)}' is unset. Every "
                    + $"use slides the {nameof(AuthenticationConfig.TicketTtlSeconds)} expiry forward, "
                    + "so an observed ticket stays valid for as long as it keeps being replayed. Set "
                    + $"'{nameof(AuthenticationConfig.TicketAbsoluteLifetimeSeconds)}' to cap the total "
                    + "lifetime (this keeps working for CMS integrations that reuse a ticket across "
                    + $"reconnects), or set '{nameof(AuthenticationConfig.TicketSingleUse)}' to true to "
                    + "consume it on first use."
            );
        }

        if (options.TicketTtlSeconds < 0)
        {
            failures.Add(
                $"'{AuthenticationConfig.SECTION_NAME}:{nameof(AuthenticationConfig.TicketTtlSeconds)}' "
                    + $"must not be negative (got {options.TicketTtlSeconds}). Use 0 to accept "
                    + "null-expiry tickets unconditionally, or a positive number of seconds."
            );
        }

        if (options.TicketAbsoluteLifetimeSeconds is < 1)
        {
            failures.Add(
                $"'{AuthenticationConfig.SECTION_NAME}:{nameof(AuthenticationConfig.TicketAbsoluteLifetimeSeconds)}' "
                    + $"must be a positive number of seconds when set (got {options.TicketAbsoluteLifetimeSeconds}). "
                    + "Leave it unset to disable the cap."
            );
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
