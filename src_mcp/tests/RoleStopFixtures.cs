using System.Text.Json;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Tests;

/// <summary>
/// One recorded stop of a vendor CLI (<c>tests/fixtures/role-stops/*.json</c>, epoch 3 E1 s3 t1): what the CLI really printed, with its version and the day it was recorded. A fixture is a
/// <see cref="ProcessResult"/> with a name — the failover tests hand it to the real executor, to the real classifier and to the fake CLI, so no test types a vendor sentence of its own.
/// </summary>
internal sealed record RoleStopFixture(string Name, string Cli, string CliVersion, string RecordedUtc, string RecordedBy, int ExitCode, string StdOut, string StdErr, IReadOnlyList<string> Argv)
{
    /// <summary>The recorded run as the launcher would have returned it.</summary>
    internal ProcessResult AsProcessResult() => new(ExitCode, StdOut, StdErr, TimedOut: false);

    /// <summary>True for a fixture this story recorded live; false for one carried over from earlier work (the file says which).</summary>
    internal bool RecordedByThisStory => RecordedBy.StartsWith("this story", StringComparison.Ordinal);
}

internal static class RoleStopFixtures
{
    internal static string Folder => Path.Combine(AppContext.BaseDirectory, "fixtures", "role-stops");

    internal static IReadOnlyList<string> Names => [.. Directory.EnumerateFiles(Folder, "*.json").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal).Select(name => name!)];

    internal static RoleStopFixture Load(string name)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder, name + ".json")));
        var root = document.RootElement;

        return new RoleStopFixture(
            name,
            Text(root, "cli"),
            Text(root, "cliVersion"),
            Text(root, "recordedUtc"),
            Text(root, "recordedBy"),
            root.TryGetProperty("exitCode", out var exit) && exit.TryGetInt32(out var code) ? code : -1,
            Text(root, "stdout"),
            Text(root, "stderr"),
            [.. root.GetProperty("argv").EnumerateArray().Select(arg => arg.GetString() ?? string.Empty)]);
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
}
