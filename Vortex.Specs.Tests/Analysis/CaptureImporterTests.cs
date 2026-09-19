using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Vortex.Specs.Captures;
using Vortex.Specs.Model;
using Vortex.Specs.Tests.Fixtures;
using Xunit;

namespace Vortex.Specs.Tests.Analysis;

public class CaptureImporterTests
{
    private static readonly RevisionRegistry Registry = new()
    {
        Id = "WIN63-202607011411-782849652",
        Origin = "vortex",
        Authority = EvidenceAuthority.VortexEmulator,
        TargetsSameRevision = true,
        Incoming = new Dictionary<string, int> { ["MoveObject"] = 1482 },
        Outgoing = new Dictionary<string, int> { ["ObjectUpdate"] = 114 },
        Evidence = new EvidenceRef
        {
            Kind = EvidenceKind.EmulatorHeader,
            Authority = EvidenceAuthority.VortexEmulator,
            Origin = "vortex",
            Source = "Vortex.Revisions/Revision20260701/Headers.cs",
        },
    };

    [Fact]
    public void A_trigger_and_the_run_that_follows_it_become_one_observation()
    {
        using TemporaryCapture capture = new("move.json", TemporaryCapture.MoveFurnitureOfficial);
        CaptureImporter importer = new();

        CaptureDocument document = importer.Read(capture.Path_);
        IReadOnlyList<CaptureObservation> observations = importer.Observe(document, Registry);

        observations.Should().HaveCount(2);

        observations[0].TriggerPacket.Should().Be("MoveObject");
        observations[0].EmittedPackets.Should().Equal("ObjectUpdate");
        observations[0].TriggerFields["x"].Should().Be("7");

        observations[1].EmittedPackets.Should().Equal("NotificationDialog", "ObjectUpdate");
    }

    /// <summary>
    ///     Two requests in the same millisecond, answered in the reverse order. Positionally both
    ///     answers belong to the second request and the first is unanswered, which is how an
    ///     official capture ends up claiming a packet the hotel never sent.
    /// </summary>
    [Fact]
    public void An_answer_that_says_which_request_it_belongs_to_is_not_paired_positionally()
    {
        using TemporaryCapture capture = new(
            "burst.json",
            """
            { "id": "burst", "source": "official", "messages": [
              { "index": 0, "direction": "client_to_server", "name": "GetRoomEntryTile" },
              { "index": 1, "direction": "client_to_server", "name": "GetOccupiedTiles" },
              { "index": 2, "direction": "server_to_client", "name": "RoomOccupiedTiles", "respondsTo": 1 },
              { "index": 3, "direction": "server_to_client", "name": "RoomEntryTile", "respondsTo": 0 } ] }
            """
        );

        CaptureImporter importer = new();
        IReadOnlyList<CaptureObservation> observations = importer.Observe(
            importer.Read(capture.Path_),
            Registry
        );

        observations
            .Single(o => o.TriggerPacket == "GetRoomEntryTile")
            .EmittedPackets.Should()
            .Equal("RoomEntryTile");

        observations
            .Single(o => o.TriggerPacket == "GetOccupiedTiles")
            .EmittedPackets.Should()
            .Equal("RoomOccupiedTiles");
    }

    [Fact]
    public void An_answer_pointing_at_a_request_that_is_not_there_is_an_error()
    {
        using TemporaryCapture capture = new(
            "dangling.json",
            """
            { "id": "dangling", "source": "official", "messages": [
              { "index": 0, "direction": "client_to_server", "name": "MoveObject" },
              { "index": 1, "direction": "server_to_client", "name": "ObjectUpdate", "respondsTo": 7 } ] }
            """
        );

        CaptureImportException error = Assert.Throws<CaptureImportException>(() =>
            new CaptureImporter().Read(capture.Path_)
        );

        error.Message.Should().Contain("answers 7");
    }

    [Fact]
    public void An_official_capture_carries_official_authority_and_a_vortex_one_does_not()
    {
        using TemporaryCapture official = new("a.json", TemporaryCapture.MoveFurnitureOfficial);
        using TemporaryCapture mine = new("b.json", TemporaryCapture.MoveFurnitureEmulator);
        CaptureImporter importer = new();

        importer.Read(official.Path_).Authority.Should().Be(EvidenceAuthority.OfficialCapture);
        importer.Read(mine.Path_).Authority.Should().Be(EvidenceAuthority.VortexEmulator);
    }

    [Fact]
    public void A_capture_that_does_not_say_where_it_came_from_is_treated_as_unbacked()
    {
        using TemporaryCapture capture = new(
            "anonymous.json",
            """
            { "id": "anon", "messages": [
              { "index": 0, "direction": "client_to_server", "name": "MoveObject" } ] }
            """
        );

        CaptureImporter importer = new();
        CaptureDocument document = importer.Read(capture.Path_);

        document.Source.Should().Be(CaptureSource.Unknown);
        document.Authority.Should().Be(EvidenceAuthority.Assumption);

        importer
            .Observe(document, Registry)[0]
            .Evidence.Note.Should()
            .Contain("does not state where it was recorded");
    }

    [Fact]
    public void A_message_carrying_only_a_header_id_is_named_through_the_registry()
    {
        using TemporaryCapture capture = new(
            "ids.json",
            """
            { "id": "ids", "source": "official", "messages": [
              { "index": 0, "direction": "client_to_server", "header": 1482 },
              { "index": 1, "direction": "server_to_client", "header": 114 } ] }
            """
        );

        CaptureImporter importer = new();
        IReadOnlyList<CaptureObservation> observations = importer.Observe(
            importer.Read(capture.Path_),
            Registry
        );

        observations.Should().ContainSingle();
        observations[0].TriggerPacket.Should().Be("MoveObject");
        observations[0].EmittedPackets.Should().Equal("ObjectUpdate");
    }

    [Fact]
    public void A_header_the_registry_does_not_know_is_left_out_rather_than_guessed()
    {
        using TemporaryCapture capture = new(
            "unknown-id.json",
            """
            { "id": "unknown-id", "source": "official", "messages": [
              { "index": 0, "direction": "client_to_server", "header": 1482 },
              { "index": 1, "direction": "server_to_client", "header": 9999 } ] }
            """
        );

        CaptureImporter importer = new();
        IReadOnlyList<CaptureObservation> observations = importer.Observe(
            importer.Read(capture.Path_),
            Registry
        );

        observations[0].EmittedPackets.Should().BeEmpty();
    }

    [Fact]
    public void A_message_with_neither_a_name_nor_a_header_is_an_error()
    {
        using TemporaryCapture capture = new(
            "broken.json",
            """
            { "id": "broken", "messages": [ { "index": 0, "direction": "client_to_server" } ] }
            """
        );

        CaptureImportException error = Assert.Throws<CaptureImportException>(() =>
            new CaptureImporter().Read(capture.Path_)
        );

        error.Message.Should().Contain("neither a name nor a header");
    }

    [Fact]
    public void An_unrecognised_direction_is_an_error_not_a_default()
    {
        using TemporaryCapture capture = new(
            "sideways.json",
            """
            { "id": "sideways", "messages": [
              { "index": 0, "direction": "sideways", "name": "MoveObject" } ] }
            """
        );

        Assert.Throws<CaptureImportException>(() => new CaptureImporter().Read(capture.Path_));
    }

    [Fact]
    public void Repeated_observations_of_one_trigger_are_summarised_with_their_agreement()
    {
        using TemporaryCapture capture = new("move.json", TemporaryCapture.MoveFurnitureOfficial);
        CaptureImporter importer = new();
        CaptureDocument document = importer.Read(capture.Path_);

        IReadOnlyList<TriggerSummary> summaries = importer.Summarize(
            importer.Observe(document, Registry)
        );

        TriggerSummary move = summaries.Single(s => s.TriggerPacket == "MoveObject");

        move.ObservationCount.Should().Be(2);
        // The two runs differ, so the order is not established — which is the honest reading.
        move.OrderingIsStable.Should().BeFalse();
        move.BestAuthority.Should().Be(EvidenceAuthority.OfficialCapture);
    }

    [Fact]
    public void A_missing_capture_directory_yields_nothing_rather_than_failing()
    {
        List<string> problems = [];

        new CaptureImporter().ReadAll("this-directory-does-not-exist", problems).Should().BeEmpty();

        problems.Should().BeEmpty();
    }

    [Fact]
    public void A_broken_capture_in_a_directory_is_reported_and_the_rest_still_load()
    {
        using TemporaryCapture good = new("good.json", TemporaryCapture.MoveFurnitureOfficial);
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(good.Directory_, "bad.json"),
            "{ not json"
        );

        List<string> problems = [];
        IReadOnlyList<CaptureDocument> captures = new CaptureImporter().ReadAll(
            good.Directory_,
            problems
        );

        captures.Should().ContainSingle();
        problems.Should().ContainSingle();
        problems[0].Should().Contain("bad.json");
    }
}
