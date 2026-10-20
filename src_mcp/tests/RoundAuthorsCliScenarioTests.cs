using System.Security.Cryptography;
using System.Text;
using CoaiMcp.Core.Authors;
using CoaiMcp.Core.Findings;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using static CoaiMcp.Tests.RoundAuthorsSeed;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>--round-authors</c>, run as the PROCESS a person runs (E6.S5): the argument parsing, the JSON contract, the table, <c>--add</c> and the exit
/// codes through the built binary against real database files under <c>testsToDelete-</c> folders — never a live store.
/// </summary>
/// <remarks>
/// The exit codes are the part nothing else can check: <b>0</b> is an answer (an empty one when no database exists), <b>65</b> a bad request,
/// <b>74</b> a database that will not read — and <b>64</b> the one code this mode must NEVER answer, because it means "this binary has never
/// heard of that mode" and sends a client down its old-binary fallback (<c>.agents/PROJECT.md</c>).
/// </remarks>
public sealed class RoundAuthorsCliScenarioTests : IDisposable
{
    private const string Sentinel = "SENTINEL-7f3a";

    private readonly TempDir _root = TempDir.For("testsToDelete-coai-authors-cli-");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.Dispose();
    }

    private string Store(string name = "data") => Directory.CreateDirectory(_root.At(name)).FullName;

    private static string[] Window() => ["--since", Stamp(T0.AddDays(-1)), "--until", Stamp(T0.AddDays(10))];

    private static (int Code, string Out, string Err) Run(string dir, params string[] args) => OneShotProcess.Run(dir, ["--round-authors", .. args]);

    private static RoundAuthorsAnswer Answer(string text)
    {
        var parsed = RoundAuthorsMerge.Parse(text);
        parsed.Ok.Should().BeTrue(parsed.Why + " — stdout was: " + text);

        return parsed.Value;
    }

    [Fact]
    public void TheDefault_IsOneJsonDocumentOfSums_ExitZero()
    {
        var dir = Store();
        Sessions(dir, true, "x1");

        var (code, text, err) = Run(dir, [.. Window(), "--cohort", "all"]);

        code.Should().Be(0, err);
        var answer = Answer(text);
        answer.Selection.Stage.Should().Be("CodeReview");
        answer.Selection.Kinds.Should().Equal(["code", "test"]);
        answer.Cells.Select(cell => (cell.Key.Cohort, cell.Key.CoderMix, cell.Rounds)).Should().Equal([("corrective", "sonnet", 1), ("first", "sonnet", 1)]);
        var first = answer.Cells.Single(cell => cell.Key.Cohort == "first");
        first.SelectedLines.Should().Be(100);
        first.Numerators.All.Total.Should().Be(2);
        first.Numerators.Accepted.Should().Be(new NumeratorSums(1, 1, 100, 0, 0, 0));
        text.Should().NotContain(dir, "the document carries no absolute path");
    }

    [Fact]
    public void TheTable_SaysAssociationOnlyFirst_PrintsUnknown_MarksSmall_AndListsEveryReason()
    {
        var dir = Store();
        Sessions(dir, false, "x1", "x2", "x3");

        var (code, text, err) = Run(dir, [.. Window(), "--table"]);

        code.Should().Be(0, err);
        text.Split('\n')[0].Should().Be(RoundAuthorsHeader.AssociationOnly);
        text.Should().Contain("Unknown", "no round was resolved: the accepted columns are Unknown, never 0");
        text.Should().MatchRegex("small", "a three-round cell carries the mark");
        foreach (var reason in Enum.GetValues<ExclusionReason>())
        {
            text.Should().Contain(ExclusionWords.Of(reason), "every reason is listed, zeros included");
        }
    }

    [Fact]
    public void AMissingDatabase_IsAnEmptyAnswer_ExitZero_AndNoStoreIsCreated()
    {
        var dir = Path.Combine(_root.Path, "never-made");

        var (code, text, _) = Run(dir, Window());

        code.Should().Be(0);
        var answer = Answer(text);
        answer.Cells.Should().BeEmpty();
        answer.Exclusions.Should().OnlyContain(count => count.Rounds == 0, "every exclusion is zero");
        answer.Exclusions.Should().NotBeEmpty("an absent row and a zero are different claims");
        File.Exists(Path.Combine(dir, RoundsDb.FileName)).Should().BeFalse();
        Directory.Exists(dir).Should().BeFalse("a reader makes no directory");
    }

    [Fact]
    public void ABadRequest_Is65_NeverSixtyFour_NamingTheFlagAndQuotingTheValue()
    {
        var dir = Store();
        (string Name, string[] Args, string Flag, string Value)[] bad =
        [
            ("an unknown flag", ["--frobnicate"], "--frobnicate", "--frobnicate"),
            ("--since twice", ["--since", Stamp(T0), "--since", Stamp(T0)], "--since", Stamp(T0)),
            ("--since yesterday", ["--since", "yesterday"], "--since", "yesterday"),
            ("--cohort both", ["--cohort", "both"], "--cohort", "both"),
            ("--kinds bogus", ["--kinds", "code,bogus"], "--kinds", "bogus"),
            ("--stage plan --kinds", ["--stage", "plan", "--kinds", "code"], "--kinds", "code"),
            ("--stage plan --without-elided", ["--stage", "plan", "--without-elided"], "--without-elided", string.Empty),
            ("--until before --since", ["--since", Stamp(T0.AddDays(2)), "--until", Stamp(T0)], "--until", Stamp(T0)),
            ("--split outside", [.. Window(), "--split", Stamp(T0.AddDays(-5))], "--split", Stamp(T0.AddDays(-5))),
            ("a value-less flag", ["--cohort"], "--cohort", string.Empty),
        ];

        foreach (var (name, args, flag, value) in bad)
        {
            var run = Run(dir, args);

            run.Code.Should().Be(65, $"{name} is a bad REQUEST to a mode this binary has; 64 would read as an old binary");
            run.Out.Should().BeEmpty($"{name}: a refused request answers nothing a caller could mistake for a report");
            run.Err.Should().Contain(flag, $"{name}: the sentence names the flag");
            run.Err.Should().Contain(value.Length == 0 ? flag : value, $"{name}: and quotes the value");
        }

        OneShotProcess.Run(dir, "--round-authorz").Code.Should().Be(64, "the control: a mode the binary does NOT have is 64, and only that");
    }

    [Fact]
    public void AnAddFault_Is65_NamingTheArgumentAsTyped()
    {
        var (a, b) = (Store("a"), Store("b"));
        Sessions(a, true, "x1");
        Sessions(b, true, "y1");
        var notJson = _root.At("not.json");
        File.WriteAllText(notJson, "not json");
        var other = _root.At("other-split.json");
        File.WriteAllText(other, Run(b, [.. Window(), "--split", Stamp(T0.AddHours(0.5))]).Out);
        var missing = _root.At("nope.json");

        foreach (var path in new[] { missing, notJson, other })
        {
            var run = Run(a, [.. Window(), "--add", path]);

            run.Code.Should().Be(65, path);
            run.Out.Should().BeEmpty();
            run.Err.Should().Contain(path, "the argument as typed");
        }

        Run(a, [.. Window(), "--add", other]).Err.Should().Contain("`split`", "the field that differs is named");
    }

    /// <summary>
    /// An <c>--add</c> argument that names no file, and a file that parses as a report but breaks a shape the table relies on, are bad REQUESTS:
    /// 65 with the argument named — never the 70 of a crash with a stack trace on stderr.
    /// </summary>
    [Fact]
    public void AnAddThatNamesNoFile_OrAReportOfTheWrongShape_Is65_NeverACrash()
    {
        var (a, b) = (Store("a"), Store("b"));
        Sessions(a, true, "x1");
        Sessions(b, true, "y1");
        var report = Run(b, [.. Window()]).Out;
        var shortId = _root.At("short-id.json");
        File.WriteAllText(shortId, System.Text.RegularExpressions.Regex.Replace(report, "\"sourceId\": \"[0-9a-f]+\"", "\"sourceId\": \"x\""));
        var noSeparator = _root.At("no-separator.json");
        File.WriteAllText(noSeparator, report.Replace("\\n", string.Empty, StringComparison.Ordinal));

        foreach (var path in new[] { string.Empty, _root.Path, shortId, noSeparator })
        {
            var run = Run(a, [.. Window(), "--add", path, "--table"]);

            run.Code.Should().Be(65, $"--add '{path}' is a bad request; stderr was: {run.Err}");
            run.Out.Should().BeEmpty();
            run.Err.Should().Contain($"--add '{path}'", "the argument as typed").And.NotContain("   at ", "no stack trace");
        }
    }

    /// <summary>
    /// A cell key's cohort and period, and an exclusion's, are words this program writes — <c>first</c> or <c>corrective</c>, <c>all</c>,
    /// <c>before</c> or <c>after</c>. A file that carries any other word (an escape sequence among them) is refused at <c>--add</c> with 65 and the
    /// argument as typed, before any table is printed — defense in depth beside the table printing nothing but printable text.
    /// </summary>
    [Fact]
    public void AnAddWithACohortOrPeriodWordThisProgramNeverWrites_Is65_NamingTheArgumentAsTyped()
    {
        var (a, b) = (Store("a"), Store("b"));
        Sessions(a, true, "x1");
        Sessions(b, true, "y1");
        var report = Answer(Run(b, [.. Window()]).Out);
        var crafted = new Dictionary<string, RoundAuthorsAnswer>
        {
            ["cell-cohort.json"] = report with { Cells = [.. report.Cells.Select(cell => cell with { Key = cell.Key with { Cohort = "first\u001b]8;;http://x\u0007" } })] },
            ["cell-period.json"] = report with { Cells = [.. report.Cells.Select(cell => cell with { Key = cell.Key with { Period = "during" } })] },
            ["exclusion-cohort.json"] = report with { Exclusions = [.. report.Exclusions.Select(count => count with { Cohort = "first\u001b[2J" })] },
        };

        foreach (var (name, answer) in crafted)
        {
            var path = _root.At(name);
            File.WriteAllText(path, RoundAuthorsMerge.Serialize(answer));

            var run = Run(a, [.. Window(), "--add", path, "--table"]);

            run.Code.Should().Be(65, $"--add '{path}' carries a word this program never writes; stderr was: {run.Err}");
            run.Out.Should().BeEmpty();
            run.Err.Should().Contain($"--add '{path}'", "the argument as typed").And.NotContain("\u001b", "the refusal does not echo the word");
        }
    }

    /// <summary>
    /// A refusal's sentence quotes what it refuses — the selection words two reports disagree on, a member name the JSON has no place for — and that
    /// text comes from an <c>--add</c> file. Whatever it holds — a line break of any kind (LF, FF, NEL, LS), a one-byte CSI, DEL, a bidi override,
    /// a byte-order mark — stderr carries exactly ONE line with nothing the terminal would act on, so the file cannot forge a second line of the
    /// binary's own voice; and the refusal is still 65 naming the argument as typed.
    /// </summary>
    [Fact]
    public void ARefusalThatQuotesAnAddFile_WritesOneLineOfPrintableTextToStderr()
    {
        var (a, b) = (Store("a"), Store("b"));
        Sessions(a, true, "x1");
        Sessions(b, true, "y1");
        var reportText = Run(b, [.. Window()]).Out;
        var report = Answer(reportText);
        var crafted = new List<(string Name, string Text, string Shown)>
        {
            ("stage", RoundAuthorsMerge.Serialize(report with { Selection = report.Selection with { Stage = HostileWord } }), HostileShown),
            ("kinds", RoundAuthorsMerge.Serialize(report with { Selection = report.Selection with { Kinds = [HostileWord] } }), HostileShown),
            ("member-escape", "{\"member\\u001b[2J\\u0007\": 1," + reportText.TrimStart()[1..], "member?[2J?"),
            ("member-newline", "{\"member\\nforged\": 1," + reportText.TrimStart()[1..], "member?forged"),
        };
        foreach (var (word, shown) in new[]
        {
            (HostileWord, HostileShown), ("first\nforged", "first?forged"), ("first\u2028forged", "first?forged"), ("first\u0085forged", "first?forged"),
            ("first\u000Cforged", "first?forged"), ("first\u009B2J", "first?2J"), ("\uFEFFfirst", "?first"), ("a\u007Fb", "a?b"), ("first\u202Eforged", "first?forged"), ("first\U000E0041x", "first?x"), ("first\U0001D173x", "first?x"),
        })
        {
            crafted.Add(($"cohort-{crafted.Count}", RoundAuthorsMerge.Serialize(report with { Selection = report.Selection with { Cohort = word } }), shown));
        }

        foreach (var (name, text, shown) in crafted)
        {
            var path = _root.At(name + ".json");
            File.WriteAllText(path, text);

            var run = Run(a, [.. Window(), "--add", path, "--table"]);

            var err = run.Err.Replace("\r\n", "\n", StringComparison.Ordinal);
            run.Code.Should().Be(65, $"--add '{path}' is a bad request; stderr was: {run.Err}");
            run.Out.Should().BeEmpty();
            RoundAuthorsFacts.UnsafeIn(err).Should().BeEmpty($"stderr for {name} carries nothing the terminal would act on");
            err.TrimEnd('\n').Split('\n').Should().HaveCount(1, $"stderr for {name} is one line: the file cannot forge another");
            err.Should().Contain($"--add '{path}'", "the argument as typed");
            err.Should().Contain(shown.Length == 0 ? "--add" : shown, $"the quoted word of {name} is shown with its unsafe characters made visible");
        }
    }

    /// <summary>The JSON mode escapes every control and non-ASCII character itself: stdout is printable ASCII and line breaks — pinned, not assumed.</summary>
    [Fact]
    public void TheJsonAnswer_OfAnAddFileWithAHostileBranchName_IsPrintableAscii()
    {
        var (a, b) = (Store("a"), Store("b"));
        Sessions(a, true, "x1");
        Sessions(b, true, "y1");
        var report = Answer(Run(b, [.. Window()]).Out);
        var hostile = report with
        {
            Cells = [.. report.Cells.Select(cell => cell with { Branches = [.. cell.Branches.Select(branch => branch with { Name = branch.Name + "\u001b]8;;http://x\u0007\u202Eevil\u2028\u0085" })] })],
        };
        var path = _root.At("hostile-branch.json");
        File.WriteAllText(path, RoundAuthorsMerge.Serialize(hostile));

        var run = Run(a, [.. Window(), "--add", path]);

        run.Code.Should().Be(0, run.Err);
        run.Out.Should().Contain("evil", "the hostile branch name is in the answer, escaped");
        run.Out.Replace("\r\n", "\n", StringComparison.Ordinal).Where(at => at != '\n' && (at < ' ' || at > '~')).Should().BeEmpty("stdout is printable ASCII and line breaks only");
    }

    private const string HostileWord = "x\u001b]8;;http://x\u0007\u001b[2J";

    private const string HostileShown = "x?]8;;http://x??[2J";

    [Fact]
    public void AStoreThatWillNotRead_Is74()
    {
        var dir = Store();
        File.WriteAllBytes(Path.Combine(dir, RoundsDb.FileName), RandomNumberGenerator.GetBytes(4096));

        var run = Run(dir, Window());

        run.Code.Should().Be(74);
        run.Out.Should().BeEmpty();
        run.Err.Should().NotBeEmpty();
    }

    [Fact]
    public void AnOlderStore_IsReadNotMigrated()
    {
        var older = new Dictionary<string, int>
        {
            ["step 21"] = StepIndex(Schema.TheRoundsSize),
            ["step 23"] = StepIndex(Schema.TheRoundsAuthors),
        };
        foreach (var (name, stopBefore) in older)
        {
            var dir = Store(name.Replace(' ', '-'));
            OlderFile(dir, stopBefore, OlderRound("old1", "D:/r", "b", T0, stopBefore > StepIndex(Schema.TheRoundsSize) ? ", effort" : string.Empty, stopBefore > StepIndex(Schema.TheRoundsSize) ? ", ''" : string.Empty));
            var (hash, version) = Fingerprint(dir);

            var (code, text, err) = Run(dir, Window());

            code.Should().Be(0, $"{name}: {err}");
            var answer = Answer(text);
            answer.Exclusions.Single(count => count.Reason == "authors not recorded" && count.Cohort == "first").Rounds.Should().Be(1, $"{name}: its code round reads authors not recorded");
            answer.EffortMarkerKnown.Should().Be(stopBefore > StepIndex(Schema.TheRoundsSize), $"{name}: effort is recorded from step 22 on");
            Fingerprint(dir).Should().Be((hash, version), $"{name}: a read that migrates is a write");
        }
    }

    [Fact]
    public void NoTextLeaves_AndTheProbeSeesTheBranch()
    {
        var dir = Store();
        using (var db = Open(dir))
        {
            Round(
                db, "x1", "D:/repo", "branch-" + Sentinel.ToLowerInvariant(), "CodeReview", 1, T0, reviewers: [Done("codex", "gpt-6-luna", "high")],
                findings: [new Finding(Severity.Major, Category.Security, Sentinel + ".cs", 1, Sentinel + " title", Sentinel + " why", Sentinel + " fix", ["codex"])],
                size: Whole(Cell("shown", "code", 10)), authors: Snapshot("CodeReview", 1, rows: [Author("a1", "claude-sonnet-5-5")]), reject: [0]);
        }

        Raw(dir, $"UPDATE rounds SET plan_text = '{Sentinel}', agent_log = '{Sentinel}', subject = '{Sentinel}', note = '{Sentinel}', commands = '{Sentinel}';");

        var json = Run(dir, Window());
        var table = Run(dir, [.. Window(), "--table"]);

        json.Code.Should().Be(0);
        table.Code.Should().Be(0);
        (json.Out + json.Err + table.Out + table.Err).Should().NotContain(Sentinel, "no text column is read or printed");
        table.Out.Should().Contain(Sentinel.ToLowerInvariant(), "the companion: the branch name IS printed, so the probe above sees strings");
    }

    [Fact]
    public void TwoProcessesAddUp_TheUnionOfTwoStores()
    {
        var (a, b, c) = (Store("a"), Store("b"), Store("c"));
        Sessions(a, true, "x1", "x2");
        Sessions(b, true, "y1");
        Sessions(c, true, "x1", "x2", "y1");
        var halfB = _root.At("b.json");
        File.WriteAllText(halfB, Run(b, Window()).Out);

        var added = Run(a, [.. Window(), "--add", halfB]);
        var union = Run(c, Window());

        added.Code.Should().Be(0, added.Err);
        var (together, whole) = (Answer(added.Out), Answer(union.Out));
        together.Cells.Should().BeEquivalentTo(whole.Cells, options => options.WithStrictOrdering());
        together.Exclusions.Should().BeEquivalentTo(whole.Exclusions, options => options.WithStrictOrdering());
        together.Sources.Should().HaveCount(2);
    }

    [Fact]
    public void TheAdvertisedWorkflow_WithDefaults_AUtf16FileWithABom_MergesAndTheSameFileTwiceIs65()
    {
        var (a, b) = (Store("a"), Store("b"));
        Sessions(a, true, "x1");
        Sessions(b, true, "y1");
        var half = _root.At("half-b.json");
        File.WriteAllText(half, Run(b).Out, new UnicodeEncoding(bigEndian: false, byteOrderMark: true));
        Thread.Sleep(1100);

        var merged = Run(a, "--add", half);
        var twice = Run(a, "--add", half, "--add", half);

        merged.Code.Should().Be(0, merged.Err);
        Answer(merged.Out).Sources.Should().HaveCount(2);
        twice.Code.Should().Be(65);
        twice.Err.Should().Contain(half, "the second add names the argument as typed");
    }

    [Fact]
    public void ThePlanStage_ReachesThePlanRounds_ByTheirStoredName()
    {
        var dir = Store();
        using (var db = Open(dir))
        {
            Round(db, "p1", "D:/repo", "feat/x", "PlanReview", 1, T0, reviewers: [Done("codex", "gpt-6-luna", "high")], authors: Snapshot("PlanReview", 1, rows: [Author("a1", "claude-sonnet-5-5", stage: "PlanReview")]));
        }

        var (code, text, err) = Run(dir, [.. Window(), "--stage", "plan"]);

        code.Should().Be(0, err);
        var answer = Answer(text);
        answer.Selection.Stage.Should().Be("PlanReview");
        answer.Cells.Should().ContainSingle().Which.Rounds.Should().Be(1);
    }

    [Fact]
    public void TheModeIsKnown_InTheHelp_AndInTheOneShotList()
    {
        Program.Classify(["--round-authors"]).Should().NotBe(Program.Startup.Usage, "64 is how a client recognises a binary that predates the mode");
        Program.HelpText.Should().Contain("`--round-authors");
        File.ReadAllText(Path.Combine(ProductionSources.RepositoryRoot(), ".agents", "PROJECT.md")).Should().Contain("`--round-authors`");
    }

    private static (string Hash, long Version) Fingerprint(string dir)
    {
        SqliteConnection.ClearAllPools();
        var file = Path.Combine(dir, RoundsDb.FileName);
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        using var check = new SqliteConnection($"Data Source={file};Pooling=False;Mode=ReadOnly");
        check.Open();
        using var ask = check.CreateCommand();
        ask.CommandText = "PRAGMA user_version";

        return (hash, (long)ask.ExecuteScalar()!);
    }
}
