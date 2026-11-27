using System.Text.Json;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// What <c>codex doctor --json</c> says about the configuration the CLI resolved (epoch 3 E3 s5 t3, ids CX-C4 and CX-C3; judgement 13.3, J18): the permission profile <c>coai</c>, the filesystem and the network sandbox <c>restricted</c>, the approval policy <c>Never</c> and <c>mcp servers = 0</c>.
/// The report says of itself that read and write access is not tested, so this is the CLI's statement of what it resolved, never a proof that a write is refused (the sandbox rows are that). The exit code of the command is never read: its network checks can fail.
/// </summary>
internal static class CodexDoctorReport
{
    /// <summary>The fields the rows read, each null when the report has none.</summary>
    internal sealed record Fields(bool ConfigLoaded, string? Profile, string? FilesystemSandbox, string? NetworkSandbox, string? Approval, string? McpServers);

    /// <summary>The fields of a report, or null when the text is not a doctor report (no JSON, no <c>checks</c>).</summary>
    internal static Fields? Read(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("checks", out var checks) || checks.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var load = Check(checks, "config.load");

            return new Fields(
                load is { } config && Text(config, "status") == "ok" && !(Text(config, "summary") ?? string.Empty).Contains("could not be loaded", StringComparison.Ordinal),
                Detail(Check(checks, "sandbox.filesystem_paths"), "permission profile"),
                Detail(Check(checks, "sandbox.helpers"), "filesystem sandbox"),
                Detail(Check(checks, "sandbox.helpers"), "network sandbox"),
                Detail(Check(checks, "sandbox.helpers"), "approval policy"),
                Detail(load, "mcp servers"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The sentence that says the permission profile is not the one coai wrote, or empty (CX-C4). A missing field is a problem, never a pass.</summary>
    internal static string ProfileProblem(Fields? report)
    {
        if (report is null)
        {
            return "the doctor report of the CLI could not be read";
        }

        var wrong = new List<string>();
        if (!report.ConfigLoaded)
        {
            wrong.Add("the config could not be loaded");
        }

        Expect(wrong, report.Profile, "coai", "permission profile");
        Expect(wrong, report.FilesystemSandbox, "restricted", "filesystem sandbox");
        Expect(wrong, report.NetworkSandbox, "restricted", "network sandbox");
        Expect(wrong, report.Approval, "Never", "approval policy");

        return wrong.Count == 0 ? string.Empty : "the doctor report of the CLI does not show the profile coai: " + string.Join(", ", wrong);
    }

    /// <summary>The sentence that says an MCP server is configured, or empty when the report says there are none (CX-C3).</summary>
    internal static string McpProblem(Fields? report) => report is null
        ? "the doctor report of the CLI could not be read"
        : report.McpServers == "0" ? string.Empty : "the doctor report of the CLI does not say that no MCP server is configured";

    private static void Expect(List<string> wrong, string? seen, string wanted, string name)
    {
        if (seen is null)
        {
            wrong.Add($"no {name} in the report");
        }
        else if (seen != wanted)
        {
            wrong.Add($"the {name} is not {wanted}");
        }
    }

    private static JsonElement? Check(JsonElement checks, string id) => checks.TryGetProperty(id, out var check) && check.ValueKind == JsonValueKind.Object ? check : null;

    private static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? Detail(JsonElement? check, string name) =>
        check is { } found && found.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Object ? Text(details, name) : null;
}
