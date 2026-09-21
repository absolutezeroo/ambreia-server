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
/// Writes a line of the builder's own text into the room's wired log.
/// </summary>
/// <remarks>
/// Int param [0] is the log level — the client's dropdown is three entries and
/// <see cref="WiredLogLevel"/> already numbers them the same way — and the string param is the
/// message. Both come straight from the form (actiontypes/WriteToLog).
/// <para>
/// This is the box that makes the rest of the wired log worth reading. The engine's own lines say
/// which action ran and which refused; this one is how a builder marks where in <em>their</em> logic
/// the room got to, which is the question they are actually debugging.
/// </para>
/// <para>
/// An empty message writes nothing rather than a blank line. A log full of blank entries from a box
/// somebody dragged out and never configured is worse than no box.
/// </para>
/// </remarks>
[RoomObjectLogic("wf_act_write_to_logs")]
// The name this hotel's furni actually carries. No row in `furniture_definitions` has a `logic` of
// `wf_act_write_to_logs`; the one write-to-log furni here is `wf_act_log`, so both halves of this
// box were fully written and completely unreachable.
[RoomObjectLogic("wf_act_log")]
public class WiredActionWriteToLog(
    IGrainFactory grainFactory,
    IStuffDataFactory stuffDataFactory,
    IRoomFloorItemContext ctx
) : FurnitureWiredActionLogic(grainFactory, stuffDataFactory, ctx)
{
    public override int WiredCode => (int)WiredActionType.WRITE_TO_LOG;

    public override List<IWiredParamRule> GetIntParamRules() =>
        [
            new WiredEnumParamRule<WiredLogLevel>(
                WiredLogLevel.Info,
                WiredLogLevel.Info,
                WiredLogLevel.Warning,
                WiredLogLevel.Error
            ),
        ];

    public override Task<bool> ExecuteAsync(IWiredExecutionContext ctx, CancellationToken ct)
    {
        string message = _wiredData.StringParam.Trim();

        if (message.Length > 0)
        {
            _ctx.Furni.WriteWiredRoomLog(
                _wiredData.IntParams.Count > 0
                    ? _wiredData.GetIntParam<WiredLogLevel>(0)
                    : WiredLogLevel.Info,
                message
            );
        }

        return Task.FromResult(true);
    }
}
