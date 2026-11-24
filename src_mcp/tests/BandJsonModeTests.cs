using System.Text.Json;
using CoaiMcp.Core.Notices;
using CoaiMcp.Server;
using CoaiMcp.ServiceDefaults;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>coai-mcp --band-json</c> (v2 E3.S4, <c>v2/architecture/extension.md</c> §6 and §10 row 5): ONE JSON object
/// <c>{phrases[], consultations[], failures[]</c>}, read from the local data folder, for the Claude mod's 5 s tick.
/// </summary>
/// <remarks>
/// <para>A reader: it opens no home, no lock and no network, and never makes the data directory (the missing-root
/// classification is <c>AReaderModeNeverMakesTheDataDirectoryTests</c>, which lists the mode). It answers 0 with the object, an
/// EMPTY one for a folder with nothing in it; a wrong argument is 65, and the mode never exits 64 — 64 is "never heard of the
/// mode", how the mod would take an old binary.</para>
/// <para>The phrases are the person's, and they live in VS Code's user settings (<c>coai.phrases</c>); the server has no copy and
/// no reader of that file, so the list is empty from here — a statement these tests pin, so a phrase source that appears
/// later is a decision and not a drift.</para>
/// </remarks>
[Collection("console-out")]
public sealed class BandJsonModeTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly string _data = TempDir.For("coai-band-json-").Path;
    private readonly string? _dirWas = Environment.GetEnvironmentVariable("COAI_DATA_DIR");
    private readonly string? _sideWas = Environment.GetEnvironmentVariable("COAI_DATA_SIDE");

    public BandJsonModeTests()
    {
        Environment.SetEnvironmentVariable("COAI_DATA_DIR", _data);
        Environment.SetEnvironmentVariable("COAI_DATA_SIDE", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("COAI_DATA_DIR", _dirWas);
        Environment.SetEnvironmentVariable("COAI_DATA_SIDE", _sideWas);
        Directory.Delete(_data, recursive: true);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private (int Code, string Out, string Err) Run(params string[] args)
    {
        var output = new StringWriter();
        var errors = new List<string>();
        var code = BandJsonMode.Run(args, name => name == "COAI_DATA_DIR" ? _data : null, output, errors.Add, new FixedClock(Now));

        return (code, output.ToString(), string.Join('\n', errors));
    }

    private JsonElement Band()
    {
        var (code, said, err) = Run("--band-json");
        code.Should().Be(0, err);
        said.TrimEnd('\n').Should().NotContain("\n", "ONE object, on one line — the whole interface is stdout");

        return JsonDocument.Parse(said).RootElement.Clone();
    }

    private static string Stamp(TimeSpan ago) => ConsultationStore.Stamp(Now.UtcDateTime - ago);

    private void Consultation(string status, TimeSpan ago, string repo = "D:/repo") =>
        new ConsultationStore(_data).Write(new ConsultationRecord(
            ConsultationStore.NewId(), $"repo:{repo}", "claude", "s1", repo, "feat/x", "sha", "codex", "gpt-6", "codex",
            ConsultationMemories.VendorRemembers, 5, Stamp(ago + TimeSpan.FromMinutes(1)))
        {
            Status = status,
            UpdatedUtc = Stamp(ago),
        });

    private void Question(string status, TimeSpan ago) =>
        new QuestionConsultStore(_data).Write(new QuestionConsultRecord(
            QuestionConsultStore.NewId(), "caller-1", "claude", "no-session", "D:/repo", "main", "sha", "Which shape?", Stamp(ago + TimeSpan.FromMinutes(1)))
        {
            Status = status,
            UpdatedUtc = Stamp(ago),
        });

    private void Notice(string @class, TimeSpan ago, string title = "it broke") =>
        ServerNotices.Append(
            ResolvedDataDir.For(_data),
            new ServerNotice
            {
                Utc = ServerNotice.Iso(Now - ago),
                Class = @class,
                Source = "test",
                Code = ServerNoticeCodes.Crash,
                Title = title,
                Detail = "the detail",
                Cure = "look at the log",
            });

    [Fact]
    public void ItIsAMode_NotTheUnknownArgumentAnOldBinaryAnswers()
    {
        Program.Classify(["--band-json"]).Should().Be(Program.Startup.BandJson, "an unknown flag exits 64, and the mod takes 64 for an old binary");
        Program.WriterModes.Should().NotContain(Program.Startup.BandJson, "it writes nothing, so a missing named data folder must not stop it — it never opens one");
    }

    [Fact]
    public void AFolderWithNothingInIt_IsOneObjectWithThreeEmptyLists()
    {
        Band().GetRawText().Should().Be("""{"phrases":[],"consultations":[],"failures":[]}""");
    }

    [Fact]
    public void ThePhrasesAreEmptyFromTheServer_BecauseTheyLiveInTheEditorsSettings()
    {
        Band().GetProperty("phrases").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void OnlyAnActiveConsultationIsListed_AskingAndOpen_WithItsKind()
    {
        Consultation(ConsultationStatuses.Open, TimeSpan.FromMinutes(2));
        Consultation(ConsultationStatuses.Asking, TimeSpan.FromSeconds(10));
        Consultation(ConsultationStatuses.Closed, TimeSpan.FromMinutes(1));
        Consultation(ConsultationStatuses.Failed, TimeSpan.FromMinutes(1));
        Consultation(ConsultationStatuses.Interrupted, TimeSpan.FromMinutes(1));
        Question(QuestionConsultStatuses.Consulting, TimeSpan.FromSeconds(5));
        Question(QuestionConsultStatuses.Answered, TimeSpan.FromMinutes(1));
        Question(QuestionConsultStatuses.Interrupted, TimeSpan.FromMinutes(1));

        var listed = Band().GetProperty("consultations").EnumerateArray().ToList();

        listed.Select(one => (one.GetProperty("kind").GetString(), one.GetProperty("status").GetString()))
            .Should().BeEquivalentTo(new[]
            {
                ("cons", "asking"),
                ("cons", "open"),
                ("consask", "consulting"),
            });
        listed.Should().OnlyContain(one => one.GetProperty("repo").GetString() == "D:/repo" && one.GetProperty("id").GetString()!.Length > 0);
    }

    [Fact]
    public void TheMostRecentlyTouchedConsultationComesFirst()
    {
        Consultation(ConsultationStatuses.Open, TimeSpan.FromMinutes(30), repo: "D:/older");
        Consultation(ConsultationStatuses.Open, TimeSpan.FromMinutes(1), repo: "D:/newer");

        Band().GetProperty("consultations").EnumerateArray().Select(one => one.GetProperty("repo").GetString())
            .Should().Equal("D:/newer", "D:/older");
    }

    [Fact]
    public void OnlyAFailureOfTheLastDayIsListed_NotARefusalNorAnOldFailure()
    {
        Notice("failure", TimeSpan.FromHours(1), title: "recent");
        Notice("failure", TimeSpan.FromHours(30), title: "yesterday-and-more");
        Notice("refusal", TimeSpan.FromMinutes(5), title: "a refusal");

        var failures = Band().GetProperty("failures").EnumerateArray().ToList();

        failures.Should().ContainSingle();
        failures[0].GetProperty("title").GetString().Should().Be("recent");
        failures[0].GetProperty("code").GetString().Should().Be(ServerNoticeCodes.Crash);
        failures[0].GetProperty("detail").GetString().Should().Be("the detail");
        failures[0].GetProperty("cure").GetString().Should().Be("look at the log");
        failures[0].GetProperty("utc").GetString().Should().Be(ServerNotice.Iso(Now - TimeSpan.FromHours(1)));
    }

    [Fact]
    public void ATornLineInTheNoticesFile_IsSkipped_AndTheRestAreRead()
    {
        Notice("failure", TimeSpan.FromHours(2), title: "before");
        File.AppendAllText(Path.Combine(_data, ServerNotices.Name), "{\"utc\":\"2026-11-22T11:00:00.000Z\",\"cla\n");
        Notice("failure", TimeSpan.FromHours(1), title: "after");

        Band().GetProperty("failures").EnumerateArray().Select(one => one.GetProperty("title").GetString())
            .Should().Equal("after", "before");
    }

    [Fact]
    public void AWrongArgumentIsAnAnswerOf65_NeverOf64_AndStdoutStaysEmpty()
    {
        foreach (var args in new[] { new[] { "--band-json", "--bogus" }, ["--band-json", "extra"], ["--band-json", "--band-json"] })
        {
            var (code, said, err) = Run(args);

            code.Should().Be(65, $"`{string.Join(' ', args)}` is a request fault in a mode this binary knows");
            said.Should().BeEmpty("a refusal's sentence goes to stderr, never into the object a caller parses");
            err.Should().Contain("--band-json");
        }
    }

    [Fact]
    public async Task TheProcessEntryAnswersTheSameWay()
    {
        Consultation(ConsultationStatuses.Open, TimeSpan.FromMinutes(1));

        var (said, code) = await Stdout.OfAsync(() => Program.RunAsync(["--band-json"]));

        code.Should().Be(0);
        JsonDocument.Parse(said).RootElement.GetProperty("consultations").GetArrayLength().Should().Be(1);

        var (refused, refusedCode) = await Stdout.OfAsync(() => Program.RunAsync(["--band-json", "--nope"]));
        refusedCode.Should().Be(65);
        refused.Should().BeEmpty();
    }

    [Fact]
    public void TheModeIsInTheHelp_AndInTheOneShotList()
    {
        Program.HelpText.Should().Contain("`--band-json`");
        File.ReadAllText(Path.Combine(ProductionSources.RepositoryRoot(), ".agents", "PROJECT.md"))
            .Should().Contain("`--band-json`", "adding a one-shot mode means adding it to PROJECT.md's list");
    }
}
