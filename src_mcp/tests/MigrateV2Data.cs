using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using CoaiMcp.Server.Migrations;
using FluentAssertions;
using Serilog.Core;

namespace CoaiMcp.Tests;

/// <summary>
/// The data folders the v2 migration tests run over (E1.S4): frozen files a v1 build wrote, copied into a test folder, and the readers every suite shares.
/// </summary>
/// <remarks>
/// <para>The fixtures are DATA, not code: <c>fixtures/migrate-v2/&lt;format&gt;/&lt;name&gt;/{sessions,escalations}</c> were raised ONCE by the real v1 code at commit
/// <c>c24e5141</c> (<c>HoldFromBeforeAutoFixture.RaiseAsync</c> under policy human), with the repository path rewritten to <c>/fixture/repo</c> and every file named by
/// the key it carries. <c>f3</c> is what 0.66.0 wrote; <c>f2</c> is the same without <c>config.autonomous</c> (0.40.0–0.65.x); <c>f1</c> also has no
/// <c>holdQuestions</c> / <c>requestQuestions</c> and bare cards (0.3.1–0.39.x). A name is <c>&lt;stage&gt;-resolved</c> (the caller had decided the held round) or
/// <c>&lt;stage&gt;-pending</c> (its finding is still pending); <c>f3</c> also holds <c>plan-answered</c> (a person answered the card), <c>plan-auto-released</c>
/// (v1 auto released the hold: no <c>humanGate</c>, an expired card) and <c>plan-not-held</c>.</para>
/// </remarks>
internal static class MigrateV2Data
{
    internal const string Repo = "/fixture/repo";

    internal static readonly string[] Formats = ["f1", "f2", "f3"];

    internal static readonly string[] Kinds = ["plan", "code", "document", "feature"];

    private static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures", "migrate-v2");

    /// <summary>
    /// Copies a fixture's <c>sessions/</c> and <c>escalations/</c> into the data folder (files already there are overwritten). A card's moments (<c>askedUtc</c>, <c>expiredUtc</c>,
    /// <c>answeredUtc</c>) are restamped to NOW: the fixtures were raised on 2026-11-22, and the start-up sweep archives an answered or expired card 30 days old (E3), so a card
    /// that kept its own date would stay history only until then. A test of the age plants its own dates (<see cref="Stamp"/>).
    /// </summary>
    internal static void Plant(string format, string name, string dataDir, bool sessions = true, bool cards = true)
    {
        var at = PlantedAt;
        foreach (var (folder, wanted) in new[] { ("sessions", sessions), ("escalations", cards) })
        {
            var from = Path.Combine(Fixtures, format, name, folder);
            if (wanted && Directory.Exists(from))
            {
                Directory.CreateDirectory(Path.Combine(dataDir, folder));
                foreach (var file in Directory.GetFiles(from))
                {
                    var target = Path.Combine(dataDir, folder, Path.GetFileName(file));
                    File.Copy(file, target, overwrite: true);
                    if (folder == "sessions" && OperatingSystem.IsWindows() && file.EndsWith(".json", StringComparison.Ordinal))
                    {
                        target = RekeyedForThisFileSystem(dataDir, target);
                    }

                    if (folder == "escalations" && file.EndsWith(".json", StringComparison.Ordinal))
                    {
                        Stamp(target, at);
                    }
                }
            }
        }
    }

    /// <summary>
    /// The fixtures were written on Linux, where a session's file is named by a key that keeps the case of the document or plan path (<c>todo/PLAN_the_cart.md</c>). On
    /// Windows the same key is folded to lower case (<see cref="CoaiMcp.Core.Rounds.DocumentId.KeyOf"/> — one file there), so the store looks for another file name. The file is
    /// moved to the name THIS platform's store gives that session, which is where a person's own session would be; its content is not touched.
    /// </summary>
    private static string RekeyedForThisFileSystem(string dataDir, string file)
    {
        var state = JsonNode.Parse(File.ReadAllText(file))!["state"]!.AsObject();
        var wanted = new SessionStore(dataDir).FileFor(
            state["repoPath"]?.GetValue<string>() ?? string.Empty,
            state["branch"]?.GetValue<string>() ?? string.Empty,
            state["document"]?.GetValue<string>() ?? string.Empty,
            state["feature"]?.GetValue<string>() ?? string.Empty);
        if (!string.Equals(wanted, file, StringComparison.OrdinalIgnoreCase))
        {
            File.Move(file, wanted, overwrite: true);
        }

        return wanted;
    }

    /// <summary>
    /// The moment planted cards carry: the start of the test process, ONE value for every plant in it, because a test plants twice and compares the two folders byte for byte
    /// (<c>MigrateV2RunTests</c>, the kill-at-every-boundary run) — two plants a millisecond apart would differ in their stamps alone.
    /// </summary>
    private static readonly DateTimeOffset PlantedAt = DateTimeOffset.UtcNow;

    /// <summary>Sets every moment a card or an answer file carries (<c>askedUtc</c>, <c>expiredUtc</c> when it has one, <c>answeredUtc</c>) to <paramref name="at"/>.</summary>
    internal static void Stamp(string file, DateTimeOffset at)
    {
        var text = File.ReadAllText(file);
        var stamped = System.Text.RegularExpressions.Regex.Replace(
            text, "\"(askedUtc|answeredUtc|expiredUtc)\"\\s*:\\s*\"[^\"]+\"", m => $"\"{m.Groups[1].Value}\": \"{at:O}\"");
        File.WriteAllText(file, stamped);
    }

    /// <summary>One fixture's session of a kind: the document's own, the feature's own, or the branch's.</summary>
    internal static string SessionFile(string dataDir, string kind) =>
        Directory.GetFiles(Path.Combine(dataDir, "sessions"), "session-*.json")
            .Single(file => KindOf(JsonNode.Parse(File.ReadAllText(file))!["state"]!.AsObject()) == (kind is "document" or "feature" ? kind : "branch"));

    private static string KindOf(JsonObject state) =>
        !string.IsNullOrEmpty(state["document"]?.GetValue<string>()) ? "document"
        : !string.IsNullOrEmpty(state["feature"]?.GetValue<string>()) ? "feature"
        : "branch";

    internal static PersistedSession Load(string dataDir, string file) => new SessionStore(dataDir).LoadFile(file)!;

    /// <summary>A mixed folder: three held sessions, six cards of every standing, both stores and a settings file with removed keys.</summary>
    internal static void Mixed(string dataDir)
    {
        // The document fixture also carries the branch's plan session, NOT held; the held plan session is planted after it and takes its file.
        Plant("f3", "document-resolved", dataDir);
        Plant("f3", "plan-pending", dataDir);
        Plant("f3", "feature-pending", dataDir);
        Plant("f3", "plan-answered", dataDir, sessions: false);
        Plant("f3", "plan-auto-released", dataDir, sessions: false);

        // A card of no session at all, open.
        var stray = JsonNode.Parse(File.ReadAllText(Directory.GetFiles(Path.Combine(dataDir, "escalations"), "b7a7e49109a7.json").Single()))!.AsObject();
        stray["id"] = "aaaaaaaaaaaa";
        stray["sessionId"] = "no-such-session";
        File.WriteAllText(Path.Combine(dataDir, "escalations", "aaaaaaaaaaaa.json"), stray.ToJsonString());

        Directory.CreateDirectory(Path.Combine(dataDir, "qphase"));
        File.WriteAllText(Path.Combine(dataDir, "qphase", "plan-a.json"), """{"batches":2}""");
        File.WriteAllText(Path.Combine(dataDir, "qphase", "plan-b.json"), """{"batches":0}""");
        Directory.CreateDirectory(Path.Combine(dataDir, "cadence"));
        File.WriteAllText(Path.Combine(dataDir, "cadence", "repo-1.json"), """{"epics":[1,2]}""");

        File.WriteAllText(Path.Combine(dataDir, "settings.json"), """
            {
              "COAI_PROVIDERS": "codex,gemini",
              "COAI_ON_EXHAUSTED": "human",
              "COAI_MAX_ROUNDS": 3,
              "COAI_ESCALATION_MINUTES": 30,
              "COAI_ESCALATION_SECONDS": "1800",
              "COAI_LOG_LEVEL": "debug"
            }
            """);
    }

    // ---------- what the folder holds ----------

    /// <summary>
    /// Every file under the folder (relative path, SHA-256) — and, with <paramref name="withFolders"/>, every folder too, so a dry run that merely made an empty one
    /// is seen. Without <paramref name="withBookkeeping"/> the journal, the marker, <c>claims/</c>, <c>*.turn</c> and <c>coai.db*</c> are left out: they are the
    /// migration's own records and handles, which a resumed run does not reproduce byte for byte.
    /// </summary>
    internal static SortedDictionary<string, string> Tree(string dir, bool withFolders = false, bool withBookkeeping = false)
    {
        var tree = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(dir, path).Replace('\\', '/');
            if (!withBookkeeping && IsBookkeeping(relative))
            {
                continue;
            }

            if (Directory.Exists(path))
            {
                if (withFolders)
                {
                    tree[relative + "/"] = "<folder>";
                }

                continue;
            }

            tree[relative] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        }

        return tree;
    }

    private static bool IsBookkeeping(string relative) =>
        relative is "migrations/v2.journal" or "migrations/v2.done"
        || relative == "claims"
        || relative.StartsWith("claims/", StringComparison.Ordinal)
        || relative.EndsWith(".turn", StringComparison.Ordinal)
        || relative.StartsWith("coai.db", StringComparison.Ordinal);

    // ---------- running it ----------

    /// <summary>One writer thread for every test of the suite that writes a notice: the process-wide one is never drained here, and this one is never closed.</summary>
    private static readonly NoticeWriter Writer = new((dir, notice) => ServerNotices.Append(dir, notice));

    /// <summary>The sink the migration writes a notice through, composed by the production composer over a writer whose append is the real one.</summary>
    internal static Func<ServerNotice, bool> Sink(string dataDir) =>
        Program.MigrationNotices(name => name == "COAI_DATA_DIR" ? dataDir : null, Writer, Logger.None, "test-run");

    internal static MigrationV2 Migration(string dataDir, MigrationV2Tables? tables = null, string version = "", Action<int>? atBoundary = null) =>
        new(dataDir, _ => { }, Sink(dataDir), tables, version: version) { AtBoundary = atBoundary };

    /// <summary>Every line of <c>server-notices.jsonl</c> (and its archive) with the given code, parsed.</summary>
    internal static List<JsonElement> NoticesOf(string dataDir, string code)
    {
        var lines = new List<JsonElement>();
        foreach (var name in new[] { ServerNotices.Archive, ServerNotices.Name })
        {
            var file = Path.Combine(dataDir, name);
            if (File.Exists(file))
            {
                lines.AddRange(File.ReadAllLines(file).Where(l => l.Length > 0).Select(l => JsonDocument.Parse(l).RootElement.Clone())
                    .Where(n => n.GetProperty("code").GetString() == code));
            }
        }

        return lines;
    }

    internal static MigrateV2Report Apply(string dataDir, MigrationV2Tables? tables = null) =>
        Migration(dataDir, tables).Apply();

    internal static MigrateV2Report Plan(string dataDir, MigrationV2Tables? tables = null) =>
        Migration(dataDir, tables).Plan();

    /// <summary>The mode as the dispatch calls it, over a folder: its exit code, its stdout and its stderr.</summary>
    internal static (int Code, string Out, string Err) Mode(string dataDir, params string[] args)
    {
        var output = new StringWriter();
        var said = new List<string>();
        var env = new Dictionary<string, string> { ["COAI_DATA_DIR"] = dataDir };
        var code = MigrateV2Mode.Run(["--migrate-v2", .. args], name => env.GetValueOrDefault(name), output, said.Add, Sink(dataDir));

        return (code, output.ToString(), string.Join('\n', said));
    }

    /// <summary>The REAL binary, started the way a person starts it.</summary>
    internal static async Task<(int Code, string Out, string Err)> Binary(string dataDir, params string[] args)
    {
        var info = new ProcessStartInfo(ServerBinary.Path)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        info.Environment["COAI_DATA_DIR"] = dataDir;
        info.Environment.Remove("COAI_DATA_SIDE");
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var err = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, await output, await err);
    }

    internal static JsonElement Report(string stdout)
    {
        using var document = JsonDocument.Parse(stdout);
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Object, "stdout is ONE JSON object");

        return document.RootElement.Clone();
    }

    /// <summary>A service over the folder, to call <c>resolve</c> through the real entry.</summary>
    internal static PanelService Service(string dataDir) =>
        new(
            new PanelSettings { Providers = [new("codex") { ExecutablePath = "FakeCli" }], Rounds = PanelConfig.Uniform(3, 2), DataDir = dataDir },
            VaultKeys.None("no vault in tests"), default, new ProcessLauncher(), Logger.None, Noticing.None);
}

/// <summary>Stands for a kill at one boundary: nothing in the migration may catch it.</summary>
internal sealed class SimulatedKill(int boundary) : Exception($"killed at boundary {boundary}");
