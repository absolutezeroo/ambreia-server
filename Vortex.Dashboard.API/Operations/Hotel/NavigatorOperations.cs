using System;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Dashboard.API.Operations.Hotel.Contracts;
using Vortex.Primitives.Navigator;
using Vortex.Primitives.Navigator.Admin;
using Vortex.Primitives.Navigator.Enums;

namespace Vortex.Dashboard.API.Operations.Hotel;

/// <summary>
/// Navigator configuration operations. Each routes through
/// <see cref="Vortex.Primitives.Navigator.INavigatorAdminService"/> (never a direct DB write), which
/// reloads the live navigator snapshot after committing, and emits a durable audit event with the
/// operator's reason — same contract as the catalog/quest operations.
/// </summary>
internal sealed class NavigatorOperations(
    OperationRunner runner,
    INavigatorAdminService navigatorAdmin
)
{
    private readonly OperationRunner _runner = runner;
    private readonly INavigatorAdminService _navigatorAdmin = navigatorAdmin;

    public Task<OperationResult> CreateNavigatorContextAsync(
        CreateNavigatorContextRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.navigator.context.create",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.SearchCode, request.QueryType },
            work: async c =>
                Throw(
                    await _navigatorAdmin
                        .CreateContextAsync(
                            new NavigatorContextSpec(
                                request.SearchCode,
                                request.Visible,
                                ToQueryType(request.QueryType),
                                request.OrderNum
                            ),
                            c
                        )
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> UpdateNavigatorContextAsync(
        UpdateNavigatorContextRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.navigator.context.update",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.ContextId,
                request.SearchCode,
                request.QueryType,
            },
            work: async c =>
                Throw(
                    await _navigatorAdmin
                        .UpdateContextAsync(
                            request.ContextId,
                            new NavigatorContextSpec(
                                request.SearchCode,
                                request.Visible,
                                ToQueryType(request.QueryType),
                                request.OrderNum
                            ),
                            c
                        )
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> DeleteNavigatorContextAsync(
        DeleteNavigatorContextRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.navigator.context.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.ContextId },
            work: async c =>
                Throw(
                    await _navigatorAdmin
                        .DeleteContextAsync(request.ContextId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> CreateNavigatorQuickLinkAsync(
        CreateNavigatorQuickLinkRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.navigator.quicklink.create",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.ContextId,
                request.SearchCode,
                request.QueryType,
            },
            work: async c =>
                Throw(
                    await _navigatorAdmin
                        .CreateQuickLinkAsync(
                            new NavigatorQuickLinkSpec(
                                request.ContextId,
                                request.SearchCode,
                                request.Filter,
                                request.Localization,
                                ToQueryType(request.QueryType),
                                request.OrderNum,
                                request.ForceClosed
                            ),
                            c
                        )
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> UpdateNavigatorQuickLinkAsync(
        UpdateNavigatorQuickLinkRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.navigator.quicklink.update",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.QuickLinkId,
                request.ContextId,
                request.SearchCode,
            },
            work: async c =>
                Throw(
                    await _navigatorAdmin
                        .UpdateQuickLinkAsync(
                            request.QuickLinkId,
                            new NavigatorQuickLinkSpec(
                                request.ContextId,
                                request.SearchCode,
                                request.Filter,
                                request.Localization,
                                ToQueryType(request.QueryType),
                                request.OrderNum,
                                request.ForceClosed
                            ),
                            c
                        )
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> DeleteNavigatorQuickLinkAsync(
        DeleteNavigatorQuickLinkRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.navigator.quicklink.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.QuickLinkId },
            work: async c =>
                Throw(
                    await _navigatorAdmin
                        .DeleteQuickLinkAsync(request.QuickLinkId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> CreateNavigatorFlatCategoryAsync(
        CreateNavigatorFlatCategoryRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.navigator.category.create",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.Name, request.StaffOnly },
            work: async c =>
                Throw(
                    await _navigatorAdmin
                        .CreateFlatCategoryAsync(
                            new NavigatorFlatCategorySpec(
                                request.Name,
                                request.Visible,
                                request.Automatic,
                                request.AutomaticCategory,
                                request.GlobalCategory,
                                request.StaffOnly,
                                request.MinRank,
                                request.OrderNum
                            ),
                            c
                        )
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> UpdateNavigatorFlatCategoryAsync(
        UpdateNavigatorFlatCategoryRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.navigator.category.update",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.CategoryId, request.Name },
            work: async c =>
                Throw(
                    await _navigatorAdmin
                        .UpdateFlatCategoryAsync(
                            request.CategoryId,
                            new NavigatorFlatCategorySpec(
                                request.Name,
                                request.Visible,
                                request.Automatic,
                                request.AutomaticCategory,
                                request.GlobalCategory,
                                request.StaffOnly,
                                request.MinRank,
                                request.OrderNum
                            ),
                            c
                        )
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> DeleteNavigatorFlatCategoryAsync(
        DeleteNavigatorFlatCategoryRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.navigator.category.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.CategoryId },
            work: async c =>
                Throw(
                    await _navigatorAdmin
                        .DeleteFlatCategoryAsync(request.CategoryId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> CreateNavigatorEventCategoryAsync(
        CreateNavigatorEventCategoryRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.navigator.eventcategory.create",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.Name },
            work: async c =>
                Throw(
                    await _navigatorAdmin
                        .CreateEventCategoryAsync(
                            new NavigatorEventCategorySpec(request.Name, request.Visible),
                            c
                        )
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> UpdateNavigatorEventCategoryAsync(
        UpdateNavigatorEventCategoryRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.navigator.eventcategory.update",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.CategoryId, request.Name },
            work: async c =>
                Throw(
                    await _navigatorAdmin
                        .UpdateEventCategoryAsync(
                            request.CategoryId,
                            new NavigatorEventCategorySpec(request.Name, request.Visible),
                            c
                        )
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> DeleteNavigatorEventCategoryAsync(
        DeleteNavigatorEventCategoryRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.navigator.eventcategory.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.CategoryId },
            work: async c =>
                Throw(
                    await _navigatorAdmin
                        .DeleteEventCategoryAsync(request.CategoryId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> SeedNavigatorDefaultsAsync(
        SeedNavigatorDefaultsRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.navigator.seed_defaults",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { seed = "client_default_tabs" },
            work: async c =>
                Throw(await _navigatorAdmin.SeedDefaultsAsync(c).ConfigureAwait(false)),
            ct
        );

    /// <summary>An ordinal the client never sends is a bug in the form, not a new query — refuse it
    /// rather than storing a value <c>ResolveQueryType</c> would later read as "every room".</summary>
    private static NavigatorQueryType ToQueryType(int value) =>
        Enum.IsDefined(typeof(NavigatorQueryType), value)
            ? (NavigatorQueryType)value
            : throw new InvalidOperationException("invalid_query_type");

    private static void Throw(NavigatorAdminResult result)
    {
        if (!result.Success)
        {
            throw new InvalidOperationException(result.ErrorCode);
        }
    }
}
