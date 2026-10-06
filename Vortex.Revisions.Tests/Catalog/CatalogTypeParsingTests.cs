using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Catalog.Enums;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Catalog;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Catalog;

/// <summary>
/// A stock client still asks for the Builders Club catalogue, removed in M0. A parser that throws on
/// its type string counts as a parse failure, and ten of those disconnect the client -- so a retired
/// or unknown type reads as the normal catalogue instead.
/// </summary>
public sealed class CatalogTypeParsingTests
{
    private const int GetCatalogIndexEvent = 2232;
    private const int GetCatalogPageEvent = 2093;

    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    [Theory]
    [InlineData("NORMAL")]
    [InlineData("BUILDERS_CLUB")]
    [InlineData("SOMETHING_NEW")]
    public void CatalogIndex_ReadsAnyTypeAsTheNormalCatalogue(string type)
    {
        ServerPacket sp = new(GetCatalogIndexEvent);
        sp.WriteString(type);
        ClientPacket packet = new(GetCatalogIndexEvent, sp.ToArray());

        GetCatalogIndexMessage message = (GetCatalogIndexMessage)
            Revision.Parsers[GetCatalogIndexEvent].Parse(packet);

        message.CatalogType.Should().Be(CatalogType.Normal);
        packet.Remaining.Should().Be(0);
    }

    [Fact]
    public void CatalogPage_ReadsBuildersClubAsTheNormalCatalogue()
    {
        ServerPacket sp = new(GetCatalogPageEvent);
        sp.WriteInteger(12).WriteInteger(-1).WriteString("BUILDERS_CLUB");
        ClientPacket packet = new(GetCatalogPageEvent, sp.ToArray());

        GetCatalogPageMessage message = (GetCatalogPageMessage)
            Revision.Parsers[GetCatalogPageEvent].Parse(packet);

        message.PageId.Should().Be(12);
        message.CatalogType.Should().Be(CatalogType.Normal);
        packet.Remaining.Should().Be(0);
    }
}
