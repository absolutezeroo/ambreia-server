namespace Vortex.Players.Configuration;

/// <summary>
/// The three numbers behind the respect buttons, served live from <c>IServerConfigGrain</c> so an
/// operator can change them without a deploy. Declared here as key/default pairs in the same shape
/// as <see cref="ClubConfig"/>; the dashboard editor learns about them from
/// <c>ConfigKeyCatalog</c>.
/// </summary>
/// <remarks>
/// All three reach the client on the user object and nowhere else, and the client counts down from
/// them on its own — it never asks again for the rest of the session. That is why they are read
/// once, at <c>InfoRetrieve</c>, and why the server re-checks every budget itself rather than
/// trusting the count the client believes it has.
/// </remarks>
public static class RespectConfig
{
    /// <summary>Respects a player may give to other players per day.</summary>
    public const string DailyLimitKey = "respect.daily_limit";
    public const int DailyLimitDefault = 3;

    /// <summary>Pets a player may scratch per day, on top of the per-pet cap the room enforces.</summary>
    public const string PetDailyLimitKey = "respect.pet_daily_limit";
    public const int PetDailyLimitDefault = 3;

    /// <summary>
    /// How many times a day a player may refill an exhausted respect budget. Zero switches the
    /// feature off: the client only offers the refill button while this is above zero.
    /// </summary>
    public const string DailyReplenishesKey = "respect.daily_replenishes";
    public const int DailyReplenishesDefault = 0;
}
