using System;
using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Marketplace;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Marketplace;

/// <summary>
///     The client's composer (unknowns/_SafePkg_1746/_SafeCls_2606.as, header 3695) writes price,
///     furniture type, the number of refs and then every ref. The parser read a third bare int, so
///     it took the <em>count</em> for the item id: listing a single item always offered item 1, and
///     anything past the first item of a batch was never read at all.
/// </summary>
public sealed class MakeOfferWireTests
{
    private const int MakeOfferMessageEvent = 3695;

    private static ClientPacket BuildClientPacket(int header, Action<ServerPacket> write)
    {
        ServerPacket sp = new(header);
        write(sp);
        return new ClientPacket(header, sp.ToArray());
    }

    [Fact]
    public void MakeOfferParser_ReadsEveryIdBehindTheCount()
    {
        Rev revision = new(Options.Create(new ProtocolLimitsConfig()));

        MakeOfferMessage message = revision
            .Parsers[MakeOfferMessageEvent]
            .Parse(
                BuildClientPacket(
                    MakeOfferMessageEvent,
                    sp =>
                        sp.WriteInteger(120)
                            .WriteInteger(1)
                            .WriteInteger(2)
                            // The strip's `ref` is negative for a floor item on this layout.
                            .WriteInteger(-4311)
                            .WriteInteger(-4312)
                )
            )
            .Should()
            .BeOfType<MakeOfferMessage>()
            .Subject;

        message.Price.Should().Be(120);
        message.FurniType.Should().Be(1);
        message.FurnitureItemIds.Should().Equal(-4311, -4312);
    }

    [Fact]
    public void MakeOfferParser_RejectsACountTheBodyCannotHold()
    {
        Rev revision = new(
            Options.Create(new ProtocolLimitsConfig { MaxMarketplaceOfferItems = 2 })
        );

        Action parse = () =>
            revision
                .Parsers[MakeOfferMessageEvent]
                .Parse(
                    BuildClientPacket(
                        MakeOfferMessageEvent,
                        sp => sp.WriteInteger(120).WriteInteger(1).WriteInteger(9_000)
                    )
                );

        parse.Should().Throw<InvalidDataException>();
    }
}
