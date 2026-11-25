using System.Diagnostics;
using System.Text.Json;
using CoaiMcp.Core.Notices;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Detach;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server.Keys;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s7 t5 — a role job's claude launch takes its key from the pool (KEY_POOL_SPEC.md sections 5 and 6), read-only roles first: the real runner, launcher, ledger and notices, the REAL pool in the rig's own data folder, and FakeCli
/// standing in for claude — it records which key id each launch carried and the variable names it had. Nothing is called and nothing is paid. K7, K9 (the route rules), K15 and K16 of the spec's table; the coder's are in <see cref="AKeyCoderLaunchTests"/>.
/// </summary>
/// <remarks>In the serial <c>fakecli-env</c> collection, like every class that steers the fake.</remarks>
[Collection("fakecli-env")]
public sealed class AKeyRoleLaunchTests
{
    private const string Switch = "COAI_ROLE_CLAUDE_PERSON_SIGN_IN";

    private static string[] Ids(RoleFailoverRig rig) => [.. KeyRig.KeyIdsOf(rig.RecordFolder)];

    private static async Task<(int Exit, RoleJobRecord Job)> RunAsync(RoleFailoverRig rig, params string[] route) =>
        await rig.RunAsync(rig.NewJob(route));

    // ---------- K7: a limit in the middle of a job ----------

    [Fact]
    public async Task K7_ALimitOnAKey_RunsTheSameModelOnTheNextKey_InANewSession_AndTheAttemptsSayWhichKey()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        var keys = KeyRig.AddKeys(rig.World.DataDir, "a", "b");
        rig.Says("sonnet", FakeRoleAnswers.ClaudeSpendLimit(), FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok));

        var (exit, job) = await RunAsync(rig, "claude/sonnet");

        exit.Should().Be(RoleRunner.Done);
        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        var launched = Ids(rig);
        launched.Should().HaveCount(2).And.OnlyContain(id => id.StartsWith("k-"), "every launch carried a pool key").And.OnlyHaveUniqueItems("the second session runs on ANOTHER key");
        launched.Should().BeEquivalentTo(keys, "the two keys of the pool were the two keys used");
        job.Attempts.Select(attempt => attempt.KeyId).Should().Equal(launched, "each attempt of job.json names the key it ran on");
        job.Attempts[0].Stop.Should().Be("limit");
        rig.LedgerLines().Select(line => line.GetProperty("keyId").GetString()).Should().Equal(launched, "one ledger line per launch, each with its key");
        KeyRig.Row(rig.World.DataDir, launched[0]).Usable.Should().BeFalse("the key that was limited is cooling");
        rig.NoticesOf(ServerNoticeCodes.RoleModelSwitched).Should().BeEmpty("a key switch is a log line, not a notice");
        rig.World.Said.Select(line => line.RenderMessage()).Should().Contain(line => line.Contains("switched key 1 of 3"));
        KeyIdsInTheDatabase(rig.World.DataDir, job.JobId).Should().Equal(launched, "role_attempts.key_id is the projection of the attempt's key");
        JsonDocument.Parse(RoleJobAnswers.Status(new RoleJobObservation(job, null, false, false, false), string.Empty)).RootElement.GetProperty("attempts").EnumerateArray().Select(entry => entry.GetProperty("keyId").GetString()).Should().Equal(launched, "role_status shows the key of each attempt");
    }

    [Fact]
    public async Task K7_ThreeKeySwitchesAtMost_ThenTheClaudeVendorIsGivenUp()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        KeyRig.AddKeys(rig.World.DataDir, "a", "b", "c", "d", "e");
        rig.Says("sonnet", FakeRoleAnswers.ClaudeSpendLimit());

        var (_, job) = await RunAsync(rig, "claude/sonnet");

        job.State.Should().Be(RoleJobStates.Failed);
        Ids(rig).Should().HaveCount(4, "the first launch and three switches").And.OnlyHaveUniqueItems();
        job.Attempts.Should().HaveCount(4);
    }

    // ---------- K9: no wait when no key is usable ----------

    [Fact]
    public async Task K9_AllKeysCooling_ARouteWithAnotherVendor_RunsThatVendor_AndOneExhaustedNoticeIsWritten()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        var key = KeyRig.AddKey(rig.World.DataDir, "a");
        new KeyPool(new KeyStoreAccess(rig.World.DataDir, TimeProvider.System, _ => { })).Record(key, new KeyLaunchReport(KeyLaunchEnd.Limit, "usage limit reached, resets in 5h")).Ok.Should().BeTrue();
        rig.Says("sonnet", FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok));
        rig.Says("gpt-5.5", FakeRoleAnswers.CodexSays(FakeRoleAnswers.Ok));

        var (_, job) = await RunAsync(rig, "claude/sonnet", "codex/gpt-5.5");

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        job.Attempts.Select(attempt => attempt.Vendor).Should().Equal("codex");
        rig.Models().Should().Equal(["gpt-5.5"], "no claude launch was made: the claude model was skipped, at once");
        rig.NoticesOf(ServerNoticeCodes.ClaudeKeysExhausted).Should().ContainSingle();
    }

    [Fact]
    public async Task K9_AClaudeOnlyRoute_WhoseOnlyKeyIsLimitedInTheJob_FailsAsALimit_WithTheEarliestFreeTime_AndWaitsForNothing()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        KeyRig.AddKeys(rig.World.DataDir, "a");
        rig.Says("sonnet", FakeRoleAnswers.ClaudeSpendLimit());
        var clock = Stopwatch.StartNew();

        var (_, job) = await RunAsync(rig, "claude/sonnet", "claude/opus");

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30), "no wait for a reset that is hours away");
        job.State.Should().Be(RoleJobStates.Failed);
        job.Stop.Should().Be("limit");
        job.Reason.Should().Contain("no usable claude key").And.Contain("UTC");
        Ids(rig).Should().ContainSingle("the one key was limited, and nothing else was launched");
    }

    [Fact]
    public async Task K9_StartRole_RefusesAClaudeOnlyRoute_WhenEveryKeyCools_InTwoSeconds_BeforeAnyFolder()
    {
        using var world = new RoleJobWorld();
        var key = KeyRig.AddKey(world.DataDir, "a");
        new KeyPool(new KeyStoreAccess(world.DataDir, TimeProvider.System, _ => { })).Record(key, new KeyLaunchReport(KeyLaunchEnd.Limit, "usage limit reached, resets in 5h")).Ok.Should().BeTrue();
        var service = ServiceFor(world);
        var clock = Stopwatch.StartNew();

        var answer = JsonDocument.Parse(await service.StartAsync(FailoverProbeWork.Role, world.DataDir, null, "write the plan", null, null, null, TestContext.Current.CancellationToken)).RootElement;

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        answer.GetProperty("error").GetString().Should().Contain("no usable claude key").And.Contain("UTC");
        world.Store.JobIds().Should().BeEmpty();
        Directory.Exists(world.Store.JobsRoot).Should().BeFalse("no folder was made");
    }

    [Fact]
    public async Task K9_StartRole_StartsARouteThatHasAnotherVendor_WhateverTheState_OfThePool()
    {
        using var world = new RoleJobWorld();
        var service = ServiceFor(world, """{"ba":[{"model":"opus","vendor":"claude"},{"model":"gpt-5.5","vendor":"codex"}]}""");

        var answer = JsonDocument.Parse(await service.StartAsync(FailoverProbeWork.Role, world.DataDir, null, "write the plan", null, null, null, TestContext.Current.CancellationToken)).RootElement;

        answer.TryGetProperty("error", out _).Should().BeFalse("a route with a codex model starts: the claude model is skipped when the job runs");
        answer.GetProperty("route").GetArrayLength().Should().Be(2);
    }

    // ---------- K15: the empty pool, and the device switch ----------

    [Fact]
    public async Task K15_AnEmptyPool_RefusesAClaudeRoleAtStart_WithTheCure_AndOneNoticeADay()
    {
        using var world = new RoleJobWorld();
        var service = ServiceFor(world);

        var first = JsonDocument.Parse(await service.StartAsync(FailoverProbeWork.Role, world.DataDir, null, "write the plan", null, null, null, TestContext.Current.CancellationToken)).RootElement;
        await service.StartAsync(FailoverProbeWork.Role, world.DataDir, null, "write the plan", null, null, null, TestContext.Current.CancellationToken);

        first.GetProperty("error").GetString().Should().Contain("--keys add");
        world.Store.JobIds().Should().BeEmpty();
        var notices = world.Notices.Where(notice => notice.Code == ServerNoticeCodes.ClaudeKeysEmpty).ToList();
        notices.Should().NotBeEmpty();
        notices.Select(notice => notice.Subject).Distinct().Should().ContainSingle("the subject is the day: a repeat on the same day is the same notice on the page");
    }

    [Fact]
    public async Task K15_WithTheSwitchAndNoKeyAtAll_TheLaunchIsTodays_AndTheLedgerSaysPersonSignIn()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        rig.Setting(Switch, "1");
        rig.Says("sonnet", FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok));

        var (_, job) = await RunAsync(rig, "claude/sonnet");

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        job.Attempts.Single().KeyId.Should().Be("person-sign-in");
        rig.LedgerLines().Single().GetProperty("keyId").GetString().Should().Be("person-sign-in");
        KeyRig.EnvNamesOf(rig.RecordFolder).Single().Should().NotContain("CLAUDE_CODE_OAUTH_TOKEN");
        rig.NoticesOf(ServerNoticeCodes.ClaudePersonSignInUsed).Should().ContainSingle();
    }

    [Fact]
    public async Task K15_TheSwitch_DoesNothingWhenAKeyExists_EvenOneThatCools()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        rig.Setting(Switch, "1");
        var key = KeyRig.AddKey(rig.World.DataDir, "a");
        new KeyPool(new KeyStoreAccess(rig.World.DataDir, TimeProvider.System, _ => { })).Record(key, new KeyLaunchReport(KeyLaunchEnd.Limit, "usage limit reached, resets in 5h")).Ok.Should().BeTrue();
        rig.Says("sonnet", FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok));

        var (_, job) = await RunAsync(rig, "claude/sonnet");

        job.State.Should().Be(RoleJobStates.Failed, "the person's own limit never pays for a role job while the pool has keys");
        rig.Launches().Should().BeEmpty();
        rig.NoticesOf(ServerNoticeCodes.ClaudePersonSignInUsed).Should().BeEmpty();
    }

    // ---------- K16: a read-only role with a key ----------

    [Fact]
    public async Task K16_AReadOnlyRoleWithAKey_HasItsOwnHome_NoApiKey_NoBare_AndThePersonsSignInIsNotTouched()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        KeyRig.AddKeys(rig.World.DataDir, "a");
        rig.Says("sonnet", FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok));
        using var person = TempDir.For("coai-person-home-");
        var canary = Path.Combine(person.Path, ".claude", ".credentials.json");
        Directory.CreateDirectory(Path.GetDirectoryName(canary)!);
        File.WriteAllText(canary, "{\"canary\":true}\n");
        var before = File.ReadAllBytes(canary);
        var changed = File.GetLastWriteTimeUtc(canary);
        var home = Environment.GetEnvironmentVariable("HOME");
        Environment.SetEnvironmentVariable("HOME", person.Path);
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "an-api-key-of-the-person");
        foreach (var name in OtherEndpoints)
        {
            Environment.SetEnvironmentVariable(name, "the-persons-other-route");
        }

        try
        {
            var (_, job) = await RunAsync(rig, "claude/sonnet");

            job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOME", home);
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
            foreach (var name in OtherEndpoints)
            {
                Environment.SetEnvironmentVariable(name, null);
            }
        }

        var names = KeyRig.EnvNamesOf(rig.RecordFolder).Single();
        names.Should().Contain("CLAUDE_CODE_OAUTH_TOKEN").And.NotContain("ANTHROPIC_API_KEY");
        names.Should().NotContain(OtherEndpoints, "no other route to a host, a cloud or a credential reaches a keyed launch: the CLI would send the key where the person's environment points");
        var homes = KeyRig.HomesOf(rig.RecordFolder).Single();
        homes["HOME"].Should().NotBeEmpty().And.NotBe(person.Path).And.StartWith(Path.Combine(rig.World.DataDir, "roles"), "the home is under the job's own folder");
        homes["CLAUDE_CONFIG_DIR"].Should().StartWith(homes["HOME"]);
        homes["XDG_CONFIG_HOME"].Should().StartWith(homes["HOME"]);
        rig.Launches().Single().Should().NotContain("--bare");
        File.ReadAllBytes(canary).Should().Equal(before, "the person's sign-in file is byte-equal");
        File.GetLastWriteTimeUtc(canary).Should().Be(changed, "and untouched");
    }

    /// <summary>The names, besides the two credentials, that would send a keyed claude to another host or another cloud (taken from the strings of the installed CLI, 2.1.332).</summary>
    private static readonly string[] OtherEndpoints =
    [
        "ANTHROPIC_BASE_URL", "ANTHROPIC_BEDROCK_BASE_URL", "ANTHROPIC_VERTEX_BASE_URL", "ANTHROPIC_VERTEX_PROJECT_ID", "ANTHROPIC_FOUNDRY_BASE_URL", "ANTHROPIC_FOUNDRY_API_KEY", "ANTHROPIC_AWS_BASE_URL",
        "CLAUDE_CODE_USE_BEDROCK", "CLAUDE_CODE_USE_VERTEX", "CLAUDE_CODE_USE_FOUNDRY", "AWS_BEARER_TOKEN_BEDROCK", "ANTHROPIC_CUSTOM_HEADERS", "ANTHROPIC_UNIX_SOCKET", "CLAUDE_CODE_OAUTH_REFRESH_TOKEN", "CLAUDE_CODE_CUSTOM_OAUTH_URL",
    ];

    // ---------- the second judgement: the output, the CLI's home, an unreadable pool ----------

    private static object PrintsItsKey() =>
        new Dictionary<string, object> { ["stdout"] = JsonSerializer.SerializeToElement(FakeRoleAnswers.ClaudeSays("{\"status\":\"ok\",\"note\":\"{{claude-key}}\"}")).GetProperty("stdout").GetString()!, ["exit"] = 0 };

    [Fact]
    public async Task S_AReadOnlyRoleThatPrintsItsKey_IsScrubbed_TheJobEndsKeyInOutput_AndTheKeyIsDead()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        var id = KeyRig.AddKey(rig.World.DataDir, "a");
        rig.Says("sonnet", PrintsItsKey());

        var (_, job) = await RunAsync(rig, "claude/sonnet");

        job.Stop.Should().Be("key-in-output");
        KeyRig.Row(rig.World.DataDir, id).DeadReason.Should().Be(KeyDeadReasons.Exposed);
        var stored = string.Join('\n', Directory.GetFiles(Path.Combine(rig.World.DataDir, "roles"), "*", SearchOption.AllDirectories).Select(File.ReadAllText));
        stored.Should().NotContain(KeyWorld.Sentinel, "the read-only road scrubs too").And.Contain($"[claude key {id}]");
    }

    [Fact]
    public async Task S_TheInvocationAKeyedLaunchLeavesBehind_HoldsNoEnvironment()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        rig.Says("sonnet", FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok));
        var token = KeyWorld.Token("a");
        var launcher = new RoleModelLauncher(name => name == RoleModelLauncher.CliKey("claude") ? RoleJobWorld.FakeCli : null, NoProcessTracking.Instance);

        var launched = await launcher.LaunchAsync(new CoaiMcp.Core.Roles.ModelRef("sonnet", "claude"), "go", rig.Repo, Path.Combine(rig.World.DataDir, "launch-out"), TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken, string.Empty, new RoleKey.Pool(new ClaudeKeySecret(KeyWorld.IdOf(token), token)));

        launched.Launch.Should().NotBeNull(launched.Refused?.ToString());
        launched.Invocation!.Request.Environment.Should().NotContainKey(ClaudeConsultant.KeyVariable, "what the ledger and the result keep says what ran, and never holds the text");
        JsonSerializer.Serialize(launched.Invocation.Request.Environment).Should().NotContain(KeyWorld.Sentinel);
    }

    [Fact]
    public async Task S_AKeyInTheHomeOfAReadOnlyRole_AfterTheLaunch_IsFound()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        var id = KeyRig.AddKey(rig.World.DataDir, "a");
        var envelope = JsonSerializer.SerializeToElement(FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok)).GetProperty("stdout").GetString()!;
        rig.Says("sonnet", new Dictionary<string, object> { ["stdout"] = envelope, ["exit"] = 0, ["homeFiles"] = new Dictionary<string, string> { [".claude/projects/x.jsonl"] = "{{claude-key}}\n" } });

        var (_, job) = await RunAsync(rig, "claude/sonnet");

        job.Stop.Should().Be("key-in-output", "the folder a keyed read-only launch ran in is searched after it ends");
        KeyRig.Row(rig.World.DataDir, id).DeadReason.Should().Be(KeyDeadReasons.Exposed);
    }

    [Fact]
    public async Task S_APoolWhoseFilesCannotBeRead_IsNotEmpty_ThePersonsSignInIsNeverUsedForIt()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        KeyRig.AddKey(rig.World.DataDir, "a");
        foreach (var meta in Directory.GetFiles(Path.Combine(rig.World.DataDir, "keys", "claude"), "k-*.json"))
        {
            File.WriteAllText(meta, "{ this is not json");
        }

        rig.Says("sonnet", FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok));

        var (_, job) = await RunAsync(rig, "claude/sonnet");

        job.State.Should().Be(RoleJobStates.Failed);
        rig.Launches().Should().BeEmpty("no launch, and above all none on the person's sign-in");
        rig.NoticesOf(ServerNoticeCodes.ClaudePersonSignInUsed).Should().BeEmpty();
    }

    // ---------- helpers ----------

    private static IReadOnlyList<string> KeyIdsInTheDatabase(string dataDir, string jobId)
    {
        using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(dataDir, CoaiMcp.Store.RoundsDb.FileName)};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var ask = db.CreateCommand();
        ask.CommandText = "SELECT key_id FROM role_attempts WHERE job_id = $id ORDER BY ordinal";
        ask.Parameters.AddWithValue("$id", jobId);
        using var rows = ask.ExecuteReader();
        var read = new List<string>();
        while (rows.Read())
        {
            read.Add(rows.GetString(0));
        }

        return read;
    }

    private sealed class NothingStarted : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, TimedOut: false));
    }

    private static RoleJobService ServiceFor(RoleJobWorld world, string? models = null)
    {
        Dictionary<string, string?> values = new() { ["COAI_ROLE_LAUNCHER"] = "setsid" };
        if (models is not null)
        {
            values["COAI_ROLES_MODELS"] = models;
        }

        return new RoleJobService(world.DataDir, name => values.GetValueOrDefault(name), world.Log, notice => { world.Notices.Enqueue(notice); return true; })
        {
            Clock = world.Clock,
            Launcher = new DetachedLauncher(new NothingStarted(), NoRunnerStarted.Succeeds),
            Descriptors = [new RoleDescriptor(FailoverProbeWork.Role, "a test-only role that asks its route's models", () => new FailoverProbeWork(TimeSpan.FromSeconds(1), []), RouteAs: "ba"), .. RoleDescriptors.Shipped],
        };
    }
}
