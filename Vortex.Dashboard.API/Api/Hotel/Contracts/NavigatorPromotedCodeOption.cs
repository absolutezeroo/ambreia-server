namespace Vortex.Dashboard.API.Api.Hotel.Contracts;

/// <summary>
/// One promoted-group code the client can put a name to, and the English wording it uses. Offered
/// as a list because the client localizes <c>promotedroomcategory.&lt;code&gt;</c> with no fallback:
/// a code typed by hand renders as the raw key.
/// </summary>
/// <param name="Taken">Already used by an existing group. One code can only title one group.</param>
public sealed record NavigatorPromotedCodeOption(string Code, string ClientText, bool Taken);
