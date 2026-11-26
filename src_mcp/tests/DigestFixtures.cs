using CoaiMcp.Core.Toolchain;

namespace CoaiMcp.Tests;

/// <summary>The recordings of story s1 (<c>fixtures/toolchain/</c>), and every reader of the digest family behind one door, for the tests of E4 s4.</summary>
internal static class DigestFixtures
{
    /// <summary>The text of a recording.</summary>
    internal static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "toolchain", name));

    /// <summary>Every recording of the folder, by name.</summary>
    internal static IEnumerable<(string Name, string Text)> All() =>
        Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "toolchain"), "*.txt").OrderBy(path => path, StringComparer.Ordinal).Select(path => (Path.GetFileName(path), File.ReadAllText(path)));

    /// <summary>Every reader of the family by name, each reading a text as the step <paramref name="step"/> would: the readers do not choose by output, the caller does, so a test can ask each of them about every recording (R2).</summary>
    internal static IReadOnlyDictionary<string, Func<string, string, IReadOnlyList<DigestItem>>> Readers { get; } = new Dictionary<string, Func<string, string, IReadOnlyList<DigestItem>>>(StringComparer.Ordinal)
    {
        ["msbuild"] = MsBuildLines.Read,
        ["mtp"] = TestExecutableFailures.Read,
        ["vstest"] = DotnetTestFailures.Read,
        ["tsc"] = TscLines.Read,
        ["npm"] = NpmErrors.Read,
        ["jest"] = JestFailures.Read,
        ["vitest"] = VitestFailures.Read,
        ["node-test"] = NodeTestFailures.Read,
    };

    /// <summary>The digest of a recording as a failed step of the adapter, with the runner fact the recording belongs to (empty for a step of the file).</summary>
    internal static FailureDigestResult Digest(string name, CheckStep step, string adapter, string runner = "") =>
        FailureDigest.Of(step, adapter, Read(name), runner: runner);
}
