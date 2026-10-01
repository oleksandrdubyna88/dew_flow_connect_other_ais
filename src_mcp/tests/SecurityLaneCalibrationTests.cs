using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Explicit hardware measurement through real code rounds. Never part of unattended CI.</summary>
public sealed class SecurityLaneCalibrationTests
{
    private const string Model = "Qwen3.5-35B-A3B-Q5_vk128:latest";
    private const string Scope = """
        Review the committed invoice-search change. A caller must only read invoices belonging to
        the tenant in their authenticated claims, and search text must remain a SQL parameter.
        Report concrete regressions with a reproduction. This is an isolated, synthetic source fixture;
        no application, database or reproduction is executed by this measurement.
        """;

    [Fact(Explicit = true)]
    public async Task Three_product_rounds_record_the_operator_prompts_and_local_model()
    {
        var output = Environment.GetEnvironmentVariable("COAI_SECURITY_CALIBRATION_OUT");
        output.Should().NotBeNullOrWhiteSpace("set an explicit output directory to retain each measured cell");
        Directory.CreateDirectory(output!);
        var real = new ProcessLauncher();
        await using var repo = await TempGitRepo.InitAsync(real, "coai-security-calibration-");
        await repo.WriteAsync("Invoices.cs", Source(false));
        await repo.CommitAsync("safe calibration baseline");
        var baseline = await repo.HeadAsync();
        await repo.WriteAsync("Invoices.cs", Source(true));
        await repo.CommitAsync("synthetic tenant and query regression");
        var head = await repo.HeadAsync();
        await File.WriteAllTextAsync(Path.Combine(output!, "fixture.cs.txt"), Source(true));
        await File.WriteAllTextAsync(Path.Combine(output!, "manifest.json"), JsonSerializer.Serialize(new
        {
            model = Model,
            endpoint = "http://localhost:11434/v1",
            contextTokens = 131072,
            maxOutputTokens = 8192,
            localConcurrency = 1,
            repeats = 3,
            baseline,
            head,
            sourceHash = Hash(Source(true)),
            utc = DateTimeOffset.UtcNow,
            prompts = new[] { "redteam-authz", "redteam-sql" }.Select(id => new
            { id, sha256 = Hash(RolePrompts.ShippedDefaultFor(id)) }),
            ordinaryReviewer = "FakeCli clean answer; only local security findings are measured",
            limitation = "Token usage cannot prove local input coverage. Reproductions are not executed.",
        }, new JsonSerializerOptions { WriteIndented = true }));
        for (var repeat = 1; repeat <= 3; repeat++)
            await RunCell(repo.Path, baseline, Path.Combine(output!, $"repeat-{repeat}"), real, Scope,
                ["redteam-authz", "redteam-sql"]);
    }

    internal static async Task RunCell(string repo, string baseline, string output, ProcessLauncher real,
        string scope, string[] promptIds)
    {
        Directory.CreateDirectory(output);
        var settings = Settings(Path.Combine(output, "state"), promptIds);
        var launcher = new RecordingLauncher(new SecurityLaneRoundTests.Reviewers(real, false, false), output);
        var service = new PanelService(settings, VaultKeys.None("local calibration"), default, launcher,
            Serilog.Core.Logger.None, Noticing.None);
        // The fixture measures code review, not plan quality; only this isolated session is pre-seeded.
        new SessionStore(settings.DataDir).Save(new(new SessionState("calibration", repo, "HEAD", settings.Rounds)
        { PlanProceeded = true, Stage = Stage.CodeReview }, [])
        { PlanText = scope });
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        var watch = Stopwatch.StartNew();
        var reply = await service.ReviewCodeAsync(repo, "HEAD", baseline, scope, ct: deadline.Token);
        await File.WriteAllTextAsync(Path.Combine(output, "reply.json"), reply);
        await File.WriteAllTextAsync(Path.Combine(output, "elapsed-seconds.txt"), watch.Elapsed.TotalSeconds.ToString("F3",
            System.Globalization.CultureInfo.InvariantCulture));
        using var parsed = JsonDocument.Parse(reply);
        parsed.RootElement.TryGetProperty("error", out _).Should().BeFalse(reply);
        Directory.GetFiles(output, "*.request.txt").Length.Should().BeGreaterThanOrEqualTo(promptIds.Length,
            "every configured security prompt must actually reach the local shim");
    }

    private static PanelSettings Settings(string data, string[] promptIds)
    {
        ProviderSettings[] providers = [
            new("codex") { ExecutablePath = Path.Combine(AppContext.BaseDirectory, "FakeCli.exe") },
            new("qwen") { Runtime = "local", Model = Model, BaseUrl = "http://localhost:11434/v1",
                Plan = false, Code = false, ExecutablePath = Path.Combine(AppContext.BaseDirectory, "coai-mcp.exe") },
        ];
        var lane = SecurityLaneSetting.Parse(JsonSerializer.Serialize(new
        {
            enabled = true,
            threshold = 0,
            maxRounds = 2,
            runs = promptIds.Select(id => new { vendor = "qwen", prompt = id, context = "slice", contextTokens = 131072 }),
        }), providers);
        return new()
        {
            DataDir = data,
            Providers = providers,
            SecurityLane = lane,
            Rounds = new() { SecurityLane = lane.Gate },
            CodeWorkspace = "none",
            LocalConcurrency = 1,
            LocalMaxTokens = 8192,
            ReviewerTimeout = TimeSpan.FromMinutes(8)
        };
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string Source(bool unsafeChange) => """
        using System.Security.Claims;
        using Dapper;
        using Microsoft.AspNetCore.Authorization;
        using Microsoft.AspNetCore.Mvc;
        [Authorize]
        [ApiController]
        public sealed class InvoicesController(System.Data.IDbConnection database) : ControllerBase
        {
            [HttpGet("/invoices")]
            public IEnumerable<Invoice> Search(int tenantId, string search)
            {
        """ + (unsafeChange ? "\n" : "\n        if (User.FindFirstValue(\"tenant_id\") != tenantId.ToString()) throw new UnauthorizedAccessException();\n")
        + (unsafeChange
            ? "        return database.Query<Invoice>($\"SELECT Id, TenantId, Name FROM Invoices WHERE TenantId = {tenantId} AND Name = '{search}'\");\n"
            : "        return database.Query<Invoice>(\"SELECT Id, TenantId, Name FROM Invoices WHERE TenantId = @tenantId AND Name = @search\", new { tenantId, search });\n")
        + "    }\n}\npublic sealed record Invoice(int Id, int TenantId, string Name);\n";

    private sealed class RecordingLauncher(IProcessLauncher inner, string output) : IProcessLauncher
    {
        private int _turn;
        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            var args = request.Arguments.ToList();
            if (!args.Contains("--ask-local")) return await inner.RunAsync(request, ct);
            var name = Path.Combine(output, $"turn-{Interlocked.Increment(ref _turn)}");
            var prompt = args[args.IndexOf("--prompt-file") + 1];
            await File.WriteAllTextAsync(name + ".request.txt", await File.ReadAllTextAsync(prompt, ct), ct);
            var result = await inner.RunAsync(request, ct);
            await File.WriteAllTextAsync(name + ".process.json", JsonSerializer.Serialize(result), ct);
            var answer = args[args.IndexOf("--out") + 1];
            if (File.Exists(answer)) File.Copy(answer, name + ".answer.json", overwrite: true);
            return result;
        }
    }
}
