using System;
using System.Collections.Generic;
using Vortex.Primitives.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Validation;

namespace Vortex.Rpg.Content.Kinds;

/// <summary>A kind bound to its record: parsing and checking are typed.</summary>
public abstract class ContentKind<TDefinition> : IContentKind
    where TDefinition : ContentDefinition
{
    public abstract string Kind { get; }

    public Type DefinitionType => typeof(TDefinition);

    public ContentDefinition Parse(string json) => ContentJson.Deserialize<TDefinition>(json);

    public IEnumerable<ContentFinding> Check(ContentDefinition definition, ContentCheck check) =>
        Check((TDefinition)definition, check);

    protected abstract IEnumerable<ContentFinding> Check(
        TDefinition definition,
        ContentCheck check
    );

    protected static ContentFinding Error(string code, string id, string message) =>
        new(ContentSeverity.Error, code, null, id, message);

    protected static ContentFinding Warning(string code, string id, string message) =>
        new(ContentSeverity.Warning, code, null, id, message);
}
