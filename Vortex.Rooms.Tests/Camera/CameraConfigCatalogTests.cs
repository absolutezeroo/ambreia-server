using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentAssertions;
using Vortex.Primitives.Camera;
using Vortex.Primitives.Server;
using Xunit;

namespace Vortex.Rooms.Tests.Camera;

/// <summary>
/// Guards the deliberate duplication between <see cref="CameraConfig"/> and the dashboard-facing
/// <see cref="ConfigKeyCatalog"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>IServerConfigGrain</c> only knows keys that already exist in the database, so the catalog is
/// what tells the dashboard a key exists at all. A key the camera reads and the catalog does not
/// declare is therefore invisible: it silently keeps its compiled default and no operator can find
/// it, let alone change it.
/// </para>
/// <para>
/// That is exactly what happened. The camera shipped with eleven new keys — the rate limit, the
/// payload ceilings, the external-image allowlist — and only the three prices were in the catalog.
/// The allowlist is the one that matters: it is empty by default, it is what decides whether a badge
/// can appear in a photo, and it could not be set from anywhere.
/// </para>
/// </remarks>
public sealed class CameraConfigCatalogTests
{
    private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static IEnumerable<object[]> ExpectedDefaults()
    {
        yield return [CameraConfig.CreditPriceKey, I(CameraConfig.DefaultCreditPrice)];
        yield return [CameraConfig.DucketPriceKey, I(CameraConfig.DefaultDucketPrice)];
        yield return
        [
            CameraConfig.PublishDucketPriceKey,
            I(CameraConfig.DefaultPublishDucketPrice),
        ];
        yield return [CameraConfig.RendersPerHourKey, I(CameraConfig.DefaultRendersPerHour)];
        yield return
        [
            CameraConfig.PublishCooldownSecondsKey,
            I(CameraConfig.DefaultPublishCooldownSeconds),
        ];
        yield return
        [
            CameraConfig.CompetitionEntriesPerDayKey,
            I(CameraConfig.DefaultCompetitionEntriesPerDay),
        ];
        yield return
        [
            CameraConfig.ExternalImagePrefixesKey,
            CameraConfig.DefaultExternalImagePrefixes,
        ];
        yield return [CameraConfig.MaxPayloadBytesKey, I(CameraConfig.DefaultMaxPayloadBytes)];
        yield return [CameraConfig.MaxSceneBytesKey, I(CameraConfig.DefaultMaxSceneBytes)];
        yield return [CameraConfig.MaxPlanesKey, I(CameraConfig.DefaultMaxPlanes)];
        yield return [CameraConfig.MaxSpritesKey, I(CameraConfig.DefaultMaxSprites)];
        yield return [CameraConfig.MaxMasksPerPlaneKey, I(CameraConfig.DefaultMaxMasksPerPlane)];
        yield return
        [
            CameraConfig.MaxTextureColumnsPerPlaneKey,
            I(CameraConfig.DefaultMaxTextureColumnsPerPlane),
        ];
        yield return
        [
            CameraConfig.TimestampSkewSecondsKey,
            I(CameraConfig.DefaultTimestampSkewSeconds),
        ];
        yield return [CameraConfig.UrlPathPrefixKey, CameraConfig.DefaultUrlPathPrefix];
        yield return [CameraConfig.PhotoFurniClassNameKey, CameraConfig.DefaultPhotoFurniClassName];
        yield return [CameraConfig.MaxPhotoDimensionKey, I(CameraConfig.DefaultMaxPhotoDimension)];
        yield return
        [
            CameraConfig.MaxExternalFetchesKey,
            I(CameraConfig.DefaultMaxExternalFetches),
        ];
        yield return
        [
            CameraConfig.ExternalTimeoutSecondsKey,
            I(CameraConfig.DefaultExternalTimeoutSeconds),
        ];
        yield return [CameraConfig.MaxExternalBytesKey, I(CameraConfig.DefaultMaxExternalBytes)];
    }

    [Theory]
    [MemberData(nameof(ExpectedDefaults))]
    public void EveryCameraKeyIsInTheCatalogWithTheMatchingDefault(
        string key,
        string expectedDefault
    )
    {
        ConfigKeyDescriptor? descriptor = ConfigKeyCatalog.Find(key);

        descriptor.Should().NotBeNull($"'{key}' must be a dashboard-editable config key");
        descriptor!.DefaultValue.Should().Be(expectedDefault);
        descriptor.Group.Should().Be("Camera");
    }

    /// <summary>
    /// The other direction: a key the camera resolves at runtime but nobody listed above would slip
    /// through the theory, because the theory only walks what it was told about.
    /// </summary>
    [Fact]
    public void TheBatchResolveAsksForNothingTheCatalogDoesNotDeclare()
    {
        string[] declared = [.. ExpectedDefaults().Select(row => (string)row[0])];

        CameraConfig.AllKeys.Should().BeEquivalentTo(declared);
    }
}
