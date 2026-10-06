using System.Collections.Generic;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Object;

namespace Vortex.Rooms.Grains;

/// <summary>Phase of an in-room trade. A trade opens in <see cref="Building"/> (both sides add and
/// remove items and accept/unaccept freely); once both have accepted it advances to
/// <see cref="Confirming"/> where the only remaining actions are the final confirm or a decline.</summary>
internal enum TradePhase
{
    Building,
    Confirming,
}

/// <summary>Live state of a single trade between two players present in the same room. Hosted on the
/// <see cref="RoomGrain"/> (the single-threaded coordinator both participants share) rather than a
/// dedicated grain, so item lists, accept/confirm flags, and the commit never race.
/// <para>
/// The room-object ids are how the trade is <em>opened</em> (<c>OpenTrading</c> names the other
/// avatar by the id the room gave it) and nothing else: every message back to the client names
/// both sides by player id. See <c>TradingOpenEventMessageComposer.UserId</c>.
/// </para></summary>
internal sealed class RoomTradeSession
{
    public required PlayerId UserOneId { get; init; }
    public required PlayerId UserTwoId { get; init; }

    /// <summary>Only the open path reads these; nothing is sent to the client with them.</summary>
    public required RoomObjectId UserOneObjectId { get; init; }
    public required RoomObjectId UserTwoObjectId { get; init; }

    public List<int> UserOneItemIds { get; } = [];
    public List<int> UserTwoItemIds { get; } = [];

    public bool UserOneAccepted { get; set; }
    public bool UserTwoAccepted { get; set; }
    public bool UserOneConfirmed { get; set; }
    public bool UserTwoConfirmed { get; set; }

    public TradePhase Phase { get; set; } = TradePhase.Building;

    public bool IsUserOne(PlayerId id) => UserOneId == id;

    public bool IsParticipant(PlayerId id) => UserOneId == id || UserTwoId == id;

    public PlayerId OtherOf(PlayerId id) => UserOneId == id ? UserTwoId : UserOneId;

    public RoomObjectId ObjectIdOf(PlayerId id) =>
        IsUserOne(id) ? UserOneObjectId : UserTwoObjectId;

    public List<int> ItemsOf(PlayerId id) => IsUserOne(id) ? UserOneItemIds : UserTwoItemIds;

    public bool AcceptedOf(PlayerId id) => IsUserOne(id) ? UserOneAccepted : UserTwoAccepted;

    public bool ConfirmedOf(PlayerId id) => IsUserOne(id) ? UserOneConfirmed : UserTwoConfirmed;

    public void SetAccepted(PlayerId id, bool value)
    {
        if (IsUserOne(id))
        {
            UserOneAccepted = value;
        }
        else
        {
            UserTwoAccepted = value;
        }
    }

    public void SetConfirmed(PlayerId id, bool value)
    {
        if (IsUserOne(id))
        {
            UserOneConfirmed = value;
        }
        else
        {
            UserTwoConfirmed = value;
        }
    }

    /// <summary>Clears both accept and confirm flags and drops the trade back to the building phase.
    /// Any change to either offer invalidates the other party's prior acceptance, mirroring the
    /// client, which resets its own accept state whenever a fresh item list arrives.</summary>
    public void ResetAgreement()
    {
        UserOneAccepted = false;
        UserTwoAccepted = false;
        UserOneConfirmed = false;
        UserTwoConfirmed = false;
        Phase = TradePhase.Building;
    }

    public bool BothAccepted => UserOneAccepted && UserTwoAccepted;

    public bool BothConfirmed => UserOneConfirmed && UserTwoConfirmed;
}
