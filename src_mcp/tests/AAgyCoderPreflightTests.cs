using System.Text.Json;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s6 t4 (V9, V10; judgement section 8, class <c>pre</c>): the preflight of an agy coder job — with no model, inside the exact launch unit, one row for each id of class <c>pre</c> of the table (the unit U1 to U9, the working directory AG-B1, the sign-in AG-R1 and AG-R2, the guard folder AG-B3, and the guard
/// handler through the exact command of <c>hooks.json</c>, AG-G1 to AG-G3). A wrong answer of any row refuses the job before any launch and names the row; each row goes red when its property or its part of the guard is dropped.
/// </summary>
/// <remarks>Real units (a user manager is needed: skipped with the reason without one). The handler is FakeCli running the product's guard code; a real agy is never started.</remarks>
[Collection("fakecli-env")]
public sealed class AAgyCoderPreflightTests
{
    private static string Job => "r20261126101010-0000000a";

    private static async Task<(CoderBed Bed, AgyCoderVendor Vendor)> BedAsync(Action<BoundaryJob>? afterPrepare = null)
    {
        Assert.SkipUnless(SystemdUser.Works(out var why), why);
        var bed = await CoderBed.CreateAsync();
        var signIn = Path.Combine(bed.RealHome, AgyCoderUnit.SignInRelative);
        Directory.CreateDirectory(Path.GetDirectoryName(signIn)!);
        await File.WriteAllTextAsync(signIn, AgyCoderBed.SignInFile(DateTime.UtcNow.AddHours(2)));

        return (bed, new AgyCoderVendor(new HookHandler(RoleJobWorld.FakeCli, [])) { AfterPrepare = afterPrepare });
    }

    private static ProcessRequest Request(CoderBed bed, string program, params string[] arguments) =>
        new(program, arguments, AgyCoderUnit.GuardFolder(bed.Job)) { Timeout = TimeSpan.FromSeconds(60), InheritsEnvironment = false, Passthrough = ProcessEnvironment.Minimal };

    private static async Task<CoderTurnOutcome> RunAsync(CoderBed bed, AgyCoderVendor vendor, Func<IReadOnlyList<string>, IReadOnlyList<string>>? degrade = null, string program = "/bin/sh", BoundaryHost? host = null)
    {
        var turns = new CoderTurns(new CoderBoundary(bed.Launcher, host ?? bed.Host) { Vendor = vendor, Degrade = degrade }, bed.Evidence);

        return await turns.RunAsync(bed.Job, Request(bed, program, program == "/bin/sh" ? ["-c", "echo started"] : ["emit", "started"]), "c1", vendor, TestContext.Current.CancellationToken, new SignInJobs());
    }

    // ---------- d1: one row for each id, in the exact launch unit ----------

    [Fact]
    public async Task D1_AHealthyJob_HasEveryRow_AndEveryRowHolds()
    {
        var (bed, vendor) = await BedAsync();
        await using var _ = bed;

        var outcome = await RunAsync(bed, vendor);

        var run = outcome.Should().BeOfType<CoderTurnOutcome.Finished>(outcome is CoderTurnOutcome.Refused refused ? refused.Sentence : string.Empty).Subject.Run;
        var rows = run.Record.Preflight;
        rows.Select(row => row.Item).Should().Contain(AgyCoderPreflight.Items, "one row for each of U1 to U9, AG-B1, AG-R1, AG-R2, AG-B3, AG-G1, AG-G2 and AG-G3");
        rows.Where(row => AgyCoderPreflight.Items.Contains(row.Item)).Should().OnlyContain(row => row.Held, string.Join("; ", rows.Where(row => !row.Held).Select(row => $"{row.Item}: {row.Detail}")));
    }

    [Fact]
    public async Task D1_ThePreflightRunsInTheExactLaunchUnit_TheSamePropertiesAndTheSameWorkingDirectory()
    {
        var (bed, vendor) = await BedAsync();
        await using var _ = bed;
        var seen = new List<IReadOnlyList<string>>();

        await RunAsync(bed, vendor, arguments =>
        {
            seen.Add(arguments);

            return arguments;
        });

        static IReadOnlyList<string> Shape(IReadOnlyList<string> arguments)
        {
            // Everything before the program: the working directory and every property, but the unit's own name and its deadline (the preflight has its own).
            var shape = new List<string>();
            var list = arguments.TakeWhile(argument => argument != "--").ToList();
            for (var at = 0; at < list.Count; at++)
            {
                if (list[at] == "--unit" || (list[at] == "-p" && list[at + 1].StartsWith("RuntimeMaxSec=", StringComparison.Ordinal)))
                {
                    at++;

                    continue;
                }

                shape.Add(list[at]);
            }

            return shape;
        }

        string UnitOf(IReadOnlyList<string> arguments) => arguments[arguments.ToList().IndexOf("--unit") + 1];
        var pre = seen.Single(arguments => UnitOf(arguments).EndsWith("-pre", StringComparison.Ordinal) && UnitOf(arguments).StartsWith("coai-coder-", StringComparison.Ordinal));
        var launch = seen.Single(arguments => UnitOf(arguments).EndsWith("-c1", StringComparison.Ordinal) && UnitOf(arguments).StartsWith("coai-coder-", StringComparison.Ordinal));

        Shape(pre).Should().Equal(Shape(launch));
        var at = pre.ToList().IndexOf("--working-directory");
        pre[at + 1].Should().Be(AgyCoderUnit.GuardFolder(bed.Job));
        launch[launch.ToList().IndexOf("--working-directory") + 1].Should().Be(AgyCoderUnit.GuardFolder(bed.Job));
    }

    // ---------- d3: the rows and the ids of the table are equal; each row goes red ----------

    [Fact]
    public void D3_ThePreIdsOfTheTableForAgy_AndTheRows_AreEqual_AnIdWithNoRowAndARowWithNoIdBothFail()
    {
        var table = CoderBoundaryTable.Shipped;
        var preIds = table.PropertiesOf("agy").Where(property => property.Class == "pre" || property.Also.Contains("pre")).Select(property => property.Id).Order(StringComparer.Ordinal).ToList();
        var rowIds = AgyCoderPreflight.RowIds.Values.Concat(AgyCoderPreflight.CheckRows.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        rowIds.Should().Equal(preIds, "an id with no row and a row with no id both fail");
        AgyCoderPreflight.CheckRows.Values.Should().OnlyContain(item => BoundaryPreflight.Items.Contains(item), "K1 and K2 are rows of the CHECK preflight, and they exist there");
        AgyCoderPreflight.Items.Should().OnlyHaveUniqueItems();
        AgyCoderPreflight.Items.Should().Equal([.. AgyCoderPreflight.RowIds.Keys, .. AgyCoderPreflight.ExtraItems]);
        AgyCoderPreflight.ExtraItems.Should().Equal("keys-folder-hidden", "sign-in-not-in-unit", "other-vendor-folder-hidden");
    }

    [Fact]
    public void D3_TheCompanion_ADroppedIdOrAnExtraRow_IsSeenByTheComparison()
    {
        string[] preIds = ["U1", "U2"];
        string[] rows = ["U1"];
        string[] extra = ["U1", "U2", "U3"];

        rows.Should().NotEqual(preIds);
        extra.Should().NotEqual(preIds);
    }

    public static TheoryData<string> Rows() => [.. AgyCoderPreflight.Items];

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task D3_EachRow_GoesRed_WhenItsPropertyOrItsPartOfTheGuardIsDropped_AndTheJobIsRefusedNamingTheRow(string item)
    {
        Assert.SkipWhen(item == "no-user-manager-inside" && Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is null, "no XDG_RUNTIME_DIR in this process, so no bus path to hand a degraded unit");
        Action<BoundaryJob>? tamper = item switch
        {
            "sign-in-is-the-jobs-copy" => job => Loosen(AgyCoderUnit.SignInFile(job)),
            "sign-in-copy-has-no-refresh-token-and-the-margin" => job => File.WriteAllText(AgyCoderUnit.SignInFile(job), File.ReadAllText(AgyCoderUnit.SignInFile(job)).Replace("\"auth_method\"", "\"refresh_token\":\"x\",\"auth_method\"", StringComparison.Ordinal)),
            "guard-files-are-the-expected-bytes" => job => File.AppendAllText(Path.Combine(AgyCoderUnit.GuardFolder(job), ".agents", "hooks.json"), " "),
            _ => null,
        };
        var (bed, vendor) = await BedAsync(tamper);
        await using var _ = bed;
        if (item == "other-job-folder-hidden-from-the-coder")
        {
            // The layout of a real coder-jobs folder: this job's folder beside another job's, so the cover over the holding folder is what hides the stand-in.
            bed.Inner.UseJobsFolderWithAnotherJob();
        }

        BoundaryHost? host = null;
        if (item == "other-vendor-folder-hidden")
        {
            bed.Inner.UseJobsFolderWithAnotherJob();
        }

        if (item == "keys-folder-hidden")
        {
            // The pool's folder where the unit would see it unless the property hides it: inside the worktree, which every unit binds.
            var keys = Path.Combine(bed.Worktree, ".keys-probe");
            Directory.CreateDirectory(keys);
            await File.WriteAllTextAsync(Path.Combine(keys, "key.txt"), "made-up-key-text\n");
            bed.Inner.UseKeys(keys);
        }

        if (item == "sign-in-not-in-unit")
        {
            // A home that is NOT under a hidden folder (inside the worktree): the person's claude sign-in file lies there unless the property hides it.
            host = bed.Host with { RealHome = StandInHome(bed) };
        }

        var launches = bed.Launcher.Requests.Count;
        var program = item == "program-cannot-be-written" ? RoleJobWorld.FakeCli : "/bin/sh";

        var outcome = await RunAsync(bed, vendor, Degrade(item, bed), program, host);

        var refused = outcome.Should().BeOfType<CoderTurnOutcome.Refused>($"the preflight must see it: {outcome}").Subject;
        // The sentence names the FIRST row that said no; the row of this item is among those that did (dropping the handler's folder fails both rows of the guard that ask it).
        refused.Sentence.Should().Contain(refused.Record.Preflight.First(row => !row.Held).Item).And.Contain("refused before the coder was launched");
        // The Windows program row can only say no on a host with interop; where there is none, its sibling rows of the same id (U5) are what see the dropped property.
        var same = item == "windows-exe" ? AgyCoderPreflight.RowIds.Where(pair => pair.Value == "U5").Select(pair => pair.Key).ToList() : [item];
        refused.Record.Preflight.Where(row => same.Contains(row.Item)).Should().Contain(row => !row.Held, $"{item} said no");
        bed.Launcher.Requests.Skip(launches).SelectMany(request => request.Arguments).Should().NotContain(argument => argument.Contains("coai-coder-" + bed.JobId + "-c1", StringComparison.Ordinal), "no launch followed");
    }

    private static void Loosen(string file)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        }
    }

    /// <summary>A stand-in for the person's home inside the worktree, with a claude sign-in file and an agy one in it (made up).</summary>
    private static string StandInHome(CoderBed bed)
    {
        var home = Path.Combine(bed.Worktree, "person-home");
        Directory.CreateDirectory(Path.Combine(home, ".claude"));
        Directory.CreateDirectory(Path.Combine(home, ".gemini", "antigravity-cli"));
        File.WriteAllText(Path.Combine(home, ".claude", ".credentials.json"), CoderBed.SignInLine + "\n");
        File.WriteAllText(Path.Combine(home, AgyCoderUnit.SignInRelative), AgyCoderBed.SignInFile(DateTime.UtcNow.AddHours(2)));

        return home;
    }

    [Fact]
    public async Task M12_TheKeysAndTheOtherVendorRows_Hold_WhenTheKeyFolderIsNamedAndHidden()
    {
        var (bed, vendor) = await BedAsync();
        await using var _ = bed;
        var keys = Path.Combine(bed.Worktree, ".keys-probe");
        Directory.CreateDirectory(keys);
        await File.WriteAllTextAsync(Path.Combine(keys, "key.txt"), "made-up-key-text\n");
        bed.Inner.UseKeys(keys);
        bed.Inner.UseJobsFolderWithAnotherJob();

        var outcome = await RunAsync(bed, vendor);

        var rows = outcome.Should().BeOfType<CoderTurnOutcome.Finished>(outcome is CoderTurnOutcome.Refused refused ? refused.Sentence : string.Empty).Subject.Run.Record.Preflight;
        foreach (var item in AgyCoderPreflight.ExtraItems)
        {
            rows.Single(row => row.Item == item).Held.Should().BeTrue(item);
        }

        rows.Single(row => row.Item == "keys-folder-hidden").Detail.Should().Contain("cannot be read", "the row looked at a key folder that IS named, not at none");
    }

    private static Func<IReadOnlyList<string>, IReadOnlyList<string>> Degrade(string item, CoderBed bed) => arguments =>
    {
        var job = bed.Job;
        var guard = AgyCoderUnit.GuardFolder(job);

        return item switch
        {
            "worktree-write" => Drop(arguments, $"BindPaths={bed.Worktree}"),
            "main-hidden" => Replace(arguments, $"TemporaryFileSystem={bed.Main}", $"BindReadOnlyPaths={bed.Main}"),
            "coder-root-hidden" => Replace(arguments, $"TemporaryFileSystem={bed.CoderRoot}", $"BindReadOnlyPaths={bed.CoderRoot}"),
            "git-readonly" => Drop(arguments, $"BindReadOnlyPaths={bed.Worktree}/.git"),
            "no-user-manager-inside" => Drop(Drop(Drop(arguments, "-i"), "TemporaryFileSystem=/run/user"), "ProtectHome=tmpfs"),
            "windows-exe" => Drop(arguments, "InaccessiblePaths=-/run/WSL"),
            "no-sockets-no-interop" => Drop(arguments, "TemporaryFileSystem=/mnt"),
            "home-is-the-jobs" => Replace(arguments, $"HOME={AgyCoderUnit.HomeFolder(job)}", $"HOME={bed.RealHome}"),
            "tmp-is-the-jobs" => Drop(arguments, $"BindPaths={job.CoderTempOf("agy")}:{BoundaryUnit.UnitTemp}"),
            "environment-is-the-allow-list" => Insert(arguments, "/usr/bin/env", "EXTRA_NAME=1"),
            "other-job-folder-hidden-from-the-coder" or "other-vendor-folder-hidden" => Replace(arguments, $"TemporaryFileSystem={job.JobCover}:ro", $"BindReadOnlyPaths={job.JobCover}"),
            "keys-folder-hidden" => Drop(arguments, $"InaccessiblePaths=-{job.KeysFolder}"),
            "sign-in-not-in-unit" => Drop(arguments, $"InaccessiblePaths=-{BoundaryJob.RealOrSpelled(Path.Combine(StandInHome(bed), ".claude", ".credentials.json"))}"),
            "program-cannot-be-written" => Drop(arguments, $"BindReadOnlyPaths={Path.GetDirectoryName(RoleJobWorld.FakeCli)}"),
            "cwd-is-the-guard-folder" => Replace(arguments, guard, bed.Worktree),
            "guard-folder-readonly" => Drop(arguments, $"BindReadOnlyPaths={guard}"),
            "guard-answers-each-case" or "guard-answers-empty-and-garbage" => Drop(arguments, $"BindReadOnlyPaths={Path.GetDirectoryName(RoleJobWorld.FakeCli)}"),
            _ => arguments,
        };
    };

    /// <summary>The list without that argument — and without the <c>-p</c> before it when it is a property.</summary>
    private static string[] Drop(IReadOnlyList<string> arguments, string what)
    {
        var at = arguments.ToList().IndexOf(what);
        at.Should().BeGreaterThan(-1, $"the degrade must find '{what}' in the real argument list, or it proves nothing");
        var drop = at > 0 && arguments[at - 1] == "-p" ? 2 : 1;

        return [.. arguments.Take(at - (drop - 1)), .. arguments.Skip(at + 1)];
    }

    private static string[] Replace(IReadOnlyList<string> arguments, string what, string with)
    {
        var at = arguments.ToList().IndexOf(what);
        at.Should().BeGreaterThan(-1, $"the degrade must find '{what}' in the real argument list, or it proves nothing");

        return [.. arguments.Take(at), with, .. arguments.Skip(at + 1)];
    }

    private static string[] Insert(IReadOnlyList<string> arguments, string after, string item)
    {
        var at = arguments.ToList().IndexOf(after);
        at.Should().BeGreaterThan(-1);

        return [.. arguments.Take(at + 2), item, .. arguments.Skip(at + 2)];
    }

    // ---------- d4: what the rows do not prove ----------

    [Fact]
    public void D4_TheEnvelopeSaysWhatTheRowsDoNotProve_AsMeasured_NeverAsProbedBeforeTheJob()
    {
        var said = AgyCoderPreflight.NotProved;

        said.Should().Contain("AG-G4").And.Contain("AG-A1").And.Contain("AG-B2").And.Contain("AG-M4");
        said.Should().Contain("measured").And.Contain("beside the installed one").And.NotContain("probed");
        AgyCoderPreflight.RowIds.Values.Should().NotContain(["AG-G4", "AG-A1", "AG-B2", "AG-M4", "AG-M1", "AG-M2", "AG-G5", "AG-A2"], "a release id has no row: a row for it could only check coai's own half");
    }

    // ---------- d5: V10 ----------

    [Fact]
    public async Task D4_TheEnvelopeOfAJob_SaysIt_WithTheReleaseItWasMeasuredOnBesideTheInstalledOne()
    {
        await using var rig = await AgyCoderBed.RigAsync();
        rig.SaysEntries(AgyCoderBed.Model, AgyCoderBed.Launch([AgyCoderBed.Writes(CoderRig.Green)], conversation: AgyCoderBed.Conversation));
        var job = rig.NewJob(route: [$"agy/{AgyCoderBed.Model}"]);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(CoaiMcp.Server.RoleJobs.RoleJobStates.Succeeded, ended.Reason);
        var cli = rig.Envelope(job).GetProperty("cli");
        cli.GetProperty("notProvedByPreflight").GetString().Should().Be(AgyCoderPreflight.NotProved);
        (cli.GetProperty("installed").GetString(), cli.GetProperty("lastMeasured").GetString()).Should().Be(("1.5.18", "1.5.18"));
    }

    [Theory]
    [InlineData("windows")]
    [InlineData("macos")]
    public async Task D5_NativeWindowsAndMacOs_AreRefusedBeforeAnyProcessStarts(string platform)
    {
        var (bed, vendor) = await BedAsync();
        await using var _ = bed;
        var launches = bed.Launcher.Requests.Count;
        var host = bed.Host with { Platform = platform == "windows" ? BoundaryPlatform.Windows : BoundaryPlatform.MacOs };

        var outcome = await RunAsync(bed, vendor, host: host);

        outcome.Should().BeOfType<CoderTurnOutcome.Refused>().Which.Sentence.Should().Contain(platform == "windows" ? "native Windows" : "macOS");
        bed.Launcher.Requests.Skip(launches).Should().BeEmpty("no process was started");
    }

    [Fact]
    public async Task D5_NoUserManager_IsRefusedBeforeTheCoderStarts()
    {
        var (bed, vendor) = await BedAsync();
        await using var _ = bed;

        var outcome = await RunAsync(bed, vendor, host: bed.Host with { SystemdRun = Path.Combine(Path.GetFullPath("nowhere"), "no-such-systemd-run") });

        var refused = outcome.Should().BeOfType<CoderTurnOutcome.Refused>().Subject;
        refused.Record.Preflight.Should().Contain(row => row.Item == "user-manager" && !row.Held);
        bed.Launcher.Requests.SelectMany(request => request.Arguments).Should().NotContain(argument => argument.Contains("coai-coder-" + bed.JobId + "-c1", StringComparison.Ordinal));
    }
}
