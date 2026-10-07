using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vortex.Primitives.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Kinds;

namespace Vortex.Rpg.Content.Validation;

/// <summary>One file → its definition and the kind that read it, or the finding that stops it
/// (spec Contenu §9, level 1).</summary>
internal static class ContentParser
{
    public const char Bom = (char)0xFEFF;

    private static readonly JsonDocumentOptions Strict = new() { AllowDuplicateProperties = false };

    public static (
        ContentDefinition? Definition,
        IContentKind? Kind,
        ContentFinding? Finding
    ) Parse(ContentFile file, IReadOnlyDictionary<string, IContentKind> kinds)
    {
        if (file.Text is null)
        {
            return (null, null, Error("Json", file, null, "The file is empty."));
        }

        JsonObject document;

        try
        {
            document =
                JsonNode.Parse(file.Text.TrimStart(Bom), documentOptions: Strict) as JsonObject
                ?? throw new JsonException("A document is a JSON object.");
        }
        catch (JsonException e)
        {
            return (null, null, Error("Json", file, null, e.Message));
        }

        // Editor autocompletion only (spec Contenu §4.2): never part of the definition.
        document.Remove("$schema");

        if (
            document["kind"] is not JsonValue kindValue
            || !kindValue.TryGetValue(out string? kindName)
        )
        {
            return (null, null, Error("NoKind", file, null, "The document has no \"kind\"."));
        }

        if (!kinds.TryGetValue(kindName, out IContentKind? kind))
        {
            return (
                null,
                null,
                Error("UnknownKind", file, null, $"Kind '{kindName}' does not exist.")
            );
        }

        ContentDefinition definition;

        try
        {
            definition = kind.Parse(document.ToJsonString());
        }
        catch (JsonException e)
        {
            return (null, null, Error("Json", file, null, e.Message));
        }

        if (!ContentIds.IsValid(definition.Id))
        {
            return (
                null,
                null,
                Error(
                    "BadId",
                    file,
                    definition.Id,
                    "An id is lowercase segments joined by dots, at most 64 characters."
                )
            );
        }

        if (ContentIds.KindOf(definition.Id) != kind.IdPrefix)
        {
            return (
                null,
                null,
                Error(
                    "IdKindMismatch",
                    file,
                    definition.Id,
                    $"A {kind.Kind} id starts with '{kind.IdPrefix}.'."
                )
            );
        }

        return (definition, kind, null);
    }

    private static ContentFinding Error(
        string code,
        ContentFile file,
        string? id,
        string message
    ) => new(ContentSeverity.Error, code, file.Path, id, message);
}
