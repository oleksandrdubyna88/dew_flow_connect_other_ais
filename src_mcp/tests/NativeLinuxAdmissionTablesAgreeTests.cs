using System.Text.RegularExpressions;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Platform;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E2 code gate G1 (findings 0 and 2): the server and the extension each hold a table of "which runtime runs on a native Linux host" — <see cref="HostAdmission.MeasuredOnNativeLinux"/> (the SET the
/// admission asks) and <c>ON_NATIVE_LINUX_SINCE</c> in <c>src_vs_code/src/runtimeSince.ts</c> (the release from which the extension PROMISES it). They encode different facts — a version cannot be derived from a
/// set — but they must name the SAME runtimes, and each named runtime must have a measured, confined <c>linux</c> row in <c>shared/consultant-limitations.json</c>. A one-sided change (Kimi added to the server
/// and not to the extension, or the reverse) is a red test here, naming the side that lacks it.
/// </summary>
/// <remarks>
/// Why a C# test and not a node one: the server side is read as the REAL set and the REAL loaded rows (no regex over C#), and only the extension's small object literal is read as text — the cheaper half to
/// parse. The TypeScript table is a flat <c>runtime: CONSTANT</c> record, so the names are the keys of that one block.
/// </remarks>
public sealed partial class NativeLinuxAdmissionTablesAgreeTests
{
    [GeneratedRegex(@"export const ON_NATIVE_LINUX_SINCE\s*:[^=]*=\s*\{(?<body>[^}]*)\}\s*;", RegexOptions.Singleline)]
    private static partial Regex TableLiteral();

    [GeneratedRegex(@"^\s*(?<name>[A-Za-z_][\w-]*)\s*:", RegexOptions.Multiline)]
    private static partial Regex Key();

    /// <summary>The runtime names the extension's <c>ON_NATIVE_LINUX_SINCE</c> block holds, lower-cased. Fails when the block is gone or empty — a missing table is not "no runtimes".</summary>
    internal static IReadOnlyList<string> ExtensionNames(string runtimeSinceSource)
    {
        var table = TableLiteral().Match(runtimeSinceSource);
        table.Success.Should().BeTrue("src_vs_code/src/runtimeSince.ts no longer holds `export const ON_NATIVE_LINUX_SINCE: … = { … };` — this guard reads that block, so it must be re-pointed, not skipped");

        var names = Key().Matches(table.Groups["body"].Value).Select(match => match.Groups["name"].Value.ToLowerInvariant()).Order().ToList();
        names.Should().NotBeEmpty("the extension's ON_NATIVE_LINUX_SINCE block names no runtime — an empty table would make the comparison vacuous");

        return names;
    }

    internal static IReadOnlyList<string> ServerNames(IEnumerable<string> measured) => [.. measured.Select(name => name.ToLowerInvariant()).Order()];

    private static string RuntimeSince() => File.ReadAllText(Path.Combine(ProductionSources.RepositoryRoot(), "src_vs_code", "src", "runtimeSince.ts"));

    [Fact]
    public void TheServersSet_AndTheExtensionsTable_NameTheSameRuntimes()
    {
        var server = ServerNames(HostAdmission.MeasuredOnNativeLinux);
        var extension = ExtensionNames(RuntimeSince());

        server.Except(extension).Should().BeEmpty(
            "HostAdmission.MeasuredOnNativeLinux admits these runtimes on a native Linux host but the extension's ON_NATIVE_LINUX_SINCE (src_vs_code/src/runtimeSince.ts) has no entry — its card would tell a person the row is refused where the server runs it");
        extension.Except(server).Should().BeEmpty(
            "the extension's ON_NATIVE_LINUX_SINCE (src_vs_code/src/runtimeSince.ts) promises these runtimes on a native Linux host but HostAdmission.MeasuredOnNativeLinux does not admit them — the card would promise a launch the server refuses");
    }

    [Fact]
    public void EveryRuntimeBothTablesName_HasAConfinedMeasuredLinuxRow()
    {
        var runtimes = ServerNames(HostAdmission.MeasuredOnNativeLinux).Union(ExtensionNames(RuntimeSince())).ToList();

        foreach (var runtime in runtimes)
        {
            var row = ConsultantLimitations.Lookup(HostKind.Linux, runtime);

            row.Standing.Should().Be(LimitationStanding.Confined, $"{runtime} is admitted on a native Linux host, so shared/consultant-limitations.json must hold a `linux` row for it that says `confined` (found: {row.Standing})");
            row.Evidence.Should().BeOfType<LimitationEvidence.Measured>($"{runtime}'s `linux` row in shared/consultant-limitations.json carries no `measured` field — an admission needs the cells it was measured by");
        }
    }

    // ---------- the guard has teeth: the break-it, as a test of the comparison itself ----------

    [Fact]
    public void TheReader_FindsEveryKeyOfTheTable_AndANewEntryOnOneSideIsSeen()
    {
        var source = "export const ON_NATIVE_LINUX_SINCE: Readonly<Record<string, string>> = {\n  vibe: VIBE_ON_NATIVE_LINUX_SINCE,\n  kimi: KIMI_ON_NATIVE_LINUX_SINCE,\n};\n";

        ExtensionNames(source).Should().Equal("kimi", "vibe");
        ServerNames(["Vibe"]).Except(ExtensionNames(source)).Should().BeEmpty();
        ExtensionNames(source).Except(ServerNames(["Vibe"])).Should().Equal(["kimi"], "an extension-only Kimi is the difference the guard exists to see");
    }
}
