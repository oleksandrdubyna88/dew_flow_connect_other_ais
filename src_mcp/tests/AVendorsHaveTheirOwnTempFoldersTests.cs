using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s6, pass 5, M13 (the same C3 as the home of pass 4): <c>/tmp</c> and <c>/var/tmp</c> of a coder's unit are the VENDOR's own folders — <c>&lt;job&gt;/coder-temp/&lt;vendor&gt;/tmp</c> and <c>vartmp</c> (pass 6, R-M15: the sources lie OUTSIDE every folder a unit binds writable) — in the unit of claude, of codex and of agy, so what a CLI with a shell leaves under <c>/tmp</c> is
/// not in the unit of a later vendor of the same job, and what agy's CLI leaves there (its token may be written there; not measured) is not read by it.
/// </summary>
[Collection("fakecli-env")]
public sealed class AVendorsHaveTheirOwnTempFoldersTests
{
    private static readonly string[] Vendors = ["claude", "codex", "agy"];

    private static BoundaryJob Job() =>
        BoundaryJob.ForCoder("r20261126101010-0000000a", new CoaiMcp.Runners.Worktrees.CoderTree(Path.Combine(Path.GetFullPath("roots"), "coai-coder-r20261126101010-0000000a"), Path.GetFullPath("main"), "gitdir: x"), Path.Combine(Path.GetFullPath("jobs"), "r20261126101010-0000000a"));

    private static BoundaryHost NoHome() => BoundaryHost.Current() with { RealHome = string.Empty, NuGetPackages = string.Empty, Path = string.Empty, ResolverFile = string.Empty };

    [Fact]
    public void TheTempFolders_AreTheVendors_AndClaudesAreTheOnesTheJobHadBefore()
    {
        var job = Job();

        foreach (var vendor in Vendors)
        {
            job.CoderTempOf(vendor).Should().Be(Path.Combine(job.JobRoot, "coder-temp", vendor, "tmp"));
            job.CoderVarTempOf(vendor).Should().Be(Path.Combine(job.JobRoot, "coder-temp", vendor, "vartmp"));
            job.CoderFoldersOf(vendor).Should().Contain([job.CoderTempOf(vendor), job.CoderVarTempOf(vendor)]);
        }

        Vendors.Select(job.CoderTempOf).Should().OnlyHaveUniqueItems();
        job.CoderTempFolder.Should().Be(job.CoderTempOf("claude"), "the name every claude caller had names claude's folder");
        job.CoderVarTempFolder.Should().Be(job.CoderVarTempOf("claude"));
    }

    [Fact]
    public void EachVendorsUnit_BindsItsOwnTempFolders_AsTmpAndVarTmp_AndNoOthers()
    {
        var job = Job();
        var program = CoderProgram.Resolve(RoleJobWorld.FakeCli, string.Empty, job);
        var lists = new Dictionary<string, IReadOnlyList<string>>
        {
            ["claude"] = CoderVendors.Claude.UnitProperties(NoHome(), job, program, TimeSpan.FromMinutes(5)),
            ["codex"] = CodexCoderVendor.Instance.UnitProperties(NoHome(), job, program, TimeSpan.FromMinutes(5)),
            ["agy"] = AgyCoderBed.Vendor().UnitProperties(NoHome(), job, program, TimeSpan.FromMinutes(5)),
        };

        foreach (var vendor in Vendors)
        {
            // codex's /tmp is a tmpfs of its unit (its own folder is not bound there); its /var/tmp is its own folder like the others'.
            if (vendor == "codex")
            {
                lists[vendor].Should().Contain($"TemporaryFileSystem={BoundaryUnit.UnitTemp}", vendor);
            }
            else
            {
                lists[vendor].Should().Contain($"BindPaths={job.CoderTempOf(vendor)}:{BoundaryUnit.UnitTemp}", vendor);
            }

            lists[vendor].Should().Contain($"BindPaths={job.CoderVarTempOf(vendor)}:{BoundaryUnit.UnitVarTemp}", vendor);
            foreach (var other in Vendors.Where(name => name != vendor))
            {
                lists[vendor].Should().NotContain(property => property.Contains(job.CoderTempOf(other), StringComparison.Ordinal) || property.Contains(job.CoderVarTempOf(other), StringComparison.Ordinal), $"the unit of {vendor} holds nothing of {other}'s temp folders");
            }
        }
    }

    [Fact]
    public async Task WhatAnAgyUnitLeavesUnderTmp_IsInItsOwnFolder_AndAClaudeUnitOfTheSameJobDoesNotSeeIt()
    {
        await using var unit = await AgyUnit.CreateAsync();
        var job = unit.Bed.Job;

        _ = await unit.RunAsync($"echo left-by-agy > {BoundaryUnit.UnitTemp}/left-by-agy.txt; echo left-by-agy > {BoundaryUnit.UnitVarTemp}/left-by-agy-too.txt");
        var claude = await unit.Bed.Turns().RunAsync(job, unit.Bed.Shell($"ls -A {BoundaryUnit.UnitTemp} {BoundaryUnit.UnitVarTemp}"), "c2", TestContext.Current.CancellationToken);

        File.Exists(Path.Combine(job.CoderTempOf("agy"), "left-by-agy.txt")).Should().BeTrue("agy's temp folder is its own");
        File.Exists(Path.Combine(job.CoderVarTempOf("agy"), "left-by-agy-too.txt")).Should().BeTrue();
        Directory.EnumerateFileSystemEntries(job.CoderTempOf("claude")).Should().NotContain(entry => entry.Contains("left-by-agy", StringComparison.Ordinal), "nothing of agy is in claude's folder on the disk");
        var seen = claude.Should().BeOfType<CoderTurnOutcome.Finished>(claude.ToString()).Subject.Run.Result.StdOut;
        seen.Should().NotContain("left-by-agy", "claude's unit does not see what agy's left");
    }

    [Fact]
    public void TheTempSources_LieInNoFolderThatAUnitBindsWritable_SoNoUnitCanSwapThemForALink()
    {
        var job = Job();
        var program = CoderProgram.Resolve(RoleJobWorld.FakeCli, string.Empty, job);
        var lists = new Dictionary<string, IReadOnlyList<string>>
        {
            ["claude"] = CoderVendors.Claude.UnitProperties(NoHome(), job, program, TimeSpan.FromMinutes(5)),
            ["codex"] = CodexCoderVendor.Instance.UnitProperties(NoHome(), job, program, TimeSpan.FromMinutes(5)),
            ["agy"] = AgyCoderBed.Vendor().UnitProperties(NoHome(), job, program, TimeSpan.FromMinutes(5)),
        };

        foreach (var vendor in Vendors)
        {
            var sources = new[] { job.CoderTempOf(vendor), job.CoderVarTempOf(vendor) };
            sources.Should().NotContain(source => source.StartsWith(job.CoderFolder + Path.DirectorySeparatorChar, StringComparison.Ordinal), $"{vendor}: a source inside the folder of the coders is a name the unit can replace");
            var writableBinds = lists[vendor].Where(property => property.StartsWith("BindPaths=", StringComparison.Ordinal)).Select(property => property["BindPaths=".Length..].Split(':')[0]).ToList();
            foreach (var bound in writableBinds)
            {
                sources.Should().NotContain(source => source.StartsWith(bound.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal), $"{vendor}: the folder '{bound}' is bound writable, so a name inside it can be moved from inside");
            }
        }
    }

    public static TheoryData<string> Swapped => new() { "tmp", "vartmp", "vendor-folder", "temp-root" };

    [Theory]
    [MemberData(nameof(Swapped))]
    public async Task ATempSourceThatIsALink_RefusesTheLaunch_BeforeAnythingIsMadeThroughIt(string which)
    {
        await using var unit = await AgyUnit.CreateAsync();
        var job = unit.Bed.Job;
        var victim = Path.Combine(unit.Bed.RealHome, "victim-" + which);
        Directory.CreateDirectory(victim);
        File.WriteAllText(Path.Combine(victim, "victim.txt"), "kept\n");
        var temp = Path.GetDirectoryName(Path.GetDirectoryName(job.CoderTempOf("agy")))!;
        var path = which switch
        {
            "tmp" => job.CoderTempOf("agy"),
            "vartmp" => job.CoderVarTempOf("agy"),
            "vendor-folder" => Path.GetDirectoryName(job.CoderTempOf("agy"))!,
            _ => temp,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.CreateSymbolicLink(path, victim);

        var outcome = await unit.RunAsync("echo must-not-run");

        var refused = outcome.Should().BeOfType<CoderTurnOutcome.Refused>(outcome.ToString()).Subject;
        refused.Sentence.Should().Contain(path, "the sentence names the name that is not a real folder");
        Directory.EnumerateFileSystemEntries(victim).Select(Path.GetFileName).Should().BeEquivalentTo(["victim.txt"], "no marker and no file was made through the link");
    }

    [Fact]
    public async Task AUnitThatSwapsItsOwnTempNamesForLinks_GivesTheNextUnitNothingOfTheTarget()
    {
        await using var unit = await AgyUnit.CreateAsync();
        var job = unit.Bed.Job;
        var victim = Path.Combine(unit.Bed.RealHome, "victim");
        Directory.CreateDirectory(victim);
        File.WriteAllText(Path.Combine(victim, "victim.txt"), "kept\n");

        // The probes of the re-check: the names a unit could reach are moved and put back as links to the victim; the next unit lists and writes its /tmp and /var/tmp.
        var swap = new[] { job.CoderTempOf("agy"), job.CoderVarTempOf("agy"), Path.Combine(job.CoderVendorFolder("agy"), "tmp"), Path.Combine(job.CoderVendorFolder("agy"), "vartmp") }
            .Select(name => $"mv '{name}' '{name}.old' 2>/dev/null; ln -s '{victim}' '{name}' 2>/dev/null")
            .Append($"if [ -e '{job.JobRoot}/coder-temp' ]; then echo coder-temp-visible; else echo coder-temp-hidden; fi");
        var first = await unit.RunAsync(string.Join("; ", swap));
        var second = await unit.RunAsync($"ls -A {BoundaryUnit.UnitTemp} {BoundaryUnit.UnitVarTemp}; echo probe > {BoundaryUnit.UnitTemp}/probe.txt; echo probe > {BoundaryUnit.UnitVarTemp}/probe.txt", "c2");

        first.Should().BeOfType<CoderTurnOutcome.Finished>(first.ToString()).Subject.Run.Result.StdOut.Should().Contain("coder-temp-hidden");
        if (second is CoderTurnOutcome.Finished finished)
        {
            finished.Run.Result.StdOut.Should().NotContain("victim.txt", "the next unit does not get the victim as its temp folder");
        }
        else
        {
            second.Should().BeOfType<CoderTurnOutcome.Refused>("a launch that is not given its own folders is refused, never run on the victim");
        }

        Directory.EnumerateFileSystemEntries(victim).Select(Path.GetFileName).Should().BeEquivalentTo(["victim.txt"], "nothing was written into the victim");
    }
}
