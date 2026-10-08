using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;

namespace Vortex.WebApi.Http.Errors;

/// <summary>403 — a session that is real but is not allowed this.</summary>
public sealed class ForbiddenError(string errorCode)
    : ApiErrorResult(errorCode),
        IEndpointMetadataProvider
{
    public override int StatusCode => StatusCodes.Status403Forbidden;

    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder) =>
        Declare(builder, StatusCodes.Status403Forbidden);
}
