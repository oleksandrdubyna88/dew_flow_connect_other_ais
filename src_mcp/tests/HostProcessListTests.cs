using System.Buffers.Binary;
using System.Text;
using CoaiMcp.Runners.Kimi;
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

        var me = HostProcessList.Current.Snapshot().Should().BeOfType<ProcessListing.Listed>("the Linux listing should be readable here").Subject.Processes.FirstOrDefault(p => p.Pid == Environment.ProcessId);

        me.Should().NotBeNull("the Linux listing should hold the test host's own pid, but was empty");
        me!.Name.Should().Be(Path.GetFileName(Environment.ProcessPath), "the name is the exe link's basename, which is stable for the whole run — comm is the kernel's thread name");
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
