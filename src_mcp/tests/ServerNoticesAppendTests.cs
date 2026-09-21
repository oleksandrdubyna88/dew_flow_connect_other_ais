using System.Text;
using CoaiMcp.Core.Notices;
using CoaiMcp.Server;
using CoaiMcp.ServiceDefaults;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The server's writer: the path by story 1.3, the bytes by story 1.2, the disk by <c>JsonlLedger</c>.
/// </summary>
/// <remarks>
/// <para><b>Carried from story 1.2's code round, discharged here.</b> These tests do not assert that
/// the writer CALLED the serialiser — they read the file back and compare the BYTES with
/// <c>ServerNoticeLine.Of</c>. Until the serialiser was wired to the writer, a call site could have
/// serialised a notice by hand and the parity harness would have stayed green, because it exercises
/// <c>NoticeTool</c> rather than the product's write path. Watched failing against exactly that:
/// a writer that spelled the JSON itself.</para>
/// <para>The directory is minted through the real resolver, never composed: the value the writer
/// takes is the one <c>PanelSettings.DataDirectoryFor</c> answers for a <c>COAI_DATA_DIR</c> pointed
/// at the test's own temp directory.</para>
/// </remarks>
public sealed class ServerNoticesAppendTests : IDisposable
{
    private const string Secret = "abcdefghijklmnopqrstuvwxyz0123456789";

    private readonly TempDir _dir = TempDir.For("coai-notices-");

    public void Dispose() => _dir.Dispose();

    private ResolvedDataDir Dir =>
        PanelSettings.DataDirectoryFor(name => name == "COAI_DATA_DIR" ? _dir.Path : null);

    /// <summary>Every string field filled with the same value, so a secret in it is a secret in every field.</summary>
    private static ServerNotice Notice(string value, int seq = 1) => new()
    {
        Utc = ServerNotice.Iso(new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero)),
        Class = "refusal",
        Source = value,
        Code = ServerNoticeCodes.Refused,
        Subject = value,
        Title = value,
        Detail = value,
        Cure = value,
        Action = value,
        Offered = value,
        Answer = value,
        Run = value,
        Repo = value,
        Branch = value,
        Session = value,
        Provider = value,
        Role = value,
        Pid = 37308,
        Seq = seq,
        Bound = 100,
        More = new Dictionary<string, object> { ["whatTheServerSent"] = value, ["n"] = 7 },
    };

    [Fact]
    public void TheBytesOnDisk_AreTheSerialisersBytes_AndCarryNoSecret()
    {
        // The bearer shape, which both halves' suites prove. NOT `said: password=<secret>`: the
        // labelled pattern is applied left to right, so `said` is matched as the label with
        // `password=<secret>` as its VALUE, `said` names no credential, and the whole match is kept
        // — identically on both sides, which makes it a limit of the contract rather than of this
        // port, and one this test is not the place to paper over.
        var notice = Notice($"refused with Authorization: Bearer {Secret}");

        ServerNotices.Append(Dir, notice).Should().BeTrue();

        var onDisk = File.ReadAllBytes(ServerNotices.PathFor(Dir));
        onDisk.Should().Equal(Encoding.UTF8.GetBytes(ServerNoticeLine.Of(notice)),
            "what lands in the file must have gone through ServerNoticeLine.Of — the redaction, the "
            + "field order and the encoding all live there, and a writer that spelled the JSON itself "
            + "would keep the parity harness green while writing a different line");
        Encoding.UTF8.GetString(onDisk).Should().NotContain(Secret).And.Contain("[redacted]");
    }

    [Fact]
    public void TwoNotices_AreTwoLines_InOrder_EachTheSerialisers()
    {
        var first = Notice("first", seq: 1);
        var second = Notice("second", seq: 2);

        ServerNotices.Append(Dir, first).Should().BeTrue();
        ServerNotices.Append(Dir, second).Should().BeTrue();

        File.ReadAllText(ServerNotices.PathFor(Dir))
            .Should().Be(ServerNoticeLine.Of(first) + ServerNoticeLine.Of(second));
    }

    [Fact]
    public void AFailingDisk_DoesNotFailTheCaller()
    {
        // A directory in the file's place — the vector `notificationsFile.ts` documents. The
        // refusal this notice is about still has to reach the calling AI; a writer that threw here
        // would turn one lost record into a failed tool call.
        Directory.CreateDirectory(ServerNotices.PathFor(Dir));

        var appending = () => ServerNotices.Append(Dir, Notice("lost"));

        appending.Should().NotThrow();
        appending().Should().BeFalse("the caller can count what was lost");
    }

    [Fact]
    public void TheWriterTakesTheResolvedType_SoTheRootCannotBeHandedToIt()
    {
        // The compile-time half of story 1.4's answer to the 1.3 reviewer, pinned by reflection so
        // that widening the parameter back to a string is a red test rather than a quiet edit:
        // `Append(DataRootFor(env), notice)` must not compile.
        var parameter = typeof(ServerNotices).GetMethod(nameof(ServerNotices.Append))!.GetParameters()[0];

        parameter.ParameterType.Should().Be(typeof(ResolvedDataDir),
            "a string parameter would accept the root, the directory BEFORE the side is applied");
    }
}
