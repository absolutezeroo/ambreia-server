using System;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Players.Enums;

namespace Vortex.Inventory.Fulfillment;

/// <summary>
/// Applies bought Builders Club days and furni to the player's subscription row. Pure, so the three
/// cases the client tells apart are testable without a database.
/// </summary>
internal static class BuildersClubGrant
{
    /// <summary>
    /// Adds <paramref name="days"/> and <paramref name="furni"/> to <paramref name="row"/>, creating
    /// it when the player never had one.
    /// </summary>
    /// <returns>
    /// The row to save, and the notification the client shows for it:
    /// <c>builders_club.membership_made</c> the first time, <c>_renewed</c> after a lapse,
    /// <c>_extended</c> while still a member — or null when no days were bought.
    /// </returns>
    public static (PlayerSubscriptionEntity Row, string? Notification) Apply(
        PlayerSubscriptionEntity? row,
        int playerId,
        int days,
        int furni,
        DateTime now
    )
    {
        string? notification = null;

        if (row is null)
        {
            row = new PlayerSubscriptionEntity
            {
                PlayerEntityId = playerId,
                SubscriptionType = SubscriptionType.BuildersClub,
                ExpiresAt = now,
                PlayerEntity = null!,
            };

            if (days > 0)
            {
                notification = "builders_club.membership_made";
            }
        }
        else
        {
            // A row written before days were counted: credit what it already spans, or the first
            // purchase would make the membership look as short as that purchase.
            if (row.TotalDays == 0)
            {
                DateTime start = row.FirstSubscribedAt ?? row.CreatedAt;

                row.TotalDays = Math.Max(0, (int)Math.Ceiling((row.ExpiresAt - start).TotalDays));
            }

            if (days > 0)
            {
                notification =
                    row.ExpiresAt > now
                        ? "builders_club.membership_extended"
                        : "builders_club.membership_renewed";
            }
        }

        if (days > 0)
        {
            row.ExpiresAt = (row.ExpiresAt > now ? row.ExpiresAt : now).AddDays(days);
            row.TotalDays += days;
            row.FirstSubscribedAt ??= now;
        }

        row.BonusFurniLimit += Math.Max(0, furni);

        return (row, notification);
    }
}
