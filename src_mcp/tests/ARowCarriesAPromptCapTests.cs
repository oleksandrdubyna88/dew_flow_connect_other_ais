using System.Text.Json;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// S1 of <c>research/PLAN_a_reviewer_is_not_sent_more_than_it_can_read.md</c> (D1, D6, D7): a vendor row may carry
/// <c>maxPromptKib</c> — an operator's number, whole KiB, 1 to 4096 — and reports it from <c>providers</c> when it is set.
/// </summary>
/// <remarks>
/// The same shape as a row's own timeout (<c>ARowsOwnTimeoutTests</c>): anything outside the range is unset, which is no cap.
/// Whether a launch honours it is S2's — <c>ARowIsOnlyGivenAPromptItCanReadTests</c> and <c>AnOverCapReviewerIsLeftOutOfTheRoundTests</c> — and so is
/// the binary listing the field in <c>--features</c>: an entry is added in the commit that makes it true, never ahead of it.
/// </remarks>
public sealed class ARowCarriesAPromptCapTests : IDisposable
{
    private readonly TempDir _data = TempDir.For("coai-prompt-cap-");

    public void Dispose() => _data.Dispose();

    [Theory]
    [InlineData("180", 180)]
    [InlineData("1", 1)]
    [InlineData("4096", 4096)]
    [InlineData("0", 0)]
    [InlineData("-1", 0)]
    [InlineData("4097", 0)]
    public void TheRowsKib_AreParsed_OutOfRangeIsUnset(string kib, int expected)
    {
        PanelSettings.ParseVendors($$"""[{"id":"codex","runtime":"codex","maxPromptKib":{{kib}}}]""")[0]
            .PromptCap.Kib.Should().Be(expected);
    }

    [Fact]
    public void AnAbsentCap_IsUnset()
    {
        PanelSettings.ParseVendors("""[{"id":"codex","runtime":"codex"}]""")[0].PromptCap.Should().Be(CoaiMcp.Core.Catalog.PromptCap.None);
    }

    [Fact]
    public void ACapWrittenAsAString_IsUnset_AndDoesNotCostTheRowItsNeighbours()
    {
        var rows = PanelSettings.ParseVendors(
            """[{"id":"codex","runtime":"codex","maxPromptKib":"180"},{"id":"gemini","runtime":"gemini","maxPromptKib":150}]""");

        rows.Select(row => (row.Provider, row.PromptCap.Kib)).Should().Equal(("codex", 0), ("gemini", 150));
    }

    [Fact]
    public void TheLimit_IsTheOneTheExtensionPins()
    {
        CoaiMcp.Core.Catalog.CatalogLimits.MaxPromptKib.Should().Be(4096, "the extension's MAX_PROMPT_KIB (promptCap.ts) says 4096 — each half pins its own");
    }

    [Fact]
    public void TheCapInBytes_IsExactlyKibTimes1024_AndZeroWhenUnset()
    {
        CoaiMcp.Core.Catalog.PromptCap.OfKib(150).Bytes.Should().Be(153_600);
        new ProviderSettings("codex").PromptCap.Bytes.Should().Be(0, "a row with no cap has no bytes to compare");
    }

    /// <summary>The boundary lives in the type: exactly the cap is read, one byte more is not, and no cap admits anything.</summary>
    [Fact]
    public void TheCapAdmitsExactlyItsBytes_AndNotOneMore_AndNoCapAdmitsEverything()
    {
        var cap = CoaiMcp.Core.Catalog.PromptCap.OfKib(1);

        cap.Admits(1024).Should().BeTrue("the cap is inclusive");
        cap.Admits(1025).Should().BeFalse("one byte over the cap is over it");
        CoaiMcp.Core.Catalog.PromptCap.None.Admits(int.MaxValue).Should().BeTrue("a row with no cap is sent what it always was");
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(4097L)]
    [InlineData(5_000_000_000L)]
    public void AKibOutOfRange_IsNoCap(long kib)
    {
        CoaiMcp.Core.Catalog.PromptCap.OfKib(kib).Should().Be(CoaiMcp.Core.Catalog.PromptCap.None);
    }

    [Fact]
    public void AFractionOfAKib_IsNoCap()
    {
        PanelSettings.ParseVendors("""[{"id":"codex","runtime":"codex","maxPromptKib":180.5}]""")[0].PromptCap.IsSet.Should().BeFalse();
    }

    [Fact]
    public async Task ProvidersReportsACappedRowsCap_AndNothingForARowWithout()
    {
        var service = new PanelService(
            new PanelSettings
            {
                DataDir = _data,
                Providers = PanelSettings.ParseVendors(
                    """[{"id":"codex","runtime":"codex","maxPromptKib":180},{"id":"gemini","runtime":"gemini"}]"""),
            },
            VaultKeys.None("no vault"), default, new RecordingLauncher(stdOut: string.Empty), Serilog.Core.Logger.None, Noticing.None);

        var answer = JsonDocument.Parse(await service.ProvidersAsync(TestContext.Current.CancellationToken)).RootElement;

        JsonElement Row(string id) =>
            answer.GetProperty("providers").EnumerateArray().Single(one => one.GetProperty("provider").GetString() == id);
        Row("codex").GetProperty("maxPromptKib").GetInt32().Should().Be(180);
        Row("gemini").TryGetProperty("maxPromptKib", out _).Should().BeFalse("a row with no cap reads exactly as it did");
    }
}
