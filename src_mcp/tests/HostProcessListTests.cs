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
    public void OffWindows_TheListingIsEmpty_SoNoOneIsEverTakenForAForeignKimi()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows answers with its real process table, which ForeignKimiTests reads");

        HostProcessList.Current.Snapshot().Should().BeEmpty();
    }
}
