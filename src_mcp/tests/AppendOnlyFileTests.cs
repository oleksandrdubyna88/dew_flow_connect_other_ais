using CoaiMcp.ServiceDefaults;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A write that lands at the END of the file, whatever else grew it in between — and the measured
/// fact that .NET's own <c>FileMode.Append</c> does not.
/// </summary>
/// <remarks>
/// <para>These two tests are the finding of story 1.4 in the smallest form that cannot quietly stop
/// being true. The story's plan demanded that the .NET append be MEASURED rather than argued from
/// node's result, and the measurement said: eight processes through <c>FileMode.Append</c> kept
/// <b>5512 of 8000</b> records and tore 1110 lines, where eight node writers over the same harness
/// kept all 8000. The mechanism is below, in four lines.</para>
/// <para>What it cost before it was found: <c>UsageLedger</c> has appended this way since it was
/// written, and its own docstring calls two servers sharing one data directory the normal case. Its
/// spending records have been overwriting each other on exactly that machine.</para>
/// <para>Both tests run in ONE process and need no concurrency, which is what makes them a unit
/// test: a handle that remembered a stale offset is enough. The scale version is
/// <c>npm run measure:append --dotnet=N</c>.</para>
/// </remarks>
public sealed class AppendOnlyFileTests : IDisposable
{
    /// <summary>
    /// A directory of this test's OWN — xUnit constructs this class once per test method, so every
    /// test gets a fresh <c>CreateTempSubdirectory</c> and disposes it after.
    /// </summary>
    /// <remarks>
    /// Written down because the code round read the shared field as shared STATE across parallel
    /// tests and asked for a directory per test method. It already is one; what the round was right
    /// about is that nothing SAID so. The invariant is not only documented but checked: the test
    /// that a missing file is created by the append itself asserts the ledger does not pre-exist,
    /// and it is the first thing that would fail if this class were ever instantiated once for all
    /// of its tests.
    /// </remarks>
    private readonly TempDir _dir = TempDir.For("coai-append-");

    public void Dispose() => _dir.Dispose();

    private string Ledger => _dir.At("ledger.jsonl");

    [Fact]
    public void TheManagedAppend_WritesAtAnOffsetItRemembered_WhichIsWhyAppendOnlyFileExists()
    {
        // The probe that named the mechanism, kept as a test. Open a stream in FileMode.Append on an
        // empty file; let 100 bytes arrive behind its back; write one byte through it. A kernel
        // append puts that byte at the new end and the file is 101 bytes.
        File.WriteAllBytes(Ledger, []);

        using (var remembering = new FileStream(Ledger, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AppendOnlyFile.Write(Ledger, new byte[100]);
            remembering.Write("X"u8);
        }

        // It is 100 bytes with an 'X' at offset ZERO: .NET wrote where it remembered the end being,
        // and destroyed what arrived in between. Windows 11 and Linux (ext4) answer identically, so
        // it is the runtime's behaviour and not one platform's.
        var bytes = File.ReadAllBytes(Ledger);
        bytes.Length.Should().Be(100, "the managed append did not append — it overwrote");
        bytes[0].Should().Be((byte)'X', "and it overwrote at the offset it was holding");
    }

    [Fact]
    public void OurAppend_LandsAtTheEnd_WhereverTheFileHasGotTo()
    {
        // The same shape, the other way round: the thing that arrives late is OURS, and it must land
        // after the hundred bytes rather than on top of them.
        File.WriteAllBytes(Ledger, []);

        using (var other = new FileStream(Ledger, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            other.Write(new byte[100]);
        }

        AppendOnlyFile.Write(Ledger, "X"u8.ToArray()).Should().BeTrue();

        var bytes = File.ReadAllBytes(Ledger);
        bytes.Length.Should().Be(101, "the kernel placed it at the end");
        bytes[^1].Should().Be((byte)'X');
    }

    [Fact]
    public void AWriteThatArrivesBetweenOurOpenAndOurWrite_DoesNotDisplaceOurRecord()
    {
        // THE TEST WITH TEETH, and the two sequential ones above are why it had to be written: they
        // pass just as happily against `FileMode.Append`, because this class opens and writes in one
        // breath and a sequential test never opens the window. Here the window is held open on
        // purpose. A handle that remembered its offset writes the 'X' at byte 0 and the file is 100
        // bytes; a handle the kernel appends through writes at 100 and the file is 101.
        File.WriteAllBytes(Ledger, []);

        AppendOnlyFile.Write(Ledger, "X"u8.ToArray(), afterOpen: path =>
        {
            using var peer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            peer.Write(new byte[100]);
        }).Should().BeTrue();

        var bytes = File.ReadAllBytes(Ledger);
        bytes.Length.Should().Be(101,
            "a hundred bytes arrived after we opened and before we wrote, and our record goes AFTER them");
        bytes[^1].Should().Be((byte)'X');
    }

    [Fact]
    public void ThePlatformHasATrueAppend_AndSaysSoBeforeAnythingIsWritten()
    {
        // On Unix the flag numbers differ between Linux and macOS, and a wrong constant would not
        // fail loudly — it would open a perfectly good handle that does not append. So the first
        // open asks fcntl what it actually got, and this is that answer. A build where it is false
        // refuses to write rather than corrupting somebody else's records.
        AppendOnlyFile.TrueAppend.Should().BeTrue(
            "this platform's O_APPEND / FILE_APPEND_DATA is what this build asked for");
    }

    [Fact]
    public void AMissingFile_IsCreatedByTheAppendItself()
    {
        // `OPEN_ALWAYS` / `O_CREAT`: the ledger's first record must not need a file to exist first,
        // because on a fresh machine nothing has written under the data directory yet.
        AppendOnlyFile.Write(Ledger, "first\n"u8.ToArray()).Should().BeTrue();

        File.ReadAllText(Ledger).Should().Be("first\n");
    }

    [Fact]
    public void ADirectoryInTheFilesPlace_Throws_SoTheLedgerCanAnswerFalse()
    {
        // The vector the extension's reader documents, from the writing side.
        //
        // THE TYPE IS NOT THE SAME ON BOTH PLATFORMS, and asserting one of them split CI. This said
        // `IOException`, which is what a refused `CreateFileW` gives on Windows, and the Linux job
        // went red on the first push. Measured on .NET 10 / Ubuntu: opening a directory as a file
        // throws `UnauthorizedAccessException`, because .NET's `Interop.ThrowExceptionForIoErrno`
        // maps a directory errno to that type. So what is asserted is the fact that MATTERS — it
        // throws something the ledger's catch list turns into a false — which is why
        // `JsonlLedgerTests.ADirectoryInTheFilesPlace_AnswersFalseAndThrowsNothing` is green on both.
        Directory.CreateDirectory(Ledger);

        var writing = () => AppendOnlyFile.Write(Ledger, "x\n"u8.ToArray());

        var thrown = writing.Should().Throw<Exception>("a directory is not a file on any platform").Which;

        // An expression tree cannot carry a type pattern, so the two types are named as types.
        thrown.Should().BeAssignableTo<Exception>()
            .And.Subject.GetType().Should().Match<Type>(
                type => typeof(IOException).IsAssignableFrom(type)
                    || typeof(UnauthorizedAccessException).IsAssignableFrom(type),
                "the ledger catches both, turns this into a false, and a gap is not a crash");
    }
}
