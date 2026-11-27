using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s8 t6 (V7, V8; SPEC C6): boundary scenarios with FakeCli as the AGY coder inside the REAL unit of the agy vendor — a write outside, a read of what is hidden, a new unit, a Windows program. The unit is the vendor's own (<see cref="AgyCoderUnit.Properties"/>) with the preflight of
/// <see cref="AgyCoderPreflight"/> asking it first and the guard folder as its working directory; the fake is not agy, which is exactly what d3 says out loud: for agy the unit is NOT the whole boundary, the agent file, the guard and the mode are the rest.
/// </summary>
/// <remarks>
/// <para><b>What the unit stops, and what it does not say.</b> The main checkout and the coder root are HIDDEN by an empty tmpfs, so a write there succeeds inside the unit and never reaches the disk: the proof is the disk, read afterwards (the main checkout hashed byte for byte), not an exit code. Only
/// a read-only bind (the <c>.git</c> pointer of the worktree and the main <c>.git</c>) answers with the OS's refusal, and that is what the outcome names. The d2 reads go through the same road as a turn (the sign-in copy, the guard folder, the preflight), then run in the unit.</para>
/// <para>Real <c>systemd-run --user</c> units on temp folders only; skipped, with the reason, where there is no user manager. In the serial <c>fakecli-env</c> collection. No test here calls agy or reads a real sign-in: every sign-in file is made up.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class AAgyCoderBoundaryTests
{
    private static void NeedAUserManager() => Assert.SkipUnless(SystemdUser.Works(out var why), why);

    // ---------- d1, V7: a write that must not happen ----------

    [Fact]
    public async Task D1_TheFakeCoder_WritesTheMainCheckout_BesideTheWorktree_TheGitPointer_AndTheStandInHome_EachIsStoppedByTheOs_TheJobEndsCoderBoundary_IsNeverGreen_AndTheMainCheckoutIsByteEqual()
    {
        NeedAUserManager();
        await using var rig = await AgyCoderBed.RigAsync();
        var job = rig.NewJob(route: [$"agy/{AgyCoderBed.Model}"]);
        var worktree = rig.WorktreeOf(job);
        string[] outside =
        [
            Path.Combine(rig.Repo, "new-in-main.txt"),
            Path.Combine(rig.Repo, ".coai", "toolchain.yaml"),
            Path.Combine(rig.Repo, ".git", "coai-t6-probe"),
            Path.Combine(rig.Root, "beside-the-worktree.txt"),
            Path.Combine(rig.Host.RealHome, "from-the-coder.txt"),
            Path.Combine(rig.Host.RealHome, ".gemini", "from-the-coder.txt"),
            Path.Combine(worktree, ".git"),
        ];
        var mainBefore = await HashesAsync(rig.Repo);
        var homeBefore = Directory.GetFileSystemEntries(rig.Host.RealHome, "*", SearchOption.AllDirectories).Order().ToList();
        var signInFile = Path.Combine(rig.Host.RealHome, AgyCoderUnit.SignInRelative);
        var signInBefore = File.ReadAllText(signInFile);
        rig.SaysEntries(AgyCoderBed.Model, AgyCoderBed.Launch([AgyCoderBed.Writes(CoderRig.Green)], conversation: AgyCoderBed.Conversation, tries: outside));

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), ended.Reason);
        ended.Reason.Should().Contain(Path.Combine(worktree, ".git"), "the OS's refusal of the one read-only pointer names it");
        (await HashesAsync(rig.Repo)).Should().BeEquivalentTo(mainBefore, "not one byte of the main checkout's files changed: what the hidden folder took never reached the disk (its .git is coai's own, which commits a worktree there, and is checked by the probe below)");
        File.Exists(Path.Combine(rig.Repo, ".git", "coai-t6-probe")).Should().BeFalse("the main .git is a read-only bind");
        File.Exists(Path.Combine(rig.Root, "beside-the-worktree.txt")).Should().BeFalse("a write beside the worktree went into the unit's own tmpfs");
        Directory.GetFileSystemEntries(rig.Host.RealHome, "*", SearchOption.AllDirectories).Order().Should().Equal(homeBefore, "nothing was added to the stand-in for the real home");
        File.ReadAllText(signInFile).Should().Be(signInBefore, "the real sign-in file of the stand-in is byte-equal: coai reads it and never writes it");
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

    // ---------- the unit of a child command: the road of a turn up to the start, then ONE launch of a shell ----------

    private sealed class Shown(AgyUnit unit, string otherJobFile, string anotherWorktree) : IAsyncDisposable
    {
        public AgyUnit Unit { get; } = unit;

        public CoderBed Bed => Unit.Bed;

        /// <summary>A file in the folder of ANOTHER job (the jobs folder holds two): a unit of this job must not read it.</summary>
        public string OtherJobFile { get; } = otherJobFile;

        public string AnotherWorktree { get; } = anotherWorktree;

        public static async Task<Shown> CreateAsync()
        {
            NeedAUserManager();
            var unit = await AgyUnit.CreateAsync();
            var bed = unit.Bed;
            // The real sign-in files of the other two vendors, made up in the stand-in for the real home: no unit of an agy job may read them (S42). The agy file is the unit's own made-up one.
            Directory.CreateDirectory(Path.Combine(bed.RealHome, ".codex"));
            await File.WriteAllTextAsync(Path.Combine(bed.RealHome, ".codex", "auth.json"), "made-up-real-codex-sign-in-line\n");
            var otherJobFile = bed.Inner.UseJobsFolderWithAnotherJob();
            var anotherWorktree = Path.Combine(bed.Job.CoderRoot, "coai-coder-someone-elses-job");
            Directory.CreateDirectory(anotherWorktree);
            await File.WriteAllTextAsync(Path.Combine(anotherWorktree, "theirs.txt"), "theirs\n");

            return new Shown(unit, otherJobFile, anotherWorktree);
        }

        /// <summary>
        /// Makes what a turn makes before it starts (the job's sign-in copy, the vendor's files and the guard folder, the preflight of the vendor in the exact launch unit), then runs the script ONCE as the program: in the unit of the healthy boundary, or — for a control — in the unit of
        /// <paramref name="runWith"/>, which the preflight never saw (the preflight of a degraded unit would refuse it, and the refusal is what other tests prove).
        /// </summary>
        public async Task<BoundaryRun> RunAsync(string script, CoderBoundary? runWith = null)
        {
            var job = Bed.Job;
            var request = Bed.Shell(script) with { WorkingDirectory = Unit.Guard };
            var healthy = new CoderBoundary(Bed.Launcher, Bed.Host) { Vendor = Unit.Vendor };
            (await healthy.PrepareSignInAsync(job, request.Executable, request.Timeout + UnitControl.Backstop, Unit.SignIns, DateTime.UtcNow, TestContext.Current.CancellationToken)).Should().BeEmpty("the copy of the sign-in is made");
            var prepared = healthy.PrepareLaunch(job, request);
            prepared.Problem.Should().BeEmpty("the guard folder is written and read back");
            var admission = await healthy.AdmitAsync(job, prepared.Request.Executable, TestContext.Current.CancellationToken, CoderBoundary.CarriesKey(prepared.Request), prepared.Request.Timeout + UnitControl.Backstop);
            var admitted = admission.Should().BeOfType<BoundaryAdmission.Admitted>(admission.ToString()).Subject;
            Unit.Vendor.BeforeStart(job).Should().BeEmpty();

            return await (runWith ?? healthy).RunAsync(admitted, job, prepared.Request, "c1", TestContext.Current.CancellationToken);
        }

        public ValueTask DisposeAsync() => Unit.DisposeAsync();
    }

    private static string[] Said(BoundaryRun run) => run.Result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    // ---------- d2, V8: a read that must not work ----------

    private static string ReadScript(Shown shown) => $$"""
        r() { if head -c 1 "$1" >/dev/null 2>&1; then echo "read $2"; else echo "denied $2"; fi; }
        r {{shown.Bed.Main}}/a.txt main-file
        r {{shown.AnotherWorktree}}/theirs.txt another-worktree
        r {{shown.OtherJobFile}} another-job-folder
        r {{shown.Bed.RealHome}}/.claude/.credentials.json real-claude-sign-in
        r {{shown.Bed.RealHome}}/.codex/auth.json real-codex-sign-in
        r {{shown.Unit.Real}} real-agy-sign-in
        if systemd-run --user --quiet --wait --collect --unit coai-t6-escape-{{Guid.NewGuid():N}} true >/dev/null 2>&1; then echo "started a unit"; else echo "no unit"; fi
        """;

    [Fact]
    public async Task D2_AChildCommandOfTheFake_CannotReadTheHiddenMainCheckout_AnotherWorktree_AnotherJobsFolder_OrTheRealSignInFiles_CannotStartAUnit_AndAControlUnitWithoutTheAdditionsReadsThemAll()
    {
        await using var shown = await Shown.CreateAsync();
        var script = ReadScript(shown);
        var job = shown.Bed.Job;

        var run = await shown.RunAsync(script);

        run.Result.ExitCode.Should().Be(0, run.Result.StdErr);
        Said(run).Should().Contain(["denied main-file", "denied another-worktree", "denied another-job-folder", "denied real-claude-sign-in", "denied real-codex-sign-in", "denied real-agy-sign-in", "no unit"]);

        // The control: the same unit with the additions taken out (what hides each of them replaced by a plain read-only or writable bind) — the same reads then work, so the refusals above are the additions' and not a path that was never there.
        var signIns = new[] { Path.Combine(".claude", ".credentials.json"), Path.Combine(".codex", "auth.json"), AgyCoderUnit.SignInRelative }.Select(relative => Path.Combine(shown.Bed.Host.ResolvedHome, relative)).ToArray();
        var degraded = new CoderBoundary(shown.Bed.Launcher, shown.Bed.Host)
        {
            Vendor = shown.Unit.Vendor,
            Degrade = arguments =>
            {
                var open = UnitArguments.Replace(UnitArguments.Replace(UnitArguments.Replace(arguments, $"TemporaryFileSystem={job.Tree.MainCheckout}", $"BindReadOnlyPaths={job.Tree.MainCheckout}"), $"TemporaryFileSystem={job.CoderRoot}", $"BindReadOnlyPaths={job.CoderRoot}"), $"TemporaryFileSystem={job.JobCover}:ro", $"BindPaths={job.JobCover}");

                return signIns.Aggregate(open, (list, file) => UnitArguments.Replace(list, $"InaccessiblePaths=-{file}", $"BindReadOnlyPaths={file}"));
            },
        };

        var control = await shown.RunAsync(script, degraded);

        Said(control).Should().Contain(["read main-file", "read another-worktree", "read another-job-folder", "read real-claude-sign-in", "read real-codex-sign-in", "read real-agy-sign-in"], "a control unit without the additions shows that the same reads then work");
    }

    [Fact]
    public async Task D2_AChildCommandOfTheFake_CannotRunAWindowsProgram()
    {
        await using var shown = await Shown.CreateAsync();
        Assert.SkipWhen(shown.Bed.Host.WindowsProgram.Length == 0, "this host has no WSL interop, so there is no Windows program to try");
        var copy = Path.Combine(shown.Bed.Worktree, "tool.exe");
        File.Copy(shown.Bed.Host.WindowsProgram, copy);
        CodexBed.SetMode(copy, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var run = await shown.RunAsync($"if {copy} >/dev/null 2>&1; then echo \"ran the exe\"; else echo \"no exe\"; fi");

        Said(run).Should().Contain("no exe");
    }

    // ---------- d3: said out loud, so that nobody reads the unit as more than it is ----------

    [Fact]
    public async Task D3_AChildCommandOfTheFake_CanReadTheJobsOwnSignInCopyInsideTheUnit_ForAgyOnlyTheAgentFileTheGuardAndTheModeKeepACommandFromExisting()
    {
        await using var shown = await Shown.CreateAsync();
        var copy = AgyCoderUnit.SignInFile(shown.Bed.Job);

        var run = await shown.RunAsync($"if head -c 1 \"{copy}\" >/dev/null 2>&1; then echo \"reads the copy\"; else echo \"cannot read the copy\"; fi");

        Said(run).Should().Contain("reads the copy", "the unit alone does not close it (accepted risk A1 of the judgement): the job's own copy is in the home the CLI itself must read, and agy has no sandbox of its own that could hide it from a command");
        File.ReadAllText(copy).Should().Contain(AgyCoderBed.Access, "what that read can see is the access token of the COPY ...").And.NotContain(AgyCoderBed.Refresh, "... and never the refresh token (AG-R2)").And.NotContain("someone@example.invalid", "only the fields of the recipe are kept");

        // What keeps a command from existing for agy: the agent file has no command tool and the policy off, the guard allows three tools, the argv names the mode and the agent on every turn.
        AntigravityCoderGuard.AgentFile.Should().Contain("commandExecutionPolicy: \"off\"").And.NotContain("run_command", "the agent file names no command tool");
        AntigravityCoderGuard.AllowedTools.Should().BeEquivalentTo(["read_file", "write_file", "edit_file"], "the guard allows these three and denies every other tool");
        var vendor = AgyCoderBed.Vendor();
        foreach (var argv in new[] { vendor.FreshArgv(AgyCoderBed.Model, "/job/wt"), vendor.ResumedArgv(AgyCoderBed.Model, "/job/wt", AgyCoderBed.Conversation) })
        {
            argv.SkipWhile(arg => arg != "--mode").Skip(1).First().Should().Be("accept-edits");
            argv.SkipWhile(arg => arg != "--agent").Skip(1).First().Should().Be(AntigravityCoderGuard.AgentName);
        }
    }
}
