namespace CoaiMcp.Tests;

/// <summary>
/// The <c>coai-mcp</c> executable a process-level test runs: the one this build produced, or a
/// published one.
/// </summary>
/// <remarks>
/// One copy. It was written out four times — in <c>CloseConsultCliScenarioTests</c>,
/// <c>LogCliScenarioTests</c> and <c>McpContractTests</c>, identical but for their comments, and as
/// <c>ShimExe</c> in <c>RemoteShimScenarioTests</c> — and
/// epic 3's run-marker scenario would have been the fourth. (reuse-first.md: the second copy is the
/// defect, and extracting the shared half is the work.)
/// </remarks>
internal static class ServerBinary
{
    /// <summary>The path to run.</summary>
    internal static string Path
    {
        get
        {
            // COAI_CONTRACT_EXE points these tests at a PUBLISHED binary — the release smoke.
            if (Environment.GetEnvironmentVariable("COAI_CONTRACT_EXE") is { Length: > 0 } published)
            {
                return published;
            }

            var configuration = AppContext.BaseDirectory.Contains("Release") ? "Release" : "Debug";

            // tests/bin/<cfg>/net10.0 → src/bin/<cfg>/net10.0/coai-mcp
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "src", "bin", configuration, "net10.0",
                OperatingSystem.IsWindows() ? "coai-mcp.exe" : "coai-mcp"));
        }
    }
}
