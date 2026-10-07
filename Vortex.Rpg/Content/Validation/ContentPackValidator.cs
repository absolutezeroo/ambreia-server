using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Vortex.Primitives.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Kinds;
using Vortex.Rpg.Content.Migrations;

namespace Vortex.Rpg.Content.Validation;

/// <summary>
/// Reads a whole pack and says everything wrong with it at once (spec Contenu §9). Never throws:
/// a broken pack comes back as findings.
/// </summary>
public sealed class ContentPackValidator(IEnumerable<IContentKind> kinds)
{
    private const string MigrationsFile = "migrations.json";
    private const string French = "fr";

    private readonly IReadOnlyDictionary<string, IContentKind> _kinds = kinds.ToDictionary(k =>
        k.Kind
    );

    public ValidatedPack Validate(ContentPack pack, Func<int, bool> furnitureExists)
    {
        List<ContentFinding> findings = [];
        Dictionary<string, ContentDefinition> definitions = new(StringComparer.Ordinal);
        Dictionary<string, string> paths = new(StringComparer.Ordinal);
        Dictionary<string, IContentKind> kindOf = new(StringComparer.Ordinal);

        // The pack arrives from the web: a null the type does not allow is still possible. No file
        // list is not an empty pack, which would remove every definition.
        if (pack.Files is null)
        {
            findings.Add(Error("Json", null, null, "The pack has no file list."));
        }

        foreach (ContentFile? file in pack.Files ?? [])
        {
            if (file is null)
            {
                findings.Add(Error("Json", null, null, "A file of the pack is null."));
                continue;
            }

            (ContentDefinition? definition, IContentKind? kind, ContentFinding? finding) =
                ContentParser.Parse(file, _kinds);

            if (finding is not null)
            {
                findings.Add(finding);
            }
            else if (paths.TryGetValue(definition!.Id, out string? first))
            {
                findings.Add(
                    Error(
                        "DuplicateId",
                        file.Path,
                        definition.Id,
                        $"'{definition.Id}' is defined in {first} and in {file.Path}."
                    )
                );
            }
            else
            {
                definitions[definition.Id] = definition;
                paths[definition.Id] = file.Path;
                kindOf[definition.Id] = kind!;
            }
        }

        ContentCheck check = new(definitions, furnitureExists);

        foreach (ContentDefinition definition in definitions.Values)
        {
            findings.AddRange(
                kindOf[definition.Id]
                    .Check(definition, check)
                    .Select(f => f with { File = paths[definition.Id] })
            );
        }

        IReadOnlyList<ContentMigrationRule> rules = ReadMigrations(
            pack.Migrations,
            definitions,
            findings
        );
        Dictionary<string, IReadOnlyDictionary<string, string>> texts = (
            pack.Texts ?? new Dictionary<string, string>()
        ).ToDictionary(t => t.Key, t => ContentTexts.Parse(t.Value ?? string.Empty));

        CheckTexts(texts, definitions, findings);

        return new ValidatedPack(definitions, rules, texts, findings);
    }

    private static IReadOnlyList<ContentMigrationRule> ReadMigrations(
        string? json,
        IReadOnlyDictionary<string, ContentDefinition> definitions,
        List<ContentFinding> findings
    )
    {
        json = json?.TrimStart(ContentParser.Bom);

        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        List<ContentMigrationRule?> rules;

        try
        {
            rules = ContentJson.Deserialize<List<ContentMigrationRule?>>(json);
        }
        catch (JsonException e)
        {
            findings.Add(Error("Json", MigrationsFile, null, e.Message));
            return [];
        }

        foreach (ContentMigrationRule? rule in rules)
        {
            if (rule is null)
            {
                findings.Add(
                    Error("BrokenMigration", MigrationsFile, null, "A rule is an object.")
                );
            }
            else if (!ContentIds.IsValid(rule.From) || rule.To is { } to && !ContentIds.IsValid(to))
            {
                findings.Add(
                    Error("BrokenMigration", MigrationsFile, rule.From, "A rule names valid ids.")
                );
            }
            else if (
                rule.Action == ContentMigrationAction.Replace
                && (rule.To is null || !definitions.ContainsKey(rule.To))
            )
            {
                findings.Add(
                    Error(
                        "BrokenMigration",
                        MigrationsFile,
                        rule.From,
                        $"'{rule.From}' is replaced by '{rule.To}', which is not in the pack."
                    )
                );
            }
            else if (
                rule.To is not null
                && ContentIds.KindOf(rule.From) != ContentIds.KindOf(rule.To)
            )
            {
                findings.Add(
                    Error(
                        "MigrationKindMismatch",
                        MigrationsFile,
                        rule.From,
                        $"'{rule.From}' cannot become '{rule.To}', of another kind."
                    )
                );
            }
        }

        return [.. rules.OfType<ContentMigrationRule>()];
    }

    private static void CheckTexts(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> texts,
        IReadOnlyDictionary<string, ContentDefinition> definitions,
        List<ContentFinding> findings
    )
    {
        if (!texts.TryGetValue(French, out IReadOnlyDictionary<string, string>? fr))
        {
            // An empty database starts with an empty registry (spec Contenu §6).
            if (definitions.Count == 0)
            {
                return;
            }

            findings.Add(
                Error("NoFrenchTexts", "texts/fr.txt", null, "French is the reference language.")
            );
            return;
        }

        foreach (string id in definitions.Keys)
        {
            foreach (string key in new[] { $"{id}.name", $"{id}.description" })
            {
                if (!fr.ContainsKey(key))
                {
                    findings.Add(
                        Warning("MissingText", "texts/fr.txt", id, $"'{key}' has no French text.")
                    );
                }
            }
        }

        foreach (string key in fr.Keys)
        {
            int dot = key.LastIndexOf('.');

            if (dot <= 0 || !definitions.ContainsKey(key[..dot]))
            {
                findings.Add(
                    Warning("UnusedText", "texts/fr.txt", null, $"'{key}' names no content.")
                );
            }
        }
    }

    private static ContentFinding Error(string code, string? file, string? id, string message) =>
        new(ContentSeverity.Error, code, file, id, message);

    private static ContentFinding Warning(string code, string file, string? id, string message) =>
        new(ContentSeverity.Warning, code, file, id, message);
}
