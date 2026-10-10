using System;

namespace Vortex.Primitives.World.Rules;

/// <summary>A declaration with nothing behind it in DI: the rooms declare their own rules so.</summary>
public sealed record DeclaredZoneRule(Type RuleType) : IZoneRuleDeclaration;
