using System.Text;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E4 s5 t1 (F1 to F7; SPEC D): the formatter runs on the files of the turn only, in a unit of its own with only a loopback device, and what it changes outside those files is undone. Pure half: which files, how a name reaches the argv. Real half: the real coder rig, real git, real units —
/// a project of the file whose <c>format</c> is a <c>sed</c> that turns <c>BAD</c> into <c>GOOD</c>.
/// </summary>
[Collection("fakecli-env")]
public sealed class AFormatStepTests
{
    private static readonly ToolchainDefaults Table = ToolchainDefaults.Current;

    // ---- d1 and d2, pure ----

    [Fact]
    public void D1_TheFilesGiven_AreTheOnesTheCommitChanged_InsideTheProject_WithTheAdaptersExtension_AndNoManifestOrLockFile()
    {
        var changed = new[] { "web/src/a.ts", "web/src/b.css", "web/package.json", "web/package-lock.json", "other/c.ts", "web/src/d.TS", "web/README" };

        var files = FileArguments.Select(changed, "web", [".js", ".ts", ".tsx", ".json"], Table);

        files.State.Should().Be("files");
        files.Files.Should().Equal("web/src/a.ts", "web/src/d.TS");
    }

    [Fact]
    public void D1_ANameWithAControlCharacterOrALineBreak_IsLeftOut()
    {
        var files = FileArguments.Select(["src/ok.ts", "src/bad\nname.ts", "src/bad\u0007.ts"], string.Empty, [".ts"], Table);

        files.Files.Should().Equal("src/ok.ts");
    }

    [Fact]
    public void D1_MoreThanTwoHundredFiles_IsNoRun_TooManyFiles()
    {
        var many = Enumerable.Range(0, FileArguments.MaxFiles + 1).Select(n => $"src/f{n}.ts").ToList();

        FileArguments.Select(many, string.Empty, [".ts"], Table).Should().Be(new FormatFiles("too-many-files", []));
        FileArguments.Select(many.Take(FileArguments.MaxFiles), string.Empty, [".ts"], Table).State.Should().Be("files");
        FileArguments.Select(["README.md"], string.Empty, [".ts"], Table).State.Should().Be("none");
    }

    [Fact]
    public void F2_FileIsReplacedInPlace_ByOneItemPerFile_EachWithADotSlash_SoAnOptionLikeNameIsAFile()
    {
        var argv = FileArguments.Argv(["npx", "--no-install", "prettier", "--write", "--ignore-unknown", "{file}", "--log-level", "{jobs}"], ["web/src/a.ts", "web/--write-everything", "web/src/c d.ts"], "web", 2);

        argv.Should().Equal("npx", "--no-install", "prettier", "--write", "--ignore-unknown", "./src/a.ts", "./--write-everything", "./src/c d.ts", "--log-level", "2");
        FileArguments.Argv(["dotnet", "format", "whitespace", ".", "--folder", "--include", "{file}"], ["src/A.cs"], ".", 1).Should().Equal("dotnet", "format", "whitespace", ".", "--folder", "--include", "./src/A.cs");
    }

    // ---- the real rig ----

    private const string Ignore = "node_modules/\n";

    private static string Toolchain(string format, string build = "echo built", string test = "echo tested", string restore = "")
    {
        var text = new StringBuilder("version: 1\nprojects:\n  - path: \".\"\n    adapter: node\n");
        if (format.Length > 0)
        {
            text.Append($"    format:\n      - \"/bin/sh\"\n      - \"-c\"\n      - {ToolchainFixture.Quote(format)}\n      - \"fmt\"\n      - \"{{file}}\"\n");
        }

        if (restore.Length > 0)
        {
            text.Append($"    restore:\n      - \"/bin/sh\"\n      - \"-c\"\n      - {ToolchainFixture.Quote(restore)}\n");
        }

        text.Append($"    build:\n      - \"/bin/sh\"\n      - \"-c\"\n      - {ToolchainFixture.Quote(build)}\n    test:\n      - \"/bin/sh\"\n      - \"-c\"\n      - {ToolchainFixture.Quote(test)}\n");

        return text.ToString();
    }

    private const string Sed = "sed -i 's/BAD/GOOD/g' \"$@\"";

    private static CoderRig.Launch Writes(params (string Path, string Text)[] files) =>
        new(files.ToDictionary(file => file.Path, file => file.Text), CoderRig.Report());

    private static async Task<(string State, int Files, string Commit)> FormatOf(CoderRig rig, RoleJobRecord job)
    {
        var format = rig.Envelope(job).GetProperty("format");

        return (format.GetProperty("state").GetString()!, format.GetProperty("files").GetInt32(), format.GetProperty("commit").GetString()!);
    }

    [Fact]
    public async Task F1_ABadlyFormattedFile_GivesOneFormatCommitWithThatFileOnly_AndTheChecksRunOnIt()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: Toolchain(Sed), extra: new Dictionary<string, string> { [".gitignore"] = Ignore, ["keep.js"] = "const kept = BAD;\n" });
        rig.Says("sonnet", Writes(("a.js", "const x = BAD;\n"), ("notes.txt", "BAD text\n"), (CoderRig.StateFile, CoderRig.Green + "\n")));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var (state, files, commit) = await FormatOf(rig, job);
        (state, files).Should().Be(("formatted", 1));
        (await rig.GitAsync(rig.Repo, "log", "--format=%s", $"{rig.Base}..refs/heads/coder/{job.JobId}")).Split('\n').Should().Equal("style: format (coai)", $"coder {job.JobId}: {CoderRig.TaskId}");
        (await rig.GitAsync(rig.Repo, "show", "--name-only", "--format=", commit)).Should().Be("a.js");
        (await rig.GitAsync(rig.Repo, "show", $"{commit}:a.js")).Should().Be("const x = GOOD;");
        rig.Envelope(job).GetProperty("headSha").GetString().Should().Be(commit, "the checks ran on the commit coai made last");
    }

    [Fact]
    public async Task F3_AFormatterThatWritesAnotherFile_IsUndone_TheStateIsRefused_AndTheRoundGoesOn()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: Toolchain(Sed + "; echo extra > extra.js; echo more >> keep.js"), extra: new Dictionary<string, string> { [".gitignore"] = Ignore, ["keep.js"] = "kept\n" });
        rig.Says("sonnet", Writes(("a.js", "const x = BAD;\n"), (CoderRig.StateFile, CoderRig.Green + "\n")));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, "a format state is never a verdict: " + ended.Reason);
        (await FormatOf(rig, job)).State.Should().Be("refused");
        rig.Envelope(job).GetProperty("format").GetProperty("note").GetString().Should().Contain("not given");
        (await rig.GitAsync(rig.Repo, "log", "--format=%s", $"{rig.Base}..refs/heads/coder/{job.JobId}")).Should().Be($"coder {job.JobId}: {CoderRig.TaskId}");
        (await rig.GitAsync(rig.Repo, "show", $"refs/heads/coder/{job.JobId}:a.js")).Should().Be("const x = BAD;", "the worktree was put back to the turn's commit");
        File.Exists(Path.Combine(rig.WorktreeOf(job), "extra.js")).Should().BeFalse();
    }

    [Fact]
    public async Task F4_AFormatterThatFails_IsUndone_TheStateIsFailed_AndTheRoundGoesOn()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: Toolchain("echo half > a.js; exit 3"), extra: new Dictionary<string, string> { [".gitignore"] = Ignore });
        rig.Says("sonnet", Writes(("a.js", "const x = BAD;\n"), (CoderRig.StateFile, CoderRig.Green + "\n")));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, "a format state is never a verdict: " + ended.Reason);
        (await FormatOf(rig, job)).State.Should().Be("failed");
        (await rig.GitAsync(rig.Repo, "show", $"refs/heads/coder/{job.JobId}:a.js")).Should().Be("const x = BAD;");
    }

    [Fact]
    public async Task F5_NoFormatterInTheToolchain_IsNotConfigured_AndNoProcessStarts()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: Toolchain(string.Empty), extra: new Dictionary<string, string> { [".gitignore"] = Ignore });
        rig.Says("sonnet", Writes(("a.js", "const x = BAD;\n"), (CoderRig.StateFile, CoderRig.Green + "\n")));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        (await FormatOf(rig, job)).State.Should().Be("not_configured");
        Directory.Exists(Path.Combine(rig.World.Store.FolderOf(job.JobId)!.Location, "checks", "format-0")).Should().BeFalse("no format step started");
    }

    [Fact]
    public async Task F5_ATurnWhoseFilesAreNotTheFormattersToGive_IsUnchanged_NoProcess()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: Toolchain(Sed), extra: new Dictionary<string, string> { [".gitignore"] = Ignore });
        rig.Says("sonnet", Writes(("notes.txt", "BAD text\n"), (CoderRig.StateFile, CoderRig.Green + "\n")));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        (await FormatOf(rig, job)).State.Should().Be("unchanged");
    }

    [Fact]
    public async Task F7_AFormatterThatRewritesAnIgnoredFile_LeavesNothingOfItInTheChecks_TheRestoreBringsTheRunnerBack()
    {
        var restore = "mkdir -p node_modules && echo original > node_modules/runner.js";
        var build = "grep -q original node_modules/runner.js && echo built-with-the-original-runner";
        await using var rig = await CoderRig.CreateAsync(toolchain: Toolchain(Sed + "; echo poisoned > node_modules/runner.js", build: build, restore: restore), extra: new Dictionary<string, string> { [".gitignore"] = Ignore });
        rig.Says("sonnet", Writes(("a.js", "const x = BAD;\n"), (CoderRig.StateFile, CoderRig.Green + "\n")));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        (await FormatOf(rig, job)).State.Should().Be("formatted");
        var envelope = rig.Envelope(job);
        envelope.GetProperty("green").GetBoolean().Should().BeTrue("the poisoned runner did not survive the clean: the restore of the round brought the original back");
        envelope.GetProperty("checks").EnumerateArray().Select(step => step.GetProperty("step").GetString()).Should().Equal("restore", "format", "restore", "build", "test"); // the formatter turn's own units are in the envelope (S5 of the s5 verify)
    }
}
