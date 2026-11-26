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
            arguments.Should().Contain("LimitCORE=0", "a unit that holds a key never dumps core");
        }

        var coder = units.Single(arguments => arguments.Any(argument => argument.Contains($"coai-coder-{job.JobId}-c1", StringComparison.Ordinal)));
        coder.Should().NotContain($"--setenv={Variable}", "the key is not a property of the unit: it would sit in `systemctl show` and in a 0644 transient file for as long as the unit lives");
        coder.Should().Contain(argument => argument.Contains($"read -r {Variable}", StringComparison.Ordinal), "the unit's shell reads the key from the first line of stdin and exports it itself, before the exec");
        coder.Where(argument => argument.Contains(Variable + "=", StringComparison.Ordinal)).Should().BeEmpty("no argument assigns the name a value");
    }

    [Fact]
    public async Task K11_TheRowThatProvesTheSignInFileIsNotInTheUnit_GoesRedWhenTheBindIsPutBack()
    {
        await using var rig = await CoderRig.CreateAsync();
        KeyRig.AddKeys(rig.World.DataDir, "a");
        var job = rig.NewJob();
        var bind = $"BindReadOnlyPaths={rig.Host.SignInFile}:{Path.Combine(rig.JobFolder(job), "coder", "claude", "home", ".claude", ".credentials.json")}";
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
        var unitText = new StringBuilder();
        var clientEnvironment = new StringBuilder();
        var properties = 0;
        var clients = 0;
        var unit = $"coai-coder-{job.JobId}-c1";
        for (var until = DateTime.UtcNow.AddSeconds(20); !run.IsCompleted && DateTime.UtcNow < until;)
        {
            var shown = UnitLeakWatch.UnitProperties($"{unit}.service");
            properties += shown.Contains("Environment=", StringComparison.Ordinal) ? 1 : 0;
            unitText.AppendLine(shown).AppendLine(UnitLeakWatch.TransientFiles(job.JobId));
            seen.Append(UnitLeakWatch.Cmdlines());
            clientEnvironment.Append(UnitLeakWatch.ClientEnvironments(unit));
            clients += UnitLeakWatch.Clients(unit);

            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        var (_, ended) = await run;

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        seen.Length.Should().BeGreaterThan(1000, "the scan must have read real command lines while the coder ran");
        properties.Should().BeGreaterThan(0, "the property of the live unit was read at least once, so the check can see a unit");
        clients.Should().BeGreaterThan(0, "the systemd-run client of the unit was found while the coder ran, so its environment was read");
        clientEnvironment.Length.Should().BeGreaterThan(0, "and its environment was readable");
        unitText.ToString().Should().NotContain(KeyWorld.Sentinel, "neither `systemctl --user show -p Environment` of the unit nor the 0644 transient unit file under /run/user ever held the key");
        seen.ToString().Should().NotContain(KeyWorld.Sentinel, "no process's command line — the client, the shell, env, the coder — held the key while the unit ran");
        clientEnvironment.ToString().Should().NotContain(KeyWorld.Sentinel, "the key is not in the environment of the systemd-run client either: no `--setenv` is needed to leave it there, and a process of the same user can read that file");
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

    // ---------- the commit search never fails open, and reads what git commits (the second judgement) ----------

    [Fact]
    public async Task M3_AKeyThatIsRemovedBeforeTheCommit_DoesNotMakeTheSearchSilent_TheFileIsStillFound()
    {
        await using var rig = await CoderRig.CreateAsync();
        var id = KeyRig.AddKey(rig.World.DataDir, "a");
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { ["src/leak.txt"] = "{{claude-key}}\n" }, CoderRig.Report()));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job, boundary: point =>
        {
            if (point == "before-commit")
            {
                new KeyAdmin(new KeyStoreAccess(rig.World.DataDir, TimeProvider.System, _ => { })).Remove(id).Ok.Should().BeTrue();
            }
        });

        ended.Stop.Should().Be("key-in-output", "the session kept its key in memory: a removed key is no reason to commit unsearched");
        (await rig.GitAsync(rig.Repo, "rev-parse", $"refs/heads/coder/{job.JobId}")).Should().Be(rig.Base, "nothing was committed");
    }

    [Fact]
    public async Task M4_AKeyInAFileWhoseWorkingTreeEncodingIsNotWhatGitCommits_IsFoundInTheStagedBlob()
    {
        await using var rig = await CoderRig.CreateAsync(extra: new Dictionary<string, string> { [".gitattributes"] = "src/leak.txt working-tree-encoding=UTF-16LE\n" });
        var id = KeyRig.AddKey(rig.World.DataDir, "a");
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { ["src/leak.txt#utf16le"] = "{{claude-key}}\n" }, CoderRig.Report()));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        var onDisk = Path.Combine(rig.WorktreeOf(job), "src", "leak.txt");
        File.Exists(onDisk).Should().BeTrue();
        KeyScanner.Walk(onDisk, [new ClaudeKeySecret(id, KeyWorld.Token("a"))], string.Empty).Hits.Should().BeEmpty("the companion: the bytes on disk are UTF-16, so a search of the working tree is blind to the key");
        ended.Stop.Should().Be("key-in-output", "git commits the blob it converts to UTF-8, and that holds the exact text");
        (await rig.GitAsync(rig.Repo, "rev-parse", $"refs/heads/coder/{job.JobId}")).Should().Be(rig.Base);
    }

    [Fact]
    public async Task S_TheWorkingTreeIsSearchedBeforeGitAdd_SoTheBlobOfALeakIsNeverWritten()
    {
        await using var rig = await CoderRig.CreateAsync();
        KeyRig.AddKey(rig.World.DataDir, "a");
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { ["src/leak.txt"] = "{{claude-key}}\n" }, CoderRig.Report()));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.Stop.Should().Be("key-in-output");
        (await rig.GitAsync(rig.WorktreeOf(job), "ls-files", "--stage")).Should().NotContain("src/leak.txt", "the file was never staged, so git never wrote a blob that holds the key");
    }

    // ---------- the CLI's own folders ----------

    [Fact]
    public async Task S_AKeyInTheCodersHome_AfterTheLaunch_IsFound_ThoughNothingWasPrintedAndNoWorktreeFileHoldsIt()
    {
        await using var rig = await CoderRig.CreateAsync();
        var id = KeyRig.AddKey(rig.World.DataDir, "a");
        rig.Says("sonnet", new CoderRig.Launch(CoderRig.Edits("src/thing.cs"), CoderRig.Report(), HomeFiles: new Dictionary<string, string> { [".claude/transcript.jsonl"] = "{\"token\":\"{{claude-key}}\"}\n" }));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.Stop.Should().Be("key-in-output", "the transcript a CLI keeps in its home is searched after the launch");
        KeyRig.Row(rig.World.DataDir, id).DeadReason.Should().Be(KeyDeadReasons.Exposed);
    }

    [Fact]
    public async Task S_TheTextOfAnotherPoolKey_InTheOutput_IsScrubbed_ThatKeyIsExposed_AndTheJobStops()
    {
        await using var rig = await CoderRig.CreateAsync();
        var ids = KeyRig.AddKeys(rig.World.DataDir, "a", "b");
        rig.Says("sonnet", new CoderRig.Launch(CoderRig.Edits("src/thing.cs"), CoderRig.Report(notes: "I found this key: " + KeyWorld.Token("b"))));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        Keys(rig, job).Single().Should().Be(ids[0], "the first key ran the job; the other one is the one that was printed");
        ended.Stop.Should().Be("key-in-output");
        KeyRig.Row(rig.World.DataDir, ids[1]).DeadReason.Should().Be(KeyDeadReasons.Exposed);
        AllStoredText(rig, job).Should().NotContain(KeyWorld.Sentinel);
    }

    [Fact]
    public async Task S_AKeysFileOfThePerson_IsHiddenInTheCodersUnit_WhereverItLies()
    {
        await using var rig = await CoderRig.CreateAsync();
        KeyRig.AddKeys(rig.World.DataDir, "a");
        using var elsewhere = TempDir.For("coai-keysfile-");
        var file = Path.Combine(elsewhere.Path, "keys.env");
        File.WriteAllText(file, "x\n");
        rig.Rig.Setting("COAI_CLAUDE_KEYS_FILE", file);
        var seen = new List<IReadOnlyList<string>>();
        rig.Degrade = Keep(seen);
        rig.Says("sonnet", CoderRig.Edits("src/thing.cs"), CoderRig.Report());
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        seen.Where(arguments => arguments.Any(argument => argument.Contains($"coai-coder-{job.JobId}-c1", StringComparison.Ordinal))).Should().ContainSingle()
            .Which.Should().Contain($"InaccessiblePaths=-{file}", "a keys file the person chose is hidden from the coder's unit as the key folder is");
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

    // ---------- K13: the canary ----------

    [Fact]
    public async Task K13_ACanaryAddedThroughTheRealOneShot_RunsACoderToGreen_AndIsInNoArgvLogLedgerNoticeResultFileOrCommit()
    {
        await using var rig = await CoderRig.CreateAsync();
        var canary = KeyWorld.Token("canary");
        var id = KeyWorld.IdOf(canary);
        KeysBinary.Run(rig.World.DataDir, canary + "\n", "--keys", "add", "--label", "canary key").Code.Should().Be(0);
        var seen = new List<IReadOnlyList<string>>();
        rig.Degrade = Keep(seen);
        rig.Says("sonnet", CoderRig.Edits("src/thing.cs"), CoderRig.Report());
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        Keys(rig, job).Single().Should().Be(id, "the canary was the one key, so the coder ran on it");
        var text = new StringBuilder();
        text.AppendLine(string.Join('\n', seen.SelectMany(arguments => arguments)));
        foreach (var argv in rig.Launches(job))
        {
            text.AppendLine(string.Join('\n', argv));
        }

        foreach (var line in rig.World.Said)
        {
            text.AppendLine(line.RenderMessage());
        }

        foreach (var notice in rig.World.Notices)
        {
            text.AppendLine(KeyRig.Line(notice));
        }

        var head = rig.Envelope(job).GetProperty("headSha").GetString()!;
        text.AppendLine(await rig.GitAsync(rig.Repo, "log", "-p", "--all", "--format=fuller", head));
        text.AppendLine(KeysBinary.Run(rig.World.DataDir, string.Empty, "--keys", "list", "--json").Out);
        text.AppendLine(RoleJobAnswers.Status(new RoleJobObservation(ended, null, false, false, false), rig.World.Store.FolderOf(job.JobId)!.ReadResult()));
        text.ToString().Should().NotContain(KeyWorld.Sentinel, "no argv, log line, notice, commit, page answer or status holds any part of the canary");

        var secret = new ClaudeKeySecret(id, canary);
        var keysFolder = Path.Combine(rig.World.DataDir, "keys");
        var folders = new[] { rig.World.DataDir, rig.JobFolder(job), rig.Repo, rig.WorktreeOf(job) };
        foreach (var folder in folders)
        {
            KeyScanner.Walk(folder, [secret], keysFolder).Hits.Should().BeEmpty($"no file under {Path.GetFileName(folder)} — job.json, request.json, attempts, result.json, usage.jsonl, the notices, coai.db — holds the canary");
        }

        // The companion: the same search, over the same data folder with the store NOT excluded, finds the canary in its own key file, and only there.
        KeyScanner.Walk(rig.World.DataDir, [secret], string.Empty).Hits.Select(hit => Path.GetFileName(hit.Path)).Should().Equal([$"{id}.key"], "the search can see the canary where it is");
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
