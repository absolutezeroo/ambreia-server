using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http.Metadata;

namespace Vortex.WebApi.Http.Errors;

/// <summary>
/// The metadata shape the API explorer reads. Declared here because the framework's own
/// implementation is internal.
/// </summary>
internal sealed class ProducesResponseTypeMetadata(
    int statusCode,
    Type type,
    IEnumerable<string> contentTypes
) : IProducesResponseTypeMetadata
{
    public int StatusCode { get; } = statusCode;

    public Type? Type { get; } = type;

    public IEnumerable<string> ContentTypes { get; } = contentTypes;
}
