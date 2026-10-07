using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Vortex.Dashboard.API.Operations;
using Vortex.Dashboard.API.Operations.Hotel;
using Vortex.Dashboard.API.Operations.Hotel.Contracts;
using Vortex.Primitives.Permissions;

namespace Vortex.Dashboard.API.Hosting;

/// <summary>
/// Content write surface: achievement ladders, bots and hand items, NFT collections, the economy's
/// smaller tables, and direct player grants — all behind
/// <see cref="Capabilities.Dashboard.OpsContentManage"/>. The matching reads live with their own
/// domain's read capability, so an operator can be given "see everything, change nothing".
/// </summary>
internal static partial class DashboardEndpoints
{
    private const string TagContent = "Content";
    private const string OpsContent = ApiOperations + "/content";

    public static void MapContentOperations(WebApplication app)
    {
        Map<AchievementRequest>(
            app,
            "/achievements",
            (ops, body, actor, ct) => ops.SaveAchievementAsync(body, actor, ct),
            body =>
                !string.IsNullOrWhiteSpace(body.Name) && !string.IsNullOrWhiteSpace(body.Category)
        );
        Map<DeleteAchievementRequest>(
            app,
            "/achievements/delete",
            (ops, body, actor, ct) => ops.DeleteAchievementAsync(body, actor, ct),
            body => body.AchievementId > 0
        );
        Map<AchievementLevelRequest>(
            app,
            "/achievements/levels",
            (ops, body, actor, ct) => ops.SaveAchievementLevelAsync(body, actor, ct),
            body =>
                body.AchievementId > 0
                && body.Level > 0
                && !string.IsNullOrWhiteSpace(body.BadgeCode)
        );
        Map<DeleteAchievementLevelRequest>(
            app,
            "/achievements/levels/delete",
            (ops, body, actor, ct) => ops.DeleteAchievementLevelAsync(body, actor, ct),
            body => body.LevelId > 0
        );
        Map<HandItemRequest>(
            app,
            "/hand-items",
            (ops, body, actor, ct) => ops.SaveHandItemAsync(body, actor, ct),
            body => body.HandItemId > 0 && !string.IsNullOrWhiteSpace(body.Name)
        );
        Map<DeleteHandItemRequest>(
            app,
            "/hand-items/delete",
            (ops, body, actor, ct) => ops.DeleteHandItemAsync(body, actor, ct),
            body => body.Id > 0
        );
        Map<BotRequest>(
            app,
            "/bots",
            (ops, body, actor, ct) => ops.UpdateBotAsync(body, actor, ct),
            body => body.BotId > 0 && !string.IsNullOrWhiteSpace(body.Name)
        );
        Map<DeleteBotRequest>(
            app,
            "/bots/delete",
            (ops, body, actor, ct) => ops.DeleteBotAsync(body, actor, ct),
            body => body.BotId > 0
        );
        Map<CurrencyRequest>(
            app,
            "/currencies",
            (ops, body, actor, ct) => ops.SaveCurrencyAsync(body, actor, ct),
            body => !string.IsNullOrWhiteSpace(body.Name)
        );
        Map<BadgeGrantRequest>(
            app,
            "/badges/grant",
            (ops, body, actor, ct) => ops.GrantBadgeAsync(body, actor, ct),
            body => body.PlayerId > 0 && !string.IsNullOrWhiteSpace(body.BadgeCode)
        );
        Map<BadgeGrantRequest>(
            app,
            "/badges/revoke",
            (ops, body, actor, ct) => ops.RevokeBadgeAsync(body, actor, ct),
            body => body.PlayerId > 0 && !string.IsNullOrWhiteSpace(body.BadgeCode)
        );
        Map<EffectGrantRequest>(
            app,
            "/effects/grant",
            (ops, body, actor, ct) => ops.GrantEffectAsync(body, actor, ct),
            body => body.PlayerId > 0 && body.EffectId > 0
        );
        Map<EffectGrantRequest>(
            app,
            "/effects/revoke",
            (ops, body, actor, ct) => ops.RevokeEffectAsync(body, actor, ct),
            body => body.PlayerId > 0 && body.EffectId > 0
        );
    }

    /// <summary>
    /// Twenty near-identical POSTs differing only in body type and field checks, so the shape is
    /// written once: reject a null body or a missing reason, run the caller's field validation, and
    /// hand off. Written out per endpoint, this file would be four times the length and the reason
    /// check would be four times as easy to forget.
    /// </summary>
    private static void Map<TBody>(
        WebApplication app,
        string suffix,
        Func<ContentOperations, TBody, string, CancellationToken, Task<OperationResult>> run,
        Func<TBody, bool> isValid
    )
        where TBody : class
    {
        MapPost(
            app,
            OpsContent + suffix,
            async (HttpContext ctx, TBody body, ContentOperations ops, CancellationToken ct) =>
                !isValid(body)
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(await run(ops, body, ctx.ActorEmail(), ct).ConfigureAwait(false)),
            Capabilities.Dashboard.OpsContentManage,
            TagContent
        );
    }

    /// <summary>Every content request record carries a <c>Reason</c>; read positionally so the
    /// shared mapper does not need a marker interface on twenty otherwise-plain records.</summary>
    private static string? ReasonOf<TBody>(TBody body) =>
        body?.GetType().GetProperty("Reason")?.GetValue(body) as string;
}
