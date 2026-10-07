using System.Text.Json.Serialization;

namespace Vortex.Primitives.World;

/// <summary>Written as text by every serializer: the editor reads "Error", not 0.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ZoneSeverity>))]
public enum ZoneSeverity
{
    Error,
    Warning,
}
