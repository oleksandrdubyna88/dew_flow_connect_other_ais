using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;
using static CoaiMcp.Tests.Leftovers.SweepPlants;

namespace CoaiMcp.Tests.Leftovers;

/// <summary>
/// The start-of-run sweep as coai wires it (E5 S2-B, <c>todo/PLAN_test_leftovers_E5_S2_spec.md</c> B.4 B3-B7): the root it sweeps, how a fresh run folder and this process's own claim survive it (in both languages), which names it judges and which it
/// never looks at, and how it lists the legacy names. The copied <see cref="LeftoversSweepTests"/> hold the sweep's own scenarios; nothing of them is repeated here.
/// </summary>
/// <remarks>
/// Every private root is an owned scope's folder, never the machine's temp root; the plants and helpers are <see cref="SweepPlants"/>. Split from one 839-line file ([S2-B code1 1]); the spec's B-numbers are the same.
/// </remarks>
public sealed class StartOfRunSweepTests : IDisposable
{
    /// <summary>On macOS every test here stands on the identity read, so the layout self-test must hold first.</summary>
    private readonly OwnedScope _owned = DarwinLayout.FreshOwnedScope();

    public void Dispose() => _owned.Dispose();

    // ---- B3 -------------------------------------------------------------------------------------------------------

    [Fact]
    public void TheSweepRoot_IsTheMachineTempRoot()
    {
        // (a) the real run: the fixture handed the sweep the machine's root, which is the run folder's PARENT.
        StartOfRunSweep.LastRoot.Should().Be(RunTemp.Real, "the real sweep ran over the machine's temp root");
        RunTemp.Real.Should().NotBe(RunTemp.Folder, "the run folder is INSIDE the machine's root");
        Trimmed(Owned.TempRoot()).Should().Be(RunTemp.Folder, "the template's unedited root, the temp path, is the run folder: a sweep over it never sees a killed run's folder");

        // (b) the sweep is depth 1: a folder of the root is judged, a folder INSIDE a run folder is never looked at.
        var root = _owned.Dir("b3-root");
        var dead = Dead();
        var inner = PlantFolder(root, Name("inner"), Rec(Environment.ProcessId));
        var g2 = PlantFolder(inner, Name("g2"), Rec(dead));
        AgeFolder(inner);
        var g1 = PlantFolder(root, Name("g1"), Rec(dead));

        var result = LeftoversSweep.Run(Options(root, now: DateTimeOffset.UtcNow + Old));

        result.Removed.Should().Equal(g1);
        Directory.Exists(g2).Should().BeTrue("a folder inside a run folder is not the root's entry: the sweep never goes down");
        File.Exists(g2 + ".owner").Should().BeTrue();
        ReasonOf(result, inner).Should().Be("live");
        result.Errors.Should().BeEmpty();
        UnmatchablePid.AssertNoSuchProcess(dead);
    }

    // ---- B4 -------------------------------------------------------------------------------------------------------

    [Fact]
    public void AFreshRunFolder_Survives_TheSweep_ByAgeAndByLiveness()
    {
        var root = _owned.Dir("b4-root");
        var variables = new Dictionary<string, string?>();
        var seams = new RunTempSeams((name, value) => variables[name] = value, name => variables.GetValueOrDefault(name)) { Warn = _ => { } };
        var run = RunTempFolder.Create(root, seams);
        var claimBefore = File.ReadAllBytes(run.ClaimFile);
        try
        {
            // (a) the real clock: young, so the folder is kept as `young` (no listed line) and its claim is not mentioned.
            var young = LeftoversSweep.Run(new SweepOptions(root, DateTimeOffset.UtcNow, LeftoversSweep.MinAge, Liveness.ProbeProcess, Scope.IsMine));

            young.Removed.Should().BeEmpty();
            young.Preserved.Should().NotContain(p => p.Path == run.ClaimFile, "a claim whose folder exists is silent");
            ReasonOf(young, run.Folder).Should().Be("young", "the copy keeps a young folder as preserved, with no listed line, and never removes it");
            young.Listed.Should().NotContain(line => line.Contains(Path.GetFileName(run.Folder), StringComparison.Ordinal));

            // (b) past the window: age no longer protects it, liveness does.
            var late = LeftoversSweep.Run(new SweepOptions(root, DateTimeOffset.UtcNow + Old, LeftoversSweep.MinAge, Liveness.ProbeProcess, Scope.IsMine));

            late.Removed.Should().BeEmpty("the folder's owner is this live process");
            var reason = ReasonOf(late, run.Folder);
            string[] accepted = OperatingSystem.IsWindows() ? ["live", "foreign-uid"] : ["live"];
            reason.Should().BeOneOf(accepted, "a live owner keeps its folder (on the hosted Windows runner the new folder is Administrators', so the scope key says foreign-uid first)");
            TestContext.Current.AddWarning($"E5 S2-B B4: the fresh run folder past the window is kept: {reason}");
            Directory.Exists(run.Folder).Should().BeTrue();
            File.ReadAllBytes(run.ClaimFile).Should().Equal(claimBefore, "the claim is the bytes it was written with");
        }
        finally
        {
            run.Restore();
        }

        Directory.Exists(run.Folder).Should().BeFalse("Restore removes the folder");
        File.Exists(run.ClaimFile).Should().BeFalse("and its claim, last");
    }

    // ---- B5 -------------------------------------------------------------------------------------------------------

    [Fact]
    public void TheSweep_NeverTouchesTheCurrentProcessOwnClaim_BetweenClaimAndFolder()
    {
        var root = _owned.Dir("b5-root");
        var variables = new Dictionary<string, string?>();
        var seen = new List<(string Arm, bool ClaimThere, bool FolderThere, bool SameBytes, int Removed, bool QuarantineMade, string? Reason)>();
        byte[]? claimBytes = null;

        void Observe(string arm, string claim, SweepResult result)
        {
            seen.Add((arm, File.Exists(claim), Directory.Exists(claim[..^".owner".Length]),
                File.Exists(claim) && File.ReadAllBytes(claim).AsSpan().SequenceEqual(claimBytes), result.Removed.Count,
                Directory.GetFileSystemEntries(root).Any(entry => Path.GetFileName(entry).Contains(QuarantineWord, StringComparison.OrdinalIgnoreCase)), ReasonOf(result, claim)));
        }

        var seams = new RunTempSeams((name, value) => variables[name] = value, name => variables.GetValueOrDefault(name))
        {
            Warn = _ => { },
            AfterClaim = claim =>
            {
                claimBytes = File.ReadAllBytes(claim);
                seen.Add(("premise", File.Exists(claim), Directory.Exists(claim[..^".owner".Length]), true, 0, false, null));
                Observe("a: real clock", claim, LeftoversSweep.Run(new SweepOptions(root, DateTimeOffset.UtcNow, LeftoversSweep.MinAge, Liveness.ProbeProcess, Scope.IsMine)));
                Observe("b: past the window", claim, LeftoversSweep.Run(new SweepOptions(root, DateTimeOffset.UtcNow + Old, LeftoversSweep.MinAge, Liveness.ProbeProcess, Scope.IsMine)));
            },
        };

        var run = RunTempFolder.Create(root, seams);
        try
        {
            using var all = new AssertionScope();
            seen.Select(s => s.Arm).Should().Equal("premise", "a: real clock", "b: past the window");
            seen[0].Should().Be(("premise", true, false, true, 0, false, null), "inside the hook the claim is written and the folder is not made: that is the window the sweep must survive");
            foreach (var (arm, claimThere, folderThere, sameBytes, removed, quarantineMade, _) in seen.Skip(1))
            {
                claimThere.Should().BeTrue($"{arm}: the claim is still there");
                folderThere.Should().BeFalse($"{arm}: the sweep made no folder");
                sameBytes.Should().BeTrue($"{arm}: the claim has the bytes it was written with");
                removed.Should().Be(0, arm);
                quarantineMade.Should().BeFalse($"{arm}: nothing was renamed");
            }

            string[] accepted = OperatingSystem.IsWindows() ? ["live", "foreign-uid"] : ["live"];
            seen[2].Reason.Should().BeOneOf(accepted, "past the window only liveness (or, on the hosted Windows runner, the scope key) keeps the claim");
            TestContext.Current.AddWarning($"E5 S2-B B5: the own claim between claim and folder, past the window: {seen[2].Reason}");
            Directory.Exists(run.Folder).Should().BeTrue("Create completed: the folder exists");
            File.Exists(run.ClaimFile).Should().BeTrue("and its claim");
        }
        finally
        {
            run.Restore();
        }
    }

    /// <summary>
    /// B5 (c), the real cross-process case: while THIS process's claim is on disk and its folder is not, the extension runner's sweep — the MOUNTED Node reference through coai's one module
    /// (<c>src_vs_code/scripts/owned.mjs</c>), a second process — judges the root past the window. It removes nothing and names the claim <c>live</c>. Skipped, with the reason, without <c>node</c>.
    /// </summary>
    [Fact]
    public void TheNodeSweep_NeverTouchesTheCurrentProcessOwnClaim_BetweenClaimAndFolder()
    {
        if (!NodeIsOnThePath(out var why))
        {
            Assert.Skip(why);
        }

        var root = _owned.Dir("b5c-root");
        var variables = new Dictionary<string, string?>();
        DirectoryLinks.ToolRun? ran = null;
        var after = (Claim: false, Folder: true, SameBytes: false, Quarantine: true);
        byte[]? claimBytes = null;
        var seams = new RunTempSeams((name, value) => variables[name] = value, name => variables.GetValueOrDefault(name))
        {
            Warn = _ => { },
            AfterClaim = claim =>
            {
                claimBytes = File.ReadAllBytes(claim);
                ran = DirectoryLinks.Run("node", ["--input-type=module", "-e", NodeSweepScript, root, RepositoryRoot.Path], TimeSpan.FromSeconds(60));
                after = (File.Exists(claim), Directory.Exists(claim[..^".owner".Length]), File.Exists(claim) && File.ReadAllBytes(claim).AsSpan().SequenceEqual(claimBytes),
                    Directory.GetFileSystemEntries(root).Any(entry => Path.GetFileName(entry).Contains(QuarantineWord, StringComparison.OrdinalIgnoreCase)));
            },
        };

        var run = RunTempFolder.Create(root, seams);
        try
        {
            using var all = new AssertionScope();
            ran.Should().NotBeNull();
            ran!.TimedOut.Should().BeFalse(ran.Output);
            ran.ExitCode.Should().Be(0, ran.Output);
            var result = JsonDocument.Parse(ran.Output.Trim().Split('\n')[^1]).RootElement;
            result.GetProperty("removed").GetArrayLength().Should().Be(0, "the Node sweep removes nothing: " + ran.Output);
            result.GetProperty("errors").GetArrayLength().Should().Be(0, ran.Output);
            var kept = result.GetProperty("preserved").EnumerateArray().Where(p => p.GetProperty("path").GetString()!.EndsWith(".owner", StringComparison.Ordinal)).ToList();
            kept.Should().ContainSingle("the claim is the one entry it names: " + ran.Output);
            string[] accepted = OperatingSystem.IsWindows() ? ["live", "foreign-uid"] : ["live"];
            kept[0].GetProperty("reason").GetString().Should().BeOneOf(accepted);
            TestContext.Current.AddWarning($"E5 S2-B B5 (c): the Node sweep, past the window, kept the own claim: {kept[0].GetProperty("reason").GetString()}");
            after.Should().Be((true, false, true, false), "the claim is still there with its bytes, the folder was not made by the sweep, nothing was renamed");
            Directory.Exists(run.Folder).Should().BeTrue("Create completed: the folder exists");
            File.Exists(run.ClaimFile).Should().BeTrue();
        }
        finally
        {
            run.Restore();
        }
    }

    /// <summary>The Node reference through coai's wrap, over <c>argv[1]</c>, with the clock 11 minutes ahead so only liveness decides; prints the result as one JSON line.</summary>
    private const string NodeSweepScript = """
        import path from 'node:path';
        import { pathToFileURL } from 'node:url';
        const [root, repo] = process.argv.slice(1);
        const owned = await import(pathToFileURL(path.join(repo, 'src_vs_code', 'scripts', 'owned.mjs')).href);
        const sweep = await owned.loadStartOfRunSweep();
        console.log(JSON.stringify(sweep({ repo, tmp: root, now: Date.now() + 11 * 60_000 })));
        """;

    private static bool NodeIsOnThePath(out string why)
    {
        var probe = DirectoryLinks.Run("node", ["--version"], TimeSpan.FromSeconds(30));
        why = $"node is not on the PATH (exit {probe.ExitCode}): the Node arm of the race is skipped here";

        return !probe.TimedOut && probe.ExitCode == 0;
    }

    // ---- B6 -------------------------------------------------------------------------------------------------------

    private static readonly string[] CoaiNames =
    [
        "coai-chat-live", "coai-answers-x", "coai-repair-x", "coai-noworkspace-x", "coai-plan-x", "coai-server-job-x", "coai-question-x",
        "coai-check-x", "coai-0.17.5", "coai-panel-x", "coai-futurething-x",
    ];

    [Fact]
    public void OnlyThisOwnersCoaiNames_AreJudged_EverythingElseIsNotEvenLookedAt()
    {
        var root = _owned.Dir("b6-root");
        var dead = Dead();
        var otherToken = PlantFolder(root, Owned.Prefix + OwnerToken.This + "-mcp-x-abc123", Rec(dead));
        var upper = PlantFolder(root, Owned.NameBase.ToUpperInvariant() + "X-ABC123", Rec(dead));
        var underscore = PlantFolder(root, Owned.NameBase.ToLowerInvariant().Replace('-', '_') + "x_abc123", Rec(dead));
        var file = Path.Combine(root, Owned.NameBase + "f-abc123");
        File.WriteAllText(file, "a file, not a folder");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow - Old);
        foreach (var name in CoaiNames)
        {
            var dir = Path.Combine(root, name);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "sentinel.txt"), "still " + name);
            Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow - Old);
        }

        // A stray dead-pid claim beside a chat's folder: a sweep that took the name for its own would remove it on that claim.
        var chatClaim = Path.Combine(root, "coai-chat-live.owner");
        File.WriteAllText(chatClaim, Rec(dead));
        File.SetLastWriteTimeUtc(chatClaim, DateTime.UtcNow - Old);

        var result = LeftoversSweep.Run(Options(root));

        using var all = new AssertionScope();
        result.Removed.Should().BeEquivalentTo([upper, underscore], "only the two spellings of this owner's coai names are judged");
        var lines = result.Preserved.Select(p => p.Path).Concat(result.Listed).Concat(result.Errors).ToList();
        foreach (var name in CoaiNames.Concat([Path.GetFileName(otherToken), Path.GetFileName(file)]))
        {
            lines.Should().NotContain(line => line.Contains(name, StringComparison.Ordinal), $"{name} is not even looked at");
        }

        foreach (var name in CoaiNames)
        {
            File.ReadAllText(Path.Combine(root, name, "sentinel.txt")).Should().Be("still " + name);
        }

        File.ReadAllText(chatClaim).Should().Be(Rec(dead), "the claim beside a coai-* folder is not an orphan claim of this sweep");
        Directory.Exists(otherToken).Should().BeTrue("another token's folder keeps its bytes");
        File.Exists(file).Should().BeTrue();
        result.Errors.Should().BeEmpty();
        UnmatchablePid.AssertNoSuchProcess(dead);
    }

    // ---- B7 -------------------------------------------------------------------------------------------------------

    [Fact]
    public void LegacyNames_AreListedWithTheirCommand_AndNeverCollected()
    {
        var root = _owned.Dir("b7-root");
        var dead = Dead();
        var legacy = Owned.Prefix + OwnedToken.RepoToken + "-run-";
        var tag = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        var linux = PlantFolder(root, legacy + "1a2b3c", $"devbox@{tag}:{dead}:1700000000");
        var win32 = PlantFolder(root, legacy + "2b3c4d", "devbox@win32:5");
        var darwin = PlantFolder(root, legacy + "3c4d5e", "devbox@darwin:5:17");
        var unmarked = PlantFolder(root, legacy + "4d5e6f", null);
        var orphan = Path.Combine(root, legacy + "5e6f7a.owner");
        File.WriteAllText(orphan, $"devbox@{tag}:{dead}:1700000000");
        File.SetLastWriteTimeUtc(orphan, DateTime.UtcNow - Old);
        var young = PlantFolder(root, legacy + "6f7a8b", $"devbox@{tag}:{dead}:1700000000", old: false);
        var before = Snapshot(root);
        var asked = new List<string>();

        var result = LeftoversSweep.Run(Options(root, mine: path =>
        {
            asked.Add(path);
            return true;
        }));

        using var all = new AssertionScope();
        foreach (var dir in new[] { linux, win32, darwin, unmarked })
        {
            ReasonOf(result, dir).Should().Be("legacy", Path.GetFileName(dir));
            result.Listed.Should().ContainSingle(line => line == $"legacy: folder {Path.GetFileName(dir)} — remove with: {RemoveCommand(dir)}");
        }

        ReasonOf(result, orphan).Should().Be("legacy");
        result.Listed.Should().ContainSingle(line => line == $"legacy: claim {Path.GetFileName(orphan)} — remove with: " + (OperatingSystem.IsWindows() ? $"Remove-Item -Force '{orphan}'" : $"rm -f -- '{orphan}'"));
        ReasonOf(result, young).Should().Be("young");
        result.Listed.Should().NotContain(line => line.Contains(Path.GetFileName(young), StringComparison.Ordinal), "a young legacy folder is silent");
        result.Removed.Should().BeEmpty("a legacy name is never collected");
        result.Errors.Should().BeEmpty();
        Snapshot(root).Should().Equal(before, "every folder and claim keeps its bytes");
        asked.Should().NotContain(path => path.Contains(legacy, StringComparison.OrdinalIgnoreCase), "a legacy name's claim is never read, so the kernel-owner question is never asked about it");
        UnmatchablePid.AssertNoSuchProcess(dead);
    }

    private static string RemoveCommand(string dir) =>
        OperatingSystem.IsWindows() ? $"Remove-Item -Recurse -Force '{dir}', '{dir}.owner'" : $"rm -rf -- '{dir}' '{dir}.owner'";
}
