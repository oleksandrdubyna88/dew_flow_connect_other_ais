using System.Text;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Core.Work;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// Toolchain files for the check-runner tests, written as text and read back by the REAL reader — a fixture the code rejects proves nothing, so <see cref="Read"/> insists the file is accepted.
/// Every command starts the fake CLI by verb, so a step is a real process and nothing is mocked but the vendor.
/// </summary>
internal static class ToolchainFixture
{
    /// <summary>A YAML double-quoted scalar: backslash and double quote escaped, nothing else.</summary>
    internal static string Quote(string value) => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    /// <summary>One project <c>app</c> of adapter <c>dotnet</c>: the build and the test as the fake CLI's verbs (empty: not stated), then <paramref name="tail"/> (skip, timeouts).</summary>
    internal static string File(string[] build, string[] test, string tail = "", string path = "app")
    {
        var text = new StringBuilder($"version: 1\nprojects:\n  - path: {Quote(path)}\n    adapter: dotnet\n");
        Append(text, "build", build);
        Append(text, "test", test);

        return text.Append(tail).ToString();
    }

    /// <summary>
    /// One project <c>app</c> of adapter <c>dotnet</c> whose build and test are two <c>/bin/sh -c</c> scripts — a step a coder job's unit can run (the unit sees system programs only; the fake CLI lives in a home folder it does not see).
    /// An empty script leaves that step out of the file.
    /// </summary>
    internal static string Shell(string build, string test, string tail = "", string path = "app")
    {
        var text = new StringBuilder($"version: 1\nprojects:\n  - path: {Quote(path)}\n    adapter: dotnet\n");
        foreach (var (key, script) in new[] { ("build", build), ("test", test) })
        {
            if (script.Length > 0)
            {
                text.Append($"    {key}:\n      - \"/bin/sh\"\n      - \"-c\"\n      - {Quote(script)}\n");
            }
        }

        return text.Append(tail).ToString();
    }

    private static void Append(StringBuilder text, string key, string[] verb)
    {
        if (verb.Length == 0)
        {
            return;
        }

        text.Append($"    {key}:\n      - {Quote(FakeCliInvocations.Exe)}\n");
        foreach (var item in verb)
        {
            text.Append($"      - {Quote(item)}\n");
        }
    }

    /// <summary>The file read by the real reader, which must accept it.</summary>
    internal static ToolchainRead Read(string text)
    {
        var read = ToolchainFile.Read(new WorkFile.Text(text));
        read.Should().BeOfType<ToolchainRead.Configured>($"the fixture must pass the real reader: {text}");

        return read;
    }
}
