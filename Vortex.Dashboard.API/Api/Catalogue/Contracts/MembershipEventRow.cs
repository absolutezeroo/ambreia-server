using System;

namespace Vortex.Dashboard.API.Api.Catalogue.Contracts;

/// <summary>
/// One club purchase, renewal or expiry.
/// </summary>
/// <remarks>
/// Everything from <paramref name="Months"/> down is read out of the audit event's JSON payload, so
/// each is null on an event whose payload did not carry it -- an expiry, most often, which records
/// that the subscription ended and nothing about what it cost.
/// </remarks>
public sealed record MembershipEventRow(
    DateTime OccurredAt,
    string Action,
    int? ActorPlayerId,
    string? ActorPlayerName,
    int? Months,
    int? TotalMonths,
    int? CreditCost,
    bool? IsRenewal,
    bool? IsVip
);
