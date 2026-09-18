using System;
using System.Collections.Generic;
using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Camera;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Server;
using Vortex.Protocol.Messages.Outgoing.Camera;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Camera;

/// <summary>
/// The camera's prices, which used to be an empty body.
/// </summary>
/// <remarks>
/// <para>
/// The composer carried no properties and the serializer wrote nothing, so the client read a body
/// with no bytes in it: <c>creditPrice</c> and <c>ducketPrice</c> are unguarded reads, and the widget
/// was left on its own defaults for the visit. Three ints, in the order
/// <c>InitCameraMessageParser</c> reads them.
/// </para>
/// <para>
/// The third is behind a bytes-available guard in the client, which is exactly why it needs a test:
/// dropping it would parse cleanly and go unnoticed.
/// </para>
/// </remarks>
public sealed class InitCameraWireTests
{
    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    [Fact]
    public void WritesTheThreePricesInTheClientsOrder()
    {
        ClientPacket body = Serialize(
            new InitCameraMessageComposer
            {
                CreditPrice = 2,
                DucketPrice = 0,
                PublishDucketPrice = 10,
            }
        );

        body.PopInt().Should().Be(2);
        body.PopInt().Should().Be(0);
        body.PopInt().Should().Be(10);

        body.Remaining.Should().Be(0);
    }

    /// <summary>
    /// A zero ducket price is a price, not an absent field. It is the official hotel's value, so a
    /// serializer that skipped zeroes would shift the publish price into its slot.
    /// </summary>
    [Fact]
    public void AZeroPriceStillOccupiesItsField()
    {
        ClientPacket body = Serialize(
            new InitCameraMessageComposer
            {
                CreditPrice = 0,
                DucketPrice = 0,
                PublishDucketPrice = 7,
            }
        );

        body.PopInt().Should().Be(0);
        body.PopInt().Should().Be(0);
        body.PopInt().Should().Be(7);
    }

    /// <summary>
    /// Guards the deliberate duplication between <see cref="CameraConfig" />'s compiled defaults and
    /// the dashboard-facing <see cref="ConfigKeyCatalog" />: a key missing from the catalog is a
    /// price no operator can find, and a default that drifts is one they would read wrong.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExpectedDefaults))]
    public void EveryCameraKeyIsInTheCatalogWithTheMatchingDefault(string key, string expected)
    {
        ConfigKeyDescriptor? descriptor = ConfigKeyCatalog.Find(key);

        descriptor.Should().NotBeNull("an operator can only edit a key the catalog declares");
        descriptor!.DefaultValue.Should().Be(expected);
    }

    public static IEnumerable<object[]> ExpectedDefaults()
    {
        yield return [CameraConfig.CreditPriceKey, I(CameraConfig.DefaultCreditPrice)];
        yield return [CameraConfig.DucketPriceKey, I(CameraConfig.DefaultDucketPrice)];
        yield return
        [
            CameraConfig.PublishDucketPriceKey,
            I(CameraConfig.DefaultPublishDucketPrice),
        ];
    }

    private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static ClientPacket Serialize(InitCameraMessageComposer composer)
    {
        byte[] bytes = Revision
            .Serializers[typeof(InitCameraMessageComposer)]
            .Serialize(composer)
            .ToArray();

        byte[] payload = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, payload, 0, payload.Length);

        return new ClientPacket(0, payload);
    }
}
