using System.Text;
using CoaiMcp.Core.Feature;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Outlining;
using CoaiMcp.Normalizer;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Feature;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A feature reviewer asks for code and gets exactly the git object at the round's pinned head —
/// never the working tree, never a path outside the repository, never a credential-looking file,
/// never more than the caps — and every refusal says why (plan §4.9, story S3.1).
/// </summary>
/// <remarks>
/// <para><b>Real git</b>, for the reason <c>TheFileAtRevisionTests</c> gives: what this guards
/// against is git's behaviour — an edit not yet committed, a symlink git will not follow, a blob
/// that is binary — and a fake would assert a belief about it. The fake launcher is used only where
/// the assertion is that NO process starts, which only a recording launcher can see.</para>
/// <para>The caps are asserted through <see cref="SourceBudget"/> rather than retyped, so the trial's
/// correction (48 KB refused more requests than anything else; it is 64 KB now) is one edit.</para>
/// </remarks>
public sealed class ASourceRequestIsServedOrRefusedTests : IAsyncLifetime
{
    private static readonly ISourceOutliner Outliner = new TreeSitterOutliner();
    private readonly ProcessLauncher _launcher = new();
    private TempGitRepo _git = null!;
    private string _head = string.Empty;

    private const string ASha = "0123456789abcdef0123456789abcdef01234567";

    /// <summary>The C# golden fixture, committed as <c>src/Cart.cs</c> — its lines are the outline golden's lines.</summary>
    private static string CartSource =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "outline", "csharp.cs.txt")).ReplaceLineEndings("\n");

    /// <summary>A launcher that records every request and answers as if git had said yes — for the assertions that nothing was asked.</summary>
    private sealed class Recording : IProcessLauncher
    {
        public List<ProcessRequest> Launched { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Launched.Add(request);

            return Task.FromResult(new ProcessResult(0, "text", string.Empty, false));
        }
    }

    /// <summary>The real launcher, counted — for the assertion about how many reads a file costs.</summary>
    private sealed class Watching(IProcessLauncher real) : IProcessLauncher
    {
        public List<ProcessRequest> Launched { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Launched.Add(request);

            return real.RunAsync(request, ct);
        }

        public int Reads => Launched.Count(one => one.Arguments.Count > 0 && one.Arguments[0] == "show");
    }

    public async ValueTask InitializeAsync()
    {
        _git = await TempGitRepo.InitAsync(_launcher, "coai-source-");
        Directory.CreateDirectory(Path.Combine(_git.Path, "src"));
        await _git.WriteAsync("src/Cart.cs", CartSource);
        await _git.WriteAsync("README.md", "# notes\n\nplain prose\n");
        await _git.CommitAsync("the feature");
        _head = await _git.HeadAsync();
    }

    public async ValueTask DisposeAsync() => await _git.DisposeAsync();

    private SourceResolver Resolver(IProcessLauncher? launcher = null, string? head = null) =>
        new(new GitHistory(launcher ?? _launcher), Outliner, _git.Path, head ?? _head);

    private static SourceRequest Whole(string file) => new(file, string.Empty, 0, 0, "why");

    private static SourceRequest Symbol(string file, string symbol) => new(file, symbol, 0, 0, "the seam");

    private static SourceRequest Lines(string file, int from, int to) => new(file, string.Empty, from, to, "the lock");

    private static string Lines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string LinesOf(string text, int from, int to) =>
        string.Join('\n', Lines(text).Split('\n').Skip(from - 1).Take(to - from + 1));

    /// <summary>Commits a file and moves the head the resolver is pinned to.</summary>
    private async Task CommitAsync(string path, string text)
    {
        Directory.CreateDirectory(Path.Combine(_git.Path, Path.GetDirectoryName(path) ?? string.Empty));
        await _git.WriteAsync(path, text);
        await _git.CommitAsync("more");
        _head = await _git.HeadAsync();
    }

    // --------------------------------------------------------------------------------------------
    // Refused by NAME, before any process — traversal, credentials, lock files, build output.
    // --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("../secrets.txt")]
    [InlineData("src/../../etc/passwd")]
    [InlineData("src/..\\..\\etc\\passwd")]
    [InlineData("/etc/passwd")]
    [InlineData("\\Windows\\win.ini")]
    [InlineData("C:/Users/x/.ssh/config")]
    [InlineData("\\\\server\\share\\x")]
    [InlineData("src/Cart.cs\0evil")]
    public async Task APathOutsideTheRepository_IsRefusedWithoutAProcess(string path)
    {
        var launcher = new Recording();

        var turn = await Resolver(launcher).ServeAsync([Whole(path)], SourceSpend.None);

        launcher.Launched.Should().BeEmpty($"'{path}' must be refused before any process is started");
        turn.Served.Should().BeEmpty();
        turn.Refused.Should().ContainSingle().Which.Reason.Should().Contain("only files in the repository are served");
    }

    [Theory]
    [InlineData(".env")]
    [InlineData("deploy/.env.production")]
    [InlineData("certs/server.pem")]
    [InlineData("certs/server.key")]
    [InlineData("certs/client.pfx")]
    [InlineData("certs/client.p12")]
    [InlineData(".ssh/id_rsa")]
    [InlineData(".ssh/id_ed25519.pub")]
    [InlineData(".ssh/id_ecdsa")]
    public async Task ACredentialLookingFile_IsRefusedWithoutAProcess(string path)
    {
        var launcher = new Recording();

        var turn = await Resolver(launcher).ServeAsync([Whole(path)], SourceSpend.None);

        launcher.Launched.Should().BeEmpty($"'{path}' is refused on its name, and git is never asked");
        turn.Served.Should().BeEmpty();
        turn.Refused.Should().ContainSingle().Which.Reason.Should().StartWith("looks like a credential file");
    }

    [Theory]
    [InlineData("package-lock.json")]
    [InlineData("src/obj/Debug/Cart.cs")]
    [InlineData("web/node_modules/left-pad/index.js")]
    [InlineData("web/dist/app.min.js")]
    public async Task ALockFileOrBuildOutput_IsRefusedWithoutAProcess(string path)
    {
        var launcher = new Recording();

        var turn = await Resolver(launcher).ServeAsync([Whole(path)], SourceSpend.None);

        launcher.Launched.Should().BeEmpty();
        turn.Refused.Should().ContainSingle().Which.Reason.Should().Contain("lock file or build output");
    }

    /// <summary>The trial's false refusals, served: a redaction word in a NAME is not a credential.</summary>
    [Theory]
    [InlineData("providers/credentials.ts", "export function credentials() { return providers; }\n")]
    [InlineData("src/Auth.cs", "public sealed class Auth { }\n")]
    [InlineData("src/TokenIdentity.cs", "public sealed record TokenIdentity(string Value);\n")]
    [InlineData("src/tokens.rs", "pub struct Tokens;\n")]
    public async Task AnOrdinaryFileWhoseNameCarriesARedactionWord_IsServed(string path, string text)
    {
        await CommitAsync(path, text);

        var turn = await Resolver().ServeAsync([Whole(path)], SourceSpend.None);

        turn.Refused.Should().BeEmpty($"'{path}' is code the trial's reviewers were wrongly refused");
        var slice = turn.Served.Should().ContainSingle().Subject;
        slice.File.Should().Be(path);
        Lines(slice.Text).Should().Be(text);
    }

    // --------------------------------------------------------------------------------------------
    // Git objects at the pinned head — never the working tree.
    // --------------------------------------------------------------------------------------------

    [Fact]
    public async Task AnUncommittedEdit_IsNotServed()
    {
        await _git.WriteAsync("README.md", "# notes\n\nEDITED BUT NOT COMMITTED\n");

        var turn = await Resolver().ServeAsync([Whole("README.md")], SourceSpend.None);

        var slice = turn.Served.Should().ContainSingle().Subject;
        slice.Text.Should().NotContain("NOT COMMITTED", "the working tree is never read");
        Lines(slice.Text).Should().Be("# notes\n\nplain prose\n");
        slice.Sha.Should().Be(_head);
    }

    /// <summary>
    /// A path through a committed symlink is not a git object, so git answers nothing for it — and
    /// the file that IS on disk at that path is never looked at.
    /// </summary>
    /// <remarks>
    /// The link is written as a tree entry of mode <c>120000</c> through the index, which needs no
    /// symlink privilege on Windows; the directory of the same name is then created on disk, uncommitted,
    /// holding the file a working-tree read would find.
    /// </remarks>
    [Fact]
    public async Task AFileBehindACommittedSymlink_IsNotServed()
    {
        await _git.WriteAsync("target.txt", "../outside");
        var blob = (await _git.RunAsync("hash-object", "-w", "target.txt")).StdOut.Trim();
        await _git.GitAsync("update-index", "--add", "--cacheinfo", $"120000,{blob},escape");
        await _git.GitAsync("-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", "commit", "-m", "a link");
        _head = await _git.HeadAsync();
        Directory.CreateDirectory(Path.Combine(_git.Path, "escape"));
        await _git.WriteAsync("escape/secret.txt", "ON DISK ONLY\n");

        var turn = await Resolver().ServeAsync([Whole("escape/secret.txt")], SourceSpend.None);

        turn.Served.Should().BeEmpty("nothing at that path is a git object at head");
        turn.Refused.Should().ContainSingle().Which.Reason.Should().Contain("not in the repository at " + _head);
        turn.Render().Should().NotContain("ON DISK ONLY");
    }

    [Fact]
    public async Task AFileNotAtHead_IsRefused_NamingTheCommit()
    {
        var turn = await Resolver().ServeAsync([Whole("src/Missing.cs")], SourceSpend.None);

        turn.Refused.Should().ContainSingle().Which.Reason.Should().Be($"not in the repository at {_head}");
    }

    [Fact]
    public async Task AHeadThatIsNotACommitId_RefusesEverything_WithoutAProcess()
    {
        var launcher = new Recording();

        var turn = await Resolver(launcher, head: "HEAD").ServeAsync([Whole("README.md")], SourceSpend.None);

        launcher.Launched.Should().BeEmpty();
        turn.Refused.Should().ContainSingle().Which.Reason.Should().Contain("not a commit id");
    }

    [Fact]
    public async Task ABinaryFile_IsRefused()
    {
        Directory.CreateDirectory(Path.Combine(_git.Path, "assets"));
        await File.WriteAllBytesAsync(Path.Combine(_git.Path, "assets/logo.png"), [0x89, 0x50, 0x4E, 0x47, 0x00, 0x01, 0x02, 0xFF]);
        await _git.CommitAsync("a binary");
        _head = await _git.HeadAsync();

        var turn = await Resolver().ServeAsync([Whole("assets/logo.png")], SourceSpend.None);

        turn.Served.Should().BeEmpty();
        turn.Refused.Should().ContainSingle().Which.Reason.Should().Contain("binary");
    }

    // --------------------------------------------------------------------------------------------
    // What is served: a whole small file, the head of a big one, a symbol, a clamped span.
    // --------------------------------------------------------------------------------------------

    [Fact]
    public async Task AWholeFile_IsServedWhenItIsSmall()
    {
        var turn = await Resolver().ServeAsync([Whole("src/Cart.cs")], SourceSpend.None);

        var slice = turn.Served.Should().ContainSingle().Subject;
        Lines(slice.Text).Should().Be(CartSource);
        (slice.StartLine, slice.EndLine).Should().Be((1, slice.TotalLines));
        slice.Note.Should().BeEmpty();
    }

    [Fact]
    public async Task ABigFile_ServesItsHead_AndSaysToAskForLines()
    {
        var big = string.Join('\n', Enumerable.Range(1, 600).Select(i => $"// line {i,4} " + new string('x', 60))) + "\n";
        Encoding.UTF8.GetByteCount(big).Should().BeGreaterThan(SourceBudget.WholeFileBytes, "the fixture must be over the whole-file ceiling");
        await CommitAsync("src/Big.cs", big);

        var turn = await Resolver().ServeAsync([Whole("src/Big.cs")], SourceSpend.None);

        var slice = turn.Served.Should().ContainSingle().Subject;
        slice.StartLine.Should().Be(1);
        slice.EndLine.Should().BeLessThan(600);
        Encoding.UTF8.GetByteCount(slice.Text).Should().BeLessThanOrEqualTo(SourceBudget.WholeFileBytes);
        Lines(slice.Text).Should().Be(LinesOf(big, 1, slice.EndLine));
        slice.Note.Should().Contain("ask for lines").And.Contain($"{slice.EndLine + 1}-600");
    }

    [Fact]
    public async Task LinesAreClampedToTheCap_AndToTheFile()
    {
        var long_ = string.Join('\n', Enumerable.Range(1, 500).Select(i => $"line {i}")) + "\n";
        await CommitAsync("src/Long.cs", long_);
        var resolver = Resolver();

        var whole = (await resolver.ServeAsync([Lines("src/Long.cs", 1, 500)], SourceSpend.None)).Served.Single();
        var tail = (await resolver.ServeAsync([Lines("src/Long.cs", 450, 999)], SourceSpend.None)).Served.Single();
        var past = await resolver.ServeAsync([Lines("src/Long.cs", 600, 700)], SourceSpend.None);

        (whole.StartLine, whole.EndLine).Should().Be((1, SourceBudget.MaxLines));
        Lines(whole.Text).Should().Be(LinesOf(long_, 1, SourceBudget.MaxLines));
        whole.Note.Should().Contain($"cut at {SourceBudget.MaxLines} lines").And.Contain("401-500");
        (tail.StartLine, tail.EndLine).Should().Be((450, 500), "the end is clamped to the file");
        Lines(tail.Text).Should().Be(LinesOf(long_, 450, 500));
        past.Served.Should().BeEmpty();
        past.Refused.Should().ContainSingle().Which.Reason.Should().Contain("has 500 lines");
    }

    [Theory]
    [InlineData("Add")]
    [InlineData("Cart.Add")]
    [InlineData("Cart::Add")]
    [InlineData("Shop.Orders.Cart.Add")]
    public async Task AQualifiedSymbol_IsServedWithItsOwnLines(string symbol)
    {
        var turn = await Resolver().ServeAsync([Symbol("src/Cart.cs", symbol)], SourceSpend.None);

        turn.Refused.Should().BeEmpty($"'{symbol}' is how the trial's reviewers asked");
        var slice = turn.Served.Should().ContainSingle().Subject;
        (slice.StartLine, slice.EndLine).Should().Be((28, 35), "the outline golden's lines for Add<T>");
        Lines(slice.Text).Should().Be(LinesOf(CartSource, 28, 35));
        slice.Symbol.Should().Be("Add");
    }

    [Fact]
    public async Task Overloads_AreServedUpToTheCap_EachWithItsOwnLines()
    {
        const string maths = """
            public static class Maths
            {
                public static int Add(int a) => a;
                public static int Add(int a, int b) => a + b;
                public static int Add(int a, int b, int c) => a + b + c;
                public static int Add(int a, int b, int c, int d) => a + b + c + d;
            }
            """;
        await CommitAsync("src/Maths.cs", maths + "\n");

        var turn = await Resolver().ServeAsync([Symbol("src/Maths.cs", "Maths.Add")], SourceSpend.None);

        turn.Served.Should().HaveCount(SourceBudget.MaxOverloads);
        turn.Served.Select(slice => slice.StartLine).Should().Equal(3, 4, 5);
        turn.Served.Should().OnlyContain(slice => slice.Note.Contains("4 overloads"));
    }

    [Fact]
    public async Task AnUnknownSymbol_IsRefused_ListingTheNamesTheFileHas()
    {
        var turn = await Resolver().ServeAsync([Symbol("src/Cart.cs", "Remove")], SourceSpend.None);

        turn.Served.Should().BeEmpty();
        var refusal = turn.Refused.Should().ContainSingle().Subject;
        refusal.Symbol.Should().Be("Remove");
        refusal.Reason.Should().Contain("no declaration named 'Remove'").And.Contain("Cart").And.Contain("Add");
    }

    [Fact]
    public async Task ASymbolInAFileTheOutlinerCannotRead_SaysToAskForLines()
    {
        var turn = await Resolver().ServeAsync([Symbol("README.md", "notes")], SourceSpend.None);

        turn.Served.Should().BeEmpty();
        turn.Refused.Should().ContainSingle().Which.Reason.Should().Contain("ask for lines");
    }

    // --------------------------------------------------------------------------------------------
    // The caps, in one place — and a refusal that names what was dropped.
    // --------------------------------------------------------------------------------------------

    [Fact]
    public async Task AtMostEightRequestsATurn_AndTheNinthIsNamed()
    {
        var requests = Enumerable.Range(1, SourceBudget.RequestsPerTurn + 1)
            .Select(i => Lines("src/Cart.cs", i, i)).ToList();

        var turn = await Resolver().ServeAsync(requests, SourceSpend.None);

        turn.Served.Should().HaveCount(SourceBudget.RequestsPerTurn);
        var dropped = turn.Refused.Should().ContainSingle().Subject;
        dropped.Reason.Should().Contain($"more than {SourceBudget.RequestsPerTurn} requests");
        dropped.File.Should().Be("src/Cart.cs");
    }

    [Fact]
    public async Task TheTurnBudget_RefusesWhatDoesNotFit_NamingWhatWasDropped()
    {
        var wide = string.Join('\n', Enumerable.Range(1, 400).Select(i => $"// {i,3} " + new string('y', 90))) + "\n";
        var sliceBytes = Encoding.UTF8.GetByteCount(LinesOf(wide, 1, 400));
        sliceBytes.Should().BeInRange(SourceBudget.TurnBytes / 2 + 1, SourceBudget.TurnBytes, "two of these overrun one turn, one fits");
        await CommitAsync("src/A.cs", wide);
        await CommitAsync("src/B.cs", wide);

        var turn = await Resolver().ServeAsync([Lines("src/A.cs", 1, 400), Lines("src/B.cs", 1, 400)], SourceSpend.None);

        turn.Served.Should().ContainSingle().Which.File.Should().Be("src/A.cs");
        var dropped = turn.Refused.Should().ContainSingle().Subject;
        dropped.File.Should().Be("src/B.cs");
        dropped.Reason.Should().Contain("budget spent").And.Contain($"{SourceBudget.TurnBytes / 1024} KB");
        turn.Spent.Bytes.Should().Be(sliceBytes);
    }

    [Fact]
    public async Task TheReviewerBudget_IsCarriedAcrossTurns_AndRefusesPastIt()
    {
        var alreadySpent = new SourceSpend(SourceBudget.ReviewerBytes - 100);
        Encoding.UTF8.GetByteCount(CartSource).Should().BeGreaterThan(100, "the fixture must not fit in what is left");

        var turn = await Resolver().ServeAsync([Whole("src/Cart.cs"), Lines("README.md", 1, 1)], alreadySpent);

        turn.Refused.Should().ContainSingle().Which.File.Should().Be("src/Cart.cs", "100 bytes are left and the file is bigger than that");
        turn.Refused.Single().Reason.Should().Contain("budget spent").And.Contain($"{SourceBudget.ReviewerBytes / 1024} KB");
        turn.Served.Should().ContainSingle().Which.File.Should().Be("README.md");
        turn.Spent.Bytes.Should().Be(alreadySpent.Bytes + Encoding.UTF8.GetByteCount(turn.Served.Single().Text));
    }

    [Fact]
    public void TheCaps_AreTheTrialsCorrection()
    {
        SourceBudget.TurnBytes.Should().Be(64 * 1024, "48 KB refused more requests than anything else in the trial");
        SourceBudget.ReviewerBytes.Should().Be(128 * 1024);
        SourceBudget.RequestsPerTurn.Should().Be(8);
        SourceBudget.MaxLines.Should().Be(400);
        SourceBudget.WholeFileBytes.Should().Be(16 * 1024);
    }

    // --------------------------------------------------------------------------------------------
    // One git read per file per round; content redacted; output fenced.
    // --------------------------------------------------------------------------------------------

    [Fact]
    public async Task OneGitReadPerFile_AcrossTwoRequests_AndTwoTurns()
    {
        var watching = new Watching(_launcher);
        var resolver = Resolver(watching);

        var first = await resolver.ServeAsync([Symbol("src/Cart.cs", "Add"), Lines("src/Cart.cs", 1, 3)], SourceSpend.None);
        var second = await resolver.ServeAsync([Whole("src/Cart.cs")], first.Spent);

        first.Served.Should().HaveCount(2);
        second.Served.Should().ContainSingle();
        watching.Reads.Should().Be(1, "the file is read out of git once per round, whatever is asked of it");
    }

    [Fact]
    public async Task AKeyShapedStringInsideServedContent_IsRedacted()
    {
        await CommitAsync("src/Keys.cs", "const string Live = \"sk-abcdefghijklmnop1234\";\nconst int Other = 1;\n");

        var turn = await Resolver().ServeAsync([Whole("src/Keys.cs")], SourceSpend.None);

        var text = turn.Served.Should().ContainSingle().Subject.Text;
        text.Should().NotContain("sk-abcdefghijklmnop1234");
        text.Should().Contain("sk-[redacted]").And.Contain("const int Other = 1;");
    }

    [Fact]
    public async Task TheServedText_IsFencedWithPathLinesAndSha()
    {
        var turn = await Resolver().ServeAsync([Symbol("src/Cart.cs", "Cart.Add"), Whole("certs/x.pem")], SourceSpend.None);

        var rendered = turn.Render();

        rendered.Should().Contain($"src/Cart.cs lines 28-35 of 49 @ {_head}");
        rendered.Should().Contain("```csharp\n" + LinesOf(CartSource, 28, 35) + "\n```");
        rendered.Should().Contain("not served: certs/x.pem — looks like a credential file (*.pem)");
    }

    /// <summary>A file whose text carries a fence of its own is fenced with a longer one, so the served code cannot close the block early.</summary>
    [Fact]
    public async Task AFileContainingAFence_IsFencedWithALongerOne()
    {
        await CommitAsync("docs/how.md", "before\n```\ncode\n```\nafter\n");

        var rendered = (await Resolver().ServeAsync([Whole("docs/how.md")], SourceSpend.None)).Render();

        rendered.Should().Contain("````markdown\nbefore\n```\ncode\n```\nafter\n````");
    }
}
