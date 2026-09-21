using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums.Wired;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Primitives.Rooms.Wired;
using Vortex.Rooms.Wired.Rules;

namespace Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Actions;

/// <summary>
/// Habbo's "bot talks to avatar" wired: the named bot addresses the people the stack resolved,
/// rather than the room at large.
/// <para>
/// Same string param as <see cref="WiredActionBotTalk"/> — <c>bot name \t message</c> — but its
/// radio pair is talk or <em>whisper</em>, so int param [0] means something different here even
/// though it is written the same way.
/// </para>
/// </summary>
[RoomObjectLogic("wf_act_bot_talk_to_avatar")]
// As with the plain bot-talk box: the hotel's furni is named for this key but binds by
// wf_act_bot_talk_to_avatar_custom.
[RoomObjectLogic("wf_act_bot_talk_to_avatar_custom")]
public class WiredActionBotTalkToAvatar(
    IGrainFactory grainFactory,
    IStuffDataFactory stuffDataFactory,
    IRoomFloorItemContext ctx
) : FurnitureWiredActionLogic(grainFactory, stuffDataFactory, ctx)
{
    private const int Whisper = 1;

    public override int WiredCode => (int)WiredActionType.BOT_TALK_DIRECT_TO_AVTR;

    public override List<IWiredParamRule> GetIntParamRules() =>
        [
            new WiredRangeParamRule(0, 1, 0), // 0 = talk, 1 = whisper
        ];

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
        (string botName, string text) = WiredActionBotTalk.SplitConfiguration(
            _wiredData.StringParam
        );

        if (botName.Length == 0 || text.Length == 0)
        {
            return true;
        }

        text = await ApplyTextAddonsAsync(text, ctx, ct);

        bool whisper = _wiredData.IntParams.Count > 0 && _wiredData.GetIntParam<int>(0) == Whisper;

        IWiredSelectionSet selection = await ctx.GetEffectiveSelectionAsync(this, ct);

        // Said aloud, the line is one line however many people the stack resolved; whispered, it is
        // one line each, because a whisper only reaches the person it is addressed to.
        if (!whisper)
        {
            await ctx.ProcessBotChatAsync(botName, text, WiredBotChatType.Say, null);

            return true;
        }

        foreach (int playerId in selection.SelectedPlayerIds)
        {
            await ctx.ProcessBotChatAsync(
                botName,
                text,
                WiredBotChatType.Whisper,
                new PlayerId(playerId)
            );
        }

        return true;
    }
}
