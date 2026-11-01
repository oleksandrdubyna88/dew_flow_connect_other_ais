using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Kimi;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Whose a process is, in the Linux listing (the E5 risk consultant, 2026-10-17): by the uids its own <c>/proc/&lt;pid&gt;/status</c> writes, never by who owns its <c>/proc/&lt;pid&gt;</c> DIRECTORY — a non-dumpable
/// process of this very account has a root-owned directory, and a refresher read as "not ours" is a refresher read as absent. Only a process that is plainly ANOTHER account's is skipped; a status that cannot be read
/// is an unreadable listing, never a skip.
/// </summary>
public sealed class LinuxOwnershipTests
{
    private const int Shim = 100;
    private const uint Me = 1000;

    private static FakeProcReads Machine() => new FakeProcReads()
        .Running(1, 0, "/sbin/init", "init")
        .Running(Shim, 1, "/usr/local/bin/coai-mcp", "coai-mcp");

    private static ProcessListing Listing(FakeProcReads proc) => new LinuxProcessListing(proc, Shim).Snapshot();

    [Fact]
    public void ALiveKimi_WhoseDirectoryIsRootsButWhoseStatusUidIsOurs_IsJudged_AndIsKimi()
    {
        var proc = Machine().Running(4242, 1, "/home/me/.kimi-code/bin/kimi").WithStatusUid(4242, directoryOwner: 0, real: Me);

        var listing = Listing(proc).Should().BeOfType<ProcessListing.Listed>("a refresher whose /proc directory is root-owned was skipped as 'not ours'").Subject;

        ForeignKimiWatch.Foreign(listing.Processes, Shim).Select(entry => entry.Pid).Should().Equal(4242);
    }

    [Fact]
    public void AProcessWhoseStatusSaysAnotherUid_IsSkipped_WhateverItsDirectoryOwnerIs()
    {
        var proc = Machine().Running(4243, 1, "/home/other/.kimi-code/bin/kimi").WithStatusUid(4243, directoryOwner: Me, real: 2000);

        Listing(proc).Should().BeOfType<ProcessListing.Listed>().Which.Processes.Select(entry => entry.Pid).Should().NotContain(4243, "a truly other account's kimi cannot refresh this person's 0700 folder");
    }

    [Fact]
    public void ASetuidProcess_WhoseEffectiveUidIsOurs_IsJudged()
    {
        var proc = Machine().Running(4244, 1, "/usr/bin/helper");
        proc.Owners[4244] = 0;
        proc.Statuses[4244] = () => $"Uid:\t2000\t{Me}\t{Me}\t{Me}\n";

        Listing(proc).Should().BeOfType<ProcessListing.Listed>().Which.Processes.Select(entry => entry.Pid).Should().Contain(4244, "it can open a 0700 folder as this account");
    }

    [Theory]
    [InlineData("io")]
    [InlineData("denied")]
    [InlineData("no-uid-line")]
    public void AStatusThatCannotBeRead_MakesTheListingUnreadable_NamingThePid_NeverASkip(string failure)
    {
        var proc = Machine().Running(4245, 1, "/home/me/.kimi-code/bin/kimi");
        proc.Owners[4245] = failure == "no-uid-line" ? Me : 0;
        proc.Statuses[4245] = () => failure switch
        {
            "io" => throw new IOException("EIO"),
            "denied" => throw new UnauthorizedAccessException("EACCES"),
            _ => "Name:\tkimi\nState:\tS (sleeping)\n",
        };

        var reason = Listing(proc).Should().BeOfType<ProcessListing.Unreadable>("a process whose uid could not be read was skipped, so a refresher could be read as absent").Subject.Reason;

        reason.Should().Contain("/proc/4245/status");
    }

    private sealed class Counting(IProcReads inner) : IProcReads
    {
        public List<int> StatusReads { get; } = [];

        public IEnumerable<int> Pids() => inner.Pids();

        public string StatText(int pid) => inner.StatText(pid);

        public IReadOnlyCollection<int> Threads(int pid) => inner.Threads(pid);

        public string ExeTarget(int pid) => inner.ExeTarget(pid);

        public string Cmdline(int pid) => inner.Cmdline(pid);

        public string StatusText(int pid)
        {
            StatusReads.Add(pid);

            return inner.StatusText(pid);
        }

        public uint OwnUid() => inner.OwnUid();
    }

    [Fact]
    public void AnUnreadableProcess_EndsTheListingAtOnce_TheRestAreNeverJudged()
    {
        var proc = Machine().Running(200, 1, "/usr/bin/a").Running(300, 1, "/usr/bin/b").Running(400, 1, "/usr/bin/c");
        proc.Statuses[200] = () => throw new IOException("EIO");
        var counting = new Counting(proc);

        var listing = new LinuxProcessListing(counting, Shim).Snapshot();

        listing.Should().BeOfType<ProcessListing.Unreadable>().Which.Reason.Should().Contain("/proc/200/status");
        counting.StatusReads.Should().Equal(1, Shim, 200).And.NotContain(300).And.NotContain(400);
    }

    [Fact]
    public void AProcessThatLeftBeforeItsStatusWasRead_IsSkipped()
    {
        var proc = Machine().Running(4246, 1, "/home/me/.kimi-code/bin/kimi");
        proc.Statuses[4246] = () => throw new DirectoryNotFoundException("/proc/4246");

        Listing(proc).Should().BeOfType<ProcessListing.Listed>("a process that left mid-read (ESRCH/ENOENT) is never evidence of anything").Which.Processes.Select(entry => entry.Pid).Should().NotContain(4246);
    }
}
