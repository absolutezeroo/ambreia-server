using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vortex.Revisions.Revision20260701;

namespace Vortex.Revisions;

/// <summary>
/// The dev revision's header ids by name, read from <c>Headers.cs</c> itself, so a release table is
/// checked against exactly what this server maps rather than against a copy of the list.
/// </summary>
public static class DevHeaders
{
    public static IReadOnlyDictionary<string, int> Events { get; } = Read(typeof(MessageEvent));

    public static IReadOnlyDictionary<string, int> Composers { get; } =
        Read(typeof(MessageComposer));

    private static Dictionary<string, int> Read(Type headers) =>
        headers
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(int))
            .ToDictionary(field => field.Name, field => (int)field.GetRawConstantValue()!);
}
