using System.Text.Json;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Platform;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E2 code gate G1 (findings 0 and 2), as PR #922's CI round left it: "which runtime runs on a native Linux host, and from which <c>coai-mcp</c>" is ONE file, <c>shared/native-linux-admission.json</c>, and each
/// side reads or is held to THAT file — the server LOADS it (<see cref="NativeLinuxAdmission"/>; <see cref="HostAdmission.MeasuredOnNativeLinux"/> is its runtime names) and the extension's
/// <c>ON_NATIVE_LINUX_SINCE</c> is asserted against it by <c>src_vs_code/src/test/nativeLinuxAdmissionFile.test.ts</c>. No test parses the other side's source (<c>NothingReadsAnotherProgramsSourceTests</c>): that
/// shape goes quiet rather than red, which is what the first draft of this guard did.
/// </summary>
/// <remarks>
/// Every runtime the file names must also have a measured, confined <c>linux</c> row in <c>shared/consultant-limitations.json</c> — an admission needs the cells it was measured by. A name added to the file
/// without that row (Kimi, before E3) is a red test here, naming the runtime.
/// </remarks>
public sealed class NativeLinuxAdmissionFileTests
{
    private static string SharedFile() => File.ReadAllText(Path.Combine(ProductionSources.RepositoryRoot(), "shared", "native-linux-admission.json"));

    private static IReadOnlyList<string> NamesOnDisk()
    {
        using var document = JsonDocument.Parse(SharedFile());

        return [.. document.RootElement.GetProperty("runtimes").EnumerateArray().Select(entry => entry.GetProperty("runtime").GetString()!.ToLowerInvariant()).Order()];
    }

    [Fact]
    public void TheServersSet_IsExactlyTheRuntimesTheSharedFileNames()
    {
        var file = NamesOnDisk();
        var server = HostAdmission.MeasuredOnNativeLinux.Select(name => name.ToLowerInvariant()).Order().ToList();

        file.Should().NotBeEmpty("shared/native-linux-admission.json names no runtime — an empty file would make the comparison vacuous");
        server.Should().Equal(file, "HostAdmission.MeasuredOnNativeLinux must be exactly the runtimes shared/native-linux-admission.json names — the server loads that embedded file, so a difference means the embedded copy is not the file on disk");
    }

    [Fact]
    public void EveryRuntimeTheFileNames_HasAConfinedMeasuredLinuxRow()
    {
        foreach (var runtime in NamesOnDisk())
        {
            var row = ConsultantLimitations.Lookup(HostKind.Linux, runtime);

            row.Standing.Should().Be(LimitationStanding.Confined, $"{runtime} is admitted on a native Linux host by shared/native-linux-admission.json, so shared/consultant-limitations.json must hold a `linux` row for it that says `confined` (found: {row.Standing})");
            row.Evidence.Should().BeOfType<LimitationEvidence.Measured>($"{runtime}'s `linux` row in shared/consultant-limitations.json carries no `measured` field — an admission needs the cells it was measured by");
        }
    }

    [Fact]
    public void TheNamesAreMatchedCaseInsensitively_AsTheAdmissionAsksThem()
    {
        HostAdmission.MeasuredOnNativeLinux.Should().Contain("VIBE").And.Contain("vibe");
        NativeLinuxAdmission.Since["VIBE"].Should().Be(NativeLinuxAdmission.Since["vibe"]);
    }

    [Fact]
    public void EveryRuntimeHasAReleaseThatLooksLikeOne()
    {
        NativeLinuxAdmission.Since.Should().NotBeEmpty();
        NativeLinuxAdmission.Since.Select(pair => (pair.Key, Ok: Version.TryParse(pair.Value, out _))).Where(one => !one.Ok).Should().BeEmpty("a `since` in shared/native-linux-admission.json must be a release number such as 0.64.0");
    }

    // ---------- the loader refuses a document that cannot be trusted: a lost or blanked file must not read as "no runtime" ----------

    [Theory]
    [InlineData("{}", "no `runtimes` array")]
    [InlineData("{\"runtimes\": []}", "names no runtime")]
    [InlineData("{\"runtimes\": [{\"runtime\": \"vibe\", \"source\": \"x\"}]}", "no `since`")]
    [InlineData("{\"runtimes\": [{\"since\": \"0.64.0\", \"source\": \"x\"}]}", "no `runtime`")]
    [InlineData("{\"runtimes\": [{\"runtime\": \"vibe\", \"since\": \"0.64.0\"}]}", "no `source`")]
    [InlineData("{\"runtimes\": [{\"runtime\": \" \", \"since\": \"0.64.0\", \"source\": \"x\"}]}", "no `runtime`")]
    public void ADocumentThatIsLostOrBlanked_IsRefusedByName_NotRead_AsAnEmptyAdmission(string json, string sentence)
    {
        var parse = () => NativeLinuxAdmission.Parse(json);

        parse.Should().Throw<InvalidOperationException>().WithMessage($"*{sentence}*");
    }

    [Fact]
    public void ADocumentThatNamesTwoRuntimes_AdmitsBothWithTheirOwnRelease()
    {
        var since = NativeLinuxAdmission.Parse("{\"runtimes\": [{\"runtime\": \"vibe\", \"since\": \"0.64.0\", \"source\": \"a\"}, {\"runtime\": \"kimi\", \"since\": \"0.65.0\", \"source\": \"b\"}]}");

        since["Kimi"].Should().Be("0.65.0");
        since["vibe"].Should().Be("0.64.0");
    }
}
