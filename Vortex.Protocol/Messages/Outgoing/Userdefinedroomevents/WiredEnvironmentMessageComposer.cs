using System.Collections.Generic;
using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Userdefinedroomevents;

/// <summary>
/// What wired in this room changes about the client's own behaviour.
/// </summary>
/// <remarks>
/// Until this arrives the client assumes no click-user box exists, opens the context menu itself and
/// never sends <c>WiredClickUser</c>. So this message is not decoration on the feature — it is the
/// switch that turns the feature on, and a room that never receives it has a click-user trigger that
/// can be built and can never be reached from the info stand.
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record WiredEnvironmentMessageComposer : IComposer
{
    /// <summary>True when the room holds a <c>wf_trg_click_user</c> box.</summary>
    [Id(0)]
    public required bool HasClickUserWired { get; init; }

    /// <summary>
    /// Achievement names the room's achievement-enabler add-ons allow, bare (no <c>ACH_WF_</c>):
    /// the choices the client offers in the "progress achievement" effect, and the filter its
    /// achievements window applies to the <c>wired_games</c> category.
    /// </summary>
    [Id(1)]
    public IReadOnlyList<string> EnabledAchievements { get; init; } = [];
}
