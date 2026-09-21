using Vortex.Dashboard.API.Hosting;

namespace Vortex.Dashboard.API.Operations.Hotel.Contracts;

/// <summary>
/// Taking one post down from the website's social feed, whoever wrote it.
/// </summary>
/// <remarks>
/// Its comments go with it, including other players' — a comment is unreadable out of the thread
/// it answers, and the feed lists roots only, so one left behind would sit in nobody's feed.
/// <c>Reason</c> is what the journal records beside the operator's name; it is the whole reason
/// this is an operation rather than a call straight to the service.
/// </remarks>
public sealed record RemoveFeedPostRequest(int PostId, string Reason) : IReasonedRequest;
