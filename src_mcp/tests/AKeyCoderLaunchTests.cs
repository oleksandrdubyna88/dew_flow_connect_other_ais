using System.Text;
using System.Text.Json;
using CoaiMcp.Core.Notices;
using CoaiMcp.Server.Keys;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s7 t5 — the coder's launch on a pool key (KEY_POOL_SPEC.md sections 5 to 7): the real runner, the real OS layer (a <c>systemd-run --user</c> unit), the real pool in the rig's data folder, FakeCli as claude. K11 (the unit with a key, and its two
/// preflight rows, each red when its property is dropped), K12 (what the child's environment is), K14 (a CLI that prints its key and writes it into the worktree), and the resume rule (a session keeps its key). No model is called and nothing is paid.
/// </summary>
/// <remarks>In the serial <c>fakecli-env</c> collection (inside <see cref="CoderRig"/>).</remarks>
[Collection("fakecli-env")]
public sealed class AKeyCoderLaunchTests
{
    private const string Variable = "CLAUDE_CODE_OAUTH_TOKEN";

    private static string Launches(CoderRig rig, RoleJobRecord job) => Path.Combine(rig.JobTemp(job), "launches");

    private static IReadOnlyList<string> Keys(CoderRig rig, RoleJobRecord job) => KeyRig.KeyIdsOf(Launches(rig, job));

    /// <summary>A degrade that changes nothing and keeps every argument list the layer started a unit with.</summary>
    private static Func<IReadOnlyList<string>, IReadOnlyList<string>> Keep(List<IReadOnlyList<string>> seen) => arguments =>
    {
        seen.Add(arguments);

        return arguments;
    };

    // ---------- the way a key reaches the coder ----------

    [Fact]
    public async Task D2_ACoderLaunch_TakesAKeyOfThePool_AndTheChildHasItAsTheVariableAlone()
    {
        await using var rig = await CoderRig.CreateAsync();
        var keys = KeyRig.AddKeys(rig.World.DataDir, "a", "b");
        rig.Says("sonnet", CoderRig.Edits("src/thing.cs"), CoderRig.Report());
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        Keys(rig, job).Should().ContainSingle().Which.Should().BeOneOf(keys);
        KeyRig.EnvNamesOf(Launches(rig, job)).Single().Should().Contain(Variable).And.NotContain("ANTHROPIC_API_KEY");
        ended.Attempts.Single().KeyId.Should().Be(Keys(rig, job)[0]);
        rig.Rig.LedgerLines().Single().GetProperty("keyId").GetString().Should().Be(Keys(rig, job)[0]);
        rig.Launches(job).Single().Should().NotContain("--bare").And.NotContain(argument => argument.Contains(KeyWorld.Sentinel, StringComparison.Ordinal), "no argv holds the text of the key");
    }

    // ---------- K11: the unit with a key ----------

    [Fact]
    public async Task K11_TheCodersUnitWithAKey_CrossesTheNameOnly_BindsNoSignInFile_AndHidesTheKeyFolder()
    {
        await using var rig = await CoderRig.CreateAsync();
        KeyRig.AddKeys(rig.World.DataDir, "a");
        var seen = new List<IReadOnlyList<string>>();
        rig.Degrade = Keep(seen);
        rig.Says("sonnet", CoderRig.Edits("src/thing.cs"), CoderRig.Report());
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var units = seen.Where(arguments => arguments.Any(argument => argument.Contains($"coai-coder-{job.JobId}-", StringComparison.Ordinal))).ToList();
        units.Should().NotBeEmpty("the layer started the preflight unit and the coder's unit");
        foreach (var arguments in units)
        {
            arguments.Where(argument => argument.StartsWith("Bind", StringComparison.Ordinal)).Should().NotContain(argument => argument.Contains(".credentials.json", StringComparison.Ordinal), "with a key the person's sign-in file is not bound");
            arguments.Should().NotContain(argument => argument.Contains(KeyWorld.Sentinel, StringComparison.Ordinal), "no argument of the unit holds the key");
            arguments.Should().Contain($"InaccessiblePaths=-{Path.Combine(rig.World.DataDir, "keys")}", "the key folder is hidden from every unit that carries a key");
        }

        var coder = units.Single(arguments => arguments.Any(argument => argument.Contains($"coai-coder-{job.JobId}-c1", StringComparison.Ordinal)));
        coder.Should().Contain($"--setenv={Variable}", "the value crosses by NAME, never in an argument");
        coder.Where(argument => argument.Contains(Variable + "=", StringComparison.Ordinal)).Should().BeEmpty("the name is spelled in the unit's own script, but no argument assigns it a value");
    }

    [Fact]
    public async Task K11_TheRowThatProvesTheSignInFileIsNotInTheUnit_GoesRedWhenTheBindIsPutBack()
    {
        await using var rig = await CoderRig.CreateAsync();
        KeyRig.AddKeys(rig.World.DataDir, "a");
        var job = rig.NewJob();
        var bind = $"BindReadOnlyPaths={rig.Host.SignInFile}:{Path.Combine(rig.JobFolder(job), "coder", "home", ".claude", ".credentials.json")}";
        rig.Degrade = arguments => arguments.Contains("NoNewPrivileges=yes") ? PutBefore(arguments, "NoNewPrivileges=yes", bind) : arguments;
        rig.Says("sonnet", CoderRig.Edits("src/thing.cs"), CoderRig.Report());

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Failed);
        ended.Stop.Should().Be("coder-boundary");
        ended.Reason.Should().Contain("sign-in-not-in-unit");
        rig.Launches(job).Should().BeEmpty("the coder was never started");
    }

    // ---------- K12: what the child's environment is ----------

    [Fact]
    public async Task K12_TheChildOfAKeyedCoder_HasTheAllowListAndTheOneName_AndNoCommandLineOfTheMachineHoldsTheKey()
    {
        await using var rig = await CoderRig.CreateAsync();
        KeyRig.AddKeys(rig.World.DataDir, "a");
        rig.Says("sonnet", new CoderRig.Launch(CoderRig.Edits("src/thing.cs"), CoderRig.Report(), SleepMs: 4000));
        var job = rig.NewJob();
        var run = rig.RunAsync(job);
        var seen = new StringBuilder();
        for (var until = DateTime.UtcNow.AddSeconds(20); !run.IsCompleted && DateTime.UtcNow < until;)
        {
            foreach (var cmdline in Directory.EnumerateDirectories("/proc").Where(folder => long.TryParse(Path.GetFileName(folder), out _)).Select(folder => Path.Combine(folder, "cmdline")))
            {
                try
                {
                    seen.Append(File.ReadAllText(cmdline).Replace('\0', ' '));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // A process that ended while it was read.
                }
            }

            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        var (_, ended) = await run;

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        seen.Length.Should().BeGreaterThan(1000, "the scan must have read real command lines while the coder ran");
        seen.ToString().Should().NotContain(KeyWorld.Sentinel, "no process's command line — the client, the shell, env, the coder — held the key while the unit ran");
        var names = KeyRig.EnvNamesOf(Launches(rig, job)).Single();
        names.Should().Contain(Variable);
        names.Where(name => name.Contains("TOKEN", StringComparison.Ordinal) || name.StartsWith("COAI_", StringComparison.Ordinal) || name.Contains("API_KEY", StringComparison.Ordinal)).Should().Equal([Variable], "the one name that crossed, and no other credential or product name");
    }

    // ---------- a session keeps its key ----------

    [Fact]
    public async Task D5_ARepairTurn_ResumesTheSessionWithTheSameKey_AndAFreshPickWouldHaveBeenAnother()
    {
        await using var rig = await CoderRig.CreateAsync();
        var keys = KeyRig.AddKeys(rig.World.DataDir, "a", "b", "c");
        rig.Says(
            "sonnet",
            new CoderRig.Launch(null, "not a report at all", Session: "11111111-1111-1111-1111-111111111111"),
            new CoderRig.Launch(CoderRig.Edits("src/thing.cs"), CoderRig.Report(), Session: "11111111-1111-1111-1111-111111111111"));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var launched = Keys(rig, job);
        launched.Should().HaveCount(2);
        launched[0].Should().Be(launched[1], "the repair turn is the same session, so the same key");
        launched[0].Should().BeOneOf(keys);
        ended.Attempts.Should().ContainSingle().Which.KeyId.Should().Be(launched[0]);
        KeyRig.Rows(rig.World.DataDir).Sum(row => row.Launches).Should().Be(1, "ONE pick for the session, not one per turn");
    }

    [Fact]
    public async Task D5_AFixTurn_ResumesTheSessionWithTheSameKey()
    {
        await using var rig = await CoderRig.CreateAsync();
        KeyRig.AddKeys(rig.World.DataDir, "a", "b", "c");
        // The first turn leaves the fixture's state red; the checks fail and the digest goes back as a fix turn.
        var job = rig.NewJob();
        rig.Says(
            "sonnet",
            new CoderRig.Launch(new Dictionary<string, string> { [CoderRig.StateFile] = CoderRig.Red + "\n" }, CoderRig.Report(), Session: "22222222-2222-2222-2222-222222222222"),
            new CoderRig.Launch(new Dictionary<string, string> { [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report(), Session: "22222222-2222-2222-2222-222222222222"));

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var launched = Keys(rig, job);
        launched.Should().HaveCount(2, "turn 0 and one fix turn");
        launched[0].Should().Be(launched[1]);
        rig.Rig.LedgerLines().Select(line => line.GetProperty("keyId").GetString()).Should().Equal(launched, "the fix turn's ledger line names the key too");
    }

    // ---------- K14: a CLI that prints its key and writes it into the worktree ----------

    [Fact]
    public async Task K14_ACliThatPrintsItsKey_AndWritesItIntoTheWorktree_IsScrubbed_NothingIsCommitted_TheKeyIsDeadExposed()
    {
        await using var rig = await CoderRig.CreateAsync();
        var keys = KeyRig.AddKeys(rig.World.DataDir, "a", "b");
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { ["src/leak.txt"] = "{{claude-key}}\n" }, CoderRig.Report(notes: "my key is {{claude-key}}")));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Failed);
        ended.Stop.Should().Be("key-in-output");
        var used = Keys(rig, job).Single();
        var row = KeyRig.Row(rig.World.DataDir, used);
        row.State.Should().Be(KeyStates.Dead);
        row.DeadReason.Should().Be(KeyDeadReasons.Exposed);
        rig.Rig.NoticesOf(ServerNoticeCodes.ClaudeKeyExposed).Should().ContainSingle();
        (await rig.GitAsync(rig.Repo, "rev-parse", $"refs/heads/coder/{job.JobId}")).Should().Be(rig.Base, "nothing was committed: the branch is still at the base");
        var stored = AllStoredText(rig, job);
        stored.Should().NotContain(KeyWorld.Sentinel, "the stored output is scrubbed everywhere");
        stored.Should().Contain($"[claude key {used}]", "and says whose key it was");
        keys.Should().Contain(used);
    }

    [Fact]
    public async Task K14_AKeyOnlyInAFileTheCoderWrote_StopsTheCommit_ThoughNothingWasPrinted()
    {
        await using var rig = await CoderRig.CreateAsync();
        KeyRig.AddKeys(rig.World.DataDir, "a");
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { ["src/leak.txt"] = "{{claude-key}}\n" }, CoderRig.Report()));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.Stop.Should().Be("key-in-output");
        KeyRig.Row(rig.World.DataDir, Keys(rig, job).Single()).DeadReason.Should().Be(KeyDeadReasons.Exposed);
        (await rig.GitAsync(rig.Repo, "rev-parse", $"refs/heads/coder/{job.JobId}")).Should().Be(rig.Base);
    }

    private static string AllStoredText(CoderRig rig, RoleJobRecord job)
    {
        var folder = rig.World.Store.FolderOf(job.JobId)!.Location;
        var text = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            text.AppendLine(File.ReadAllText(file));
        }

        foreach (var line in File.Exists(Path.Combine(rig.World.DataDir, "usage.jsonl")) ? File.ReadAllLines(Path.Combine(rig.World.DataDir, "usage.jsonl")) : [])
        {
            text.AppendLine(line);
        }

        return text.ToString();
    }

    // ---------- helpers ----------

    private static string[] PutBefore(IReadOnlyList<string> arguments, string property, string added)
    {
        var at = arguments.ToList().IndexOf(property);

        return [.. arguments.Take(at - 1), "-p", added, .. arguments.Skip(at - 1)];
    }

    private static string[] Without(IReadOnlyList<string> arguments, string property)
    {
        var at = arguments.ToList().IndexOf(property);

        return [.. arguments.Take(at - 1), .. arguments.Skip(at + 1)];
    }
}
