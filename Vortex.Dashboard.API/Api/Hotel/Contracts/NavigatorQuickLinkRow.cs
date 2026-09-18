namespace Vortex.Dashboard.API.Api.Hotel.Contracts;

/// <summary>One quick link under a tab, with the same known-code check.</summary>
public sealed record NavigatorQuickLinkRow(
    int Id,
    string SearchCode,
    string Filter,
    string Localization,
    int QueryType,
    string QueryTypeLabel,
    int OrderNum,
    bool ForceClosed,
    bool KnownCode
);
