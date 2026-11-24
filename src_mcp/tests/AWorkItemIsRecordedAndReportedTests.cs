using System.Text.Json;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// coai v2 E3.S8 end to end: a review, a <c>cons</c> and a <c>consask</c> that name a plan item are recorded with it, and <c>--plan-report</c> — the REAL binary, over the
/// data folder the service wrote — lists the item with its counts; a call that names none is the old call, unchanged; and a path that could leave the repository is
/// refused before anything runs.
/// </summary>
/// <remarks>
/// Driven through the real <see cref="PanelService"/> and FakeCli, like <see cref="ARoundIsMintedAKeyTests"/>: the item travels from the argument through the engine, the
/// live record and the projection, and no unit of the three proves they agree.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AWorkItemIsRecordedAndReportedTests : ConsultScenarioBase
{
    private const string Epic = "v2/epoch-1/e3-x";

    private const string Story = "v2/epoch-1/e3-x/stories/s8-y";

    private const string Task = "v2/epoch-1/e3-x/stories/s8-y/tasks/t1-z";

    private const string Clean = """{"findings": []}""";

    private const string Plan = "# PLAN — attributed\n\n1. one\n2. two\n";

    private const string Scope = """
        # SCOPE

        The parser counts the separator as a field. A reviewer that reads only the diff can say whether the change is defensible and never whether it is what was asked for.

        When it is done: a two-field input counts two, the suite shows it, and the separator is no field. Constraint: no new dependency.
        """;

    private static void Script(string answer)
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "0");
    }

    private void MakeTheFeature()
    {
        foreach (var folder in new[] { Task, "v2/epoch-1/e3-x/stories/s9-other/tasks/t1-a", "v2/epoch-2/e1-later" })
        {
            Directory.CreateDirectory(Path.Combine(_repo, folder));
        }
    }

    private async Task<PanelService> OpenedAsync(string branch)
    {
        await Git("branch", branch);
        var service = Service();
        await service.OpenAsync(_repo, branch);

        return service;
    }

    private static JsonElement Item(JsonElement report, string path) =>
        report.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("path").GetString() == path);

    private async Task<JsonElement> ReportOfTheBinary(string featureDir = "v2")
    {
        var (code, text, err) = await MigrateV2Data.Binary(_data, "--plan-report", Path.Combine(_repo, featureDir), "--json");
        code.Should().Be(0, err);

        return JsonDocument.Parse(text).RootElement;
    }

    private async Task ResolvedAsync(PanelService service, string branch) =>
        JsonDocument.Parse(await service.ResolveAsync(_repo, branch, "[]")).RootElement.TryGetProperty("error", out _).Should().BeFalse();

    [Fact]
    public async Task AReviewThatNamesAnItem_IsInThePlanReport_OfTheRealBinary_AndEveryOtherItemReportsZeros()
    {
        MakeTheFeature();
        var service = await OpenedAsync("feature-a");
        Script(Clean);

        var answer = JsonDocument.Parse(await service.ReviewPlanAsync(
            _repo, "feature-a", new PlanInput(Plan, string.Empty, string.Empty) { WorkItem = Task }, TestContext.Current.CancellationToken)).RootElement;

        answer.GetProperty("verdict").GetString().Should().Be("proceed", answer.ToString());
        var report = await ReportOfTheBinary();
        report.GetProperty("mode").GetString().Should().Be("plan-report");
        report.GetProperty("featureDir").GetString().Should().Be("v2");
        var task = Item(report, Task);
        task.GetProperty("kind").GetString().Should().Be("task");
        task.GetProperty("parent").GetString().Should().Be(Story);
        task.GetProperty("gates").GetInt32().Should().Be(1);
        task.GetProperty("rounds").GetInt32().Should().Be(1);
        task.GetProperty("cons").GetInt32().Should().Be(0);
        task.GetProperty("consask").GetInt32().Should().Be(0);
        task.GetProperty("firstUtc").GetString().Should().NotBeEmpty();
        task.GetProperty("lastUtc").GetString().Should().NotBeEmpty();
        string.CompareOrdinal(task.GetProperty("lastUtc").GetString(), task.GetProperty("firstUtc").GetString()).Should().BeGreaterThanOrEqualTo(0);
        task.GetProperty("wallSeconds").GetDouble().Should().BeGreaterThan(0, "a round has a length");

        foreach (var other in report.GetProperty("items").EnumerateArray().Where(item => item.GetProperty("path").GetString() != Task))
        {
            other.GetProperty("gates").GetInt32().Should().Be(0, other.ToString());
            other.GetProperty("rounds").GetInt32().Should().Be(0);
            other.GetProperty("cons").GetInt32().Should().Be(0);
            other.GetProperty("consask").GetInt32().Should().Be(0);
            other.GetProperty("firstUtc").GetString().Should().BeEmpty("an item with no events reports empty instants, not a missing key");
            other.GetProperty("lastUtc").GetString().Should().BeEmpty();
            other.GetProperty("wallSeconds").GetDouble().Should().Be(0);
        }

        report.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("path").GetString()).Should().Equal(
            "v2", "v2/epoch-1", Epic, Story, Task, "v2/epoch-1/e3-x/stories/s9-other", "v2/epoch-1/e3-x/stories/s9-other/tasks/t1-a", "v2/epoch-2", "v2/epoch-2/e1-later");
        report.GetProperty("unlisted").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task TheSessionFile_AndTheTable_BothSayWhichItemTheRoundWasFor()
    {
        var service = await OpenedAsync("feature-b");
        Script(Clean);

        await service.ReviewPlanAsync(_repo, "feature-b", new PlanInput(Plan, string.Empty, string.Empty) { WorkItem = "v2\\epoch-1\\e3-x\\" }, TestContext.Current.CancellationToken);

        new SessionStore(_data).Load(_repo, "feature-b")!.Rounds.Single().WorkItem.Should().Be(Epic, "one spelling, however the caller wrote it");
        using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(_data, RoundsDb.FileName)};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT work_item FROM rounds";
        read.ExecuteScalar().Should().Be(Epic);
    }

    [Fact]
    public async Task ACallThatNamesNoItem_IsTheOldCall_Unchanged_AndIsInNoItemsReport()
    {
        MakeTheFeature();
        var service = await OpenedAsync("feature-c");
        Script(Clean);

        var answer = JsonDocument.Parse(await service.ReviewPlanAsync(_repo, "feature-c", Plan, TestContext.Current.CancellationToken)).RootElement;
        var consult = await Consult(service, "the parser returns 3 where 4 is expected");

        answer.GetProperty("verdict").GetString().Should().Be("proceed", answer.ToString());
        consult.TryGetProperty("error", out _).Should().BeFalse(consult.ToString());
        new SessionStore(_data).Load(_repo, "feature-c")!.Rounds.Single().WorkItem.Should().BeEmpty();
        service.Consultations.All().Single().WorkItem.Should().BeEmpty();
        var report = await ReportOfTheBinary();
        report.GetProperty("items").EnumerateArray().Should().AllSatisfy(item => item.GetProperty("rounds").GetInt32().Should().Be(0));
        report.GetProperty("unlisted").GetArrayLength().Should().Be(0, "a call with no item is attributed to nothing, so no item lists it");
    }

    [Fact]
    public async Task ACons_AndAFollowUp_AreCountedForTheItemTheFirstTurnNamed()
    {
        MakeTheFeature();
        var service = Service();

        var first = JsonDocument.Parse(await service.ConsultAsync(_repo, "the parser returns 3", "[]", string.Empty, Task, TestContext.Current.CancellationToken)).RootElement;
        first.TryGetProperty("error", out _).Should().BeFalse(first.ToString());
        var id = first.GetProperty("consultationId").GetString()!;
        Answer("0198-second", Advice);
        var follow = JsonDocument.Parse(await service.ConsultAsync(_repo, "I ran your check and it printed 3", "[]", id, Epic, TestContext.Current.CancellationToken)).RootElement;
        follow.TryGetProperty("error", out _).Should().BeFalse(follow.ToString());

        service.Consultations.Read(id)!.WorkItem.Should().Be(Task, "a follow-up does not move the conversation to another item");
        var report = await ReportOfTheBinary();
        Item(report, Task).GetProperty("cons").GetInt32().Should().Be(1, "one consultation, however many turns");
        Item(report, Epic).GetProperty("cons").GetInt32().Should().Be(0);
        Item(report, Task).GetProperty("rounds").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task AQuestion_IsCountedForItsItem_AndAnItemOfThisFeatureWithNoFolderIsListedUnder_unlisted()
    {
        MakeTheFeature();
        var row = new QuestionConsultRow("q1", "k", "claude", "s", _repo, "main", "abc", "", "q", "c", false, "", "complete", "", "", "2026-11-22T10:00:00.000Z", "2026-11-22T10:01:00.000Z", 1, 1, 60, 1, 1, null, "")
        {
            WorkItem = Story,
        };
        var gone = row with { Id = "q2", WorkItem = "v2/epoch-1/e9-renamed-away", StartedUtc = "2026-11-22T11:00:00.000Z", EndedUtc = "2026-11-22T11:00:30.000Z" };
        var elsewhere = row with { Id = "q3", WorkItem = "other/place/e1-x" };
        using (var db = RoundsDb.Open(_data, Serilog.Core.Logger.None)!)
        {
            QuestionConsultTable.Record(db, row, []);
            QuestionConsultTable.Record(db, gone, []);
            QuestionConsultTable.Record(db, elsewhere, []);
        }

        var report = await ReportOfTheBinary();

        var story = Item(report, Story);
        story.GetProperty("consask").GetInt32().Should().Be(1);
        story.GetProperty("firstUtc").GetString().Should().Be("2026-11-22T10:00:00.000Z");
        story.GetProperty("lastUtc").GetString().Should().Be("2026-11-22T10:01:00.000Z");
        story.GetProperty("wallSeconds").GetDouble().Should().Be(60);
        var unlisted = report.GetProperty("unlisted").EnumerateArray().Should().ContainSingle("only an item under this feature folder, and with no folder now").Subject;
        unlisted.GetProperty("path").GetString().Should().Be("v2/epoch-1/e9-renamed-away");
        unlisted.GetProperty("kind").GetString().Should().Be("unlisted");
        unlisted.GetProperty("consask").GetInt32().Should().Be(1);
        unlisted.GetProperty("wallSeconds").GetDouble().Should().Be(30);
    }

    [Fact]
    public async Task ACallWhoseItemCouldLeaveTheRepository_IsRefusedBeforeAnythingRuns_BySixTools()
    {
        var service = await OpenedAsync("feature-d");
        await File.WriteAllTextAsync(Path.Combine(_repo, "PLAN_x.md"), Plan);
        Script(Clean);
        const string Bad = "../outside";

        var refusals = new[]
        {
            await service.ReviewPlanAsync(_repo, "feature-d", new PlanInput(Plan, string.Empty, string.Empty) { WorkItem = Bad }, TestContext.Current.CancellationToken),
            await service.ReviewCodeAsync(_repo, "feature-d", "main", "scope", false, Bad, TestContext.Current.CancellationToken),
            await service.ReviewDocumentAsync(_repo, "feature-d", "purpose", "PLAN_x.md", null, null, false, Bad, TestContext.Current.CancellationToken),
            await service.ReviewFeatureAsync(_repo, "PLAN_x.md", "main", "[]", "{}", false, string.Empty, string.Empty, string.Empty, string.Empty, Bad, TestContext.Current.CancellationToken),
            await service.ConsultAsync(_repo, "stuck", "[]", string.Empty, Bad, TestContext.Current.CancellationToken),
            await service.AskConsultantsAsync(_repo, "question", "context", string.Empty, string.Empty, Bad, TestContext.Current.CancellationToken),
        };

        foreach (var refusal in refusals)
        {
            JsonDocument.Parse(refusal).RootElement.GetProperty("error").GetString().Should().StartWith("workItem ").And.Contain("`..`");
        }

        new SessionStore(_data).Load(_repo, "feature-d")!.Rounds.Should().BeEmpty("a refused call ran no round and wrote none");
        service.Consultations.All().Should().BeEmpty("a refused call opened no consultation");
        service.QuestionConsults.All().Should().BeEmpty();
    }

    [Fact]
    public async Task AReviewThatRanTwice_ForTwoItems_IsTwoGates_AndEachItemCountsItsOwn()
    {
        MakeTheFeature();
        var service = await OpenedAsync("feature-e");
        Script(Clean);
        await service.ReviewPlanAsync(_repo, "feature-e", new PlanInput(Plan, string.Empty, string.Empty) { WorkItem = Epic }, TestContext.Current.CancellationToken);
        await ResolvedAsync(service, "feature-e");
        Script(Clean);
        await Git("checkout", "feature-e");
        await File.WriteAllTextAsync(Path.Combine(_repo, "Parser.cs"), "int Count() => 4;\n");
        await Git("add", ".");
        await Git("commit", "-m", "the change");
        var code = JsonDocument.Parse(await service.ReviewCodeAsync(_repo, "feature-e", "main", Scope, false, Task, TestContext.Current.CancellationToken)).RootElement;
        code.TryGetProperty("error", out var error).Should().BeFalse($"{error}");

        var report = await ReportOfTheBinary();

        Item(report, Epic).GetProperty("gates").GetInt32().Should().Be(1);
        Item(report, Epic).GetProperty("rounds").GetInt32().Should().Be(1);
        Item(report, Task).GetProperty("gates").GetInt32().Should().Be(1);
        Item(report, Task).GetProperty("rounds").GetInt32().Should().Be(1);
    }

    // ---------- the mode's own arguments and folders ----------

    [Fact]
    public async Task ABadRequest_IsA65Not64_AndAFolderOutsideACheckoutIsRefusedToo()
    {
        var outside = TempDir.For("coai-plan-report-outside-").Path;
        var missing = Path.Combine(_repo, "no-such-folder");

        var none = await MigrateV2Data.Binary(_data, "--plan-report");
        var noJson = await MigrateV2Data.Binary(_data, "--plan-report", _repo);
        var unknownFlag = await MigrateV2Data.Binary(_data, "--plan-report", _repo, "--yaml");
        var absent = await MigrateV2Data.Binary(_data, "--plan-report", missing, "--json");
        var notAnItem = await MigrateV2Data.Binary(_data, "--plan-report", outside, "--json");

        foreach (var (name, run) in new[] { ("no folder", none), ("no --json", noJson), ("an unknown flag", unknownFlag), ("a folder that is not there", absent), ("a folder outside a checkout", notAnItem) })
        {
            run.Code.Should().Be(65, $"{name} is a bad REQUEST to a mode this binary has; 64 would read as an old binary");
            run.Out.Should().BeEmpty(name);
            run.Err.Should().Contain("--plan-report", name);
        }

        (await MigrateV2Data.Binary(_data, "--plan-reports", _repo, "--json")).Code.Should().Be(64, "the control: a mode the binary does not have is 64, and only that");
    }

    [Fact]
    public async Task AFolderWithNoDatabase_ReportsEveryItemAsZeros_AndMakesNothing()
    {
        MakeTheFeature();
        var fresh = Path.Combine(TempDir.For("coai-plan-report-fresh-").Path, "never-made");

        var (code, text, err) = await MigrateV2Data.Binary(fresh, "--plan-report", Path.Combine(_repo, "v2"), "--json");

        code.Should().Be(0, err);
        JsonDocument.Parse(text).RootElement.GetProperty("items").EnumerateArray().Should().NotBeEmpty().And.AllSatisfy(item => item.GetProperty("rounds").GetInt32().Should().Be(0));
        Directory.Exists(fresh).Should().BeFalse("a reader never makes the data directory");
    }

    [Fact]
    public void TheItemsAreReadFromFolderNames_InTheOrderAPersonNumbersThem_AndAFolderThatIsNoItemIsNotWalkedInto()
    {
        var root = TempDir.For("coai-plan-report-names-").Path;
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        foreach (var folder in new[]
        {
            "feat/epoch-10/e1-a", "feat/epoch-2/e10-j/stories/s2-b/tasks/t10-x", "feat/epoch-2/e10-j/stories/s2-b/tasks/t2-y", "feat/epoch-2/e9-i",
            "feat/epoch-2/notes", "feat/epoch-2/e9-i/stories/readme-not-a-story", "feat/archive/e1-not-under-an-epoch", "feat/epochs",
        })
        {
            Directory.CreateDirectory(Path.Combine(root, folder));
        }

        File.WriteAllText(Path.Combine(root, "feat", "epoch-2", "e9-i", "epic.yaml"), "this is never read: ]]]");

        var items = PlanReportMode.Items(root, Path.Combine(root, "feat"));

        items.Select(item => item.Path + ":" + item.Kind).Should().Equal(
            "feat:feature",
            "feat/epoch-2:epoch", "feat/epoch-2/e9-i:epic", "feat/epoch-2/e10-j:epic", "feat/epoch-2/e10-j/stories/s2-b:story",
            "feat/epoch-2/e10-j/stories/s2-b/tasks/t2-y:task", "feat/epoch-2/e10-j/stories/s2-b/tasks/t10-x:task",
            "feat/epoch-10:epoch", "feat/epoch-10/e1-a:epic");
    }

    [Fact]
    public void TheWallTime_IsTheUnionOfTheIntervals_SoAnOverlapIsCountedOnce_AndAnOpenEventAddsNothing()
    {
        static DateTimeOffset At(int minute, int second = 0) => new(2026, 11, 22, 10, minute, second, TimeSpan.Zero);
        PlanReportMode.Event Ev(DateTimeOffset? start, DateTimeOffset? end) => new(Task, "round", "g", start, end);

        PlanReportMode.WallSeconds([Ev(At(0), At(10)), Ev(At(5), At(15)), Ev(At(30), At(31))]).Should().Be(15 * 60 + 60, "ten and ten minutes overlapping by five are fifteen, then a minute");
        PlanReportMode.WallSeconds([Ev(At(0), At(10)), Ev(At(2), At(4))]).Should().Be(600, "an interval inside another adds nothing");
        PlanReportMode.WallSeconds([Ev(At(0), null), Ev(At(5), At(5, 30)), Ev(null, null)]).Should().Be(30, "no end, or no start, is a point");
        PlanReportMode.WallSeconds([]).Should().Be(0);
    }
}
