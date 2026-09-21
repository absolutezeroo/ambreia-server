using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Rooms.Enums.Wired;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Primitives.Rooms.Wired;
using Vortex.Rooms.Wired.Rules;

namespace Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Actions;

/// <summary>
/// Habbo's "bot talks" wired: the named bot says a line to the room.
/// <para>
/// Read off the client's own setup form, which packs both fields into the string param as
/// <c>bot name \t message</c> and writes the radio pair — talk or shout — into int param [0].
/// </para>
/// </summary>
[RoomObjectLogic("wf_act_bot_talk")]
// The furni in this hotel is *named* wf_act_bot_talk but carries wf_act_bot_talk_custom in its
// `logic` column, which is the only thing a binding reads. Same box, same wired code.
[RoomObjectLogic("wf_act_bot_talk_custom")]
public class WiredActionBotTalk(
    IGrainFactory grainFactory,
    IStuffDataFactory stuffDataFactory,
    IRoomFloorItemContext ctx
) : FurnitureWiredActionLogic(grainFactory, stuffDataFactory, ctx)
{
    /// <summary>The client's own delimiter between the bot's name and what it should say.</summary>
    internal const char FieldSeparator = '\t';

    private const int Shout = 1;

    public override int WiredCode => (int)WiredActionType.BOT_TALK;

    public override List<IWiredParamRule> GetIntParamRules() =>
        [
            new WiredRangeParamRule(0, 1, 0), // 0 = talk, 1 = shout
        ];

    public override async Task<bool> ExecuteAsync(IWiredExecutionContext ctx, CancellationToken ct)
    {
        (string botName, string text) = SplitConfiguration(_wiredData.StringParam);

        if (botName.Length == 0 || text.Length == 0)
        {
            return true;
        }

        text = await ApplyTextAddonsAsync(text, ctx, ct);

        bool shout = _wiredData.IntParams.Count > 0 && _wiredData.GetIntParam<int>(0) == Shout;

        await ctx.ProcessBotChatAsync(
            botName,
            text,
            shout ? WiredBotChatType.Shout : WiredBotChatType.Say,
            null
        );

        return true;
    }

    /// <summary>
    /// Splits the form's two fields. A message may itself contain the delimiter — the box is a free
    /// text area — so only the first one separates, and everything after it is the line.
    /// </summary>
    internal static (string BotName, string Text) SplitConfiguration(string? stringParam)
    {
        if (string.IsNullOrEmpty(stringParam))
        {
            return (string.Empty, string.Empty);
        }

        int separator = stringParam.IndexOf(FieldSeparator);

        return separator < 0
            ? (stringParam.Trim(), string.Empty)
            : (stringParam[..separator].Trim(), stringParam[(separator + 1)..].Trim());
    }
}
