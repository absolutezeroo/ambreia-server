using Vortex.Primitives.Rpg.Content;

namespace Vortex.Rpg.Content.Migrations;

/// <summary>One line of <c>migrations.json</c>: <c>From</c> becomes <c>To</c>, or is retired.</summary>
public sealed record ContentMigrationRule(string From, ContentMigrationAction Action, string? To);
