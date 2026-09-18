using System;

namespace Vortex.Players;

/// <summary>
/// Pure daily-respect-budget rule, extracted from <c>PlayerGrain</c> so the reset/limit edges can be
/// unit-tested. The budget resets whenever the stored reset date is not the current day.
/// </summary>
public static class RespectBudget
{
    /// <summary>
    /// Evaluates whether a respect can be given. Returns whether it is allowed plus the updated
    /// given-today count and reset date to store.
    /// </summary>
    public static (bool Allowed, int GivenToday, DateTime ResetDate) TryConsume(
        int givenToday,
        DateTime? resetDate,
        DateTime now,
        int dailyLimit
    )
    {
        DateTime today = now.Date;
        int given = resetDate?.Date == today ? givenToday : 0;

        if (given >= dailyLimit)
        {
            return (false, given, today);
        }

        return (true, given + 1, today);
    }

    /// <summary>
    /// What is left of today's budget. The client is told this number once, at login, and counts
    /// down on its own — it is what decides whether the respect button is drawn at all.
    /// </summary>
    public static int Remaining(int givenToday, DateTime? resetDate, DateTime now, int dailyLimit)
    {
        int given = resetDate?.Date == now.Date ? givenToday : 0;

        return Math.Max(0, dailyLimit - given);
    }
}
