using System.Text.Json;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Roles;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Detach;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s8 t1 (SPEC C5 and V12) — the failover of a coder ACROSS vendors, for each combination of verdicts, with an injected table, FakeCli and two TEST vendors that stand in for codex and agy (a real one is never started: neither has a measured release yet).
/// d1: for each of the four combinations of ship and refuse the job goes through exactly the shipped vendors in route order, every switch from the last coai commit, in a new session, with one line and one notice that names the vendor. d2: zero launches of a refused vendor — a
/// vendor that ships with no measured release is refused — and a model the caller named on one is refused before any folder. d3: the unit of a vendor holds only that vendor's sign-in or variable name (read from the record of the unit and from inside it), the copy of the vendor
/// that was left is deleted before the next unit starts, the fix-turn count is the job's, and a spent quota skips only that vendor's other models.
/// </summary>
/// <remarks>
/// <para>What FakeCli does INSIDE the unit is the evidence: its model map says which absolute paths it tries to read (<c>readFiles</c>), and it records the answer, with the variable names it was started with, beside the launch record. A test vendor leaves a made-up "sign-in copy" in
/// its own home (<c>homeFiles</c>) the way the job's copy will lie there (s7 t6, not built): the deletion on a switch acts on a file at the place the vendor's seam names, whoever made it.</para>
/// <para>Needs a systemd user manager like every coder test (skipped, with the reason, without one).</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class ACoderFailsOverAcrossVendorsTests
{
    private const string Argon = "gemini-4-argon-medium";

    private const string Claude1 = "11111111-1111-1111-1111-111111111111";
    private const string ClaudeLast = "33333333-3333-3333-3333-333333333333";
    private const string CodexLast = "44444444-4444-4444-4444-444444444444";
    private const string AgyLast = "55555555-5555-5555-5555-555555555555";

    private const string CodexCopy = "CODEX-COPY-CANARY";
    private const string AgyCopy = "AGY-COPY-CANARY";

    /// <summary>What a vendor's verdict is in a table of a test.</summary>
    public enum Verdict
    {
        /// <summary>Verdict ship and one measured release (a record).</summary>
        Ship,

        /// <summary>Verdict refuse.</summary>
        Refuse,

        /// <summary>Verdict ship, the mark launchable on and NO history of measures: ships all the same — the history is a record and never a condition (owner decision of 2026-11-26).</summary>
        ShipUnmeasured,

        /// <summary>Verdict ship and the mark launchable OFF (the table of the product today for codex and agy): not launchable.</summary>
        ShipNotLaunchable,
    }

    private static readonly ModelRef Sonnet = new("sonnet", "claude");
    private static readonly ModelRef Opus = new("opus", "claude");
    private static readonly ModelRef Terra = new("terra", "codex");
    private static readonly ModelRef ArgonModel = new(Argon, "agy");

    private static readonly ModelRef[] Full = [Sonnet, Opus, Terra, ArgonModel];

    public static TheoryData<Verdict, Verdict> Combinations => new()
    {
        { Verdict.Ship, Verdict.Ship },
        { Verdict.Ship, Verdict.Refuse },
        { Verdict.Refuse, Verdict.Ship },
        { Verdict.Refuse, Verdict.Refuse },
    };

    public static TheoryData<Verdict, Verdict> CombinationsAndUnmeasured => new()
    {
        { Verdict.Ship, Verdict.Ship },
        { Verdict.Ship, Verdict.Refuse },
        { Verdict.Refuse, Verdict.Ship },
        { Verdict.Refuse, Verdict.Refuse },
        { Verdict.ShipUnmeasured, Verdict.Ship },
        { Verdict.Ship, Verdict.ShipUnmeasured },
        { Verdict.ShipUnmeasured, Verdict.ShipUnmeasured },
        { Verdict.ShipNotLaunchable, Verdict.Ship },
        { Verdict.Ship, Verdict.ShipNotLaunchable },
        { Verdict.ShipNotLaunchable, Verdict.ShipNotLaunchable },
    };

    private const string OneMeasure = "\"measured\": [ { \"release\": \"1.0.0\", \"resultsHeading\": \"H\", \"judgementHeading\": \"J\", \"programs\": [ { \"file\": \"x\", \"sha256\": \"0000000000000000000000000000000000000000000000000000000000000000\" } ] } ]";

    private static string Row(string id, Verdict verdict) =>
        $$"""{ "id": "{{id}}", "verdict": "{{(verdict == Verdict.Refuse ? "refuse" : "ship")}}", "launchable": {{(Ships(verdict) ? "true" : "false")}}, "results": "r.md", {{(verdict == Verdict.Ship ? OneMeasure : "\"measured\": []")}}, "properties": [], "pending": [] }""";

    private static CoderVendorSet Set(Verdict codex, Verdict agy, CrossVendor? codexVendor = null, CrossVendor? agyVendor = null) =>
        CoderVendorSet.Of(CoderBoundaryTable.Parse($$"""{ "version": 1, "judgement": "j.md", "shared": [], "vendors": [ {{Row("codex", codex)}}, {{Row("agy", agy)}} ] }"""), codexVendor ?? new CrossVendor("codex"), agyVendor ?? new CrossVendor("agy"));

    private static bool Ships(Verdict verdict) => verdict is Verdict.Ship or Verdict.ShipUnmeasured;

    /// <summary>The vendors that are launched, in route order, for the verdicts.</summary>
    private static string[] Expected(Verdict codex, Verdict agy) => ["claude", .. Ships(codex) ? new[] { "codex" } : [], .. Ships(agy) ? new[] { "agy" } : []];

    /// <summary>The models that are launched, in route order: both claude models, then the one model of each shipped vendor.</summary>
    private static string[] ExpectedModels(Verdict codex, Verdict agy) => ["sonnet", "opus", .. Ships(codex) ? new[] { "terra" } : [], .. Ships(agy) ? new[] { Argon } : []];

    private static RouteResult.Routed Narrowed(CoderVendorSet set) =>
        CoderRoute.Narrow(new RouteResult.Routed(Full, "the coder list"), vendors: set).Should().BeOfType<RouteResult.Routed>().Subject;

    private static string Resumed(string[] argv) => argv.SkipWhile(arg => arg != "--resume").Skip(1).FirstOrDefault() ?? string.Empty;

    private static string ModelOf(string[] argv) => argv[Array.FindIndex(argv, arg => arg is "--model" or "-m") + 1];

    private static async Task<CoderRig> RigAsync(CoderVendorSet set)
    {
        var rig = await CoderRig.CreateAsync();
        rig.Vendors = set;
        rig.Rig.Setting("COAI_ROLE_CODEX_CLI", RoleJobWorld.FakeCli).Setting("COAI_ROLE_AGY_CLI", RoleJobWorld.FakeCli);

        // A vendor that signs in with a file is given a copy of the person's own file (s7 t6): the stand-in for the real home holds one for each, with nothing in it the recipe of these stand-ins keeps.
        rig.RealHomeFile(".codex/auth.json", "{}");
        rig.RealHomeFile(".agy/auth.json", "{}");

        return rig;
    }

    private static CoderRig.Launch Writes(string text, string session, IReadOnlyList<string>? reads = null, IReadOnlyDictionary<string, string>? home = null) =>
        new(new Dictionary<string, string> { [CoderRig.StateFile] = text + "\n" }, CoderRig.Report(), Session: session, HomeFiles: home, Reads: reads);

    private static string Stray(string model) => $"stray-{model}.txt";

    /// <summary>The CLI falls over with a build break and a file nobody asked for half written, and exit 1.</summary>
    private static CoderRig.Launch Falls(string model, IReadOnlyList<string> reads, IReadOnlyDictionary<string, string>? home = null) =>
        new(new Dictionary<string, string> { [CoderRig.StateFile] = CoderRig.Broken + " half\n", [Stray(model)] = "half\n" }, string.Empty, Exit: 1, Stderr: "the CLI fell over: boom", HomeFiles: home, Reads: reads);

    private static IReadOnlyDictionary<string, string>? CopyOf(string vendor) => vendor switch
    {
        "codex" => new Dictionary<string, string> { [".codex/auth.json"] = CodexCopy },
        "agy" => new Dictionary<string, string> { [".agy/auth.json"] = AgyCopy },
        _ => null,
    };

    private static string Home(CoderRig rig, RoleJobRecord job, string vendor = "claude") => Path.Combine(rig.JobFolder(job), "coder", vendor, "home");

    private static string CredentialsInUnit(CoderRig rig, RoleJobRecord job) => Path.Combine(Home(rig, job), ".claude", ".credentials.json");

    private static string CopyPath(CoderRig rig, RoleJobRecord job, string vendor) => Path.Combine(Home(rig, job, vendor), $".{vendor}", "auth.json");

    /// <summary>What FakeCli tried to read inside each unit, launch by launch: path, what it saw (<c>read</c>, <c>absent</c>, <c>denied</c>) and the first characters.</summary>
    private static IReadOnlyList<IReadOnlyDictionary<string, (string Saw, string Text)>> ReadsOf(CoderRig rig, RoleJobRecord job)
    {
        return
        [
            .. rig.LaunchFolders(job).Select(folder => Path.Combine(folder, "sidecars")).Where(Directory.Exists).SelectMany(folder => new DirectoryInfo(folder).EnumerateFiles("*.reads")).OrderBy(file => file.CreationTimeUtc).ThenBy(file => file.LastWriteTimeUtc)
                .Select(file => (IReadOnlyDictionary<string, (string, string)>)File.ReadAllLines(file.FullName).Select(line => line.Split('\t')).ToDictionary(parts => parts[0], parts => (parts[1], parts.Length > 2 ? parts[2] : string.Empty))),
        ];
    }

    /// <summary>The properties each launch's unit was started with, from the job's record: the attempt (and so the vendor) and its list.</summary>
    private static IReadOnlyList<(int Attempt, IReadOnlyList<string> Properties)> UnitsOf(CoderRig rig, RoleJobRecord job)
    {
        var folder = rig.World.Store.FolderOf(job.JobId)!.AttemptsFolder;
        var found = new List<(int, string, IReadOnlyList<string>)>();
        foreach (var file in Directory.EnumerateFiles(folder, "unit-*.json", SearchOption.AllDirectories))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            var properties = json.RootElement.GetProperty("properties").EnumerateArray().Select(property => $"{property.GetProperty("name").GetString()}={property.GetProperty("value").GetString()}").ToList();
            found.Add((int.Parse(new DirectoryInfo(Path.GetDirectoryName(file)!).Name, System.Globalization.CultureInfo.InvariantCulture), Path.GetFileName(file), properties));
        }

        return [.. found.OrderBy(entry => entry.Item1).ThenBy(entry => entry.Item2, StringComparer.Ordinal).Select(entry => (entry.Item1, (IReadOnlyList<string>)entry.Item3))];
    }

    // ---------- d1 and d3: the whole failover, for each combination ----------

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task D1_TheJobFailsOverThroughExactlyTheShippedVendors_InRouteOrder_EachSwitchFromTheLastCoaiCommit_InANewSession(Verdict codex, Verdict agy)
    {
        var codexVendor = new CrossVendor("codex");
        var agyVendor = new CrossVendor("agy");
        var set = Set(codex, agy, codexVendor, agyVendor);
        await using var rig = await RigAsync(set);
        var route = Narrowed(set);
        var job = rig.NewJob(route: RoleRoute.Format(route.Models));
        var launchedModels = ExpectedModels(codex, agy);
        var last = launchedModels[^1];

        string[] probes = [CredentialsInUnit(rig, job), CopyPath(rig, job, "codex"), CopyPath(rig, job, "agy"), Path.Combine(rig.WorktreeOf(job), CoderRig.StateFile), .. launchedModels.Select(model => Path.Combine(rig.WorktreeOf(job), Stray(model)))];
        var finalSession = Expected(codex, agy)[^1] switch { "claude" => ClaudeLast, "codex" => CodexLast, _ => AgyLast };
        foreach (var model in new[] { "sonnet", "opus", "terra", Argon })
        {
            var vendor = Full.First(known => known.Model == model).Vendor;
            if (model == last)
            {
                rig.Says(model, Writes(CoderRig.Red + " f", finalSession, probes, CopyOf(vendor)), Writes(CoderRig.Green, finalSession, probes, CopyOf(vendor)));
            }
            else if (model == "sonnet")
            {
                rig.Says(model, Writes(CoderRig.Red + " s", Claude1, probes), Falls(model, probes));
            }
            else
            {
                rig.Says(model, Falls(model, probes, CopyOf(vendor)));
            }
        }

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var launches = rig.Launches(job);
        var order = launches.Select(ModelOf).ToArray();
        order.Distinct().Should().Equal(launchedModels, "the job went through exactly the shipped vendors' models, in route order");
        order.Select(model => Full.First(known => known.Model == model).Vendor).Distinct().Should().Equal(Expected(codex, agy));
        ended.Attempts.Select(attempt => (attempt.Model, attempt.Vendor)).Should().Equal(launchedModels.Select(model => (model, Full.First(known => known.Model == model).Vendor)));

        // Every switch: one line, one notice, and the notice names the vendor the repository text now goes to.
        var switches = launchedModels.Length - 1;
        rig.Rig.SwitchLines().Should().HaveCount(switches);
        var notices = rig.Rig.NoticesOf(ServerNoticeCodes.RoleModelSwitched);
        notices.Should().HaveCount(switches);
        for (var i = 0; i < switches; i++)
        {
            var to = Full.First(known => known.Model == launchedModels[i + 1]);
            notices[i].Provider.Should().Be(to.Vendor);
            notices[i].Detail.Should().Contain($"to the {to.Vendor} vendor ({to.Model})");
        }

        // From inside each unit, at the moment it started.
        var reads = ReadsOf(rig, job);
        reads.Should().HaveCount(launches.Count);
        var firstOf = launchedModels.ToDictionary(model => model, model => Array.IndexOf(order, model));
        var lastOf = launchedModels.ToDictionary(model => model, model => Array.LastIndexOf(order, model));
        for (var i = 0; i < launches.Count; i++)
        {
            var vendor = Full.First(known => known.Model == order[i]).Vendor;
            var seen = reads[i];
            if (vendor == "claude")
            {
                seen[CredentialsInUnit(rig, job)].Text.Should().Contain(CoderBed.SignInLine, $"CONTROL, launch {i}: the unit of claude does hold its sign-in, so the read sees what is there");
            }
            else
            {
                seen[CredentialsInUnit(rig, job)].Text.Should().NotContain(CoderBed.SignInLine, $"launch {i} ({order[i]}): the person's claude sign-in is not in the unit of {vendor}");
            }

            foreach (var other in new[] { "codex", "agy" }.Where(other => other != vendor))
            {
                seen[CopyPath(rig, job, other)].Saw.Should().Be("absent", $"launch {i} ({order[i]}): the sign-in copy of {other} is not in the unit of {vendor}");
            }

            // From the last coai commit: what an earlier model left half written is gone, and the tree is what coai committed.
            foreach (var model in launchedModels.Where(model => lastOf[model] < i))
            {
                seen[Path.Combine(rig.WorktreeOf(job), Stray(model))].Saw.Should().Be("absent", $"launch {i}: the file {model} left half written was dropped before {order[i]} started");
            }

            var state = seen[Path.Combine(rig.WorktreeOf(job), CoderRig.StateFile)];
            if (i == 0)
            {
                state.Saw.Should().Be("absent");
            }
            else
            {
                state.Text.Should().Contain(i <= firstOf[last] ? "state: red s" : "state: red f", $"launch {i}: the worktree holds the last coai commit").And.NotContain(CoderRig.Broken);
            }
        }

        // A new session for each model: no resume of another model's session; the last model's fix turn resumes ITS turn 0.
        var firstLaunches = launchedModels.Select(model => launches[firstOf[model]]).ToArray();
        firstLaunches.Select(Resumed).Should().OnlyContain(session => session.Length == 0);
        var finalLaunches = launches.Where(argv => ModelOf(argv) == last).ToArray();
        Resumed(finalLaunches[^1]).Should().Be(finalSession, "the fix turn resumes the session of the new vendor's own turn 0");
        var vendorOfLast = Expected(codex, agy)[^1];
        if (vendorOfLast != "claude")
        {
            (vendorOfLast == "codex" ? codexVendor : agyVendor).Expected.Should().Equal(string.Empty, finalSession);
            (vendorOfLast == "codex" ? agyVendor : codexVendor).Expected.Should().NotContain(session => session.Length > 0, "a vendor that is not the last one is asked only about its own turn 0 — a launch that ended badly is checked too (pass 5, M11) — and never about the session of the last");
        }

        // The unit of each vendor, from its record: claude's sign-in bind is in the unit of claude and of no other vendor, and another vendor's unit hides the claude key pool.
        var attemptVendor = ended.Attempts.ToDictionary(attempt => attempt.N, attempt => attempt.Vendor);
        foreach (var (attempt, properties) in UnitsOf(rig, job))
        {
            var vendor = attemptVendor[attempt];
            var binds = properties.Where(property => property.StartsWith("BindReadOnlyPaths=", StringComparison.Ordinal) && property.EndsWith(".claude/.credentials.json", StringComparison.Ordinal)).ToList();
            binds.Should().HaveCount(vendor == "claude" ? 1 : 0, $"the unit of attempt {attempt} ({vendor}) holds {(vendor == "claude" ? "claude's" : "no claude")} sign-in");
            properties.Where(property => property.StartsWith("Bind", StringComparison.Ordinal) && (property.Contains("/.codex", StringComparison.Ordinal) || property.Contains("/.agy", StringComparison.Ordinal))).Should().BeEmpty("no unit BINDS another vendor's sign-in (the real files are hidden: InaccessiblePaths)");
            if (vendor != "claude")
            {
                properties.Should().Contain(property => property.StartsWith("InaccessiblePaths=", StringComparison.Ordinal) && property.Contains("/keys", StringComparison.Ordinal), "the key pool of claude is hidden from the unit of another vendor");
            }
        }

        // The variable names of each unit, from inside: its vendor's own, none of another's.
        var names = KeyRig.EnvNamesOf(rig.LaunchFolders(job));
        names.Should().HaveCount(launches.Count);
        for (var i = 0; i < launches.Count; i++)
        {
            var vendor = Full.First(known => known.Model == order[i]).Vendor;
            foreach (var other in new[] { "codex", "agy" })
            {
                names[i].Contains($"{other.ToUpperInvariant()}_AUTH_FILE").Should().Be(vendor == other, $"launch {i} ({vendor}) holds the variable name of its own vendor only");
            }

            names[i].Should().NotContain("CLAUDE_CODE_OAUTH_TOKEN");
        }

        // The end: the tree is clean and holds the one commit chain of the work.
        (await rig.GitAsync(rig.WorktreeOf(job), "status", "--porcelain", "--ignored")).Should().BeEmpty();
        rig.Envelope(job).GetProperty("green").GetBoolean().Should().BeTrue();
    }

    // ---------- d2: zero launches of a refused vendor ----------

    [Theory]
    [MemberData(nameof(CombinationsAndUnmeasured))]
    public async Task D2_AJobWhoseRouteWasNeverNarrowed_NeverLaunchesARefusedVendor_AndGoesOnToTheNextOne(Verdict codex, Verdict agy)
    {
        var set = Set(codex, agy);
        await using var rig = await RigAsync(set);
        var job = rig.NewJob(route: RoleRoute.Format(Full));
        var launchedModels = ExpectedModels(codex, agy);
        foreach (var model in new[] { "sonnet", "opus", "terra", Argon })
        {
            var vendor = Full.First(known => known.Model == model).Vendor;
            var empty = Array.Empty<string>();
            if (model == launchedModels[^1])
            {
                rig.Says(model, Writes(CoderRig.Green, vendor == "claude" ? ClaudeLast : vendor == "codex" ? CodexLast : AgyLast, empty));
            }
            else if (launchedModels.Contains(model))
            {
                rig.Says(model, Falls(model, empty));
            }
            else
            {
                // A refused vendor's model would ANSWER if it were launched: a launch would make the job succeed on it, and be seen in the record.
                rig.Says(model, Writes(CoderRig.Green, Claude1, empty));
            }
        }

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var order = rig.Launches(job).Select(ModelOf).ToArray();
        order.Distinct().Should().Equal(launchedModels, "no model of a refused vendor was launched, and the route went on past it");
        order.Should().NotContain(model => !launchedModels.Contains(model));
        ended.Attempts.Should().OnlyContain(attempt => Expected(codex, agy).Contains(attempt.Vendor) || attempt.Stop.Length > 0, "an attempt on a refused vendor is a refusal, never a launch");
    }

    [Theory]
    [MemberData(nameof(CombinationsAndUnmeasured))]
    public async Task D2_TheStart_RoutesExactlyTheShippedVendors_AndRefusesAModelNamedOnARefusedVendor_BeforeAnyFolder(Verdict codex, Verdict agy)
    {
        var set = Set(codex, agy);
        await using var rig = await CoderRig.CreateAsync(needsLayer: false);

        var answer = Parse(await ServiceFor(rig, set).StartAsync(RoleDescriptors.Coder, rig.Repo, CoderRig.TaskFile, null, null, null, null, null, TestContext.Current.CancellationToken));

        var route = answer.GetProperty("route").EnumerateArray().Select(entry => entry.GetString()!).ToArray();
        route.Select(entry => entry.Split('/')[0]).Distinct().Should().Equal(Expected(codex, agy), "the route holds exactly the shipped vendors, in order");

        foreach (var (vendor, model, verdict) in new[] { ("codex", "terra", codex), ("agy", Argon, agy) })
        {
            await using var fresh = await CoderRig.CreateAsync(needsLayer: false);
            var service = ServiceFor(fresh, set);

            var named = Parse(await service.StartAsync(RoleDescriptors.Coder, fresh.Repo, CoderRig.TaskFile, null, model, null, null, null, TestContext.Current.CancellationToken));

            if (Ships(verdict))
            {
                named.GetProperty("route")[0].GetString().Should().Be($"{vendor}/{model}");
                continue;
            }

            named.GetProperty("error").GetString().Should().StartWith($"the coder cannot run {model} on {vendor}: ");
            fresh.World.Store.JobIds().Should().BeEmpty("a refused start makes no job");
            Directory.Exists(fresh.World.Store.JobsRoot).Should().BeFalse("no folder, no runner");
            Directory.EnumerateFileSystemEntries(fresh.Root).Should().BeEmpty("no worktree was made");
        }
    }

    [Fact]
    public async Task D2_TheProductsOwnTable_HoldsTheVerdictShipAndTheMarkLaunchableOffForCodexAndAgy_SoNothingLaunchesThem()
    {
        await using var rig = await CoderRig.CreateAsync(needsLayer: false);
        var service = ServiceFor(rig, CoderVendors.Shipped);

        foreach (var (vendor, model) in new[] { ("codex", "terra"), ("agy", Argon) })
        {
            var named = Parse(await service.StartAsync(RoleDescriptors.Coder, rig.Repo, CoderRig.TaskFile, null, model, null, null, null, TestContext.Current.CancellationToken));

            named.GetProperty("error").GetString().Should().StartWith($"the coder cannot run {model} on {vendor}: ").And.Contain("not launchable yet").And.NotContain("release", "the refusal is the mark launchable, which is off, never a missing release");
        }

        rig.World.Store.JobIds().Should().BeEmpty();
        CoderVendors.Shipped.Launchable.Should().Equal("claude");
        CoderVendors.Shipped.Table.Vendors.Should().OnlyContain(row => row.Verdict == "ship" && !row.Launchable && row.Measured.Count == 1, "both marks are off, and the history of each measure is there and decides nothing");
    }

    private static string CoderList => """{"coder":[{"model":"sonnet","vendor":"claude"},{"model":"opus","vendor":"claude"},{"model":"terra","vendor":"codex"},{"model":"gemini-4-argon-medium","vendor":"agy"}]}""";

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private sealed class NothingStarted : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, TimedOut: false));
    }

    private static RoleJobService ServiceFor(CoderRig rig, CoderVendorSet set) =>
        new(rig.World.DataDir, name => name switch { "COAI_ROLE_LAUNCHER" => "setsid", "COAI_ROLE_CLAUDE_PERSON_SIGN_IN" => "1", "COAI_ROLES_MODELS" => CoderList, "COAI_CODER_ROOT" => rig.Root, _ => null }, rig.World.Log, _ => true)
        {
            Clock = rig.World.Clock,
            Launcher = new DetachedLauncher(new NothingStarted(), NoRunnerStarted.Succeeds),
            CoderSet = set,
        };

    // ---------- d3: the copy of the vendor that was left ----------

    [Fact]
    public async Task D3_TheCopyOfTheVendorThatWasLeft_IsDeletedBeforeTheUnitOfTheNextVendorStarts()
    {
        var set = Set(Verdict.Ship, Verdict.Ship);
        await using var rig = await RigAsync(set);
        var job = rig.NewJob(route: ["codex/terra", $"agy/{Argon}"]);
        string[] probes = [CopyPath(rig, job, "codex"), CopyPath(rig, job, "agy")];
        rig.Says("terra", Falls("terra", probes, CopyOf("codex")), Falls("terra", probes, CopyOf("codex")))
            .Says(Argon, Writes(CoderRig.Green, AgyLast, probes, CopyOf("agy")));

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var reads = ReadsOf(rig, job);
        reads.Should().HaveCount(2);
        reads[0][CopyPath(rig, job, "codex")].Saw.Should().Be("read", "the first launch of codex finds the copy coai made for it from the real file (s7 t6)");
        reads[1][CopyPath(rig, job, "codex")].Saw.Should().Be("absent", "the copy codex's unit left in the job's home is deleted before agy's unit starts");
        File.Exists(CopyPath(rig, job, "codex")).Should().BeFalse();
        File.Exists(CopyPath(rig, job, "agy")).Should().BeFalse("the copy of the vendor that ran last is deleted when the job ends (s7 t6): no sign-in copy is kept after a job");
    }

    [Fact]
    public async Task D3_ACopyThatCannotBeDeleted_EndsTheJobCoderBoundary_AndTheNextVendorIsNeverStarted()
    {
        var set = Set(Verdict.Ship, Verdict.Ship);
        await using var rig = await RigAsync(set);
        var job = rig.NewJob(route: ["codex/terra", $"agy/{Argon}"]);
        // The vendor leaves a FOLDER where its copy belongs: coai cannot delete it as a file, and the next vendor must not start with it there.
        rig.Says("terra", Falls("terra", [], new Dictionary<string, string> { [".codex/auth.json#folder"] = CodexCopy })).Says(Argon, Writes(CoderRig.Green, AgyLast, []));

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), ended.Reason);
        ended.Reason.Should().Contain("sign-in copy").And.Contain("codex");
        rig.Launches(job).Select(ModelOf).Should().Equal("terra");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task D3_AFirstVendorWhoseUnitIgnoresTheStopSignal_EndsTheJobCoderBoundary_TheNextVendorNeverStarts_AndTheCopyIsNotDeletedUnderALiveUnit(bool inAFixTurn)
    {
        var set = Set(Verdict.Ship, Verdict.Ship);
        await using var rig = await RigAsync(set);
        rig.AttemptTimeout = TimeSpan.FromSeconds(3);
        // TimeoutStopSec is taken out of the coder units (the degrade seam), as an old or odd manager would leave them: the unit that ignores the signal is still alive when coai's wait ends.
        rig.Degrade = arguments => arguments.Any(argument => argument.StartsWith("coai-coder-", StringComparison.Ordinal)) ? WithoutStopTimeout(arguments) : arguments;
        var job = rig.NewJob(route: ["codex/terra", $"agy/{Argon}"]);
        var hangs = new CoderRig.Launch(null, CoderRig.Report(), SleepMs: 300_000, IgnoreTerm: true, HomeFiles: CopyOf("codex"));
        rig.Says("terra", inAFixTurn ? [Writes(CoderRig.Red + " 0", CodexLast, null, CopyOf("codex")), hangs] : [hangs]).Says(Argon, Writes(CoderRig.Green, AgyLast, [CopyPath(rig, job, "codex")], CopyOf("agy")));
        try
        {
            var (_, ended) = await rig.RunAsync(job);

            (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), ended.Reason);
            ended.Reason.Should().Contain("could not be stopped");
            rig.Launches(job).Select(ModelOf).Should().OnlyContain(model => model == "terra", "the next vendor is never started beside a unit that is still alive");
            File.ReadAllText(CopyPath(rig, job, "codex")).Should().Contain(CodexCopy, "the copy of the vendor whose unit may still be alive is not deleted: nothing was reached that deletes it");
            File.Exists(CopyPath(rig, job, "agy")).Should().BeFalse("the next vendor never ran");
            rig.Rig.SwitchLines().Should().BeEmpty();
        }
        finally
        {
            foreach (var unit in await rig.UnitsOfAsync(job))
            {
                await new ProcessLauncher().RunAsync(new ProcessRequest("systemctl", ["--user", "kill", "--signal=SIGKILL", "--kill-whom=all", unit.Replace(".service", string.Empty, StringComparison.Ordinal) + ".service"], rig.Repo) { Timeout = TimeSpan.FromSeconds(20) });
            }

            var until = System.Diagnostics.Stopwatch.StartNew();
            while (until.Elapsed < TimeSpan.FromSeconds(30) && (await rig.UnitsOfAsync(job)).Count > 0)
            {
                await Task.Delay(200, TestContext.Current.CancellationToken);
            }
        }
    }

    private static IReadOnlyList<string> WithoutStopTimeout(IReadOnlyList<string> arguments)
    {
        var list = arguments.ToList();
        var at = list.FindIndex(argument => argument.StartsWith("TimeoutStopSec=", StringComparison.Ordinal));
        if (at > 0)
        {
            list.RemoveRange(at - 1, 2);
        }

        return list;
    }

    // ---------- the installed release of a CLI is a record, never a condition (owner decision of 2026-11-26; SPEC V6) ----------

    [Theory]
    [InlineData("9.9.9 (Claude Code)\n", "9.9.9")]
    [InlineData("2.1.1 (Claude Code)\n", "2.1.1")]
    [InlineData("a banner that names no release\n", "unknown")]
    public async Task AnInstalledReleaseOtherThanTheMeasuredOne_RunsTheJob_AndTheEnvelopeAndTheAttemptNameBothReleases(string printed, string recorded)
    {
        await using var rig = await CoderRig.CreateAsync();
        Environment.SetEnvironmentVariable("FAKECLI_VERSION_STDOUT", printed);
        rig.Says("sonnet", Writes(CoderRig.Green, Claude1));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var cli = rig.Envelope(job).GetProperty("cli");
        (cli.GetProperty("vendor").GetString(), cli.GetProperty("installed").GetString(), cli.GetProperty("lastMeasured").GetString()).Should().Be(("claude", recorded, "2.1.330"));
        (ended.Attempts[0].CliVersion, ended.Attempts[0].LastMeasuredVersion).Should().Be((recorded, "2.1.330"));
    }

    [Fact]
    public async Task AVendorOfTheTable_RecordsItsInstalledReleaseBesideTheLastOneTheTableHolds_AndLaunchesOnAnyOther()
    {
        var set = Set(Verdict.Ship, Verdict.Refuse);
        await using var rig = await RigAsync(set);
        Environment.SetEnvironmentVariable("FAKECLI_VERSION_STDOUT", "7.7.7 (Claude Code)\n");
        var job = rig.NewJob(route: ["codex/terra"]);
        rig.Says("terra", Writes(CoderRig.Green, CodexLast));

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var cli = rig.Envelope(job).GetProperty("cli");
        (cli.GetProperty("vendor").GetString(), cli.GetProperty("installed").GetString(), cli.GetProperty("lastMeasured").GetString()).Should().Be(("codex", "7.7.7", "1.0.0"));
    }

    [Fact]
    public async Task S21_AVendorThatNeverGotAsFarAsItsFolders_FailsOver_NotCoderBoundary()
    {
        var set = Set(Verdict.Ship, Verdict.Ship);
        await using var rig = await RigAsync(set);
        // The first vendor's CLI is not there: nothing of it was ever made in the job's home, and the job goes on to the next vendor.
        rig.Rig.Setting("COAI_ROLE_CODEX_CLI", Path.Combine(rig.Root, "no-such-cli"));
        var job = rig.NewJob(route: ["codex/terra", $"agy/{Argon}"]);
        rig.Says(Argon, Writes(CoderRig.Green, AgyLast, []));

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        rig.Launches(job).Select(ModelOf).Should().Equal(Argon);
        rig.Rig.SwitchLines().Should().ContainSingle();
    }

    [Fact]
    public void S21_ACopyOrAFolderThatIsAbsent_IsNothingToDelete_ButALinkOrAnotherTypeIsNeverTakenForAbsent()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "a symbolic link needs a privilege on Windows");
        using var home = TempDir.For("coai-copy-home-");
        using var outside = TempDir.For("coai-copy-outside-");
        CoderSignInCopy.Remove(home.Path, Path.Combine(home.Path, ".codex", "auth.json")).Should().BeEmpty("the folder was never made");
        Directory.CreateDirectory(Path.Combine(home.Path, ".agy"));
        CoderSignInCopy.Remove(home.Path, Path.Combine(home.Path, ".agy", "auth.json")).Should().BeEmpty("the folder is there and the copy is not");

        Directory.CreateSymbolicLink(Path.Combine(home.Path, ".dangling"), Path.Combine(outside.Path, "not-there"));
        CoderSignInCopy.Remove(home.Path, Path.Combine(home.Path, ".dangling", "auth.json")).Should().NotBeEmpty("a link at the folder's place is not an absent folder, even when it leads nowhere");

        Directory.CreateDirectory(Path.Combine(home.Path, ".inner", "auth.json"));
        CoderSignInCopy.Remove(home.Path, Path.Combine(home.Path, ".inner", "auth.json")).Should().NotBeEmpty("a folder where the copy belongs is another type, not an absent file");
        File.WriteAllText(Path.Combine(home.Path, "plain"), "x");
        CoderSignInCopy.Remove(home.Path, Path.Combine(home.Path, "plain", "auth.json")).Should().NotBeEmpty("a file where the folder belongs is another type, not an absent folder");
    }

    [Fact]
    public void D3_TheDeletionOfACopy_NeverFollowsALinkOutOfTheJobsHome()
    {
        using var home = TempDir.For("coai-copy-home-");
        using var outside = TempDir.For("coai-copy-outside-");
        File.WriteAllText(Path.Combine(outside.Path, "auth.json"), "THE REAL SIGN-IN");
        Directory.CreateSymbolicLink(Path.Combine(home.Path, ".codex"), outside.Path);

        var said = CoderSignInCopy.Remove(home.Path, Path.Combine(home.Path, ".codex", "auth.json"));

        said.Should().NotBeEmpty("a copy whose folder is a link out of the home is not deleted through it");
        File.ReadAllText(Path.Combine(outside.Path, "auth.json")).Should().Be("THE REAL SIGN-IN");
        Directory.CreateDirectory(Path.Combine(home.Path, ".agy"));
        File.WriteAllText(Path.Combine(home.Path, ".agy", "auth.json"), "copy");
        CoderSignInCopy.Remove(home.Path, Path.Combine(home.Path, ".agy", "auth.json")).Should().BeEmpty();
        File.Exists(Path.Combine(home.Path, ".agy", "auth.json")).Should().BeFalse();
        CoderSignInCopy.Remove(home.Path, Path.Combine(home.Path, ".agy", "auth.json")).Should().BeEmpty("no copy is nothing to delete");
        CoderSignInCopy.Remove(home.Path, Path.Combine(outside.Path, "auth.json")).Should().NotBeEmpty("a path outside the home is never deleted");

        // The empty place a bind of a real file leaves is deleted; a file with something in it is not that place and stays.
        var place = Path.Combine(home.Path, ".agy", "auth.json");
        File.WriteAllText(place, "something");
        CoderSignInCopy.Remove(home.Path, place, mountPointOnly: true).Should().NotBeEmpty();
        File.Exists(place).Should().BeTrue();
        File.WriteAllText(place, string.Empty);
        CoderSignInCopy.Remove(home.Path, place, mountPointOnly: true).Should().BeEmpty();
        File.Exists(place).Should().BeFalse();
    }

    // ---------- d3: the count is the job's, across vendors ----------

    [Fact]
    public async Task D3_TheFixTurnCountStaysTheCountOfTheJob_WhenTheJobMovesToAnotherVendor_AndAnother()
    {
        var set = Set(Verdict.Ship, Verdict.Ship);
        await using var rig = await RigAsync(set);
        var job = rig.NewJob(route: ["claude/sonnet", "codex/terra", $"agy/{Argon}"]);
        // sonnet: turn 0 red, one fix turn red, a second fix turn that crashes (two turns of the job); codex: turn 0 red, ONE fix turn that crashes (the third); agy: turn 0 red and no fix turn is left.
        rig.Says("sonnet", Writes(CoderRig.Red + " 0", Claude1), Writes(CoderRig.Red + " 1", string.Empty), Falls("sonnet", []))
            .Says("terra", Writes(CoderRig.Red + " 3", CodexLast), Falls("terra", []))
            .Says(Argon, Writes(CoderRig.Red + " 5", AgyLast), Writes(CoderRig.Red + " 6", AgyLast));

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "not-green"), ended.Reason);
        rig.Launches(job).Select(ModelOf).Should().Equal("sonnet", "sonnet", "sonnet", "terra", "terra", Argon);
        var envelope = rig.Envelope(job);
        envelope.GetProperty("fixTurns").GetInt32().Should().Be(3, "the cap is the job's: two turns of the first vendor, one of the second, none left for the third");
        envelope.GetProperty("fixRounds").GetProperty("cap").GetInt32().Should().Be(3);
    }

    // ---------- d3: a spent quota ----------

    private static string SpentQuota => RoleStopFixtures.Load("claude-spend-limit-429-envelope").StdOut;

    [Fact]
    public async Task D3_ASpentQuotaOfClaude_SkipsTheOtherClaudeModels_AndTheJobGoesOnToTheNextVendor()
    {
        var set = Set(Verdict.Ship, Verdict.Ship);
        await using var rig = await RigAsync(set);
        var job = rig.NewJob(route: RoleRoute.Format(Full));
        rig.Says("sonnet", Falls("sonnet", []) with { Stdout = SpentQuota }).Says("opus", Writes(CoderRig.Green, Claude1)).Says("terra", Writes(CoderRig.Green, CodexLast));

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        rig.Launches(job).Select(ModelOf).Should().Equal("sonnet", "terra");
    }

    [Fact]
    public async Task D3_ASpentQuotaOfTheMiddleVendor_SkipsOnlyItsOtherModels_AndTheLastVendorStillRuns()
    {
        var set = Set(Verdict.Ship, Verdict.Ship);
        await using var rig = await RigAsync(set);
        var job = rig.NewJob(route: ["claude/sonnet", "codex/terra", "codex/astra", $"agy/{Argon}"]);
        rig.Says("sonnet", Falls("sonnet", [])).Says("terra", Falls("terra", []) with { Stdout = SpentQuota }).Says("astra", Writes(CoderRig.Green, CodexLast)).Says(Argon, Writes(CoderRig.Green, AgyLast));

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        rig.Launches(job).Select(ModelOf).Should().Equal("sonnet", "terra", Argon);
    }

    [Fact]
    public async Task M9_ATokenOfTheCopy_PrintedInTheAnswer_EndsTheJobCoderBoundary_NamesAnId_AndIsInNoFileOfTheJobsRecord()
    {
        const string Token = "made-up-printed-access-token-xyz";
        var recipe = new CoderSignInRecipe.CopyForJob(".codex/auth.json", ["tokens/access_token"], new Dictionary<string, string>(), string.Empty, "gate") { TokenFields = ["tokens/access_token"] };
        var codexVendor = new CrossVendor("codex", recipe);
        var set = Set(Verdict.Ship, Verdict.Refuse, codexVendor, new CrossVendor("agy"));
        await using var rig = await RigAsync(set);
        rig.RealHomeFile(".codex/auth.json", $"{{\"tokens\":{{\"access_token\":\"{Token}\"}}}}");
        var job = rig.NewJob(route: ["codex/terra"]);
        // The coder reads its own copy and puts the token into the notes of an otherwise valid report.
        rig.Says("terra", new CoderRig.Launch(new Dictionary<string, string> { [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report(notes: "debug: " + Token), Session: CodexLast));

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), ended.Reason);
        ended.Reason.Should().Contain("sign-in-codex-1").And.NotContain(Token);
        var stored = string.Concat(Directory.EnumerateFiles(rig.World.Store.FolderOf(job.JobId)!.Location, "*", SearchOption.AllDirectories).Select(File.ReadAllText));
        stored.Should().NotContain(Token, "the token is in no file of the job's record: not a turn file, not the envelope, not the result");
        stored.Should().Contain("[sign-in copy sign-in-codex-1]", "the mark names the copy, so the record says whose token was printed").And.NotContain("[claude key", "the mark of a vendor that is not claude does not say claude");
    }

    /// <summary>
    /// A vendor that stands in for a real one, with an id the route knows: claude's argv, probe and unit (FakeCli answers as claude), a prompt of its own, a variable NAME of its own in the unit's environment (<c>&lt;ID&gt;_AUTH_FILE</c>, the path of the sign-in copy it would
    /// keep in the job's home), a recipe that says it is a COPY for the job, and a check of its stream that records the session every turn was expected to continue.
    /// </summary>
    private sealed class CrossVendor(string id, CoderSignInRecipe? recipe = null) : ICoderVendor
    {
        public List<string> Expected { get; } = [];

        public string Id => id;

        public string PromptId => $"coder-cross-{id}";

        public string PromptResource => CoderVendors.Claude.PromptResource;

        public CoderSignInRecipe SignIn => recipe ?? new CoderSignInRecipe.CopyForJob($".{id}/auth.json", [], new Dictionary<string, string>(), string.Empty, "gate");

        public IConsultantRuntime Runtime => new ClaudeConsultant(new ClaudeRuntime(), id);

        public string FixClause => "you run no command of your own";

        public bool PointsAtInstructionBlock => true;

        public CoderTurnVerdict CheckTurn(ProcessResult process, string expectedSession)
        {
            Expected.Add(expectedSession);
            var said = Runtime.ReadHandle(process);

            return expectedSession.Length > 0 && said.Length > 0 && said != expectedSession ? new CoderTurnVerdict.Crash("the session of the turn is not the handle's") : new CoderTurnVerdict.Fine();
        }

        public Task<CoderCapability> ProbeAsync(IProcessLauncher launcher, string executable, string workingDirectory, CancellationToken ct) => CoderVendors.Claude.ProbeAsync(launcher, executable, workingDirectory, ct);

        public IReadOnlyList<string> FreshArgv(string model, string worktree) => CoderVendors.Claude.FreshArgv(model, worktree);

        public IReadOnlyList<string> ResumedArgv(string model, string worktree, string sessionId) => CoderVendors.Claude.ResumedArgv(model, worktree, sessionId);

        public IReadOnlyList<string> UnitProperties(CoaiMcp.Runners.Toolchain.BoundaryHost host, CoaiMcp.Runners.Toolchain.BoundaryJob job, CoaiMcp.Runners.Toolchain.CoderProgram program, TimeSpan runtimeMax) => CoaiMcp.Runners.Toolchain.CoderUnit.Properties(host, job, program, runtimeMax, id); //erties(host, job, program, runtimeMax);

        public string WorkingDirectory(CoaiMcp.Runners.Toolchain.BoundaryJob job) => CoderVendors.Claude.WorkingDirectory(job);

        public IReadOnlyList<KeyValuePair<string, string>> Environment(CoaiMcp.Runners.Toolchain.BoundaryHost host, CoaiMcp.Runners.Toolchain.BoundaryJob job) =>
            [.. CoaiMcp.Runners.Toolchain.CoderUnit.Environment(host, job, id), new($"{id.ToUpperInvariant()}_AUTH_FILE", Path.Combine(job.CoderHomeOf(id), $".{id}", "auth.json"))];

        public CoderHomeLayout Home(CoaiMcp.Runners.Toolchain.BoundaryJob job) =>
            new(job.CoderHomeOf(id), Path.Combine(job.CoderHomeOf(id), $".{id}", "auth.json"), [.. job.CoderFoldersOf(id), Path.Combine(job.CoderHomeOf(id), $".{id}")]);

        public CoaiMcp.Runners.Toolchain.CoderProgram Program(string executable, string path, CoaiMcp.Runners.Toolchain.BoundaryJob job) => CoderVendors.Claude.Program(executable, path, job);
    }
}
