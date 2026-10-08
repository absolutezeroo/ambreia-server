using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;

namespace Vortex.WebApi.Http.Errors;

/// <summary>401 — no session, or a bad password.</summary>
public sealed class UnauthorizedError(string errorCode)
    : ApiErrorResult(errorCode),
        IEndpointMetadataProvider
{
    public override int StatusCode => StatusCodes.Status401Unauthorized;

    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder) =>
        Declare(builder, StatusCodes.Status401Unauthorized);
}
