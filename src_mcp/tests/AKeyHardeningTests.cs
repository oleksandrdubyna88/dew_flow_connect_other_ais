using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server.Keys;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The five must-change findings of the security judgement of s7 part 1 (<c>v2/epoch-3/e2-coder-one-task/RESULTS_s7_security_judgement.md</c>), each with a test that was RED on cca029ee from the missing behaviour, and the should-change
/// findings that have a test. Every token is made up at run time and carries the sentinel word, so a test that asserts the sentinel absent proves that no part of any key reached the place.
/// </summary>
[System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
[Collection("key-process-environment")]
public sealed partial class AKeyHardeningTests
{
    private const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private static string[] Everything(string folder) => Directory.GetFiles(folder, "*", SearchOption.AllDirectories);

    private static string AllText(string folder) => string.Join('\n', Everything(folder).Where(file => !file.EndsWith(".key", StringComparison.Ordinal)).Select(File.ReadAllText));
}
