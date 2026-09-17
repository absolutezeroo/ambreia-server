using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Vortex.Primitives.Chain;
using Vortex.WebApi.Http;

namespace Vortex.WebApi.Hosting;

/// <summary>
/// The hotel's chain, as a player's browser is allowed to see it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the only route to the node, and that is the point.</b> anvil serves
/// <c>anvil_setBalance</c> and <c>anvil_impersonateAccount</c> beside the standard methods and
/// cannot be told not to, so its port stays on the compose network and is never published: anyone
/// who could reach it directly would mint themselves anything, or act as the treasury, with one
/// request. Everything a wallet legitimately needs goes through the allowlist in
/// <c>PublicRpcMethods</c> instead.
/// </para>
/// <para>
/// Anonymous on purpose. A chain is public — its balances, its blocks and its contracts are readable
/// by definition, and putting a session in front of that would protect nothing while breaking the
/// one thing this exists for, which is a wallet extension talking to the hotel before the player has
/// linked anything.
/// </para>
/// </remarks>
internal static partial class WebApiEndpointsChain
{
    /// <summary>
    /// A chain as a wallet's <c>wallet_addEthereumChain</c> wants it. <c>rpcUrl</c> is this API's own
    /// proxy and never the node: handing out the node's address would defeat the whole arrangement.
    /// </summary>
    public sealed record ChainDescriptor(
        bool Enabled,
        long ChainId,
        string ChainIdHex,
        string RpcUrl,
        string CurrencyName,
        string CurrencySymbol,
        int CurrencyDecimals,
        string? CoinAddress,
        string? CollectibleAddress,
        // Where a Relic is sent to bring it back into the hotel. Public by nature — it owns both
        // contracts, so anyone reading the chain has it already — and the player cannot use the
        // claim door without being told it.
        string? TreasuryAddress
    );

    /// <summary>One JSON-RPC call, as the browser sent it.</summary>
    public sealed record RpcRequest(string? Method, JsonElement[]? Params);

    /// <summary>
    /// The scheme the CALLER used, which behind a TLS-terminating proxy is not the one Kestrel saw.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This matters for exactly one value and it is not cosmetic: a wallet refuses an
    /// <c>http://</c> RPC endpoint offered by an <c>https://</c> page, and
    /// <c>wallet_addEthereumChain</c> requires HTTPS outside localhost. A hotel behind Coolify,
    /// Traefik or any other reverse proxy speaks plain HTTP to Kestrel, so
    /// <c>ctx.Request.Scheme</c> reads "http" and the network the hotel advertises cannot be added.
    /// </para>
    /// <para>
    /// The header is trusted here and nowhere else, and only to BUILD A URL TO PRINT. Spoofing it
    /// changes which scheme this one string carries and grants nothing — no session, no
    /// authorisation, no routing decision. That is why this is four lines rather than
    /// <c>UseForwardedHeaders</c>, which would apply to the whole pipeline and needs a list of
    /// trusted proxies to be safe at all.
    /// </para>
    /// </remarks>
    private static string PublicScheme(HttpContext ctx)
    {
        string? forwarded = ctx.Request.Headers["X-Forwarded-Proto"].ToString();

        // A chain of proxies appends, so the first entry is the caller's.
        int separator = forwarded.IndexOf(',', StringComparison.Ordinal);

        if (separator >= 0)
        {
            forwarded = forwarded[..separator];
        }

        forwarded = forwarded.Trim();

        return forwarded is "http" or "https" ? forwarded : ctx.Request.Scheme;
    }

    public static void MapChain(WebApplication app, string tag, string rateLimitPolicy)
    {
        // [FromServices] is not decoration here. Minimal APIs infer an unannotated parameter as the
        // request BODY when they cannot see it registered, and a body on a GET is illegal — so an
        // unregistered IChainService does not fail this route, it fails the construction of EVERY
        // route in the API with "Body was inferred but the method does not allow inferred body
        // parameters". Saying where it comes from settles the inference whatever the container holds.
        app.MapGet(
                "/api/public/chain",
                (HttpContext ctx, [FromServices] IChainService chain) =>
                {
                    ChainInfo info = chain.GetInfo();

                    return TypedResults.Ok(
                        new ChainDescriptor(
                            chain.Enabled,
                            info.ChainId,
                            // Wallets want the id as a hex quantity, minimal and 0x-prefixed. A
                            // decimal here is silently rejected by MetaMask's own validation.
                            "0x"
                                + info.ChainId.ToString(
                                    "x",
                                    System.Globalization.CultureInfo.InvariantCulture
                                ),
                            // The operator's own answer wins. Deriving it works behind ONE proxy and
                            // not behind two: the site proxies /api here over plain HTTP, and that
                            // inner hop rewrites X-Forwarded-Proto to "http" — true for its own leg,
                            // and an endpoint every wallet refuses. No header survives an arbitrary
                            // chain, so past one hop this has to be configured rather than guessed.
                            info.PublicRpcUrl
                                ?? new Uri(
                                    new Uri($"{PublicScheme(ctx)}://{ctx.Request.Host}"),
                                    "/api/public/chain/rpc"
                                ).ToString(),
                            info.CurrencyName,
                            info.CurrencySymbol,
                            // 18, like every EVM chain's native coin. Not configurable, because it
                            // is not ours to choose: it is how the node counts wei.
                            18,
                            info.CoinAddress,
                            info.CollectibleAddress,
                            chain.TreasuryAddress
                        )
                    );
                }
            )
            .WithName("ChainDescriptor")
            .WithSummary("What a wallet needs to add the hotel's network.")
            .WithTags(tag);

        app.MapPost(
                "/api/public/chain/rpc",
                async Task<IResult> (
                    JsonElement body,
                    [FromServices] IChainService chain,
                    CancellationToken ct
                ) =>
                {
                    // A batch. Every entry gets its own envelope and the whole thing is a 200,
                    // because one status cannot describe several answers — a batch where the second
                    // call reverted is not a failed request.
                    if (body.ValueKind == JsonValueKind.Array)
                    {
                        JsonArray answers = [];

                        foreach (JsonElement call in body.EnumerateArray())
                        {
                            answers.Add(await AnswerAsync(call, chain, ct).ConfigureAwait(false));
                        }

                        return TypedResults.Json(answers);
                    }

                    // The hotel's own three refusals keep their status codes. They are not the node
                    // speaking and they are not what a wallet is meant to retry against; a 403 on
                    // `anvil_setBalance` is the one line in the log that says the allowlist did its
                    // job, and a 200 carrying an error member would not be.
                    if (!chain.Enabled)
                    {
                        // 503 and not 404: the route exists and will work once an operator turns the
                        // chain on, which is a different thing for a wallet to be told.
                        return TypedResults.Json(
                            new ApiErrorResponse("chain_disabled"),
                            statusCode: StatusCodes.Status503ServiceUnavailable
                        );
                    }

                    string? method = MethodOf(body);

                    if (method is null)
                    {
                        return TypedResults.BadRequest(new ApiErrorResponse("invalid_request"));
                    }

                    if (!chain.IsPublicMethodAllowed(method))
                    {
                        // Named in the answer rather than hidden behind a generic refusal: a wallet
                        // asking for a method this hotel does not proxy is a configuration problem
                        // somebody has to be able to diagnose, and the method name is not a secret.
                        return TypedResults.Json(
                            new ApiErrorResponse($"method_not_allowed:{method}"),
                            statusCode: StatusCodes.Status403Forbidden
                        );
                    }

                    return TypedResults.Json(
                        await AnswerAsync(body, chain, ct).ConfigureAwait(false)
                    );
                }
            )
            .RequireRateLimiting(rateLimitPolicy)
            .WithName("ChainRpc")
            .WithSummary("Forwards an allowlisted JSON-RPC call, or a batch of them, to the node.")
            .WithTags(tag);
    }

    /// <summary>
    /// One call, answered as JSON-RPC 2.0 wants it: <c>jsonrpc</c>, the caller's own <c>id</c>, and
    /// exactly one of <c>result</c> or <c>error</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The envelope is not decoration.</b> This route used to answer the node's <c>result</c>
    /// member on its own — <c>"0x7a69"</c> where a wallet expects
    /// <c>{"jsonrpc":"2.0","id":1,"result":"0x7a69"}</c> — so MetaMask could not match the answer to
    /// the request it had pending and reported the network as unreachable. Nothing in the hotel
    /// noticed, because the proxy had done its job and the node had answered correctly.
    /// </para>
    /// <para>
    /// The id is echoed verbatim, whatever its type. A wallet matches on it, and an id that comes
    /// back as a string where a number went out is an answer nobody claims.
    /// </para>
    /// </remarks>
    private static async Task<JsonObject> AnswerAsync(
        JsonElement call,
        IChainService chain,
        CancellationToken ct
    )
    {
        JsonObject answer = new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] =
                call.ValueKind == JsonValueKind.Object
                && call.TryGetProperty("id", out JsonElement id)
                    ? JsonNode.Parse(id.GetRawText())
                    : null,
        };

        string? method = MethodOf(call);

        if (method is null)
        {
            return Failed(answer, -32600, "Invalid Request");
        }

        if (!chain.Enabled)
        {
            return Failed(answer, -32603, "chain_disabled");
        }

        if (!chain.IsPublicMethodAllowed(method))
        {
            return Failed(answer, -32601, $"Method not found: {method}");
        }

        List<JsonElement> parameters = [];

        if (
            call.TryGetProperty("params", out JsonElement sent)
            && sent.ValueKind == JsonValueKind.Array
        )
        {
            parameters.AddRange(sent.EnumerateArray());
        }

        try
        {
            JsonElement result = await chain
                .ForwardPublicCallAsync(method, parameters, ct)
                .ConfigureAwait(false);

            // A JSON `null` result is a real answer and not a missing one — an unmined
            // `eth_getTransactionReceipt` is exactly that — so it is written rather than skipped.
            answer["result"] = JsonNode.Parse(result.GetRawText());

            return answer;
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // The node answering "execution reverted" is not this API failing, and it arrives as an
            // exception because JsonRpcClient turns an error member back into one. It goes out as a
            // JSON-RPC error on a 200, which is where a wallet looks for it; an HTTP 502 reads to
            // the wallet as "the network is down" and makes it retry something that will never
            // succeed.
            return Failed(answer, -32000, failure.Message);
        }
    }

    /// <summary>The <c>method</c> member, or <c>null</c> when there is not a usable one.</summary>
    private static string? MethodOf(JsonElement call)
    {
        if (
            call.ValueKind != JsonValueKind.Object
            || !call.TryGetProperty("method", out JsonElement method)
            || method.ValueKind != JsonValueKind.String
        )
        {
            return null;
        }

        string? name = method.GetString();

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static JsonObject Failed(JsonObject answer, int code, string message)
    {
        answer["error"] = new JsonObject { ["code"] = code, ["message"] = message };

        return answer;
    }
}
