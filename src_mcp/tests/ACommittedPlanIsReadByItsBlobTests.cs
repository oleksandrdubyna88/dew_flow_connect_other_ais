using System.Text;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A committed file is read as the plan only when the text handed on re-encodes to the blob git names — real git, a
/// repository built once for the class, and a launcher that notes every <c>git</c> it was asked to run so "no blob was
/// fetched" is an observation and not a hope.
/// </summary>
public sealed class ACommittedPlanIsReadByItsBlobTests(ACommittedPlanIsReadByItsBlobTests.Repository fixture)
    : IClassFixture<ACommittedPlanIsReadByItsBlobTests.Repository>
{
    private const string PlanOid = "707279fd268816a6390d22d1db277a907635e45f";

    private const string Pointer =
        "version https://git-lfs.github.com/spec/v1\noid sha256:4d7a214614ab2935c943f9e0ff69d22eadbb8f32b1258daaa5e2ca24d17e2393\nsize 130\n";

    /// <summary>One repository for every test: building it costs a few dozen git processes.</summary>
    public sealed class Repository : IAsyncLifetime
    {
        public ProcessLauncher Launcher { get; } = new();

        private TempGitRepo _git = null!;

        public string Path => _git.Path;

        public string Sha { get; private set; } = string.Empty;

        public async ValueTask InitializeAsync()
        {
            _git = await TempGitRepo.InitAsync(Launcher, "coai-blob-");
            // The machine's autocrlf must not turn "hello plan\n" into twelve bytes; --no-filters guards the blobs below.
            await _git.GitAsync("config", "core.autocrlf", "false");
            await _git.StageBlobAsync("plan.md", "hello plan\n"u8.ToArray());
            await _git.StageBlobAsync("bom.md", [0xEF, 0xBB, 0xBF, .. "bom text\n"u8]);
            await _git.StageBlobAsync("bad.md", [.. "abc "u8, 0xFF, 0xFE, .. " def\n"u8]);
            await _git.StageBlobAsync("empty.md", []);
            // FF FE + UTF-16LE "plan\n": the launcher's decoder detects the BOM and decodes it WITHOUT a U+FFFD.
            await _git.StageBlobAsync("utf16.md", [0xFF, 0xFE, .. "plan\n"u8.ToArray().SelectMany(b => new byte[] { b, 0 })]);
            await _git.StageBlobAsync("blank.md", "  \n\t\n"u8.ToArray());
            await _git.StageBlobAsync("big.md", Encoding.ASCII.GetBytes(new string('x', DocumentRules.MaxBytes) + "\n"));
            await _git.StageBlobAsync("lfs.md", Encoding.ASCII.GetBytes(Pointer));
            await _git.StageBlobAsync("replacement.md", Encoding.UTF8.GetBytes("caf\u00e9 \uFFFD end\n"));
            await _git.StageBlobAsync("todo/PLAN x.md", "a plan\n"u8.ToArray());
            await _git.StageBlobAsync("d/a.md", "a\n"u8.ToArray());
            await _git.StageBlobAsync("d/b.md", "b\n"u8.ToArray());
            var target = await _git.StageBlobAsync("target.md", "target\n"u8.ToArray());
            await _git.StageEntryAsync("link.md", "120000", target);
            await _git.StageEntryAsync("sub", "160000", "0123456789012345678901234567890123456789");
            await _git.CommitIndexAsync("the plans");
            Sha = await _git.HeadAsync();
            // Written, never committed: the working tree has a file the commit does not.
            await _git.WriteAsync("uncommitted.md", "not in the commit\n");
        }

        public ValueTask DisposeAsync() => _git.DisposeAsync();
    }

    private Task<PlanRead> ReadAsync(IProcessLauncher launcher, string path) =>
        CommittedPlanReader.ReadAsync(new GitHistory(launcher), fixture.Path, fixture.Sha, path, TestContext.Current.CancellationToken);

    private static string RefusedBy(PlanRead read) => read.Should().BeOfType<PlanRead.Refused>().Subject.Sentence;

    [Fact]
    public async Task ACommittedPlan_IsReadWhole_AndItsSourceIsTheEntry()
    {
        var read = await ReadAsync(fixture.Launcher, "plan.md");

        var accepted = read.Should().BeOfType<PlanRead.Accepted>().Subject;
        accepted.Text.Should().Be("hello plan\n");
        accepted.Note.Should().BeEmpty();
        accepted.Source.Should().Be(new PlanSource(
            PlanOrigin.Commit, fixture.Sha, "plan.md", PlanOid, 11, DocumentRules.ArtifactIdOf("hello plan\n")));
    }

    [Theory]
    [InlineData("link.md", "symbolic link")]
    [InlineData("sub", "submodule")]
    [InlineData("todo", "directory")]
    public async Task ASymlink_ASubmodule_ADirectory_AreRefusedByTheirWord_BeforeAnyCatFile(string path, string word)
    {
        var watching = new Watching(fixture.Launcher);

        var read = await ReadAsync(watching, path);

        RefusedBy(read).Should().Contain(word);
        watching.Ran.Should().ContainSingle(a => a.Contains("ls-tree")).And.NotContain(a => a.Contains("cat-file"));
    }

    [Fact]
    public async Task ACaptureTheLauncherSaysWasCut_IsAnIncompleteRead()
    {
        // The WHOLE text, exit 0, the flag set — the id would match, so only the flag can refuse it.
        var watching = new Watching(
            fixture.Launcher,
            request => request.Arguments.Contains("cat-file")
                ? new ProcessResult(0, "hello plan\n", string.Empty, TimedOut: false, Truncated: true)
                : null);

        var read = await ReadAsync(watching, "plan.md");

        RefusedBy(read).Should().Contain("incomplete read");
        watching.Ran.Should().Contain(a => a.Contains("cat-file"), "the blob WAS asked for; its answer was refused");
    }

    [Fact]
    public async Task AGitThatFailsOrNeverFinishes_IsRefusedWithItsOwnWords()
    {
        var failing = new Watching(fixture.Launcher, request => request.Arguments.Contains("cat-file")
            ? new ProcessResult(128, string.Empty, "fatal", TimedOut: false)
            : null);
        var hung = new Watching(fixture.Launcher, request => request.Arguments.Contains("ls-tree")
            ? new ProcessResult(-1, string.Empty, string.Empty, TimedOut: true)
            : null);

        var failingAndCut = new Watching(fixture.Launcher, request => request.Arguments.Contains("cat-file")
            ? new ProcessResult(128, string.Empty, "fatal", TimedOut: false, Truncated: true)
            : null);

        RefusedBy(await ReadAsync(failing, "plan.md")).Should().Contain("git fsck");
        RefusedBy(await ReadAsync(failingAndCut, "plan.md")).Should().Contain("git fsck").And.NotContain("cut short",
            "a cut capture of a command that FAILED is a failed command, and its cure is not a re-run");
        RefusedBy(await ReadAsync(hung, "plan.md")).Should().Contain("within 30 s");
    }

    [Fact]
    public async Task AnInvalidUtf8Blob_IsNotUtf8()
    {
        RefusedBy(await ReadAsync(fixture.Launcher, "bad.md")).Should().Contain("not UTF-8").And.Contain("'bad.md'");
    }

    [Fact]
    public async Task AUtf16Blob_IsToldToSaveAsUtf8_NotToRerun()
    {
        var read = await ReadAsync(fixture.Launcher, "utf16.md");

        RefusedBy(read).Should().Contain("differs from the blob").And.Contain("UTF-16").And.Contain("save it as UTF-8");
    }

    [Fact]
    public async Task ABomBlob_IsAccepted_WithTheBlobsByteCount()
    {
        var accepted = (await ReadAsync(fixture.Launcher, "bom.md")).Should().BeOfType<PlanRead.Accepted>().Subject;

        accepted.Text.Should().Be("bom text\n");
        accepted.Source.Bytes.Should().Be(12, "what git holds, not the nine bytes the launcher handed back");
    }

    [Fact]
    public async Task TheTextIdIsOverTheTextHandedOn()
    {
        var accepted = (await ReadAsync(fixture.Launcher, "bom.md")).Should().BeOfType<PlanRead.Accepted>().Subject;

        accepted.Source.TextId.Should().Be(DocumentRules.ArtifactIdOf(accepted.Text));
    }

    [Theory]
    [InlineData("empty.md", "empty", false)]
    [InlineData("blank.md", "only whitespace", true)]
    [InlineData("big.md", "-byte ceiling", false)]
    [InlineData("lfs.md", "git-lfs pointer", true)]
    public async Task AnEmptyFile_AWhitespaceFile_A257KiBFile_AnLfsPointer_AreRefused(string path, string word, bool blobIsRead)
    {
        var watching = new Watching(fixture.Launcher);

        var read = await ReadAsync(watching, path);

        RefusedBy(read).Should().Contain(word);
        watching.Ran.Any(a => a.Contains("cat-file")).Should().Be(
            blobIsRead, blobIsRead ? "only the TEXT can be empty or a pointer" : "size alone refuses it, before a byte is fetched");
    }

    [Fact]
    public async Task APlanHoldingALiteralReplacementCharacter_IsAccepted()
    {
        var read = await ReadAsync(fixture.Launcher, "replacement.md");

        read.Should().BeOfType<PlanRead.Accepted>().Which.Text.Should().Be("caf\u00e9 \uFFFD end\n");
    }

    [Theory]
    [InlineData("missing.md")]
    [InlineData("PLAN.MD")]
    [InlineData("*.md")]
    [InlineData("uncommitted.md")]
    public async Task APathNotInTheCommit_IsRefusedNamingIt_BeforeAnyCatFile(string path)
    {
        var watching = new Watching(fixture.Launcher);

        var read = await ReadAsync(watching, path);

        RefusedBy(read).Should().Contain("not in commit").And.Contain($"'{path}'");
        watching.Ran.Should().NotContain(a => a.Contains("cat-file"));
    }

    [Fact]
    public async Task ATrailingSlash_IsNotOneFile()
    {
        RefusedBy(await ReadAsync(fixture.Launcher, "d/")).Should().Contain("names 2 entries");
        RefusedBy(await ReadAsync(fixture.Launcher, "todo/")).Should().Contain("git listed 'todo/PLAN x.md' for 'todo/'");
    }

    [Theory]
    [InlineData("../outside.md", "climbs out")]
    [InlineData("/etc/x", "is absolute")]
    [InlineData("C:\\x", "names a drive")]
    [InlineData("", "names no file")]
    public async Task APathOutsideTheRepository_GetsThePathCure_AndStartsNoProcess(string path, string why)
    {
        var watching = new Watching(fixture.Launcher);

        var read = await ReadAsync(watching, path);

        RefusedBy(read).Should().Contain(why).And.Contain("relative to the repository root").And.NotContain("git fsck");
        watching.Ran.Should().BeEmpty();
    }

    [Fact]
    public async Task ASha256Repository_IsRefused_WithNoProcess()
    {
        await using var repo = await TempGitRepo.InitAsync(fixture.Launcher, "coai-s256-", "sha256");
        await repo.WriteAsync("plan.md", "hello plan\n");
        await repo.CommitAsync("a plan");
        var sha = await repo.HeadAsync();
        var watching = new Watching(fixture.Launcher);

        var read = await CommittedPlanReader.ReadAsync(
            new GitHistory(watching), repo.Path, sha, "plan.md", TestContext.Current.CancellationToken);

        sha.Should().HaveLength(64);
        RefusedBy(read).Should().Contain("SHA-1").And.Contain("64 hex");
        watching.Ran.Should().BeEmpty();
    }

    /// <summary>Real git (or a stand-in answer for one request), and a note of every command it was asked.</summary>
    private sealed class Watching(IProcessLauncher real, Func<ProcessRequest, ProcessResult?>? instead = null) : IProcessLauncher
    {
        public List<string> Ran { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Ran.Add($"{request.Executable} {string.Join(' ', request.Arguments)}");

            return instead?.Invoke(request) is { } answer ? Task.FromResult(answer) : real.RunAsync(request, ct);
        }
    }
}
