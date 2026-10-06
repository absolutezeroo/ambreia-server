using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Rooms.Enums.Games;
using Vortex.Primitives.Rooms.Games.Components;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Logic;

namespace Vortex.Rooms.Object.Logic.Furniture.Floor.Games;

/// <summary>
/// A team scoreboard: the client's <c>furniture_score</c> logic shows the item's raw state value as
/// a number and animates the delta.
/// <para>
/// It is an <see cref="IScoreDisplayComponent"/> rather than a game component, because a board
/// belongs to no game: <c>bb_score_*</c> binds to <c>furniture_score</c> and carries its colour in
/// the classname, so the colour resolves from the logic key first and falls back to the classname.
/// (<c>fball_score_*</c> is NOT here — the furnidata binds it to <c>furniture_hockey_score</c>, and
/// <see cref="FurnitureHockeyScoreLogic"/> is the same kind of board on that key.) The number is
/// pushed by the game
/// scoreboard presenter on every score change, in or out of a match; it is live display and never
/// persisted.
/// </para>
/// </summary>
[RoomObjectLogic("furniture_score")]
public sealed class FurnitureScoreboardLogic(
    IStuffDataFactory stuffDataFactory,
    IRoomFloorItemContext ctx
) : FurnitureFloorLogic(stuffDataFactory, ctx), IScoreDisplayComponent
{
    protected override StuffPersistanceType _stuffPersistanceType =>
        StuffPersistanceType.RoomActive;

    public GameTeamColor Team { get; } =
        GameColorKey.FromKeySuffix(ctx.Definition.LogicName) is var byKey
        && byKey != GameTeamColor.None
            ? byKey
            : GameColorKey.FromKeySuffix(ctx.Definition.Name);
}
