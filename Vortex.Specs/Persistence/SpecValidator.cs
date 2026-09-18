using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vortex.Specs.Model;
using Vortex.Specs.Yaml;

namespace Vortex.Specs.Persistence;

public enum ValidationSeverity
{
    Warning,
    Error,
}

public sealed record ValidationIssue(ValidationSeverity Severity, string Path, string Message);

/// <summary>
/// Checks the spec tree for the mistakes that would make it untrustworthy.
/// </summary>
/// <remarks>
/// The rules are all variations on one theme: a spec is only useful if a reader can tell how much of
/// it is known. So it is an error for a claim to outrank the evidence behind it, for an evidence id
/// to point at nothing, or for a header id to appear where a symbolic name belongs — that last one
/// because ids are per-revision and a behavioural spec written against one becomes quietly wrong the
/// day the hotel changes build.
/// </remarks>
public sealed class SpecValidator
{
    public IReadOnlyList<ValidationIssue> Validate(SpecStore store)
    {
        List<ValidationIssue> issues = [];
        HashSet<string> knownEvidence = new(StringComparer.Ordinal);
        List<(string Path, YamlMapping Document)> documents = [];

        foreach (string file in store.Enumerate())
        {
            YamlMapping document;

            try
            {
                document = YamlReader.ReadMapping(File.ReadAllText(file));
            }
            catch (YamlParseException error)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, file, error.Message));
                continue;
            }

            documents.Add((file, document));
            CollectEvidenceIds(document, knownEvidence);
        }

        foreach ((string path, YamlMapping document) in documents)
        {
            issues.AddRange(ValidateDocument(path, document, knownEvidence));
        }

        return
        [
            .. issues
                .OrderByDescending(i => i.Severity)
                .ThenBy(i => i.Path, StringComparer.Ordinal)
                .ThenBy(i => i.Message, StringComparer.Ordinal),
        ];
    }

    private static IEnumerable<ValidationIssue> ValidateDocument(
        string path,
        YamlMapping document,
        IReadOnlySet<string> knownEvidence
    )
    {
        if (document.String("spec") is null)
        {
            yield return new ValidationIssue(
                ValidationSeverity.Error,
                path,
                "no 'spec' key: the document does not say what kind of spec it is"
            );
        }

        if (document.Int("spec_version") is not int version)
        {
            yield return new ValidationIssue(
                ValidationSeverity.Error,
                path,
                "no 'spec_version' key"
            );
        }
        else if (version > SpecConstants.SpecFormatVersion)
        {
            yield return new ValidationIssue(
                ValidationSeverity.Error,
                path,
                $"written by a newer format (v{version}); this build understands v{SpecConstants.SpecFormatVersion}"
            );
        }

        if (document[GeneratedKeyName] is not YamlNode generated)
        {
            yield return new ValidationIssue(
                ValidationSeverity.Error,
                path,
                "no 'generated' block"
            );
            yield break;
        }

        string? storedDigest = document.String("generated_digest");

        if (storedDigest is null)
        {
            yield return new ValidationIssue(
                ValidationSeverity.Error,
                path,
                "no 'generated_digest': hand edits to the generated block cannot be detected"
            );
        }
        else if (
            !string.Equals(SpecStore.Digest(generated), storedDigest, StringComparison.Ordinal)
        )
        {
            yield return new ValidationIssue(
                ValidationSeverity.Warning,
                path,
                "the generated block has been hand-edited; the next scan will refuse to overwrite it. "
                    + "Move the change into 'verified' or 're-run with --force' once it is upstreamed"
            );
        }

        foreach (ValidationIssue issue in ValidateConfidences(path, generated))
        {
            yield return issue;
        }

        foreach (ValidationIssue issue in ValidateEvidenceLinks(path, generated, knownEvidence))
        {
            yield return issue;
        }

        foreach (ValidationIssue issue in ValidateNoRawHeaderIds(path, document, generated))
        {
            yield return issue;
        }
    }

    private const string GeneratedKeyName = "generated";

    /// <summary>
    /// A claim may never carry more confidence than the strongest authority cited beside it.
    /// </summary>
    private static IEnumerable<ValidationIssue> ValidateConfidences(string path, YamlNode node)
    {
        foreach (YamlMapping mapping in Mappings(node))
        {
            foreach (KeyValuePair<string, YamlNode> entry in mapping.Entries)
            {
                if (
                    !entry.Key.Contains("confidence", StringComparison.Ordinal)
                    && entry.Key != "status"
                )
                {
                    continue;
                }

                if (entry.Value is not YamlScalar scalar || scalar.Value is null)
                {
                    continue;
                }

                // A scenario's `status` is an outcome, not a confidence. The two share the key and
                // share the word `unknown`, which is why every scenario validated cleanly right up
                // until a capture let one of them say `success`.
                if (entry.Key == "status" && ScenarioOutcomeNames.IsOutcome(scalar.Value))
                {
                    continue;
                }

                if (!SpecNames.TryParseConfidence(scalar.Value, out Confidence confidence))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error,
                        path,
                        $"'{entry.Key}: {scalar.Value}' is not one of the confidence levels"
                    );
                    continue;
                }

                if (confidence < Confidence.MultiReferenceConfirmed)
                {
                    continue;
                }

                string? authority = mapping.String("authority");

                if (
                    authority is not null
                    && SpecNames.TryParseAuthority(authority, out EvidenceAuthority parsed)
                    && confidence > ConfidencePolicy(parsed)
                )
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error,
                        path,
                        $"'{entry.Key}: {scalar.Value}' outranks the '{authority}' evidence next to it"
                    );
                }
            }
        }
    }

    private static Confidence ConfidencePolicy(EvidenceAuthority authority) =>
        Reasoning.ConfidencePolicy.FromAuthority(authority);

    private static IEnumerable<ValidationIssue> ValidateEvidenceLinks(
        string path,
        YamlNode node,
        IReadOnlySet<string> knownEvidence
    )
    {
        foreach (YamlMapping mapping in Mappings(node))
        {
            foreach (KeyValuePair<string, YamlNode> entry in mapping.Entries)
            {
                if (entry.Key is not ("evidence" or "known_evidence"))
                {
                    continue;
                }

                foreach (string reference in EvidenceReferences(entry.Value))
                {
                    if (!knownEvidence.Contains(reference))
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Warning,
                            path,
                            $"evidence id '{reference}' is cited but defined in no spec file"
                        );
                    }
                }
            }
        }
    }

    /// <summary>
    /// Behavioural specs speak in symbolic names. A numeric header in one would tie it to a single
    /// client build without saying so — the exact failure this format's separate revision registries
    /// exist to prevent.
    /// </summary>
    private static IEnumerable<ValidationIssue> ValidateNoRawHeaderIds(
        string path,
        YamlMapping document,
        YamlNode generated
    )
    {
        if (document.String("spec") is "revision")
        {
            yield break;
        }

        foreach (YamlMapping mapping in Mappings(generated))
        {
            foreach (KeyValuePair<string, YamlNode> entry in mapping.Entries)
            {
                if (entry.Key is "header" or "header_id" or "opcode")
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error,
                        path,
                        $"'{entry.Key}' carries a header id; behavioural specs must name packets "
                            + "symbolically and leave ids to the revision registries"
                    );
                }
            }
        }
    }

    private static void CollectEvidenceIds(YamlNode node, HashSet<string> sink)
    {
        foreach (YamlMapping mapping in Mappings(node))
        {
            string? id = mapping.String("id");

            if (id is not null && id.StartsWith("ev_", StringComparison.Ordinal))
            {
                sink.Add(id);
            }
        }
    }

    private static IEnumerable<string> EvidenceReferences(YamlNode node)
    {
        switch (node)
        {
            case YamlScalar { Value: { } value }
                when value.StartsWith("ev_", StringComparison.Ordinal):
                yield return value;
                break;

            case YamlSequence sequence:
                foreach (YamlNode item in sequence.Items)
                {
                    foreach (string reference in EvidenceReferences(item))
                    {
                        yield return reference;
                    }
                }

                break;
        }
    }

    private static IEnumerable<YamlMapping> Mappings(YamlNode node)
    {
        switch (node)
        {
            case YamlMapping mapping:
                yield return mapping;

                foreach (KeyValuePair<string, YamlNode> entry in mapping.Entries)
                {
                    foreach (YamlMapping nested in Mappings(entry.Value))
                    {
                        yield return nested;
                    }
                }

                break;

            case YamlSequence sequence:
                foreach (YamlNode item in sequence.Items)
                {
                    foreach (YamlMapping nested in Mappings(item))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }
}
