namespace Vortex.Revisions.Configuration;

/// <summary>
///     Wire-safety bounds on client-declared collection sizes inside packet parsers. These guard
///     against malformed/hostile length-prefixed fields, not business policy (see
///     <c>Vortex.Social.Configuration.MessengerConfig</c> for the account-level friend cap).
/// </summary>
public sealed class ProtocolLimitsConfig
{
    public const string SECTION_NAME = "Vortex:Protocol";

    public int MaxFriendRemovalIds { get; init; } = 1000;
    public int MaxRoomTags { get; init; } = 100;

    /// <summary>Wire-safety ceiling on a single <c>AddItemsToTrade</c> batch. The real per-trade
    /// item cap is business policy enforced in the trade session; this only bounds the length-prefixed
    /// array so a hostile client can't force a huge allocation.</summary>
    public int MaxTradeItems { get; init; } = 1500;

    /// <summary>Wire-safety ceiling on a submitted quiz. The real quizzes are ten and thirteen
    /// questions; this only stops a hostile client declaring a length it will not send.</summary>
    public int MaxQuizAnswers { get; init; } = 100;

    /// <summary>Wire-safety ceiling on a single <c>MakeOffer</c> batch. The client lists a whole
    /// inventory selection at one price, so the array is as large as the strip; how many offers a
    /// seller may actually hold is business policy enforced in the marketplace grain.</summary>
    public int MaxMarketplaceOfferItems { get; init; } = 1500;
}
