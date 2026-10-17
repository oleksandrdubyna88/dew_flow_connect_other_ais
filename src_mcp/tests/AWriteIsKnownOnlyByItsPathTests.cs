using System.Text.Json;
using CoaiMcp.Runners.Worktrees;
using CoaiMcp.Runners.Context;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E3.S3 of <c>todo/PLAN_a_round_records_who_wrote_the_code_and_how_much_E3.md</c>: from ONE <c>tool_use</c> block, is it a write, and
/// which checkout received it? Nothing but the path is read; <c>Bash</c> and <c>PowerShell</c> are never a write.
/// </summary>
/// <remarks>
/// <para>The trees are built in a <c>TempDir</c> with <c>.git</c> folders and files — no git process. A file full of negative
/// assertions needs one positive (testing.md), so the shell and non-string tests sit beside tests that DO find a write, and
/// every refusal fixture also carries a <c>file_path</c> that WOULD resolve if the tool counted.</para>
/// <para>The fixture cases with writes (c06, c12–c20) are read through E3.S4's materialiser, not here: S3's trees stay synthetic.</para>
/// </remarks>
public sealed class AWriteIsKnownOnlyByItsPathTests
{
    private const string Sentinel = "COAI-WRITE-CONTENT-SENTINEL";

    private static JsonElement Input(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }

    private static string Quoted(string path) => JsonSerializer.Serialize(path);

    private static WriteTarget Resolve(string path) => new CheckoutRoots().Resolve(path);

    private static string Key(string path) => RepositoryIdentity.Normalised(path);

    private static string MakeFile(TempDir root, params string[] parts)
    {
        var file = root.At(parts);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "x");

        return file;
    }

    // ---- is it a write ---------------------------------------------------------------------------------------------

    [Fact]
    public void Each_write_tool_yields_its_path()
    {
        WriteTools.Names.Should().Equal("Edit", "Write", "MultiEdit", "NotebookEdit");

        WriteTools.TryPath("Edit", Input("{\"file_path\":\"/r/a.cs\",\"old_string\":\"x\",\"new_string\":\"y\"}"), out var edit).Should().BeTrue();
        WriteTools.TryPath("Write", Input("{\"file_path\":\"/r/b.cs\",\"content\":\"x\"}"), out var write).Should().BeTrue();
        WriteTools.TryPath("MultiEdit", Input("{\"file_path\":\"/r/c.cs\",\"edits\":[]}"), out var multi).Should().BeTrue();
        WriteTools.TryPath("NotebookEdit", Input("{\"notebook_path\":\"/r/d.ipynb\",\"new_source\":\"x\"}"), out var notebook).Should().BeTrue();

        (edit, write, multi, notebook).Should().Be(("/r/a.cs", "/r/b.cs", "/r/c.cs", "/r/d.ipynb"));
    }

    [Fact]
    public void A_notebook_edit_reads_notebook_path_and_the_others_read_file_path()
    {
        WriteTools.TryPath("NotebookEdit", Input("{\"file_path\":\"/r/a.ipynb\"}"), out _).Should().BeFalse("its ONE property is notebook_path");
        WriteTools.TryPath("Edit", Input("{\"notebook_path\":\"/r/a.cs\"}"), out _).Should().BeFalse("and Edit's is file_path");
    }

    [Fact]
    public void A_shell_command_that_writes_a_file_is_not_a_write()
    {
        // Each input ALSO carries a file_path: only the four names count, so a tool that merely carries one is still no write.
        WriteTools.TryPath("Bash", Input("{\"command\":\"echo x > f\",\"file_path\":\"/r/f\"}"), out var bash).Should().BeFalse();
        WriteTools.TryPath("PowerShell", Input("{\"command\":\"Set-Content f x\",\"file_path\":\"/r/f\"}"), out var shell).Should().BeFalse();
        WriteTools.TryPath("mcp__files__save", Input("{\"file_path\":\"/r/f\"}"), out var mcp).Should().BeFalse();
        WriteTools.TryPath("edit", Input("{\"file_path\":\"/r/f\"}"), out var lower).Should().BeFalse("names are case-sensitive");

        (bash, shell, mcp, lower).Should().Be((string.Empty, string.Empty, string.Empty, string.Empty), "a refusal leaves no path behind");
    }

    [Theory]
    [InlineData("{\"file_path\":5}")]
    [InlineData("{\"file_path\":null}")]
    [InlineData("{\"file_path\":[\"/r/a.cs\"]}")]
    [InlineData("{\"file_path\":{\"x\":1}}")]
    [InlineData("{\"file_path\":\"\"}")]
    [InlineData("{}")]
    [InlineData("\"a string, not an object\"")]
    [InlineData("[\"/r/a.cs\"]")]
    public void A_path_that_is_not_a_string_is_not_a_write(string input)
    {
        WriteTools.TryPath("Write", Input(input), out var path).Should().BeFalse();
        path.Should().BeEmpty();
    }

    [Fact]
    public void Nothing_but_the_path_is_read()
    {
        var block = Input("{\"file_path\":\"/r/a.cs\",\"content\":\"" + Sentinel + "\",\"old_string\":\"" + Sentinel + "\",\"new_string\":\"" + Sentinel + "\",\"edits\":[{\"old_string\":\"" + Sentinel + "\"}]}");

        WriteTools.TryPath("Write", block, out var path).Should().BeTrue("the positive half: the path IS read");

        path.Should().Be("/r/a.cs").And.NotContain(Sentinel);
    }

    // ---- which checkout --------------------------------------------------------------------------------------------

    [Fact]
    public void A_write_lands_in_the_nearest_checkout()
    {
        using var root = TempDir.For("testsToDelete-coai-write-nearest-");
        Directory.CreateDirectory(root.At("repo", ".git"));
        var file = MakeFile(root, "repo", "src", "a.cs");

        var target = Resolve(file);

        target.Should().Be(new WriteTarget(WriteTargetKind.Checkout, Key(root.At("repo"))));
    }

    [Fact]
    public void A_worktree_is_its_own_checkout()
    {
        // A linked worktree's .git is a FILE, and two worktrees are two checkouts: the common dir is the wrong tool here.
        using var root = TempDir.For("testsToDelete-coai-write-worktree-");
        Directory.CreateDirectory(root.At("repo", ".git"));
        MakeFile(root, "wt", ".git");
        var file = MakeFile(root, "wt", "src", "a.cs");

        Resolve(file).Should().Be(new WriteTarget(WriteTargetKind.Checkout, Key(root.At("wt"))));
    }

    [Fact]
    public void A_submodule_write_resolves_to_the_submodule_root()
    {
        // Whether an inner root under the gate's repo_path counts as "here" is E4's rule; this only says where the file landed.
        using var root = TempDir.For("testsToDelete-coai-write-submodule-");
        Directory.CreateDirectory(root.At("repo", ".git"));
        MakeFile(root, "repo", "sub", ".git");
        var file = MakeFile(root, "repo", "sub", "lib", "a.cs");

        Resolve(file).Should().Be(new WriteTarget(WriteTargetKind.Checkout, Key(root.At("repo", "sub"))));
    }

    [Fact]
    public void A_scratch_file_is_outside_any_checkout()
    {
        using var root = TempDir.For("testsToDelete-coai-write-scratch-");
        AssertNoGitAbove(root.Path);
        var file = MakeFile(root, "scratch", "note.txt");

        Resolve(file).Should().Be(new WriteTarget(WriteTargetKind.OutsideAnyCheckout, string.Empty), "a scratchpad is not another repository");
    }

    private static void AssertNoGitAbove(string path)
    {
        for (var dir = path; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            var marker = Path.Combine(dir, ".git");
            (File.Exists(marker) || Directory.Exists(marker)).Should().BeFalse("the precondition of this test: a .git at '" + marker + "' would make the temp root a checkout");
        }
    }

    [Fact]
    public void A_write_into_a_removed_worktree_is_unresolved_not_its_parent()
    {
        // The directory is gone and its parent folder is inside a checkout two levels up: walking up from a missing directory
        // would answer with whatever contains it, and be wrong.
        using var root = TempDir.For("testsToDelete-coai-write-removed-");
        Directory.CreateDirectory(root.At("repo", ".git"));
        Directory.CreateDirectory(root.At("repo", "worktrees"));
        var gone = root.At("repo", "worktrees", "wt-gone", "src", "a.cs");

        var target = Resolve(gone);

        target.Should().Be(new WriteTarget(WriteTargetKind.Unresolved, Key(root.At("repo", "worktrees", "wt-gone", "src"))));
        target.Kind.Should().NotBe(WriteTargetKind.Checkout);
    }

    [Fact]
    public void A_relative_path_is_unresolved_never_joined_to_our_cwd()
    {
        // This process's cwd is not the agent's. Whatever the cwd holds, the answer is the same.
        Resolve("a.cs").Should().Be(new WriteTarget(WriteTargetKind.Unresolved, string.Empty));
        Resolve(Path.Combine("src", "deeper", "a.cs")).Should().Be(new WriteTarget(WriteTargetKind.Unresolved, string.Empty));
    }

    [Fact]
    public void Backslashes_and_case_give_one_key()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "drive letters and backslashes are a Windows spelling");
        using var root = TempDir.For("testsToDelete-coai-write-keys-");
        Directory.CreateDirectory(root.At("X", "repo", ".git"));
        MakeFile(root, "X", "repo", "src", "a.cs");
        var roots = new CheckoutRoots();
        var plain = root.At("X", "repo", "src", "a.cs");
        var odd = char.ToLowerInvariant(plain[0]) + plain[1..].Replace('\\', '/').ToLowerInvariant();

        var first = roots.Resolve(plain);
        var second = roots.Resolve(odd);

        second.Should().Be(first);
        first.Kind.Should().Be(WriteTargetKind.Checkout);
        first.Where.Should().Be(Key(root.At("X", "repo")));
    }

    [Fact]
    public void A_mixed_separator_lower_case_drive_path_finds_its_checkout()
    {
        // E1 Q8: on Windows 90 % of write paths use backslashes and 219 subagent writes have a lower-case drive.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows spelling");
        using var root = TempDir.For("testsToDelete-coai-write-mixed-");
        Directory.CreateDirectory(root.At("repo", ".git"));
        MakeFile(root, "repo", "src", "p", "a.cs");
        var real = root.At("repo", "src", "p", "a.cs");
        var mixed = char.ToLowerInvariant(real[0]) + real[1..].Replace("\\src\\", "/src\\").Replace("\\p\\", "/p/");

        mixed.Should().NotBe(real, "the fixture must really be a different spelling");
        Resolve(mixed).Should().Be(new WriteTarget(WriteTargetKind.Checkout, Key(root.At("repo"))));
    }

    [Fact]
    public void A_windows_shaped_path_on_linux_is_unresolved()
    {
        // E1 found no Windows-shaped path on WSL. The outcome is fixed in case one appears.
        Assert.SkipWhen(OperatingSystem.IsWindows(), "on Windows this is a real absolute path");

        Resolve("D:\\x\\a.cs").Should().Be(new WriteTarget(WriteTargetKind.Unresolved, string.Empty));
    }

    [Fact]
    public void One_instance_answers_the_same_directory_from_its_cache()
    {
        using var root = TempDir.For("testsToDelete-coai-write-cache-");
        AssertNoGitAbove(root.Path);
        Directory.CreateDirectory(root.At("repo", ".git"));
        var one = MakeFile(root, "repo", "src", "a.cs");
        var two = MakeFile(root, "repo", "src", "b.cs");
        var roots = new CheckoutRoots();

        roots.Resolve(one).Should().Be(new WriteTarget(WriteTargetKind.Checkout, Key(root.At("repo"))));
        Directory.Delete(root.At("repo", ".git"));

        roots.Resolve(two).Kind.Should().Be(WriteTargetKind.Checkout, "the directory was answered once for this read, and one instance serves one read");
        new CheckoutRoots().Resolve(two).Kind.Should().Be(WriteTargetKind.OutsideAnyCheckout, "a new instance asks the filesystem again");
    }

    [Fact]
    public void A_dot_dot_segment_is_resolved_before_the_walk()
    {
        // The walk went up the path TEXT: `repo/../other/a.cs` climbed through `repo` and answered Checkout(repo) for a file
        // that is in `other`, which is no checkout.
        using var root = TempDir.For("testsToDelete-coai-write-dotdot-");
        AssertNoGitAbove(root.Path);
        Directory.CreateDirectory(root.At("repo", ".git"));
        Directory.CreateDirectory(root.At("other"));
        MakeFile(root, "repo", "src", "a.cs");
        var roots = new CheckoutRoots();

        roots.Resolve(Path.Combine(root.At("repo"), "..", "other", "a.cs"))
            .Should().Be(new WriteTarget(WriteTargetKind.OutsideAnyCheckout, string.Empty), "the file is in other, not in repo");
        roots.Resolve(Path.Combine(root.At("other"), "..", "repo", "src", "a.cs"))
            .Should().Be(new WriteTarget(WriteTargetKind.Checkout, Key(root.At("repo"))), "and the other way round the file IS in repo");
    }

    [Fact]
    public void The_cache_key_keeps_case_where_the_file_system_does()
    {
        // The key was RepositoryIdentity.Normalised, which lower-cases: on a case-sensitive file system /work/App (a checkout) and
        // /work/app (a plain folder) shared one cache entry and the first answer won.
        var app = Path.Combine(Path.GetTempPath(), "work", "App");
        var lower = Path.Combine(Path.GetTempPath(), "work", "app");

        CheckoutRoots.CacheKey(app, caseSensitive: true).Should().NotBe(CheckoutRoots.CacheKey(lower, caseSensitive: true), "two directories that differ by case are two directories here");
        CheckoutRoots.CacheKey(app, caseSensitive: false).Should().Be(CheckoutRoots.CacheKey(lower, caseSensitive: false), "and on a case-insensitive file system they are one");
    }

    [Fact]
    public void Case_distinct_directories_are_two_answers_on_a_case_sensitive_file_system()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(), "this needs a case-sensitive file system (WSL, Linux CI)");
        using var root = TempDir.For("testsToDelete-coai-write-case-");
        AssertNoGitAbove(root.Path);
        Directory.CreateDirectory(root.At("work", "App", ".git"));
        var withGit = MakeFile(root, "work", "App", "a.cs");
        var plain = MakeFile(root, "work", "app", "a.cs");
        var roots = new CheckoutRoots();

        roots.Resolve(withGit).Kind.Should().Be(WriteTargetKind.Checkout);
        roots.Resolve(plain).Kind.Should().Be(WriteTargetKind.OutsideAnyCheckout, "work/app is not work/App");
    }

    [Fact]
    public void GitMarker_answers_for_a_folder_a_file_and_nothing()
    {
        using var root = TempDir.For("testsToDelete-coai-write-marker-");
        Directory.CreateDirectory(root.At("clone", ".git"));
        MakeFile(root, "worktree", ".git");
        Directory.CreateDirectory(root.At("empty-mount"));

        GitMarker.HasGit(root.At("clone")).Should().BeTrue();
        GitMarker.HasGit(root.At("worktree")).Should().BeTrue("a linked worktree and a populated submodule carry a .git FILE");
        GitMarker.HasGit(root.At("empty-mount")).Should().BeFalse();
    }
}
