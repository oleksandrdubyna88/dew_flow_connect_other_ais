using System.Buffers.Binary;
using System.Text;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The decisions of the host's process listing, on any OS (the native toolhelp calls are <c>HostProcessListNative</c>'s, run for real by
/// <c>ForeignKimiTests.TheHostsOwnListing_NamesThisProcess_WithItsParent</c> in the <c>windows-latest</c> job): when it applies, the buffer a call is
/// given, and what a filled <c>PROCESSENTRY32W</c> says at its documented offsets.
/// </summary>
public sealed class HostProcessListTests
{
    private const int Pid = 8;
    private const int ParentPid = 32;
    private const int NameAt = 44;

    private static byte[] Filled(int pid, int parent, string name)
    {
        var entry = HostProcessList.FreshEntry();
        BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(Pid), pid);
        BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(ParentPid), parent);
        Encoding.Unicode.GetBytes(name).CopyTo(entry, NameAt);

        return entry;
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void TheSnapshotIsReadOnlyOnA64BitWindowsProcess(bool windows, bool is64Bit, bool applies) =>
        HostProcessList.Applies(windows, is64Bit).Should().Be(applies, "the offsets are the 64-bit ones and only Windows has the call");

    [Fact]
    public void AFreshBuffer_IsZeroedAndCarriesItsOwnSize()
    {
        var entry = HostProcessList.FreshEntry();

        entry.Length.Should().Be(568, "sizeof(PROCESSENTRY32W) on a 64-bit process");
        BinaryPrimitives.ReadUInt32LittleEndian(entry).Should().Be(568u, "dwSize, which the call refuses to run without");
        entry.AsSpan(4).ToArray().Should().OnlyContain(b => b == 0);
        HostProcessList.FreshEntry().Should().NotBeSameAs(entry, "a listing never shares a buffer");
    }

    [Fact]
    public void AFilledBuffer_IsReadAtTheDocumentedOffsets()
    {
        var read = HostProcessList.Parsed(Filled(4242, 99, "kimi.exe"));

        read.Should().Be(new ProcessEntry(4242, 99, "kimi.exe"));
    }

    [Fact]
    public void AName_EndsAtItsNul_NeverAtTheBuffersEnd()
    {
        var entry = Filled(1, 0, "rg.exe");
        Encoding.Unicode.GetBytes("stale").CopyTo(entry, NameAt + ((("rg.exe".Length + 1) * 2)));

        HostProcessList.Parsed(entry).Name.Should().Be("rg.exe", "what follows the terminator is a previous, longer name's tail");
    }

    [Fact]
    public void AnUnterminatedName_IsReadAsNoName_NotAsGarbage()
    {
        var entry = HostProcessList.FreshEntry();
        Encoding.Unicode.GetBytes(new string('x', 260)).CopyTo(entry, NameAt);

        HostProcessList.Parsed(entry).Name.Should().BeEmpty("a name the kernel did not terminate is not one this reader can trust");
    }

    [Fact]
    public void OnLinux_TheListingHoldsThisProcess_ByItsExeBasename()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the Linux listing reads /proc");

        // The REAL /proc reads, narrowed to this process: what this machine's OTHER processes allow (a same-account process whose exe EACCES is Unreadable by DL10) is a separate question.
        var listing = new LinuxProcessListing(new Only(ProcReads.Real, Environment.ProcessId), Environment.ProcessId).Snapshot();
        var me = listing.Should().BeOfType<ProcessListing.Listed>($"the Linux listing should be readable here ({(listing as ProcessListing.Unreadable)?.Reason})").Subject.Processes.FirstOrDefault(p => p.Pid == Environment.ProcessId);

        me.Should().NotBeNull("the Linux listing should hold the test host's own pid, but was empty");
        me!.Name.Should().Be(Path.GetFileName(Environment.ProcessPath), "the name is the exe link's basename, which is stable for the whole run — comm is the kernel's thread name");
    }

    /// <summary>The real <c>/proc</c>, listing only the given pids.</summary>
    private sealed class Only(IProcReads inner, params int[] pids) : IProcReads
    {
        public IEnumerable<int> Pids() => pids;

        public string StatText(int pid) => inner.StatText(pid);

        public IReadOnlyCollection<int> Threads(int pid) => inner.Threads(pid);

        public uint OwnerUid(int pid) => inner.OwnerUid(pid);

        public string Cmdline(int pid) => inner.Cmdline(pid);

        public string ExeTarget(int pid) => inner.ExeTarget(pid);

        public uint OwnUid() => inner.OwnUid();
    }

    [Fact]
    public void OnLinux_TheWholeMachinesListing_IsReadable_OrSaysWhichProcessCouldNotBe()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the Linux listing reads /proc");

        var listing = HostProcessList.Current.Snapshot();
        if (listing is not ProcessListing.Unreadable unreadable)
        {
            listing.Should().BeOfType<ProcessListing.Listed>().Which.Processes.Should().Contain(p => p.Pid == Environment.ProcessId);

            return;
        }

        // A same-account process hiding its exe is NOT enough (DL10, re-decided 2026-10-17: argv[0] and comm name it): only one whose cmdline is hidden too may make the machine's listing unreadable —
        // an environment fact, said with what the kernel says about that process. Any other unreadable listing is a defect of the reader.
        unreadable.Reason.Should().Contain("/cmdline", "a hidden exe alone made the listing unreadable: the argv[0] fall-back did not run");
        Assert.Skip($"this machine's process list is unreadable by DL10's rule: {unreadable.Reason} — {Describe(unreadable.Reason)}");
    }

    private static string Describe(string reason)
    {
        var match = System.Text.RegularExpressions.Regex.Match(reason, @"/proc/(\d+)/", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
        if (!match.Success)
        {
            return "no pid named";
        }

        var pid = match.Groups[1].Value;
        var lines = new List<string>();
        foreach (var file in new[] { "comm", "status", "attr/current" })
        {
            try
            {
                var text = File.ReadAllText($"/proc/{pid}/{file}");
                lines.Add(file == "status" ? string.Join(' ', text.Split('\n').Where(l => l.StartsWith("Name:") || l.StartsWith("Uid:") || l.StartsWith("NoNewPrivs:") || l.StartsWith("Seccomp:") || l.StartsWith("CapEff:")).Select(l => l.Replace('\t', ' '))) : $"{file}={text.Trim()}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                lines.Add($"{file}: {e.GetType().Name}");
            }
        }

        return string.Join("; ", lines);
    }

    [Fact]
    public void AFailedSnapshot_IsUnreadable_NotEmpty()
    {
        HostProcessList.Failed(5).Should().BeOfType<ProcessListing.Unreadable>("a failed Toolhelp snapshot was read as 'nobody is running'").Which.Reason.Should().Contain("Win32 error 5");
        HostProcessList.Walked([], 5).Should().BeOfType<ProcessListing.Unreadable>("a walk that ended on a real error is not a complete listing");
    }

    [Theory]
    [InlineData(18)]
    [InlineData(0)]
    public void AWalkThatEndedTheNormalWay_IsTheListing(int lastError) =>
        HostProcessList.Walked([new ProcessEntry(1, 0, "init")], lastError).Should().BeOfType<ProcessListing.Listed>().Which.Processes.Should().ContainSingle();

    [Fact]
    public void OnAHostWithNoListing_TheAnswerIsUnreadable_NeverEmpty()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || OperatingSystem.IsLinux(), "Windows and Linux have a listing");

        HostProcessList.Current.Snapshot().Should().BeOfType<ProcessListing.Unreadable>();
    }
}
