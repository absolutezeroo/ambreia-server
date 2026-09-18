using Vortex.Dashboard.API.Hosting;

namespace Vortex.Dashboard.API.Operations.Hotel.Contracts;

/// <summary><c>ClientStyleId</c> is the id in the client's own chat-style library, not a row id.</summary>
public sealed record ChatStyleGrantRequest(int PlayerId, int ClientStyleId, string Reason)
    : IReasonedRequest;
