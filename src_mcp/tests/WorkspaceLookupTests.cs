using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Feature;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// coai's own read-only list and search, served to an antigravity consultant that cannot list or search in plan mode
/// (todo/PLAN_agy_searches_through_coai.md §3): contained in the granted roots with links resolved, never showing
/// credentials, <c>.git</c>, dependency folders or binary contents, redacted, and saying out loud when a cap cut it.
/// </summary>
public sealed class WorkspaceLookupTests : IAsyncLifetime
{
    private const string MarkerOne = "MARKER_ONE_7731";
    private const string MarkerTwo = "MARKER_TWO_5512";

    private string _base = string.Empty;
    private string _root = string.Empty;
    private string _outside = string.Empty;

    public ValueTask InitializeAsync()
    {
        _base = Directory.CreateTempSubdirectory("coai-lookup-").FullName;
        _root = Directory.CreateDirectory(Path.Combine(_base, "root")).FullName;
        _outside = Directory.CreateDirectory(Path.Combine(_base, "outside")).FullName;
        Write("projA/README.md", "hello\n");
        Write("projB/src/mailer.ts", $"line one\nconst sent = '{MarkerOne}';\n");
        Write("projB/keep.txt", "keep me\n");
        Write(".git/config", $"{MarkerOne}\n");
        Write("node_modules/dep/index.js", $"{MarkerOne}\n");
        Write("projB/id_rsa", $"{MarkerOne}\n");
        Write("projB/notes.md", "password = hunter2\n");
        File.WriteAllBytes(Path.Combine(_root, "projB", "blob.bin"), [0x00, .. System.Text.Encoding.ASCII.GetBytes(MarkerOne)]);
        File.WriteAllText(Path.Combine(_outside, "secret.txt"), $"{MarkerTwo}\n");

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DirectoryLink.Remove(Path.Combine(_root, "escape"));
        try
        {
            Directory.Delete(_base, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        return ValueTask.CompletedTask;
    }

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private WorkspaceLookup Lookup(LookupLimits? limits = null, params string[] roots) =>
        new(roots.Length == 0 ? [_root] : roots, limits);

    private static string Serve(WorkspaceLookup lookup, params LookupRequest[] requests) =>
        lookup.Serve(requests, TestContext.Current.CancellationToken).Text;

    private static LookupRequest.List List(string path) => new($"list {path}", path);

    private static LookupRequest.Search Search(string text, string path = "") =>
        new($"search \"{text}\"" + (path.Length > 0 ? $" in {path}" : string.Empty), text, path);

    [Fact]
    public void AFolder_IsListed_OneLevel_FoldersMarked_AndACredentialNameNeverShown()
    {
        var text = Serve(Lookup(), List("projB"));

        text.Should().Contain("src/").And.Contain("keep.txt").And.Contain("notes.md");
        text.Should().NotContain("id_rsa", "a credential-shaped name is never shown");
        text.Should().NotContain("mailer.ts", "one level only");
    }

    [Fact]
    public void TheRootItself_IsListed_WithoutGitOrDependencyFolders()
    {
        var text = Serve(Lookup(), List("."));

        text.Should().Contain("projA/").And.Contain("projB/");
        text.Should().NotContain(".git").And.NotContain("node_modules");
    }

    [Fact]
    public void ASearch_FindsTheLine_AndNeverLooksInGitDependenciesCredentialsOrBinaries()
    {
        var text = Serve(Lookup(), Search(MarkerOne));

        text.Should().Contain("projB/src/mailer.ts:2:");
        text.Should().NotContain(".git").And.NotContain("node_modules").And.NotContain("id_rsa").And.NotContain("blob.bin");
    }

    [Fact]
    public void ASearch_IsALiteral_NeverARegex_AndIgnoresCase()
    {
        Serve(Lookup(), Search("MARKER.ONE")).Should().NotContain("mailer.ts", "a dot is a dot");
        Serve(Lookup(), Search(MarkerOne.ToLowerInvariant())).Should().Contain("mailer.ts:2:");
    }

    [Fact]
    public void WhatASearchReturns_IsRedacted()
    {
        var text = Serve(Lookup(), Search("password"));

        text.Should().Contain("notes.md:1:").And.Contain("[redacted]").And.NotContain("hunter2");
    }

    [Fact]
    public void DotDot_OutOfTheRoot_IsRefusedByName_AndNothingOutsideIsRead()
    {
        var text = Serve(Lookup(), List("../outside"), Search(MarkerTwo, "../outside"));

        text.Should().Contain("not served").And.Contain("outside the granted roots");
        // The request line itself names the marker; a LEAKED hit would end a line with it.
        text.Should().NotContain("secret.txt").And.NotContain(MarkerTwo + "\n");
    }

    [Fact]
    public void AFolderThatCannotBeRead_IsSkippedAndSaid_NeverTheWholeTurnLost()
    {
        var lookup = Lookup();
        var locked = Path.Combine(_root, "projA", "locked.txt");
        File.WriteAllText(locked, $"{MarkerOne}\n");
        using var held = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var text = Serve(lookup, Search(MarkerOne), List("projA"));

        text.Should().Contain("mailer.ts:2:", "the rest of the walk is served");
        if (OperatingSystem.IsWindows())
        {
            text.Should().Contain("could not be read");
        }

        text.Should().Contain("README.md", "the next request is still served");
    }

    [Fact]
    public void AnAbsolutePathInNoRoot_IsRefusedByName()
    {
        var text = Serve(Lookup(), List(_outside));

        text.Should().Contain("not served").And.Contain("outside the granted roots").And.NotContain("secret.txt");
    }

    [Fact]
    public async Task ALinkInsideTheRootLeadingOut_IsNeitherListedThroughNorSearched()
    {
        await DirectoryLink.MakeAsync(new ProcessLauncher(), Path.Combine(_root, "escape"), _outside);

        var text = Serve(Lookup(), List("escape"), Search(MarkerTwo));

        text.Should().Contain("not served").And.Contain("outside the granted roots");
        text.Should().NotContain("secret.txt").And.NotContain(MarkerTwo + "\n").And.NotContain("escape/secret");
    }

    [Fact]
    public void WithSeveralRoots_ARelativePathOrNoFolder_IsRefused_NamingTheRootsToUse()
    {
        var second = Directory.CreateDirectory(Path.Combine(_base, "second")).FullName;
        var lookup = Lookup(null, _root, second);

        var text = Serve(lookup, List("projB"), Search(MarkerOne));

        text.Should().Contain("absolute").And.Contain(_root).And.Contain(second);
        text.Should().NotContain("keep.txt").And.NotContain("mailer.ts");
        Serve(lookup, List(Path.Combine(_root, "projB"))).Should().Contain("keep.txt", "an absolute path inside a root is served");
    }

    [Fact]
    public void EveryCap_IsSaid_NeverSilent()
    {
        Write("projB/src/second.ts", $"{MarkerOne}\n");

        Serve(Lookup(LookupLimits.Default with { ListEntries = 2 }), List("projB")).Should().Contain("stopped at 2 entries");
        Serve(Lookup(LookupLimits.Default with { SearchHits = 1 }), Search(MarkerOne)).Should().Contain("stopped at 1 hit");
        Serve(Lookup(LookupLimits.Default with { WalkFiles = 1 }), Search(MarkerOne)).Should().Contain("stopped after 1 file");
        Serve(Lookup(LookupLimits.Default with { FileBytes = 4 }), Search(MarkerOne)).Should().Contain("over the size limit");
        Serve(Lookup(LookupLimits.Default with { WalkTime = TimeSpan.Zero }), Search(MarkerOne)).Should().Contain("time limit");
    }

    [Fact]
    public void TheTurnBudget_ServesWhatFits_AndNamesWhatItDidNot()
    {
        var text = Serve(Lookup(LookupLimits.Default with { TurnBytes = 64 }), List("projB"), List("projA"));

        text.Should().Contain("not served").And.Contain("list projA").And.Contain("turn's budget");
    }

    [Fact]
    public void APathTheFileSystemCannotHold_IsRefusedByName_NeverThrown_AndTheNextRequestIsServed()
    {
        // code round, codex + gemini: Path.GetFullPath throws on a NUL, and one bad line must not lose the turn.
        var text = Serve(Lookup(), List("pro\0jA"), List("projA"));

        text.Should().Contain("not served").And.Contain("not a valid path");
        text.Should().Contain("README.md", "the next request is still served");
    }

    [Fact]
    public void AFileTheDiffExcludes_IsNotSearchedWhenNamedDirectly()
    {
        // code round, gemini: a folder walk skipped it, but naming the file went straight to the read.
        Write("projB/package-lock.json", $"{MarkerOne}\n");

        var text = Serve(Lookup(), Search(MarkerOne, "projB/package-lock.json"));

        text.Should().Contain("not served").And.Contain("excluded").And.NotContain("package-lock.json:1:");
    }

    [Fact]
    public void ABlockPastItsTimeLimit_ServesWhatStarted_AndNamesTheRest()
    {
        var text = Serve(Lookup(LookupLimits.Default with { BlockTime = TimeSpan.Zero }), List("projA"), List("projB"));

        text.Should().Contain("README.md", "the first request always runs");
        text.Should().Contain("list projB\nnot served").And.Contain("time limit").And.NotContain("keep.txt");
    }

    [Fact]
    public void ATurnBudgetThatCannotHoldAResult_ReadsNothingMore()
    {
        // code round, codex + gemini: a request the budget can no longer hold was walked anyway, then thrown away.
        var resolved = 0;
        var lookup = new WorkspaceLookup([_root], LookupLimits.Default with { TurnBytes = 64 }, path => { resolved++; return DocumentReader.FollowLink(path); });

        var text = Serve(lookup, List("projB"), Search(MarkerOne));

        text.Should().Contain("list projB\nnot served").And.Contain("turn's budget");
        resolved.Should().Be(0, "nothing is resolved, listed or walked for a result that could not be sent");
    }

    [Fact]
    public void AMissingFolder_IsSaidAsMissing()
    {
        Serve(Lookup(), List("nowhere")).Should().Contain("not served").And.Contain("no folder");
    }

    [Fact]
    public void TheRootsAreTheGrantedOnes_ForThePrompt()
    {
        Lookup().Roots.Should().Equal(_root);
        LookupLimits.Default.RequestsPerTurn.Should().Be(LookupBudget.RequestsPerTurn);
    }
}
