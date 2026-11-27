using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Runners.Worktrees;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.CodexScript;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s5 t5 (V7, V8; SPEC C6): boundary scenarios with FakeCli as the codex coder inside the REAL unit of the codex vendor — a write outside, a read of what is hidden, a new unit, a Windows program. The unit is the vendor's own (<see cref="CodexCoderVendor.UnitProperties"/>) with the preflight of
/// <see cref="CodexPreflight"/> asking it first; the fake has no vendor sandbox, which is exactly what d3 says out loud: the unit alone is NOT the whole boundary of codex.
/// </summary>
/// <remarks>
/// <para><b>What the unit stops, and what it does not say.</b> The main checkout and the coder root are HIDDEN by an empty tmpfs, so a write there succeeds inside the unit and never reaches the disk: the proof is the disk, read afterwards (the main checkout hashed byte for byte), not an exit code. Only
/// a read-only bind (the <c>.git</c> pointer of the worktree and the main <c>.git</c>) answers with the OS's refusal, and that is what the outcome names.</para>
/// <para>Real <c>systemd-run --user</c> units on temp folders only; skipped, with the reason, where there is no user manager. In the serial <c>fakecli-env</c> collection.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class ACodexCoderBoundaryTests
{
    private static void NeedAUserManager() => Assert.SkipUnless(SystemdUser.Works(out var why), why);

    private static ProcessRequest Request(CodexBed bed, params string[] arguments) =>
        new(FakeCliInvocations.Exe, arguments, bed.Worktree) { Timeout = TimeSpan.FromSeconds(60), InheritsEnvironment = false, Passthrough = ProcessEnvironment.Minimal };

    /// <summary>Admits the job with FakeCli as the codex (the preflight of the vendor runs for it) and runs ONE launch of <paramref name="program"/> in the vendor's unit.</summary>
    private static async Task<BoundaryRun> RunAsync(CodexBed bed, ProcessRequest launch, CoderBoundary? runWith = null)
    {
        var admission = await bed.AdmitAsync(bed.Boundary());
        var admitted = admission.Should().BeOfType<BoundaryAdmission.Admitted>(admission.ToString()).Subject;

        // A control unit is admitted by the healthy boundary and run by the degraded one: its preflight would refuse it, which is the point of the control only after the refusal has been seen elsewhere.
        return await (runWith ?? bed.Boundary()).RunAsync(admitted, bed.Job, launch, "c1", TestContext.Current.CancellationToken);
    }

    private static ProcessRequest Shell(CodexBed bed, string script) =>
        new("/bin/sh", ["-c", script], bed.Worktree) { Timeout = TimeSpan.FromSeconds(60), InheritsEnvironment = false, Passthrough = ProcessEnvironment.Minimal };

    // ---------- d1, V7: a write that must not happen ----------

    [Fact]
    public async Task D1_TheFakeCoder_WritesTheMainCheckout_BesideTheWorktree_TheGitPointer_AndTheStandInHome_EachIsStoppedByTheOs_TheJobEndsCoderBoundary_IsNeverGreen_AndTheMainCheckoutIsByteEqual()
    {
        NeedAUserManager();
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig;
        var job = rig.NewJob(route: ["codex/terra"]);
        var worktree = rig.WorktreeOf(job);
        string[] outside =
        [
            Path.Combine(rig.Repo, "new-in-main.txt"),
            Path.Combine(rig.Repo, ".coai", "toolchain.yaml"),
            Path.Combine(rig.Repo, ".git", "coai-t5-probe"),
            Path.Combine(rig.Root, "beside-the-worktree.txt"),
            Path.Combine(rig.Host.RealHome, "from-the-coder.txt"),
            Path.Combine(rig.Host.RealHome, ".codex", "from-the-coder.txt"),
            Path.Combine(worktree, ".git"),
        ];
        var mainBefore = await HashesAsync(rig.Repo);
        var homeBefore = Directory.GetFileSystemEntries(rig.Host.RealHome, "*", SearchOption.AllDirectories).Order().ToList();
        var signInBefore = File.ReadAllText(Path.Combine(rig.Host.RealHome, ".codex", "auth.json"));
        rig.SaysRaw("terra", Turn(Writes(CoderRig.Green), Report(), tryWrites: outside));

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), ended.Reason);
        ended.Reason.Should().Contain(Path.Combine(worktree, ".git"), "the OS's refusal of the one read-only pointer names it");
        (await HashesAsync(rig.Repo)).Should().BeEquivalentTo(mainBefore, "not one byte of the main checkout's files changed: what the hidden folder took never reached the disk (its .git is coai's own, which commits a worktree there, and is checked by the probe below)");
        File.Exists(Path.Combine(rig.Repo, ".git", "coai-t5-probe")).Should().BeFalse("the main .git is a read-only bind");
        File.Exists(Path.Combine(rig.Root, "beside-the-worktree.txt")).Should().BeFalse("a write beside the worktree went into the unit's own tmpfs");
        Directory.GetFileSystemEntries(rig.Host.RealHome, "*", SearchOption.AllDirectories).Order().Should().Equal(homeBefore, "nothing was added to the stand-in for the real home");
        File.ReadAllText(Path.Combine(rig.Host.RealHome, ".codex", "auth.json")).Should().Be(signInBefore);
        (await rig.GitAsync(rig.Repo, "rev-list", "--count", $"{rig.Base}..coder/{job.JobId}")).Should().Be("0", "nothing of a turn that was stopped is committed: the job is never green");
    }

    private static async Task<IReadOnlyDictionary<string, string>> HashesAsync(string folder)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(file => !Path.GetRelativePath(folder, file).StartsWith(".git" + Path.DirectorySeparatorChar, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            seen[Path.GetRelativePath(folder, file)] = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(file)));
        }

        return seen;
    }

    // ---------- d2, V8: a read that must not work ----------

    private static string ReadScript(CodexBed bed, string anotherWorktree, string otherJob) => $$"""
        r() { if head -c 1 "$1" >/dev/null 2>&1; then echo "read $2"; else echo "denied $2"; fi; }
        r {{bed.Coder.Main}}/a.txt main-file
        r {{anotherWorktree}}/theirs.txt another-worktree
        r {{otherJob}} another-job-folder
        r {{bed.Coder.RealHome}}/.claude/.credentials.json real-claude-sign-in
        r {{bed.Coder.RealHome}}/.codex/auth.json real-codex-sign-in
        if systemd-run --user --quiet --wait --collect --unit coai-t5-escape-{{Guid.NewGuid():N}} true >/dev/null 2>&1; then echo "started a unit"; else echo "no unit"; fi
        """;

    private static async Task<string> PlantAsync(CodexBed bed)
    {
        var anotherWorktree = Path.Combine(bed.Job.CoderRoot, "coai-coder-someone-elses-job");
        Directory.CreateDirectory(anotherWorktree);
        await File.WriteAllTextAsync(Path.Combine(anotherWorktree, "theirs.txt"), "theirs\n");
        return anotherWorktree;
    }

    [Fact]
    public async Task D2_AChildCommandOfTheFake_CannotReadTheHiddenMainCheckout_AnotherWorktree_AnotherJobsFolder_OrTheRealSignInFiles_CannotStartAUnit_AndAControlUnitWithoutTheAdditionsReadsThemAll()
    {
        NeedAUserManager();
        await using var bed = await CodexBed.CreateAsync();
        var worktree = await PlantAsync(bed);
        var script = ReadScript(bed, worktree, bed.OtherJobFile);

        var run = await RunAsync(bed, Shell(bed, script));

        run.Result.ExitCode.Should().Be(0, run.Result.StdErr);
        var said = run.Result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        said.Should().Contain(["denied main-file", "denied another-worktree", "denied another-job-folder", "denied real-claude-sign-in", "denied real-codex-sign-in", "no unit"]);

        // The control: the same unit with the additions taken out (what hides each of them replaced by a plain read-only or writable bind) — the same reads then work, so the refusals above are the additions' and not a path that was never there.
        var signIns = new[] { Path.Combine(bed.Coder.Host.ResolvedHome, ".claude", ".credentials.json"), Path.Combine(bed.Coder.Host.ResolvedHome, ".codex", "auth.json") };
        var control = await RunAsync(bed, Shell(bed, script), bed.Boundary(arguments =>
        {
            var open = UnitArguments.Replace(UnitArguments.Replace(UnitArguments.Replace(arguments, $"TemporaryFileSystem={bed.Job.Tree.MainCheckout}", $"BindReadOnlyPaths={bed.Job.Tree.MainCheckout}"), $"TemporaryFileSystem={bed.Job.CoderRoot}", $"BindReadOnlyPaths={bed.Job.CoderRoot}"), $"TemporaryFileSystem={bed.Job.JobCover}:ro", $"BindPaths={bed.Job.JobCover}");

            return signIns.Aggregate(open, (list, file) => UnitArguments.Replace(list, $"InaccessiblePaths=-{file}", $"BindReadOnlyPaths={file}"));
        }));

        var opened = control.Result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        opened.Should().Contain(["read main-file", "read another-worktree", "read another-job-folder", "read real-claude-sign-in", "read real-codex-sign-in"], "a control unit without the additions shows that the same reads then work");
    }

    [Fact]
    public async Task D2_AChildCommandOfTheFake_CannotRunAWindowsProgram()
    {
        NeedAUserManager();
        await using var bed = await CodexBed.CreateAsync();
        Assert.SkipWhen(bed.Coder.Host.WindowsProgram.Length == 0, "this host has no WSL interop, so there is no Windows program to try");
        var copy = Path.Combine(bed.Worktree, "tool.exe");
        File.Copy(bed.Coder.Host.WindowsProgram, copy);
        CodexBed.SetMode(copy, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var run = await RunAsync(bed, Shell(bed, $"if {copy} >/dev/null 2>&1; then echo \"ran the exe\"; else echo \"no exe\"; fi"));

        run.Result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().Contain("no exe");
    }

    // ---------- d3: said out loud, so that nobody reads the unit as more than it is ----------

    [Fact]
    public async Task D3_AChildCommandOfTheFake_CanReadTheJobsOwnSignInCopyInsideTheUnit_ThatReadIsClosedByTheVendorsSandbox_WhichTheFakeDoesNotHave()
    {
        NeedAUserManager();
        await using var bed = await CodexBed.CreateAsync();

        var run = await RunAsync(bed, Shell(bed, $"if head -c 1 \"{bed.AuthFile}\" >/dev/null 2>&1; then echo \"reads the copy\"; else echo \"cannot read the copy\"; fi"));

        run.Result.StdOut.Should().Contain("reads the copy", "the unit alone does not close it: the job's own copy is in the home the CLI itself must read. The rows CX-V3 and CX-V5 of the preflight ask the vendor's sandbox, with its own namespaces, and the fake has no such sandbox");
        File.ReadAllText(bed.AuthFile).Should().NotContain("made-up-refresh-token-not-a-credential", "and what that read can see is the COPY: a placeholder in the place of the refresh token (CX-R4)");
        File.ReadAllText(bed.AuthFile).Should().Contain(SignInCopy.Placeholder);
    }

    // ---------- d4: the mark stays off, and what the flip rests on is built ----------

    [Fact]
    public void D4_TheCodexRowIsNotLaunchableByData_UntilTheLastCommitOfTheStoryTurnsTheMarkOn()
    {
        var row = CoderBoundaryTable.Shipped.Row("codex");

        row.Should().NotBeNull();
        row!.Verdict.Should().Be("ship");
        row.Launchable.Should().BeFalse("ONE commit turns the mark on, the last of the story, after the must-changes are built and green: this test holds it off until then and that commit changes it");
        CoderVendors.Shipped.Launchable.Should().NotContain("codex");
    }

    [Fact]
    public void D4_EveryMustChangeOfTheJudgementsAddendum_IsBuilt_ThisIsTheListTheFlipRestsOn()
    {
        var vendor = CodexCoderVendor.Instance;
        var argv = vendor.FreshArgv("terra", "w", new CoderLaunchFiles("o"));
        var resumed = vendor.ResumedArgv("terra", "w", "019aa111-2222-7333-8444-555566667777", new CoderLaunchFiles("o"));

        argv.Should().Contain("agents.enabled=false", "13.4: the argv carries the agent switch");
        resumed.Should().Contain("agents.enabled=false", "13.4: on every turn");
        CodexCoderVendor.EnvironmentsFile.Should().Be("environments.toml", "13.11: the launch half of CX-C1");
        var preIds = CoderBoundaryTable.Shipped.PropertiesOf("codex").Where(property => property.Class == "pre" || property.Also.Contains("pre")).Select(property => property.Id).ToHashSet();
        CodexPreflight.RowIds.Values.Should().Contain(preIds.Where(id => id is not ("K1" or "K2")), "the preflight rows with doctor and the job's own model (t3 d3)");
        CodexToolList.Measured.Should().HaveCount(11);
        var recipe = (CoderSignInRecipe.CopyForJob)vendor.SignIn;
        recipe.Margin.Should().Be(TimeSpan.FromMinutes(60), "the margin CX-R1");
        recipe.Placeholders.Should().ContainKey("tokens/refresh_token", "the copy with the placeholder");
        CodexCoderVendor.RefreshOverrideUrl.Should().StartWith("http://127.0.0.1:", "the override CX-R2");
        vendor.CheckTurn(new ProcessResult(0, "{\"type\":\"thread.started\",\"thread_id\":\"01a217ad-59bd-7543-907c-6b343bd876d3\"}\n{\"type\":\"item.completed\",\"item\":{\"type\":\"web_search\"}}\n", string.Empty, false), string.Empty)
            .Should().BeOfType<CoderTurnVerdict.Boundary>("the rule of CX-T4 and CX-T5 is built");
        RoleStops.SignInLost("codex", new ProcessResult(1, string.Empty, "Failed to refresh token: x", false)).Should().BeTrue("the three signs of the failover");
    }

    /// <summary>
    /// S57: the must-changes of passes 6 and 7 are part of the list the flip rests on. Each line is the smallest proof that the fix is in place; the full tests are in their own classes (M15 <c>ABoundaryFoldersNeverWriteThroughALinkTests</c>, M16 <c>ACodexTurnCheckTests</c>, M17 <c>ACodexCoderPreflightTests</c>,
    /// M11 a <c>ACodexCoderFixTurnsTests</c>, M11 b <c>AAgyCoderFixTurnsTests</c>, R-M15 <c>AVendorsHaveTheirOwnTempFoldersTests</c>). M18 is a gap of the agy road only and is closed on the agy branch.
    /// </summary>
    [Fact]
    public void D4_EveryMustChangeOfPassesSixAndSeven_IsBuilt_ThisIsTheRestOfTheListTheFlipRestsOn()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "a symbolic link needs a privilege on Windows");
        using var bed = TempDir.For("coai-d4-passes-six-seven-");
        var worktree = Path.Combine(bed.Path, "wt");
        var outside = Path.Combine(bed.Path, "outside");
        var program = Path.Combine(bed.Path, "windows-program.exe");
        Directory.CreateDirectory(worktree);
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(program, "MZ made-up\n"u8.ToArray());
        var job = new BoundaryJob("r20261127101010-ab12cd34", new CoderTree(worktree, Path.Combine(bed.Path, "main"), "gitdir: x\n"), Path.Combine(bed.Path, "job"), NetworkChoice.None("t"), NetworkChoice.None("t"));
        var host = new BoundaryHost(BoundaryPlatform.Linux, Path.Combine(bed.Path, "no-home"), string.Empty, string.Empty, "systemd-run", "systemctl", program, string.Empty);

        // M15: a link at the name of a marker is never followed.
        var target = Path.Combine(outside, "a-host-file.txt");
        File.WriteAllBytes(target, "precious\n"u8.ToArray());
        var marker = Path.Combine(job.TempFolder, BoundaryPreflight.MarkerName(job));
        Directory.CreateDirectory(job.TempFolder);
        File.CreateSymbolicLink(marker, target);
        BoundaryFolders.Prepare(host, job, job.Folders, job.TempFolder, job.VarTempFolder).Sentence.Should().BeEmpty();
        File.ReadAllBytes(target).Should().Equal("precious\n"u8.ToArray(), "M15: the host file behind the link keeps its bytes");

        // M16: a stream that ended with a bad exit and holds a web search is a boundary; one with no thread and no item is not.
        var vendor = CodexCoderVendor.Instance;
        const string thread = "{\"type\":\"thread.started\",\"thread_id\":\"01a217ad-59bd-7543-907c-6b343bd876d3\"}\n";
        vendor.CheckTurn(new ProcessResult(1, thread + "{\"type\":\"item.completed\",\"item\":{\"type\":\"web_search\"}}\n", string.Empty, false), string.Empty)
            .Should().BeOfType<CoderTurnVerdict.Boundary>("M16: a bad end answers a boundary for what is proved");
        vendor.CheckTurn(new ProcessResult(1, string.Empty, string.Empty, false), string.Empty)
            .Should().NotBeOfType<CoderTurnVerdict.Boundary>("M16: a missing thread on a bad end is the launch's own stop, not a boundary");

        // M17: a program whose folder is the home is refused; a narrow folder of the home is not.
        new CoderProgram("/h/codex", "/h", string.Empty).HomeProblem("/h").Should().NotBeEmpty("M17");
        new CoderProgram("/h/.codex/packages/bin/codex", "/h/.codex/packages/bin", string.Empty).HomeProblem("/h").Should().BeEmpty("M17: the measured install is a narrow folder");

        // M11 a: a turn the job's token cut is checked with a time of its own.
        CoderTurnCheck.CutCheckBudget.Should().BeGreaterThan(TimeSpan.Zero, "M11 a");

        // M11 b: on a cut stream, an event of a tool outside the three is a boundary in every state.
        var init = "{\"event\":\"init\",\"conversation_id\":\"c\",\"init\":{\"permission_mode\":\"request-review\"}}";
        var step = "{\"event\":\"step_update\",\"step_update\":{\"conversation_id\":\"c\",\"step_index\":2,\"state\":\"ERROR\",\"step_type\":\"tool\",\"tool_name\":\"run_command\"}}";
        AgyTurnCheck.Check(new ProcessResult(1, init + "\n" + step + "\n", string.Empty, false), string.Empty, string.Empty).Should().BeOfType<CoderTurnVerdict.Boundary>("M11 b");

        // R-M15: the temp sources lie beside the coder's folder, never inside it, and a link at one of the names is refused.
        job.CoderTempOf("codex").Should().StartWith(job.CoderTempRoot);
        RealPath.IsInside(job.CoderTempRoot, job.CoderFolder, StringComparison.Ordinal).Should().BeFalse("R-M15: no folder a unit binds writable holds the sources");
        Directory.CreateDirectory(job.CoderTempVendorFolder("codex"));
        File.CreateSymbolicLink(job.CoderTempOf("codex"), outside);
        job.CoderTempOnDiskProblem("codex").Should().NotBeEmpty("R-M15: the names are asked before every admission");
    }
}
