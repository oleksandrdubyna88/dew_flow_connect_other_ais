using System.Text.Json;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Platform;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E2 code gate G1 (findings 0 and 2), as PR #922's CI round left it: "which runtime runs on a native Linux host, and from which <c>coai-mcp</c>" is ONE file, <c>shared/native-linux-admission.json</c>, and each
/// side reads or is held to THAT file — the server LOADS it (<see cref="NativeLinuxAdmission"/>; <see cref="HostAdmission.AdmittedOnNativeLinux"/> is its runtime names) and the extension's
/// <c>ON_NATIVE_LINUX_SINCE</c> is asserted against it by <c>src_vs_code/src/test/nativeLinuxAdmissionFile.test.ts</c>. No test parses the other side's source (<c>NothingReadsAnotherProgramsSourceTests</c>): that
/// shape goes quiet rather than red, which is what the first draft of this guard did.
/// </summary>
/// <remarks>
/// Every runtime the file names must also have a <c>linux</c> row in <c>shared/consultant-limitations.json</c> that says the SAME thing the file does: a <c>measured</c> basis needs a confined, measured row (an admission
/// needs the cells it was measured by); an <c>owner-decision</c> basis (the owner's decision of 2026-10-23: unmeasured is a note, not a refusal) needs a row that says it was NOT measured and why the runtime runs anyway, and the
/// file must carry the one-sentence <c>note</c> the person is shown. A name added without its row is a red test here, naming the runtime.
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
        var server = HostAdmission.AdmittedOnNativeLinux.Select(name => name.ToLowerInvariant()).Order().ToList();

        file.Should().NotBeEmpty("shared/native-linux-admission.json names no runtime — an empty file would make the comparison vacuous");
        server.Should().Equal(file, "HostAdmission.AdmittedOnNativeLinux must be exactly the runtimes shared/native-linux-admission.json names — the server loads that embedded file, so a difference means the embedded copy is not the file on disk");
    }

    [Fact]
    public void EveryMeasuredRuntimeTheFileNames_HasAConfinedMeasuredLinuxRow()
    {
        foreach (var runtime in NativeLinuxAdmission.Entries.Values.Where(entry => entry.Basis == AdmissionBasis.Measured).Select(entry => entry.Runtime.ToLowerInvariant()))
        {
            var row = ConsultantLimitations.Lookup(HostKind.Linux, runtime);

            row.Standing.Should().Be(LimitationStanding.Confined, $"{runtime} is admitted on its MEASUREMENT on a native Linux host by shared/native-linux-admission.json, so shared/consultant-limitations.json must hold a `linux` row for it that says `confined` (found: {row.Standing})");
            row.Evidence.Should().BeOfType<LimitationEvidence.Measured>($"{runtime}'s `linux` row in shared/consultant-limitations.json carries no `measured` field — a measured admission needs the cells it was measured by");
        }
    }

    /// <summary>The owner's decision of 2026-10-23: the row says NOT measured (a note, never a refusal — nothing reads a standing to refuse) and names the decision; the file carries the note the launch shows.</summary>
    [Fact]
    public void EveryOwnerDecisionRuntimeTheFileNames_HasAnUnmeasuredLinuxRowThatSaysSo_AndAFileNote()
    {
        var owner = NativeLinuxAdmission.Entries.Values.Where(entry => entry.Basis == AdmissionBasis.OwnerDecision).ToList();

        owner.Should().NotBeEmpty("Kimi is admitted on a native Linux host by the owner's decision of 2026-10-23 — if none is, this guard is vacuous and the decision was dropped");
        foreach (var entry in owner)
        {
            var row = ConsultantLimitations.Lookup(HostKind.Linux, entry.Runtime);

            row.Standing.Should().Be(LimitationStanding.Unmeasured, $"{entry.Runtime} is admitted on a native Linux host by the owner's decision, not a measurement, so its `linux` row must say `unmeasured` (found: {row.Standing})");
            row.Evidence.Should().BeOfType<LimitationEvidence.Sourced>($"{entry.Runtime}'s `linux` row says what was measured, so it carries `source`, not `measured`");
            row.Text.Should().Contain("owner's decision", $"{entry.Runtime}'s `linux` row must say why it runs here although it is not measured");
            entry.Note.Should().Contain("not measured").And.Contain("owner's decision", $"{entry.Runtime}'s `note` in shared/native-linux-admission.json is the sentence the person is shown");
        }
    }

    /// <summary>Only Kimi's launch carries the note (KimiLaunch.HostNote); a runtime whose launch does not must stay on a measurement, or the owner's note would never reach the person.</summary>
    [Fact]
    public void VibeIsAdmittedOnItsMeasurement_BecauseItsLaunchCarriesNoHostNote()
    {
        NativeLinuxAdmission.Entries["vibe"].Basis.Should().Be(AdmissionBasis.Measured);
        NativeLinuxAdmission.Entries["kimi"].Basis.Should().Be(AdmissionBasis.OwnerDecision);
        NativeLinuxAdmission.Entries["kimi"].Since.Should().Be("0.64.0");
    }

    [Fact]
    public void TheNamesAreMatchedCaseInsensitively_AsTheAdmissionAsksThem()
    {
        HostAdmission.AdmittedOnNativeLinux.Should().Contain("VIBE").And.Contain("vibe");
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
    [InlineData("{\"runtimes\": [{\"runtime\": \"vibe\", \"source\": \"x\", \"basis\": \"measured\"}]}", "no `since`")]
    [InlineData("{\"runtimes\": [{\"since\": \"0.64.0\", \"source\": \"x\", \"basis\": \"measured\"}]}", "no `runtime`")]
    [InlineData("{\"runtimes\": [{\"runtime\": \"vibe\", \"since\": \"0.64.0\", \"basis\": \"measured\"}]}", "no `source`")]
    [InlineData("{\"runtimes\": [{\"runtime\": \" \", \"since\": \"0.64.0\", \"source\": \"x\", \"basis\": \"measured\"}]}", "no `runtime`")]
    [InlineData("{\"runtimes\": [{\"runtime\": \"vibe\", \"since\": \"0.64.0\", \"source\": \"x\"}]}", "no `basis`")]
    [InlineData("{\"runtimes\": [{\"runtime\": \"vibe\", \"since\": \"0.64.0\", \"source\": \"x\", \"basis\": \"trust-me\"}]}", "basis `trust-me`")]
    [InlineData("{\"runtimes\": [{\"runtime\": \"kimi\", \"since\": \"0.64.0\", \"source\": \"x\", \"basis\": \"owner-decision\"}]}", "no `note`")]
    public void ADocumentThatIsLostOrBlanked_IsRefusedByName_NotRead_AsAnEmptyAdmission(string json, string sentence)
    {
        var parse = () => NativeLinuxAdmission.Parse(json);

        parse.Should().Throw<InvalidOperationException>().WithMessage($"*{sentence}*");
    }

    [Fact]
    public void ADocumentThatNamesTwoRuntimes_AdmitsBothWithTheirOwnRelease()
    {
        var since = NativeLinuxAdmission.Parse("{\"runtimes\": [{\"runtime\": \"vibe\", \"since\": \"0.64.0\", \"source\": \"a\", \"basis\": \"measured\"}, {\"runtime\": \"kimi\", \"since\": \"0.65.0\", \"source\": \"b\", \"basis\": \"owner-decision\", \"note\": \"n\"}]}");

        since["Kimi"].Should().Be("0.65.0");
        since["vibe"].Should().Be("0.64.0");
    }
}
