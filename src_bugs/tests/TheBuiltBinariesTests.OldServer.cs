using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// The PREVIOUS release, run: it has no route for a comment, and that is the whole mechanism.
/// </summary>
/// <remarks>
/// <para><b>Why the real old artefact and not a description of one.</b> Every other test of this
/// story is two halves of one checkout agreeing with each other. The promise that matters is about a
/// binary built weeks ago and running on somebody's host, and the only thing that can be asked about
/// that binary is the binary. A plan round put it plainly: a capability the client merely ASKS about
/// cannot make an old server refuse — the probe reaches one node and the POST reaches another during
/// a rollout, and by then the comment is gone and a retry is answered <c>duplicate</c>. So a comment
/// travels on a route the old binary does not have, and this asserts both halves of that: 404 on the
/// new route, and the old route accepting the same document with nowhere to put the field.</para>
/// <para><b>Pinned, not "the latest release".</b> The tag, the file name and the SHA-256 are
/// constants here, and the archive is verified against the hash before anything in it is executed.
/// A test that downloads whatever is newest would one day download a build that HAS the route and
/// pass by agreeing with itself; and running an unverified binary from the network inside a test
/// suite is not something this repository does.</para>
/// <para><b>It SKIPS locally and FAILS on CI.</b> The archive is linux-x64 and most work here
/// happens on Windows, so the honest local answer is a skip with a reason. On CI the workflow
/// downloads the archive and sets the variable, so an unset variable there means the download step
/// was removed or broke — which must be red, because a silent skip of this one test is a silent
/// removal of the only evidence the mechanism works.</para>
/// <para>A partial of <see cref="TheBuiltBinariesTests"/> rather than a class of its own, which is
/// where the plan first put it: the machinery for launching a real server on a real port, draining
/// its pipes and retrying a lost port is all here already, along with the throwaway data directory
/// and the collection attribute that stops process-launching tests running beside each other. A
/// second class would have copied that or exported it; the harness's own docblock says why it is
/// bound to this fixture.</para>
/// </remarks>
public sealed partial class TheBuiltBinariesTests
{
    /// <summary>The release this story's promise is made against.</summary>
    private const string OldTag = "bugs-v0.2.0";

    /// <summary>The variable CI fills with the downloaded archive's path.</summary>
    private const string OldArchiveVariable = "COAI_BUGS_OLD_ARCHIVE";

    /// <summary>
    /// The published checksum of that release's archive, per architecture, as its own `.sha256`
    /// assets state them.
    /// </summary>
    /// <remarks>
    /// Both, because the release workflow is a matrix and one leg of it runs on `ubuntu-24.04-arm`.
    /// A single x64 hash there would refuse the right archive as tampered-with — a failure that
    /// reads as a supply-chain alarm and is really a missing row in this table.
    /// </remarks>
    private static readonly IReadOnlyDictionary<Architecture, string> OldArchiveSha256 =
        new Dictionary<Architecture, string>
        {
            [Architecture.X64] = "d203a043969925f9cf2356643c4c854c543628e009280e24e5fdc6bfdb372a97",
            [Architecture.Arm64] = "2ef0ddbd84aa72dd6fc3d64a75edd1fbf8964b5c808868ac25adc29ee9d490cb",
        };

    [Fact]
    public async Task TheOldServerHasNoCommentedRouteAndDropsACommentSentToTheOldOne()
    {
        var archive = TheOldArchiveOrSkip();
        var exe = Unpacked(archive);

        // The pin and the unpacking are checked on any platform — they are file arithmetic. RUNNING
        // it is not: the release publishes linux archives only, so anywhere else the honest answer
        // is a skip AFTER the verification rather than "not a valid application for this OS
        // platform" out of the process launcher, which is a true sentence that reads as a broken
        // test.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Assert.Skip($"{OldTag} ships linux archives only; this one verified but cannot run here");
        }

        var (server, port) = await Serving(string.Empty, exe: exe);
        try
        {
            var issued = Run(exe, "--issue-key --note the-old-release", _dir);
            issued.Code.Should().Be(0, $"the old one-shot said: {issued.Err}");
            using var http = Talking(port, issued.Out.Trim());

            using var refused = await http.PostAsJsonAsync(
                "/ingest/commented", OneCommentedPair, TestContext.Current.CancellationToken);

            refused.StatusCode.Should().Be(
                HttpStatusCode.NotFound,
                $"{OldTag} has no route for a comment, which is what makes the client's refusal "
                + "structural rather than a matter of deployment order");

            // And the defect the route exists to prevent, demonstrated rather than assumed: the SAME
            // document on the old route is a perfectly ordinary success.
            using var accepted = await http.PostAsJsonAsync(
                "/ingest", OneCommentedPair, TestContext.Current.CancellationToken);

            accepted.StatusCode.Should().Be(HttpStatusCode.OK);
            (await accepted.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
                .Should().Contain("accepted", "it takes the pair and says nothing about the field it dropped");
        }
        finally
        {
            Stop(server);
        }

        TestSql.Column(Path.Combine(_dir, "coai-bugs.db"), "SELECT name FROM pragma_table_info('quarantine')")
            .Should().NotContain(
                "comment",
                "there was nowhere for it to go: a server older than this story drops an unknown "
                + "field in silence, and the person who typed it is told their pair was accepted");
    }

    /// <summary>The verified archive, or a skip that says why there is nothing to run.</summary>
    /// <remarks>
    /// The hash is checked BEFORE anything is extracted, let alone executed. An archive that does
    /// not match is a failure and never a skip: something fetched the wrong artefact, and running it
    /// would be running an unknown binary to find out what it does.
    /// </remarks>
    private static string TheOldArchiveOrSkip()
    {
        var given = Environment.GetEnvironmentVariable(OldArchiveVariable) ?? string.Empty;
        if (given.Length == 0)
        {
            Environment.GetEnvironmentVariable("CI").Should().BeNullOrEmpty(
                $"CI downloads {OldTag}'s archive and sets {OldArchiveVariable}; unset there means "
                + "the download step is gone, and this is the only test that runs the old artefact");
            Assert.Skip($"{OldArchiveVariable} is unset: {OldTag}'s archive is not here");
        }

        var here = RuntimeInformation.ProcessArchitecture;
        OldArchiveSha256.Should().ContainKey(
            here, $"{OldTag} published no archive for {here}, so there is nothing to pin");
        File.Exists(given).Should().BeTrue($"{OldArchiveVariable} points at {given}");
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(given)))
            .Should().Be(
                OldArchiveSha256[here],
                $"the archive must be the {here} one {OldTag} published, byte for byte — it is "
                + "about to be executed");

        return given;
    }

    /// <summary>Extracts the server out of the archive and makes it runnable.</summary>
    /// <remarks>
    /// Into the test's own throwaway directory, which the fixture deletes — a released binary left
    /// behind on a runner is a released binary somebody later runs by accident.
    /// </remarks>
    private string Unpacked(string archive)
    {
        var into = Path.Combine(_dir, "old");
        Directory.CreateDirectory(into);
        using (var file = File.OpenRead(archive))
        using (var plain = new GZipStream(file, CompressionMode.Decompress))
        {
            TarFile.ExtractToDirectory(plain, into, overwriteFiles: true);
        }

        var exe = Directory.EnumerateFiles(into, "coai-bugs", SearchOption.AllDirectories).FirstOrDefault();
        exe.Should().NotBeNull($"{OldTag}'s archive carries the server it was built to carry");

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // tar keeps the mode, but an archive re-packed by anything that does not is a file the
            // runner refuses to execute — with an error that reads as a missing binary.
            File.SetUnixFileMode(
                exe!,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        return exe!;
    }
}
