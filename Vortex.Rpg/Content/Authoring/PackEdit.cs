using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vortex.Primitives.Rpg.Content;
using Vortex.Rpg.Content.Kinds;

namespace Vortex.Rpg.Content.Authoring;

/// <summary>
/// One write applied to the stored pack (content authoring §4): the pack to import, every other
/// document, text and migration rule as stored. What is refused here never reaches the import; what
/// passes is still validated whole by it.
/// </summary>
public static class PackEdit
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static PackEditResult Put(
        ContentExport stored,
        IContentKind kind,
        string id,
        string? path,
        string source,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? texts
    )
    {
        if (!ContentIds.IsValid(id) || ContentIds.KindOf(id) != kind.IdPrefix)
        {
            return PackEditResult.Refused(PackEditResult.IdMismatch);
        }

        if (DocumentSource.Parse(source) is not { } document)
        {
            return PackEditResult.Refused(PackEditResult.BadSource);
        }

        if (
            DocumentSource.IdOf(source) != id
            || document["kind"] is JsonValue declared
                && declared.GetValueKind() == JsonValueKind.String
                && declared.GetValue<string>() != kind.Kind
        )
        {
            return PackEditResult.Refused(PackEditResult.IdMismatch);
        }

        List<ContentFile> files = [.. stored.Files];
        int at = files.FindIndex(f => DocumentSource.IdOf(f.Text) == id);
        string target = path ?? (at >= 0 ? files[at].Path : ContentPaths.Derive(kind.Kind, id));

        if (
            path is not null && !ContentPaths.IsSafe(kind.Kind, path)
            || files.Where((_, index) => index != at).Any(f => f.Path == target)
        )
        {
            return PackEditResult.Refused(PackEditResult.BadPath);
        }

        if (texts is not null && !DocumentTexts.IsValid(id, texts))
        {
            return PackEditResult.Refused(PackEditResult.BadText);
        }

        // Unchanged but for its layout: the file keeps the text it has, no diff in git.
        string text =
            at >= 0
            && DocumentSource.Parse(files[at].Text) is { } old
            && DocumentSource.Same(old, document)
                ? files[at].Text
                : DocumentSource.Format(kind.Kind, document);

        if (at >= 0)
        {
            files[at] = new ContentFile(target, text);
        }
        else
        {
            files.Add(new ContentFile(target, text));
        }

        return new PackEditResult(
            new ContentPack(files, stored.Migrations, Texts(stored, id, texts)),
            null
        );
    }

    public static PackEditResult Remove(ContentExport stored, string id, string? replaceWith)
    {
        if (replaceWith is not null && !ContentIds.IsValid(replaceWith))
        {
            return PackEditResult.Refused(PackEditResult.IdMismatch);
        }

        List<ContentFile> files = [.. stored.Files];
        int at = files.FindIndex(f => DocumentSource.IdOf(f.Text) == id);

        if (at < 0)
        {
            return PackEditResult.Refused(PackEditResult.UnknownDocument);
        }

        files.RemoveAt(at);

        Dictionary<string, IReadOnlyDictionary<string, string>> none =
            stored.Texts.Keys.ToDictionary(
                language => language,
                _ => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>()
            );

        return new PackEditResult(
            new ContentPack(
                files,
                replaceWith is null
                    ? stored.Migrations
                    : WithRule(stored.Migrations, id, replaceWith),
                Texts(stored, id, none)
            ),
            null
        );
    }

    /// <summary>Each language's file with the document's lines replaced; a language the write does
    /// not name is left as it is.</summary>
    private static Dictionary<string, string> Texts(
        ContentExport stored,
        string id,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? texts
    )
    {
        Dictionary<string, string> bodies = new(stored.Texts, StringComparer.Ordinal);

        if (texts is null)
        {
            return bodies;
        }

        // A longer id under this one (quest.camp.wolves under quest.camp) keeps its own lines.
        string[] nested =
        [
            .. stored
                .Files.Select(f => DocumentSource.IdOf(f.Text))
                .OfType<string>()
                .Where(other => other.StartsWith(id + ".", StringComparison.Ordinal)),
        ];

        foreach ((string language, IReadOnlyDictionary<string, string> lines) in texts)
        {
            bodies[language] = DocumentTexts.Rewrite(
                bodies.GetValueOrDefault(language, string.Empty),
                id,
                nested,
                lines
            );
        }

        return bodies;
    }

    private static string WithRule(string? migrations, string from, string to)
    {
        JsonArray rules =
            (migrations is null ? null : JsonNode.Parse(migrations) as JsonArray) ?? [];

        rules.Add(
            new JsonObject
            {
                ["from"] = from,
                ["action"] = "replace",
                ["to"] = to,
            }
        );

        return rules.ToJsonString(Indented) + "\n";
    }
}
