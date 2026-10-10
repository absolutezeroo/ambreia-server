using System;
using System.Collections.Generic;
using Vortex.Primitives.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Validation;

namespace Vortex.Rpg.Content.Kinds;

/// <summary>
/// One kind of content (spec Contenu §4.3): its name, its record and its rules. Declared in the
/// container by the module that owns it (<c>services.AddSingleton&lt;IContentKind, ItemKind&gt;()</c>).
/// </summary>
public interface IContentKind
{
    /// <summary>The kind as written in documents and as the first segment of their ids.</summary>
    public string Kind { get; }

    /// <summary>The first segment of this kind's ids: the kind itself, except <c>spec</c> for
    /// specializations (spec Contenu §4.1).</summary>
    public string IdPrefix { get; }

    /// <summary>Served to anyone at <c>/api/public/content/definitions/{kind}</c>: what the client
    /// draws (skills, statuses). False keeps a kind server-side (monsters, loot).</summary>
    public bool Public { get; }

    public Type DefinitionType { get; }

    /// <exception cref="System.Text.Json.JsonException">The document does not fit the record.</exception>
    public ContentDefinition Parse(string json);

    /// <summary>Text keys the definition uses besides its name and description (a monster's yells,
    /// spec M1c §5.4): each one without a French text is a warning.</summary>
    public IEnumerable<string> TextKeys(ContentDefinition definition);

    /// <summary>The kind's own rules and its references (spec Contenu §9, levels 2 and 3).</summary>
    public IEnumerable<ContentFinding> Check(ContentDefinition definition, ContentCheck check);
}
