using System.Text.Json;
using CoaiMcp.Server;

namespace CoaiMcp.Tests;

/// <summary>The notices of one code in a notices file a live server may still be writing — issue #512.</summary>
/// <remarks>
/// Read the way the product reads its data directory — <see cref="SharedRead"/>, which does not forbid the
/// live server's append (<c>File.ReadAllLines</c> did, and Windows refused the read). Only TERMINATED
/// lines are notices: an unterminated tail is an append still in flight, "not yet", while a terminated line
/// that is not a notice — malformed, or empty — still throws, because that is a defect in the writer and
/// hiding it would hide that.
/// </remarks>
internal static class NoticeLines
{
    public static string[] Of(string file, string code) =>
        (File.Exists(file) ? Terminated(SharedRead.Text(file)) : [])
            .Where(line => JsonDocument.Parse(line).RootElement.GetProperty("code").GetString() == code)
            .ToArray();

    /// <summary>Every line that has its newline; the tail after the last one is still being written.</summary>
    private static IEnumerable<string> Terminated(string text) =>
        text.Split('\n')[..^1].Select(line => line.TrimEnd('\r'));
}
