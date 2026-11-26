using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s6, pass 5, M13 (the same C3 as the home of pass 4): <c>/tmp</c> and <c>/var/tmp</c> of a coder's unit are the VENDOR's own folders — <c>&lt;job&gt;/coder/&lt;vendor&gt;/tmp</c> and <c>vartmp</c> — in the unit of claude, of codex and of agy, so what a CLI with a shell leaves under <c>/tmp</c> is
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
            job.CoderTempOf(vendor).Should().Be(Path.Combine(job.CoderFolder, vendor, "tmp"));
            job.CoderVarTempOf(vendor).Should().Be(Path.Combine(job.CoderFolder, vendor, "vartmp"));
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
}
