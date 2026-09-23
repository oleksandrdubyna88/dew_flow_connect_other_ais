using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// The root helper frames what arrives on stdin into exactly the environment file the server reads.
/// </summary>
/// <remarks>
/// <para><b>What was not covered until 2026-09-23.</b> <see cref="TheHostsHelperIsTheOneThisDeployNeedsTests"/>
/// proves the installed helper DECLARES the two-record protocol; nothing proved it FRAMES two records
/// correctly. Every rule in <c>install-env.sh</c>'s reading of stdin came out of a review round — an
/// empty second record told from an absent one, an unterminated record accepted, a third line refused
/// because a wrapped base64 value would decode to a PREFIX of the key list — and each was written,
/// reviewed and shipped with no test able to say it still held.</para>
/// <para><b>The real script, unmodified.</b> It runs as root and writes <c>/etc/coai-bugs/env</c>, so
/// it cannot run here as it runs there. What it calls is replaced instead: stand-ins for
/// <c>install</c> and <c>chown</c> that do nothing, and one for <c>mv</c> that insists the destination
/// is <c>/etc/coai-bugs/env</c> and puts the file where this test can read it. A test-only switch in a
/// root helper would be one more thing root executes; a PATH is not.</para>
/// </remarks>
public sealed class TheRootHelperFramesTwoRecordsTests : IDisposable
{
    private const string Helper = "deploy/bugs/install-env.sh";

    private readonly string _dir = Directory.CreateTempSubdirectory("coai-install-env-").FullName;

    public TheRootHelperFramesTwoRecordsTests()
    {
        var shims = Directory.CreateDirectory(Path.Combine(_dir, "shims")).FullName;
        Shim(shims, "install", "exit 0");
        Shim(shims, "chown", "exit 0");
        // `mv -f "$TMP" "$ENV_FILE"` is the script's only write: anything else is a change this test
        // has not seen, so it refuses rather than guessing.
        Shim(shims, "mv",
            "[ \"$1\" = -f ] && [ \"$3\" = /etc/coai-bugs/env ] || { echo \"unexpected mv $*\" >&2; exit 3; }\n"
            + $"cat \"$2\" > '{ShellScript.Posix(Written)}' && rm -f \"$2\"");
        Shims = shims;
    }

    private string Shims { get; }

    private string Written => Path.Combine(_dir, "env");

    public void Dispose() => Scratch.Delete(_dir);

    [Fact]
    public void TwoRecords_BecomeTheThreeLinesTheServerReads()
    {
        var run = Deliver("s3cret\nQUJD\n");

        run.Code.Should().Be(0, run.Error);
        File.ReadAllText(Written).Should().Be(
            "COAI_BUGS_SECRET=s3cret\nCOAI_BUGS_DATA=/opt/coai-bugs/data\nCOAI_BUGS_ADMIN_KEYS=QUJD\n");
        (run.Output + run.Error).Should().NotContain("s3cret").And.NotContain("QUJD",
            "a helper that confirmed a credential would put it in a CI log");
    }

    [Fact]
    public void AnEmptySecondRecord_MeansNoAdministrators()
    {
        var run = Deliver("s3cret\n\n");

        run.Code.Should().Be(0, "running with no administrators is legitimate: {0}", run.Error);
        File.ReadAllText(Written).Should().EndWith("COAI_BUGS_ADMIN_KEYS=\n");
    }

    [Fact]
    public void AnAbsentSecondRecord_IsRefused_AndNothingIsWritten()
    {
        var run = Deliver("s3cret\n");

        run.Code.Should().Be(1, "writing it would remove every administrator and print success");
        run.Error.Should().Contain("administrator line is missing");
        File.Exists(Written).Should().BeFalse();
    }

    [Fact]
    public void ASecondRecordWithNoNewline_IsStillComplete()
    {
        var run = Deliver("s3cret\nQUJD");

        run.Code.Should().Be(0, run.Error);
        File.ReadAllText(Written).Should().EndWith("COAI_BUGS_ADMIN_KEYS=QUJD\n");
    }

    [Theory]
    [InlineData("s3cret\nQUJD\nRUZH\n")]
    [InlineData("s3cret\nQUJD\nRUZH")]
    public void AThirdRecord_IsRefused_BecauseAWrappedListWouldDecodeToAPrefix(string delivered)
    {
        var run = Deliver(delivered);

        run.Code.Should().Be(1);
        run.Error.Should().Contain("more than two lines");
        File.Exists(Written).Should().BeFalse();
    }

    [Fact]
    public void CarriageReturns_AreNotPartOfEitherValue()
    {
        var run = Deliver("s3cret\r\nQUJD\r\n");

        run.Code.Should().Be(0, run.Error);
        File.ReadAllText(Written).Should().Be(
            "COAI_BUGS_SECRET=s3cret\nCOAI_BUGS_DATA=/opt/coai-bugs/data\nCOAI_BUGS_ADMIN_KEYS=QUJD\n",
            "a secret with a carriage return hashes differently from the one the client sent");
    }

    [Fact]
    public void NoSecret_IsRefused()
    {
        var run = Deliver(string.Empty);

        run.Code.Should().Be(1);
        run.Error.Should().Contain("no secret arrived");
        File.Exists(Written).Should().BeFalse();
    }

    [Fact]
    public void AListOverSixtyFourKiB_IsRefused_NotTruncated()
    {
        var run = Deliver($"s3cret\n{new string('A', 65_537)}\n");

        run.Code.Should().Be(1, "a value cut in half decodes to nonsense");
        File.Exists(Written).Should().BeFalse();
    }

    private ShellRun Deliver(string stdin) => ShellScript.Fed(Helper, stdin, Shims);

    /// <summary>A stand-in command: LF endings and an execute bit, which is all `sh` asks of one.</summary>
    private static void Shim(string directory, string name, string body)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, $"#!/bin/sh\n{body}\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
