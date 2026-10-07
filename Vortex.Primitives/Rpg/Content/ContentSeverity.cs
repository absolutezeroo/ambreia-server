using System.Text.Json.Serialization;

namespace Vortex.Primitives.Rpg.Content;

/// <summary>An error refuses the whole import; a warning lets it through (spec Contenu §6). Written as text.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContentSeverity>))]
public enum ContentSeverity
{
    Error = 0,
    Warning = 1,
}
