using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A consultation's ledger row keeps what the turn's usage said — the cached and reasoning tokens, a missing price,
/// and above all a usage the vendor never reported — exactly as a reviewer's row does.
/// </summary>
/// <remarks>
/// The whole-branch review of PLAN_the_consultant_works_on_every_vendor.md (2026-10-03), finding C: the consult path
/// wrote its row through <c>RecordJob</c>'s bare token counts, so a turn killed before its vendor reported anything was
/// filed as a free 0/0 — the one thing <see cref="UsageEntry.UsageNote"/> exists to prevent.
/// </remarks>
public sealed class TheConsultLedgerKeepsEveryUsageFieldTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-consult-ledger-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private JsonElement OnlyRow()
    {
        var ledger = new UsageLedger(_data);

        return JsonDocument.Parse(File.ReadAllLines(ledger.Path).Should().ContainSingle().Subject).RootElement.Clone();
    }

    private void Billed(Usage usage, string role = ConsultantRoles.Consult) =>
        ConsultantTurnBooks.Billed(new UsageLedger(_data), new ProviderSettings("antigravity"), "gemini-3.1-pro-high", role, "timeout", TimeSpan.FromSeconds(3), usage);

    [Fact]
    public void ATurnWhoseUsageWasNeverReported_IsWrittenAsNotCaptured_NotAsAFreeZero()
    {
        Billed(Usage.Unknown);

        var row = OnlyRow();
        row.GetProperty("tokensIn").GetInt64().Should().Be(0);
        row.GetProperty("usageNote").GetString().Should().Be(CostText.UsageNotCaptured, "0/0 alone says the turn was free");
    }

    [Fact]
    public void ACheckRow_KeepsTheCachedAndReasoningTokens_AndAMissingPrice()
    {
        Billed(new Usage(1200, 300, null, TokensCached: 800, NoPriceSet: true, TokensReasoning: 120), ConsultantRoles.Check);

        var row = OnlyRow();
        row.GetProperty("role").GetString().Should().Be(ConsultantRoles.Check);
        row.GetProperty("kind").GetString().Should().Be(UsageKinds.Consult);
        row.GetProperty("stage").GetString().Should().Be("Consultation");
        row.GetProperty("tokensCached").GetInt64().Should().Be(800);
        row.GetProperty("tokensReasoning").GetInt64().Should().Be(120);
        row.GetProperty("costNote").GetString().Should().Be(CostText.NoPriceSet);
        row.GetProperty("usageNote").GetString().Should().BeEmpty();
    }
}
