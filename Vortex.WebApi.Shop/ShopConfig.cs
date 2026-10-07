using System;
using System.Collections.Generic;

namespace Vortex.WebApi.Shop;

/// <summary>
/// What an operator has to decide before the hotel may take money.
/// </summary>
/// <remarks>
/// The shop is OFF by default and stays off until <see cref="Enabled"/> is set and at least one
/// provider has a secret. That default is deliberate: a shop that switched itself on with a blank
/// signing key would accept any webhook body anyone posted at it, and the failure would look exactly
/// like the feature working.
/// </remarks>
public sealed class ShopConfig
{
    public const string SECTION_NAME = "Vortex:Shop";

    /// <summary>
    /// The minimum length of a webhook signing secret. Below it the provider is treated as
    /// unconfigured and its webhook answers as if it did not exist.
    /// </summary>
    public const int MinimumSecretLength = 32;

    public bool Enabled { get; set; }

    /// <summary>
    /// Which provider a new order is opened against. Must be the <c>Key</c> of a registered
    /// <c>IShopPaymentProvider</c> with a secret, or no order can be opened at all.
    /// </summary>
    public string Provider { get; set; } = "manual";

    /// <summary>
    /// The HMAC-SHA256 signing secret per provider key. Not a password: it is compared byte for
    /// byte, so it wants entropy, not memorability.
    /// </summary>
    /// <remarks>
    /// A provider with no entry here cannot open orders and its webhook answers 404. There is no
    /// "unsigned" mode, not even for development: the one place a shortcut like that gets left in is
    /// production.
    /// </remarks>
    public Dictionary<string, string> ProviderSecrets { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// How far out of date a signed notification may be. Bounds the window in which a captured
    /// request can be replayed by someone who never had the secret.
    /// </summary>
    public int WebhookToleranceSeconds { get; set; } = 300;

    /// <summary>
    /// How many unpaid orders one account may have at once. Not a security control — an unpaid order
    /// costs nobody anything — but it stops a loop from filling the table, and a player with fifty
    /// half-started payments is confused rather than served.
    /// </summary>
    public int MaximumOpenOrders { get; set; } = 10;
}
