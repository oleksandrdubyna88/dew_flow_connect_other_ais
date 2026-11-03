namespace CoaiMcp.Tests;

/// <summary>xUnit's generated entry point (xunit.v3 4.0.1), unchanged, behind the run-folder gate (E5 S3): a start without a proven run folder ends with exit 78 before discovery.</summary>
[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
internal static class TestEntryPoint
{
    public static int Main(string[] args) => global::CoaiMcp.Tests.RunTemp.Gate(args, static a =>
        global::System.Linq.Enumerable.Any(a, arg => arg == "-automated" || arg == "@@")
            ? global::Xunit.Runner.InProc.SystemConsole.ConsoleRunner.Run(a).GetAwaiter().GetResult()
            : global::Xunit.MicrosoftTestingPlatform.TestPlatformTestFramework.RunAsync(a, global::CoaiMcp.Tests.SelfRegisteredExtensions.AddSelfRegisteredExtensions).GetAwaiter().GetResult());
}
