using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace Vortex.Crypto.Configuration;

/// <summary>
/// Fails startup on an unusable RSA handshake configuration instead of letting
/// <see cref="RsaService" /> throw a raw <see cref="FormatException" /> from
/// <c>new BigInteger(value, 16)</c> on the first connecting client — or, worse, letting the
/// committed placeholder keys through.
/// <para>
/// <c>KeySize</c> is the public exponent, <c>PublicKey</c> the modulus and <c>PrivateKey</c> the
/// private exponent, all hexadecimal (see <see cref="RsaService" />'s constructor). Nothing here
/// changes the cryptographic protocol the client speaks — it only rejects values that could never
/// have worked.
/// </para>
/// </summary>
public sealed class CryptoConfigValidator : IValidateOptions<CryptoConfig>
{
    /// <summary>
    /// Below this the modulus is too small to carry the client's single-block handshake payload and
    /// offers no meaningful security. The shipped keypair is 1024-bit.
    /// </summary>
    internal const int MIN_MODULUS_BITS = 512;

    private static readonly string[] PLACEHOLDER_MARKERS =
    [
        "CHANGE_ME",
        "REPLACE_ME",
        "YOUR_KEY",
        "EXAMPLE",
        "PLACEHOLDER",
        "TODO",
    ];

    public ValidateOptionsResult Validate(string? name, CryptoConfig options)
    {
        List<string> failures = [];

        ValidateHexField(
            failures,
            nameof(CryptoConfig.KeySize),
            options.KeySize,
            $"{CryptoConfig.SECTION_NAME}:{nameof(CryptoConfig.KeySize)}"
        );
        ValidateHexField(
            failures,
            nameof(CryptoConfig.PublicKey),
            options.PublicKey,
            $"{CryptoConfig.SECTION_NAME}:{nameof(CryptoConfig.PublicKey)}"
        );
        ValidateHexField(
            failures,
            nameof(CryptoConfig.PrivateKey),
            options.PrivateKey,
            $"{CryptoConfig.SECTION_NAME}:{nameof(CryptoConfig.PrivateKey)}"
        );

        // Only inspect the numeric shape once every field is known-parseable.
        if (failures.Count == 0)
        {
            if (!TryParseHex(options.KeySize, out System.Numerics.BigInteger exponent))
            {
                failures.Add(
                    $"'{CryptoConfig.SECTION_NAME}:{nameof(CryptoConfig.KeySize)}' is not a valid "
                        + "hexadecimal RSA public exponent."
                );
            }
            else if (exponent < 3 || exponent.IsEven)
            {
                failures.Add(
                    $"'{CryptoConfig.SECTION_NAME}:{nameof(CryptoConfig.KeySize)}' is the RSA public "
                        + $"exponent and must be an odd value of at least 3 (got {exponent}). The "
                        + "usual values are 3 or 10001 (65537)."
                );
            }

            if (!TryParseHex(options.PublicKey, out System.Numerics.BigInteger modulus))
            {
                failures.Add(
                    $"'{CryptoConfig.SECTION_NAME}:{nameof(CryptoConfig.PublicKey)}' is not a valid "
                        + "hexadecimal RSA modulus."
                );
            }
            else
            {
                long bits = modulus.GetBitLength();

                if (bits < MIN_MODULUS_BITS)
                {
                    failures.Add(
                        $"'{CryptoConfig.SECTION_NAME}:{nameof(CryptoConfig.PublicKey)}' is the RSA "
                            + $"modulus and is only {bits} bits; at least {MIN_MODULUS_BITS} are "
                            + "required. Generate a fresh 1024-bit keypair."
                    );
                }
            }
        }

        if (options.EnableServerToClientEncryption && string.IsNullOrWhiteSpace(options.PrivateKey))
        {
            failures.Add(
                $"'{CryptoConfig.SECTION_NAME}:{nameof(CryptoConfig.EnableServerToClientEncryption)}' "
                    + "is enabled but no private key is configured. The server cannot complete the "
                    + "handshake without one."
            );
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static void ValidateHexField(
        List<string> failures,
        string fieldName,
        string? value,
        string optionPath
    )
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add(
                $"'{optionPath}' is required and must be a hexadecimal value. Supply it via the "
                    + $"VORTEX__Vortex__Crypto__{fieldName} environment variable or user-secrets."
            );

            return;
        }

        foreach (string marker in PLACEHOLDER_MARKERS)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add(
                    $"'{optionPath}' is still the placeholder value shipped in appsettings.json. "
                        + $"Generate a real RSA keypair and supply it via the "
                        + $"VORTEX__Vortex__Crypto__{fieldName} environment variable or user-secrets."
                );

                return;
            }
        }

        foreach (char c in value)
        {
            if (!Uri.IsHexDigit(c))
            {
                failures.Add(
                    $"'{optionPath}' must contain only hexadecimal digits (0-9, a-f); found "
                        + $"'{c}'."
                );

                return;
            }
        }
    }

    private static bool TryParseHex(string value, out System.Numerics.BigInteger result)
    {
        // A leading "0" forces the unsigned interpretation: without it a value whose first hex digit
        // is >= 8 parses as a negative number.
        return System.Numerics.BigInteger.TryParse(
            "0" + value,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out result
        );
    }
}
