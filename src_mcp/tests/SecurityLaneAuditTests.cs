using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CoaiMcp.Core.Security;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Operator-requested, sequential audit of a committed change using every shipped module.</summary>
public sealed class SecurityLaneAuditTests
{
    [Fact(Explicit = true)]
    public async Task Each_module_reviews_the_committed_change_on_Windows_Ollama()
    {
        var repo = Required("COAI_SECURITY_AUDIT_REPO");
        var baseline = Required("COAI_SECURITY_AUDIT_BASE");
        var output = Required("COAI_SECURITY_AUDIT_OUT");
        var scope = await File.ReadAllTextAsync(Path.Combine(repo, "todo", "PLAN_a_security_lane_runs_beside_the_gate.md"));
        Directory.CreateDirectory(output);
        var launcher = new ProcessLauncher();
        var head = await launcher.RunAsync(new("git", ["rev-parse", "HEAD"], repo));
        head.ExitCode.Should().Be(0);
        await File.WriteAllTextAsync(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new
        {
            repo,
            baseline,
            head = head.StdOut.Trim(),
            utc = DateTimeOffset.UtcNow,
            model = "Qwen3.5-35B-A3B-Q5_vk128:latest",
            contextTokens = 131072,
            ordinaryReviewer = "FakeCli clean response; this campaign measures only the local security lane",
            limitation = "One pass per module, no executed reproductions, input coverage unverified",
            prompts = SecurityCatalog.Prompts.Select(p => new
            {
                id = p.Id,
                sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RolePrompts.ShippedDefaultFor(p.Id)))),
            }),
        }, new JsonSerializerOptions { WriteIndented = true }));
        var failures = new List<string>();
        foreach (var prompt in SecurityCatalog.Prompts)
        {
            try
            {
                await SecurityLaneCalibrationTests.RunCell(repo, baseline, Path.Combine(output, prompt.Id), launcher,
                    scope, [prompt.Id]);
            }
            catch (Exception error)
            {
                failures.Add(prompt.Id);
                await File.WriteAllTextAsync(Path.Combine(output, prompt.Id + ".failure.txt"), error.ToString());
            }
        }
        failures.Should().BeEmpty("all twelve module attempts must complete; individual failures were retained");
    }

    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        value.Should().NotBeNullOrWhiteSpace($"set {name} explicitly before a hardware measurement");
        return value!;
    }
}
