using System.Text.Json;
using CoaiMcp.Core.Collecting;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Opening a file at the revision the reviewers read — <c>--file-at</c> — and the guard in front of
/// git that decides what may be asked of it at all.
/// </summary>
/// <remarks>
/// <para><b>Real git and real SQLite</b>, for the reason <c>TheRealMethodTests</c> gives: the failures
/// this guards against are git's — a commit pruned, a file deleted, a branch squashed away — and a
/// fake of git would assert a belief about them. The one place a fake launcher IS used is where the
/// assertion is that NO process starts, which only a recording launcher can see.</para>
/// <para>The row this mode reads is the row the collector stored: the path is validated against the
/// STRING the row holds and never against today's filesystem, because the case this mode exists for
/// is a file that is not on disk any more — 99.6 % of orphaned blobs still read, and a canonicalised
/// path would refuse every one of them.</para>
/// </remarks>
[Collection("console-out")]
public sealed class TheFileAtRevisionTests : IAsyncLifetime
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "coai-fileat-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string? _was = Environment.GetEnvironmentVariable("COAI_DATA_DIR");
    private readonly ProcessLauncher _launcher = new();
    private TempGitRepo _git = null!;

    private const string ASha = "0123456789abcdef0123456789abcdef01234567";

    /// <summary>The file the reviewers read — the whole of it is what this mode answers.</summary>
    private const string Racy = """
        using System.Collections.Generic;

        public sealed class Totals
        {
            private readonly Dictionary<string, int> _items = new();

            public int GetOrAdd(string key, int value)
            {
                if (!_items.ContainsKey(key))
                {
                    _items.Add(key, value);
                }

                return _items[key];
            }
        }
        """;

    private static readonly CollectedPair Skeletons = new(
        "GetOrAdd", "CSharp", "public int method_1(string var_1, int var_2) { }", "public int method_1(string var_1, int var_2) { lock (var_3) { } }");

    /// <summary>A launcher that records every request and answers as if git had said yes.</summary>
    private sealed class Recording : IProcessLauncher
    {
        public List<ProcessRequest> Launched { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Launched.Add(request);

            return Task.FromResult(new ProcessResult(0, "text", string.Empty, false));
        }
    }

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("COAI_DATA_DIR", _dir);
        _git = await TempGitRepo.InitAsync(_launcher, "coai-fileat-git-");
        await _git.WriteAsync("Totals.cs", Racy);
        await _git.CommitAsync("the defect");
    }

    public async ValueTask DisposeAsync()
    {
        Environment.SetEnvironmentVariable("COAI_DATA_DIR", _was);
        SqliteConnection.ClearAllPools();
        await _git.DisposeAsync();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// A fix commit nothing here can read — DIFFERENT from the head on purpose, so a read that took
    /// the wrong column would answer <c>commit_unreachable</c> rather than pass by coincidence.
    /// </summary>
    private const string AFixNobodyReads = "ffffffffffffffffffffffffffffffffffffffff";

    /// <summary>One collected pair whose reviewers read <paramref name="file"/> at <paramref name="headSha"/>.</summary>
    private long Seed(string headSha, string file = "Totals.cs", string? repoPath = null) =>
        PairSeed.One(_dir, repoPath ?? _git.Path, headSha, AFixNobodyReads, file, 7, Skeletons);

    /// <summary>The mode's answer for one pair, typed — and always exit 0.</summary>
    private static async Task<FileAtRevision> AnswerAsync(long findingId)
    {
        var (json, code) = await Stdout.OfAsync(() => Program.FileAtAsync(["--file-at", "--id", findingId.ToString()]));
        code.Should().Be(0, "a domain outcome is data on stdout, never an exit code");

        return JsonSerializer.Deserialize(json, ServerJsonContext.Default.FileAtRevision)!;
    }

    /// <summary>Line endings folded, because git normalises what it stores and a text compare should not care.</summary>
    private static string Lines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    // --------------------------------------------------------------------------------------------
    // The file, whole, at the commit the reviewers read.
    // --------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheFileIsReadAtTheRecordedCommit_Verbatim()
    {
        var head = await _git.HeadAsync();
        var id = Seed(head);

        var file = await AnswerAsync(id);

        file.Reason.Should().BeEmpty();
        file.FindingId.Should().Be(id);
        file.Sha.Should().Be(head, "the document names the commit it is of, so the tab can say which revision");
        file.Path.Should().Be("Totals.cs");
        Lines(file.Text).Should().Be(Lines(Racy), "the whole file, as it was — not the method, not today's copy");
    }

    /// <summary>
    /// The row's path is validated against the STRING, so a file deleted since still reads at its
    /// revision — the 99.6 %, and the case a filesystem check would refuse outright.
    /// </summary>
    [Fact]
    public async Task AFileDeletedSince_StillReadsAtItsRevision()
    {
        var head = await _git.HeadAsync();
        await _git.GitAsync("rm", "-q", "Totals.cs");
        await _git.CommitAsync("gone from the tree");
        File.Exists(Path.Combine(_git.Path, "Totals.cs")).Should().BeFalse("the fixture must really have removed it");
        var id = Seed(head);

        var file = await AnswerAsync(id);

        file.Reason.Should().BeEmpty("a canonical-path check would have refused this, and it is the point of the mode");
        Lines(file.Text).Should().Be(Lines(Racy));
    }

    /// <summary>
    /// An orphaned head — squash-merged, branch deleted, no ref reaches it — still reads, because its
    /// object survives. 55.7 % of recorded heads are orphaned and 99.6 % of those still read.
    /// </summary>
    [Fact]
    public async Task AnOrphanedCommitWhoseObjectSurvives_StillReads()
    {
        await _git.GitAsync("checkout", "-q", "-b", "feature");
        await _git.WriteAsync("Totals.cs", Racy.Replace("int value)", "int value) // round one", StringComparison.Ordinal));
        await _git.CommitAsync("the round the reviewers read");
        var orphan = await _git.HeadAsync();
        await _git.GitAsync("checkout", "-q", "main");
        await _git.GitAsync("merge", "--squash", "feature");
        await _git.GitAsync("-c", "user.email=t@t", "-c", "user.name=t", "commit", "-m", "squashed");
        await _git.GitAsync("branch", "-D", "feature");
        (await _git.RunAsync("for-each-ref", "--format=%(refname)", "--contains", orphan))
            .StdOut.Trim().Should().BeEmpty("the fixture must actually produce an orphan");
        var id = Seed(orphan);

        var file = await AnswerAsync(id);

        file.Reason.Should().BeEmpty("the object survives, so the file at it still reads");
        file.Text.Should().Contain("// round one", "and it is the file at THAT commit");
    }

    /// <summary>`docs/notes;v2.md` and `src/[legacy].cs` are committed paths; a semicolon is a character in a filename.</summary>
    [Theory]
    [InlineData("docs/notes;v2.md")]
    [InlineData("src/[legacy].cs")]
    [InlineData("src/a b (c).cs")]
    public async Task AnOrdinaryMetacharacterInAPath_IsNotRefused(string path)
    {
        Directory.CreateDirectory(Path.Combine(_git.Path, Path.GetDirectoryName(path)!));
        await _git.WriteAsync(path, "// legal\n");
        await _git.CommitAsync("a path with a character git is fine with");
        var id = Seed(await _git.HeadAsync(), file: path);

        var file = await AnswerAsync(id);

        file.Reason.Should().BeEmpty($"'{path}' is a committed path and execution is exe-plus-argv");
        file.Text.Should().Contain("// legal");
    }

    // --------------------------------------------------------------------------------------------
    // What the row itself rules out, before any process.
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// A path that is not repository-relative is refused BEFORE git is asked anything: the launcher is
    /// never called, and the reason names the path rather than the commit.
    /// </summary>
    [Theory]
    [InlineData("../Totals.cs")]
    [InlineData("src/../../Totals.cs")]
    [InlineData("/etc/passwd")]
    [InlineData("\\Windows\\win.ini")]
    [InlineData("C:/Users/x/.ssh/config")]
    [InlineData("c:\\Users\\x\\.ssh\\config")]
    [InlineData("\\\\server\\share\\x")]
    [InlineData("src/Totals.cs\0evil")]
    [InlineData("")]
    public async Task APathThatIsNotRepositoryRelative_NeverReachesGit(string path)
    {
        var launcher = new Recording();
        var reader = new FileAtReader(new GitHistory(launcher));

        var file = await reader.ReadAsync(1, new FilePlace(_git.Path, ASha, path), TestContext.Current.CancellationToken);

        launcher.Launched.Should().BeEmpty($"'{path}' must be refused before any process is started");
        file.Reason.Should().Be(FileAtReason.PathRefused);
        file.Text.Should().BeEmpty();
    }

    /// <summary>
    /// And the guard is on the road in, not only in the reader: <c>GitHistory.FileAtAsync</c> itself
    /// refuses the path, so a caller that forgets to check is still covered.
    /// </summary>
    [Theory]
    [InlineData("../Totals.cs")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/x")]
    public async Task TheRoadIntoShaColonPath_RefusesItToo(string path)
    {
        var launcher = new Recording();

        var answer = await new GitHistory(launcher).FileAtAsync(_git.Path, ASha, path, TestContext.Current.CancellationToken);

        launcher.Launched.Should().BeEmpty();
        answer.Ran.Should().BeTrue("a refusal is an answer, not a failure");
        answer.Ok.Should().BeFalse();
    }

    /// <summary>The paths git is fine with are the paths the guard is fine with — including a name that merely CONTAINS two dots.</summary>
    [Theory]
    [InlineData("src/Totals.cs")]
    [InlineData("docs/notes;v2.md")]
    [InlineData("src/[legacy].cs")]
    [InlineData("./src/Totals.cs")]
    [InlineData("src/..hidden/x.cs")]
    [InlineData("a b/c d.cs")]
    public void ARepositoryRelativePath_IsAccepted(string path) =>
        GitHistory.IsRepoRelative(path).Should().BeTrue();

    /// <summary>A sha that is not one is refused before git is asked — the one validator, reused, never a second regex.</summary>
    [Theory]
    [InlineData("abc")]
    [InlineData("0123456")]
    [InlineData("--upload-pack=evil")]
    [InlineData("HEAD")]
    [InlineData("")]
    public async Task AShaThatIsNotOne_NeverReachesGit(string sha)
    {
        var launcher = new Recording();
        var reader = new FileAtReader(new GitHistory(launcher));

        var file = await reader.ReadAsync(1, new FilePlace(_git.Path, sha, "Totals.cs"), TestContext.Current.CancellationToken);

        launcher.Launched.Should().BeEmpty($"'{sha}' must be refused before any process is started");
        file.Reason.Should().Be(FileAtReason.CommitUnreachable, "the word --real-method answers for the same row");
    }

    // --------------------------------------------------------------------------------------------
    // The repository has changed since: each way is an answer naming which.
    // --------------------------------------------------------------------------------------------

    /// <summary>"The commit is gone" and "the file was not at that path then" are two answers, not one 128.</summary>
    [Fact]
    public async Task ACommitTheRepositoryNeverHad_AndAFileNotAtThatPath_AreTwoAnswers()
    {
        var head = await _git.HeadAsync();

        var gone = await AnswerAsync(Seed(ASha));
        var moved = await AnswerAsync(Seed(head, file: "Elsewhere.cs"));

        gone.Reason.Should().Be(FileAtReason.CommitUnreachable);
        gone.Text.Should().BeEmpty();
        moved.Reason.Should().Be(FileAtReason.FileNotInCommit);
        moved.Text.Should().BeEmpty();
        moved.Path.Should().Be("Elsewhere.cs", "the page says WHICH path was not there, and offers the current one");
    }

    [Fact]
    public async Task ACheckoutThatIsGone_SaysSo()
    {
        var id = Seed(await _git.HeadAsync(), repoPath: Path.Combine(Path.GetTempPath(), $"gone-{Guid.NewGuid():N}"));

        var file = await AnswerAsync(id);

        file.Reason.Should().Be(FileAtReason.RepoPathMissing);
        file.Text.Should().BeEmpty();
    }

    /// <summary>A pair the database does not have is an ANSWER, exit 0 — recollected under the page, or never there.</summary>
    [Fact]
    public async Task APairTheDatabaseDoesNotHave_IsAnAnswer_NotAnExitCode()
    {
        var file = await AnswerAsync(999_999);

        file.FindingId.Should().Be(999_999);
        file.Reason.Should().Be(FileAtReason.PairNotFound);
    }

    // --------------------------------------------------------------------------------------------
    // The repository is the store's, and the read writes nothing.
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// The checkout, the commit and the path come from the ROW the id names, and nothing on argv can
    /// point the read at another repository on the machine.
    /// </summary>
    /// <remarks>
    /// The plan's brief asked for a caller-supplied <c>repo_path</c> to be resolved against the
    /// store's records and refused outside them. Read against the code, there is no caller-supplied
    /// anything: <c>--real-method</c> takes an id and the row supplies the rest, and this mode takes
    /// the same door. This test is what makes that a decision rather than an accident — a
    /// <c>--repo</c> override added later, and a second repository's content comes back here.
    /// </remarks>
    [Fact]
    public async Task TheCoordinatesComeFromTheStore_AndNothingOnArgvCanRedirectThem()
    {
        var head = await _git.HeadAsync();
        var id = Seed(head);
        await using var other = await TempGitRepo.InitAsync(_launcher, "coai-fileat-other-");
        await other.WriteAsync("Totals.cs", "// ANOTHER REPOSITORY\n");
        await other.CommitAsync("elsewhere");
        var otherHead = await other.HeadAsync();

        var (json, code) = await Stdout.OfAsync(() => Program.FileAtAsync(
            ["--file-at", "--id", id.ToString(), "--repo", other.Path, "--sha", otherHead, "--file", "Totals.cs"]));

        code.Should().Be(0);
        var file = JsonSerializer.Deserialize(json, ServerJsonContext.Default.FileAtRevision)!;
        file.Sha.Should().Be(head, "the commit is the row's, whatever argv said");
        file.Text.Should().NotContain("ANOTHER REPOSITORY", "the checkout is the row's, whatever argv said");
        file.Text.Should().Contain("_items.ContainsKey(key)");
    }

    [Fact]
    public async Task AReadWritesNothing()
    {
        var id = Seed(await _git.HeadAsync());
        var before = await File.ReadAllBytesAsync(Path.Combine(_dir, RoundsDb.FileName));

        var file = await AnswerAsync(id);

        file.Text.Should().Contain("_items", "the fixture must really have read the file");
        (await File.ReadAllBytesAsync(Path.Combine(_dir, RoundsDb.FileName)))
            .Should().Equal(before, "a read must write nothing — not a row, not a column, not a stamp");
    }

    /// <summary>The answer is read by property NAME, as the extension's reader (`roundsDbRead.fileAtOf`) reads it.</summary>
    [Fact]
    public async Task TheAnswerNamesItsFieldsAsTheExtensionReadsThem()
    {
        var head = await _git.HeadAsync();
        var id = Seed(head);

        var (json, _) = await Stdout.OfAsync(() => Program.FileAtAsync(["--file-at", "--id", id.ToString()]));

        using var answer = JsonDocument.Parse(json);
        var root = answer.RootElement;
        root.GetProperty("findingId").GetInt64().Should().Be(id);
        root.GetProperty("sha").GetString().Should().Be(head);
        root.GetProperty("path").GetString().Should().Be("Totals.cs");
        root.GetProperty("reason").GetString().Should().BeEmpty();
        root.GetProperty("text").GetString().Should().Contain("_items");
    }

    // --------------------------------------------------------------------------------------------
    // The exit-code contract: 64 means "never heard of the mode", and this binary has heard of it.
    // --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("--id")]
    [InlineData("--id abc")]
    [InlineData("--id -1")]
    [InlineData("--id 1.5")]
    public async Task ABadIdIs65_Never64(string rest)
    {
        string[] args = ["--file-at", .. rest.Split(' ', StringSplitOptions.RemoveEmptyEntries)];

        var (_, code) = await Stdout.OfAsync(() => Program.FileAtAsync(args));

        code.Should().Be(65, "64 is reserved for a mode this binary does not have, and it has this one");
    }

    [Fact]
    public void TheModeHasItsOwnArgument()
    {
        Program.Classify(["--file-at"]).Should().Be(Program.Startup.FileAt);
        Program.Classify(["--file-at-revision"]).Should().Be(Program.Startup.Usage, "a near miss is refused, not guessed");
    }

    /// <summary>The file is on the mode's record and on nothing the send touches — pinned on the types.</summary>
    [Fact]
    public void TheFileIsOnTheModesRecord_AndOnNothingTheSendTouches()
    {
        typeof(FileAtRevision).GetProperties().Select(one => one.Name).Should().Contain(["Text", "Sha", "Path", "Reason"]);
        typeof(StoredPair).GetProperties().Select(one => one.Name).Should().NotContain("Text");
        typeof(UploadedPair).GetProperties().Select(one => one.Name).Should().NotContain("Text");
    }
}
