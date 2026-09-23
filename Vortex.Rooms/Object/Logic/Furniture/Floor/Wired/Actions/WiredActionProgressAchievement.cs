using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Action;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms.Enums.Wired;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Primitives.Rooms.Snapshots.Wired;
using Vortex.Primitives.Rooms.Wired;
using Vortex.Primitives.Rooms.Wired.Variable;
using Vortex.Protocol.Messages.Outgoing.Userdefinedroomevents;
using Vortex.Rooms.Wired;
using Vortex.Rooms.Wired.Rules;

namespace Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Actions;

/// <summary>
/// "WIRED Effect: Progress Achievement" (action code 51) — adds to, or sets, the resolved users'
/// progress on one wired achievement.
/// </summary>
/// <remarks>
/// <para>
/// The dialog (<c>ProgressAchievement.as</c>) sends the achievement's bare name as the string, then
/// [mode, value-or-variable option, value, variable target]; mode 1 is "add to existing progress",
/// 0 "set progress". The achievement advanced is <c>WF_</c> + that name, which is how the client's
/// achievements window ties its <c>wired_games</c> category to the room's list.
/// </para>
/// <para>
/// Only a name one of the room's enabler add-ons allows can be saved or advanced — refused at save
/// with the client's own <c>wiredfurni.error.achievement_not_allowed</c>, and skipped at run time for
/// a pile whose enabler has since been picked up. "Set" never lowers progress: an achievement does
/// not go backwards (<see cref="IPlayerAchievementGrain.ProgressToAsync"/>).
/// </para>
/// <para>
/// The primary tree declares this box's code as its <c>negativeCode</c> only, with no positive
/// <c>code</c>. The client resolves a dialog by either, so the code alone does not settle the
/// branch; this runs on the ordinary one, where the conditions held, which is what a box named
/// after an achievement's progress is for.
/// </para>
/// </remarks>
[RoomObjectLogic("wf_act_progress_achievement")]
// The furni this hotel already carries for it, filed under "Customs" only because no official code
// used to recognise it.
[RoomObjectLogic("wf_act_give_achievement")]
public class WiredActionProgressAchievement(
    IGrainFactory grainFactory,
    IStuffDataFactory stuffDataFactory,
    IRoomFloorItemContext ctx
) : FurnitureWiredActionLogic(grainFactory, stuffDataFactory, ctx)
{
    public override int WiredCode => (int)WiredActionType.PROGRESS_ACHIEVEMENT;

    private const int AddMode = 1;

    /// <summary>A value-or-variable section's radio: 0 is the typed number, 1 a variable.</summary>
    private const int FromVariable = 1;

    public override List<IWiredParamRule> GetIntParamRules() =>
        [
            new WiredRangeParamRule(0, 1, AddMode), // 0 set / add
            new WiredRangeParamRule(0, 1, 0), // 1 number or variable
            new WiredRangeParamRule(0, int.MaxValue, 1), // 2 amount
            new WiredParamRule(0), // 3 the variable's target
        ];

    public override int GetMaxVariableIds() => 1;

    public override List<WiredPlayerSourceType[]> GetAllowedPlayerSources() =>
        [
            [
                WiredPlayerSourceType.TriggeredUser,
                WiredPlayerSourceType.SelectorUsers,
                WiredPlayerSourceType.SignalUsers,
                WiredPlayerSourceType.ClickedUser,
            ],
        ];

    public override async Task<bool> ApplyWiredUpdateAsync(
        ActionContext ctx,
        WiredUpdateRequest update,
        CancellationToken ct
    )
    {
        if (!await IsEnabledAsync(update.StringParam, ct))
        {
            await _grainFactory
                .GetPlayerPresenceGrain(ctx.PlayerId)
                .SendComposerAsync(
                    new WiredValidationErrorEventMessageComposer
                    {
                        LocalizationKey = "wiredfurni.error.achievement_not_allowed",
                        Parameters = [],
                    }
                );

            return false;
        }

        return await base.ApplyWiredUpdateAsync(ctx, update, ct);
    }

    public override async Task<bool> ExecuteAsync(IWiredExecutionContext ctx, CancellationToken ct)
    {
        string name = _wiredData.StringParam.Trim();

        // ponytail: walks the room's items on every run; index the enablers if a fast timer on a
        // big room ever makes it show.
        if (!await IsEnabledAsync(name, ct))
        {
            return true;
        }

        IWiredSelectionSet selection = await ctx.GetEffectiveSelectionAsync(this, ct);
        bool add = Param(0) == AddMode;

        foreach (int playerId in selection.SelectedPlayerIds)
        {
            int amount = Amount(playerId);

            if (amount <= 0)
            {
                continue;
            }

            IPlayerAchievementGrain achievements = _grainFactory.GetPlayerAchievementGrain(
                playerId
            );

            await (
                add
                    ? achievements.ProgressAsync("WF_" + name, amount, ct)
                    : achievements.ProgressToAsync("WF_" + name, amount, ct)
            );
        }

        return true;
    }

    private async Task<bool> IsEnabledAsync(string name, CancellationToken ct)
    {
        name = name.Trim();

        if (name.Length == 0)
        {
            return false;
        }

        IReadOnlyList<string> enabled = await _ctx.RoomAs<IRoomWired>()
            .GetEnabledAchievementsAsync(ct);

        return enabled.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The typed amount, or the variable's for this user. A variable that holds nothing
    /// advances nothing rather than falling back: a missing score is not a score of one.</summary>
    private int Amount(int playerId)
    {
        if (Param(1) != FromVariable)
        {
            return Param(2);
        }

        if (_wiredData.VariableIds.Count == 0)
        {
            return 0;
        }

        WiredSelectionSet self = new();
        self.SelectedPlayerIds.Add(playerId);

        return WiredVariableAccess.TryRead(
            _ctx.Furni,
            _wiredData.VariableIds[0],
            (WiredVariableTargetType)Param(3),
            self,
            out WiredVariableValue read
        )
            ? read.Value
            : 0;
    }

    private int Param(int index) =>
        index < _wiredData.IntParams.Count ? _wiredData.IntParams[index] : 0;
}
