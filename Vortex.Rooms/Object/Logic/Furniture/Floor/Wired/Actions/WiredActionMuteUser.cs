using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Rooms.Enums.Wired;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Primitives.Rooms.Wired;
using Vortex.Protocol.Messages.Outgoing.Room.Chat;
using Vortex.Rooms.Wired.Rules;

namespace Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Actions;

/// <summary>
/// "WIRED Effect: Mute User" — silences the users the pile is acting on for a while.
/// </summary>
/// <remarks>
/// The form is a message (max 100 characters) and a minute slider from 0 to 10, so the wire is
/// <c>stringParam</c> plus int param [0] in minutes. Zero minutes mutes nobody: the slider allows it,
/// and a mute of no duration is not an unmute — treating it as one would hand a room a way to lift
/// a moderator's mute.
/// <para>
/// Muting has no actor here, exactly like the wired kick: the room's own wiring is doing it, so the
/// who-can-mute setting has nobody to check against.
/// </para>
/// </remarks>
[RoomObjectLogic("wf_act_mute_triggerer")]
public class WiredActionMuteUser(
    IGrainFactory grainFactory,
    IStuffDataFactory stuffDataFactory,
    IRoomFloorItemContext ctx
) : FurnitureWiredActionLogic(grainFactory, stuffDataFactory, ctx)
{
    private const int MaxMinutes = 10;

    private const int SecondsPerMinute = 60;

    public override int WiredCode => (int)WiredActionType.MUTE_USER;

    /// <summary>The mute message is shown to the player it mutes.</summary>
    public override (int Start, int Length)? ProseInStringParam(string stringParam) =>
        (0, stringParam.Length);

    public override List<IWiredParamRule> GetIntParamRules() =>
        [new WiredRangeParamRule(0, MaxMinutes, 0)];

    public override List<WiredPlayerSourceType[]> GetAllowedPlayerSources() =>
        [
            [
                WiredPlayerSourceType.TriggeredUser,
                WiredPlayerSourceType.SelectorUsers,
                WiredPlayerSourceType.SignalUsers,
            ],
        ];

    public override async Task<bool> ExecuteAsync(IWiredExecutionContext ctx, CancellationToken ct)
    {
        int minutes = _wiredData.IntParams.Count > 0 ? _wiredData.GetIntParam<int>(0) : 0;

        if (minutes <= 0)
        {
            return true;
        }

        IWiredSelectionSet selection = await ctx.GetEffectiveSelectionAsync(this, ct);
        string message = await ApplyTextAddonsAsync(_wiredData.StringParam, ctx, ct);

        foreach (int playerId in selection.SelectedPlayerIds)
        {
            // Tell them why they went quiet, before the mute lands — the same whisper route the
            // wired kick uses, since a silenced player would not see a later one either way.
            if (
                !string.IsNullOrWhiteSpace(message)
                && _ctx.Lookup.TryFindAvatarByPlayer(playerId, out IRoomAvatar? avatar)
            )
            {
                await _grainFactory
                    .GetPlayerPresenceGrain(playerId)
                    .SendComposerAsync(
                        new WhisperMessageComposer
                        {
                            ObjectId = avatar.ObjectId,
                            Text = message,
                            Gesture = default,
                            StyleId = 0,
                            Links = [],
                            TrackingId = 0,
                        }
                    );
            }

            await _ctx.Furni.MuteUserFromWiredAsync(playerId, minutes * SecondsPerMinute, ct);
        }

        return true;
    }
}
