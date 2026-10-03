using System.Text.Json;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The staleness a reader on the OTHER side of the Windows/WSL seam judges a <c>checking</c> state by is PUBLISHED by the
/// server, in the files that reader reads — never a number the extension keeps for itself.
/// </summary>
/// <remarks>
/// Epic 5's plan round of PLAN_the_consultant_works_on_every_vendor.md: a plain Windows window reads a WSL store's
/// <c>&lt;kind&gt;.check.json</c> and <c>consultants.json</c> through <c>coai.alsoWatchDataDirectories</c>, cannot see the
/// lock across 9P, and must judge a <c>checking</c> by its <c>heartbeatUtc</c>. The margin it uses is
/// <see cref="ConsultCheckState.HeartbeatStaleAfter"/> — the one the server's own <see cref="ConsultCheckState.SettledAcross"/>
/// uses — so it travels with the record, and a TypeScript constant beside it would be a second copy that drifts.
/// </remarks>
public sealed class HeartbeatStaleAfterIsPublishedTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-stale-after-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not a failing test.
        }
    }

    private static readonly double Published = ConsultCheckState.HeartbeatStaleAfter.TotalSeconds;

    private static ConsultCheckRecord Checking() => new()
    {
        CallerKind = "claude",
        State = ConsultCheckStates.Checking,
        StartedUtc = "2026-10-03T10:00:00.0000000Z",
        DeadlineUtc = "2026-10-03T10:06:00.0000000Z",
        HeartbeatUtc = "2026-10-03T10:00:00.0000000Z",
        Side = "wsl",
    };

    private static double StaleAfterIn(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.TryGetProperty("heartbeatStaleAfterSeconds", out var field) && field.ValueKind == JsonValueKind.Number
            ? field.GetDouble()
            : double.NaN;
    }

    [Fact]
    public void TheCheckStateFile_CarriesTheStalenessItsHeartbeatIsJudgedBy()
    {
        var store = new ConsultCheckStore(_data);
        store.Write(Checking());

        StaleAfterIn(File.ReadAllText(store.StatePath("claude"))).Should().Be(Published,
            "the other side judges this file's heartbeat, and the margin must come from the server that wrote it");
    }

    [Fact]
    public void TheOneDocumentACheckPrints_CarriesItToo()
    {
        StaleAfterIn(ConsultCheckStore.Serialize(Checking())).Should().Be(Published);
    }

    [Fact]
    public void TheConsultantsAnswer_CarriesIt_ForTheOtherSidesReadOfConsultantsJson()
    {
        var answer = new ConsultantsAnswer { Utc = "2026-10-03T10:00:00.0000000Z", Side = "wsl", Distro = "Ubuntu" };

        StaleAfterIn(JsonSerializer.Serialize(answer, ConsultantsJsonContext.Default.ConsultantsAnswer)).Should().Be(Published);
    }

    [Fact]
    public void AFileWrittenBeforeTheFieldExisted_StillReads()
    {
        var store = new ConsultCheckStore(_data);
        Directory.CreateDirectory(store.Directory);
        File.WriteAllText(store.StatePath("claude"),
            """{"callerKind":"claude","state":"checking","startedUtc":"2026-10-03T10:00:00.0000000Z","heartbeatUtc":"2026-10-03T10:00:00.0000000Z","side":"wsl"}""");

        store.Read("claude")!.State.Should().Be(ConsultCheckStates.Checking);
    }
}
