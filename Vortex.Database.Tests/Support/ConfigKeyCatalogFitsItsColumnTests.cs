using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Vortex.Database.Entities.Server;
using Vortex.Primitives.Server;
using Xunit;

namespace Vortex.Database.Tests.Support;

/// <summary>
/// A config key's description is written to the database beside its value, so it has to fit the
/// column.
/// </summary>
/// <remarks>
/// <para>
/// <c>ConfigOperations.SetAsync</c> passes <c>descriptor.Description</c> straight into
/// <c>IServerConfigGrain.SetValueAsync</c>, which stores it. A description longer than
/// <c>server_config.description</c> therefore makes its key <b>unsaveable</b> — and the operator
/// never learns why: the write throws inside the operation runner's generic fault branch, which
/// deliberately answers <c>operation_failed</c> and a correlation id rather than putting a MySQL
/// message on their screen.
/// </para>
/// <para>
/// That is how it was found. <c>camera.external_image_prefixes</c> shipped at 449 characters against
/// a 256 column, so the one key that decides whether a badge may appear in a photo was the one key
/// nobody could set. Everything about it looked right on the page — the name, the description, the
/// input — and SAVE failed with a code that says nothing.
/// </para>
/// <para>
/// The limit is read off the entity rather than written down again here, so raising the column
/// raises this test with it.
/// </para>
/// </remarks>
public sealed class ConfigKeyCatalogFitsItsColumnTests
{
    private static readonly int DescriptionLimit = typeof(ServerConfigEntity)
        .GetProperty(nameof(ServerConfigEntity.Description))!
        .GetCustomAttribute<MaxLengthAttribute>()!
        .Length;

    [Fact]
    public void EveryDescriptionFitsTheColumnItIsStoredIn()
    {
        string[] tooLong = ConfigKeyCatalog
            .All.Where(descriptor => descriptor.Description.Length > DescriptionLimit)
            .Select(descriptor => $"{descriptor.Key} ({descriptor.Description.Length})")
            .ToArray();

        tooLong
            .Should()
            .BeEmpty(
                $"a description longer than server_config.description ({DescriptionLimit}) makes its "
                    + "key impossible to save, and the operator only sees operation_failed"
            );
    }

    /// <summary>The key itself is stored too, in a narrower column.</summary>
    [Fact]
    public void EveryKeyFitsTheColumnItIsStoredIn()
    {
        int keyLimit = typeof(ServerConfigEntity)
            .GetProperty(nameof(ServerConfigEntity.Key))!
            .GetCustomAttribute<MaxLengthAttribute>()!
            .Length;

        ConfigKeyCatalog.All.Should().OnlyContain(descriptor => descriptor.Key.Length <= keyLimit);
    }
}
