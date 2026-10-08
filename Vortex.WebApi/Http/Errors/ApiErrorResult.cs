using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Vortex.WebApi.Http.Responses;

namespace Vortex.WebApi.Http.Errors;

/// <summary>
/// A refusal that carries <see cref="ApiErrorResponse"/> at a status ASP.NET Core has no typed
/// result for.
/// </summary>
/// <remarks>
/// <para>
/// <c>TypedResults</c> covers 200/400/404 with a body — <c>Ok&lt;T&gt;</c>,
/// <c>BadRequest&lt;T&gt;</c>, <c>NotFound&lt;T&gt;</c> — and each declares its own status and shape,
/// so a handler returning one cannot disagree with the document. It has NO equivalent for 401 or 403
/// WITH a body: <c>TypedResults.Unauthorized()</c> and <c>Forbid()</c> write no content.
/// </para>
/// <para>
/// The body at 401 is load-bearing here: the site tells a wrong password
/// (<c>pocket.auth.invalid_login</c>) apart from a missing session by that string alone.
/// <c>TypedResults.Json(…, statusCode: 401)</c> would compile, but
/// <c>JsonHttpResult&lt;T&gt;</c> reports its status to the API explorer as 200 — the document would
/// claim a 200 that never happens and omit the 401 that does.
/// </para>
/// </remarks>
public abstract class ApiErrorResult : IResult, IStatusCodeHttpResult
{
    private readonly ApiErrorResponse _body;

    private protected ApiErrorResult(string errorCode) => _body = new ApiErrorResponse(errorCode);

    public abstract int StatusCode { get; }

    int? IStatusCodeHttpResult.StatusCode => StatusCode;

    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.StatusCode = StatusCode;

        return httpContext.Response.WriteAsJsonAsync(_body);
    }

    /// <summary>
    /// Declares one status against <see cref="ApiErrorResponse"/>. Each subclass passes its own, so
    /// a handler's declared union names exactly the statuses it can answer with — a single type
    /// covering both 401 and 403 would document a 403 on every route that can only refuse with a
    /// 401, which is the kind of near-miss the typed results were adopted to stop.
    /// </summary>
    private protected static void Declare(EndpointBuilder builder, int statusCode)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Metadata.Add(
            new ProducesResponseTypeMetadata(
                statusCode,
                typeof(ApiErrorResponse),
                new[] { "application/json" }
            )
        );
    }
}
