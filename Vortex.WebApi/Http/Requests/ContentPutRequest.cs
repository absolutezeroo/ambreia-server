using System.Collections.Generic;

namespace Vortex.WebApi.Http.Requests;

/// <summary>
/// A document written through PUT /api/content/documents/{id} (content authoring §4): the version it
/// was read at, its kind, optionally its path, its source, and its texts by language.
/// </summary>
public sealed record ContentPutRequest(
    long BaseVersion,
    string? Kind,
    string? Path,
    string? Source,
    Dictionary<string, Dictionary<string, string>>? Texts
)
{
    public bool IsValid => !string.IsNullOrEmpty(Kind) && !string.IsNullOrEmpty(Source);
}
