using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Groups;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Server.Grains;

namespace Vortex.Social.Events;

/// <summary>
///     Rejects guild creation with an empty/whitespace-only or too-long name. Neither
///     <see cref="GroupDirectoryGrain" /> nor <c>CreateGuildMessageHandler</c> validated
///     <c>Name</c> before this — <c>GroupEntity.Name</c> has no length constraint either — so a
///     player could create a guild with an empty or arbitrarily long name.
/// </summary>
public sealed class GroupNameValidationBehavior(IGrainFactory grainFactory)
    : IEventBehavior<GroupCreatingEvent>
{
    public async ValueTask InvokeAsync(
        GroupCreatingEvent env,
        EventContext ctx,
        Func<ValueTask> next,
        CancellationToken ct
    )
    {
        int maxNameLength = await grainFactory
            .GetServerConfigGrain()
            .GetIntAsync(GroupConfig.MaxNameLengthKey, GroupConfig.MaxNameLengthDefault)
            .ConfigureAwait(false);

        if (GroupNameRules.Validate(env.GroupName, maxNameLength) is string reason)
        {
            ctx.Cancel = true;
            ctx.CancelReason = reason;
        }

        await next().ConfigureAwait(false);
    }
}
