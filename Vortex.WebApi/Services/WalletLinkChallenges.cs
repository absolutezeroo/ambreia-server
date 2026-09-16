using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Vortex.WebApi.Services;

/// <summary>
/// The nonces a wallet signs to prove it is the player's.
/// </summary>
/// <remarks>
/// <para>
/// A signature proves possession of a key and nothing more. Without a nonce, one captured signature
/// would link that wallet to whoever replayed it, forever — so the hotel names the exact bytes to
/// sign, remembers that it did, and accepts them once.
/// </para>
/// <para>
/// Kept per SESSION and not per player: the challenge is issued before anything is proved, and
/// binding it to the session is what stops one visitor from spending another's. A second request
/// replaces the first, so a player who reloads the page cannot be tripped by a stale one.
/// </para>
/// <para>
/// In memory, with the hotel's one silo — the same assumption <c>ChainService</c>'s send lock makes
/// and for the same reason. The cost of being wrong is small and visible: a player whose request
/// landed on another process is told the challenge expired and clicks again. Persisting it would
/// mean a table whose rows are all garbage after five minutes.
/// </para>
/// </remarks>
public sealed class WalletLinkChallenges
{
    /// <summary>
    /// How long a challenge stands. Long enough to read what is being signed and approve it in a
    /// wallet, short enough that one left on a shared screen is worthless.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, (string Nonce, DateTime IssuedUtc)> _issued = new(
        StringComparer.Ordinal
    );

    /// <summary>
    /// A fresh challenge for <paramref name="sessionId" />, replacing any it already had.
    /// </summary>
    public string Issue(string sessionId)
    {
        string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        _issued[sessionId] = (nonce, DateTime.UtcNow);

        return nonce;
    }

    /// <summary>
    /// The message that challenge asks for, which is what the wallet SHOWS the player. Readable on
    /// purpose: somebody is being asked to sign something, and "0x8f3a…" tells them nothing about
    /// what they are agreeing to.
    /// </summary>
    public static string MessageFor(string hotelName, string nonce) =>
        $"{hotelName} wants to link this wallet to your account.\n\n"
        + "Signing this proves you control the address. It costs nothing and authorises no "
        + "transaction.\n\n"
        + $"Nonce: {nonce}";

    /// <summary>
    /// Takes the challenge for <paramref name="sessionId" /> if it exists and has not expired, and
    /// removes it either way.
    /// </summary>
    /// <remarks>
    /// One shot, and that is the whole point: a nonce that survived its use would let the same
    /// signature be presented twice.
    /// </remarks>
    public string? Consume(string sessionId)
    {
        if (!_issued.TryRemove(sessionId, out (string Nonce, DateTime IssuedUtc) challenge))
        {
            return null;
        }

        return DateTime.UtcNow - challenge.IssuedUtc <= Lifetime ? challenge.Nonce : null;
    }
}
