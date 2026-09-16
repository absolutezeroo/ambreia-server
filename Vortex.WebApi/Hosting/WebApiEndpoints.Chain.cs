using System;
using System.Collections.Generic;
using System.Text.Json;
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
        string? CollectibleAddress
    );

    /// <summary>One JSON-RPC call, as the browser sent it.</summary>
    public sealed record RpcRequest(string? Method, JsonElement[]? Params);

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
                            new Uri(
                                new Uri($"{ctx.Request.Scheme}://{ctx.Request.Host}"),
                                "/api/public/chain/rpc"
                            ).ToString(),
                            info.CurrencyName,
                            info.CurrencySymbol,
                            // 18, like every EVM chain's native coin. Not configurable, because it
                            // is not ours to choose: it is how the node counts wei.
                            18,
                            info.CoinAddress,
                            info.CollectibleAddress
                        )
                    );
                }
            )
            .WithName("ChainDescriptor")
            .WithSummary("What a wallet needs to add the hotel's network.")
            .WithTags(tag);

        app.MapPost(
                "/api/public/chain/rpc",
                async Task<
                    Results<
                        Ok<JsonElement>,
                        BadRequest<ApiErrorResponse>,
                        JsonHttpResult<ApiErrorResponse>
                    >
                > (RpcRequest? body, [FromServices] IChainService chain, CancellationToken ct) =>
                {
                    if (!chain.Enabled)
                    {
                        // 503 and not 404: the route exists and will work once an operator turns the
                        // chain on, which is a different thing for a wallet to be told.
                        return TypedResults.Json(
                            new ApiErrorResponse("chain_disabled"),
                            statusCode: StatusCodes.Status503ServiceUnavailable
                        );
                    }

                    if (body is null || string.IsNullOrWhiteSpace(body.Method))
                    {
                        return TypedResults.BadRequest(new ApiErrorResponse("invalid_request"));
                    }

                    if (!chain.IsPublicMethodAllowed(body.Method))
                    {
                        // Named in the answer rather than hidden behind a generic refusal: a wallet
                        // asking for a method this hotel does not proxy is a configuration problem
                        // somebody has to be able to diagnose, and the method name is not a secret.
                        return TypedResults.Json(
                            new ApiErrorResponse($"method_not_allowed:{body.Method}"),
                            statusCode: StatusCodes.Status403Forbidden
                        );
                    }

                    IReadOnlyList<JsonElement> parameters = body.Params ?? [];

                    try
                    {
                        JsonElement result = await chain
                            .ForwardPublicCallAsync(body.Method, parameters, ct)
                            .ConfigureAwait(false);

                        return TypedResults.Ok(result);
                    }
                    catch (Exception failure) when (failure is not OperationCanceledException)
                    {
                        // The node answering "execution reverted" is not this API failing, and it
                        // arrives as an exception because JsonRpcClient turns an error member into
                        // one. 502 says the upstream refused; the message carries its reason.
                        return TypedResults.Json(
                            new ApiErrorResponse(failure.Message),
                            statusCode: StatusCodes.Status502BadGateway
                        );
                    }
                }
            )
            .RequireRateLimiting(rateLimitPolicy)
            .WithName("ChainRpc")
            .WithSummary("Forwards an allowlisted JSON-RPC call to the hotel's node.")
            .WithTags(tag);
    }
}
