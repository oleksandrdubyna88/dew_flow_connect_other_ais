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
    private static readonly JsonSerializerOptions ManifestJson = new() { WriteIndented = true };
    [Fact(Explicit = true)]
    public async Task Each_module_reviews_the_committed_change_on_Windows_Ollama()
    {
        var repo = Required("COAI_SECURITY_AUDIT_REPO");
        var baseline = Required("COAI_SECURITY_AUDIT_BASE");
        var output = Required("COAI_SECURITY_AUDIT_OUT");
        var contextTokens = int.Parse(Environment.GetEnvironmentVariable("COAI_SECURITY_AUDIT_CONTEXT_TOKENS") ?? "24000",
            System.Globalization.CultureInfo.InvariantCulture);
        // The model's window is an operator SETTING for this run, not something the audit measures.
        var modelContextTokens = int.Parse(Environment.GetEnvironmentVariable(ModelContextTokensVariable) ?? "131072",
            System.Globalization.CultureInfo.InvariantCulture);
        contextTokens.Should().BeInRange(1024, modelContextTokens, "the slice budget must fit the model window configured for this run");
        var selected = Environment.GetEnvironmentVariable("COAI_SECURITY_AUDIT_PROMPTS")?.Split(',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var modules = selected is null ? SecurityCatalog.Prompts.ToArray()
            : selected.Select(id => SecurityCatalog.Prompts.Single(p => p.Id == id)).DistinctBy(p => p.Id).ToArray();
        modules.Should().NotBeEmpty("select at least one known module before a hardware measurement");
        var scopeFile = ScopeFile(repo);
        var scope = await File.ReadAllTextAsync(scopeFile);
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
            model = SecurityLaneCalibrationTests.Model,
            endpoint = SecurityLaneCalibrationTests.Endpoint,
            configuredModelContextTokens = modelContextTokens,
            modelContextTokensSource = $"{ModelContextTokensVariable} (default 131072); configured by the operator, not measured by this run",
            scopeFile,
            contextTokens,
            ordinaryReviewer = "FakeCli clean response; this campaign measures only the local security lane",
            limitation = "One pass per module, no executed reproductions, input coverage unverified",
            prompts = modules.Select(p => new
            {
                id = p.Id,
                sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RolePrompts.ShippedDefaultFor(p.Id)))),
            }),
        }, ManifestJson));
        var failures = new List<string>();
        foreach (var prompt in modules)
        {
            try
            {
                await SecurityLaneCalibrationTests.RunCell(repo, baseline, head.StdOut.Trim(), Path.Combine(output, prompt.Id), launcher,
                    scope, [prompt.Id], contextTokens);
            }
            catch (Exception error)
            {
                failures.Add(prompt.Id);
                await File.WriteAllTextAsync(Path.Combine(output, prompt.Id + ".failure.txt"), error.ToString());
            }
        }
        failures.Should().BeEmpty("all selected module attempts must complete; individual failures were retained");
    }

    private const string ModelContextTokensVariable = "COAI_SECURITY_AUDIT_MODEL_CONTEXT_TOKENS";
    private const string ScopePlan = "PLAN_a_security_lane_runs_beside_the_gate.md";

    /// <summary>
    /// The scope text the audited change is reviewed against: an explicit <c>COAI_SECURITY_AUDIT_SCOPE</c>
    /// file, else the lane's plan wherever the audited checkout keeps it — <c>todo/</c> while open,
    /// <c>research/</c> once promoted — so the audit survives the plan's promotion.
    /// </summary>
    private static string ScopeFile(string repo)
    {
        var explicitScope = Environment.GetEnvironmentVariable("COAI_SECURITY_AUDIT_SCOPE");
        if (!string.IsNullOrWhiteSpace(explicitScope))
        {
            File.Exists(explicitScope).Should().BeTrue($"COAI_SECURITY_AUDIT_SCOPE names {explicitScope}, which must exist");
            return explicitScope;
        }
        string[] candidates = [Path.Combine(repo, "todo", ScopePlan), Path.Combine(repo, "research", ScopePlan)];
        var found = candidates.FirstOrDefault(File.Exists);
        found.Should().NotBeNull($"set COAI_SECURITY_AUDIT_SCOPE, or keep {ScopePlan} in todo/ or research/ of the audited repo");
        return found!;
    }

    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        value.Should().NotBeNullOrWhiteSpace($"set {name} explicitly before a hardware measurement");
        return value!;
    }
}
