using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CoaiMcp.Core.Notices;
using CoaiMcp.Server.Keys;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s7 t6, the part that covers t1, t2 and t4 — a key put in the pool is in no argv, log line, notice, answer, metadata file or exception text; the one file that holds it is its own <c>.key</c> (K1, K2, K3 and the
/// store-level half of K13 of KEY_POOL_SPEC.md section 10; the launch half of K13, K14 and K16 need the wiring of t5). The structural scans each have a companion that proves the scan matches something.
/// </summary>
[System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
public sealed partial class AKeyNoLeakTests
{
    // ---------- the canary through the real one-shot ----------

    [Fact]
    public void K13_TheCanary_IsInOneFileOnly_ItsOwnKeyFile_AfterEveryVerbAndEveryOutcome()
    {
        using var data = TempDir.For("coai-keycanary-");
        var canary = KeyWorld.Token("canary");
        var id = KeyWorld.IdOf(canary);
        var seen = new StringBuilder();
        var argv = new List<string>();

        void Real(string stdin, params string[] args) => RealRun(record: true, stdin, args);

        void Attack(string stdin, params string[] args) => RealRun(record: false, stdin, args);

        void RealRun(bool record, string stdin, string[] args)
        {
            if (record)
            {
                argv.AddRange(args);
            }

            var run = KeysBinary.Run(data.Path, stdin, args);
            seen.AppendLine(run.Out).AppendLine(run.Err);
        }

        Real(canary + "\n", "--keys", "add", "--label", "canary key", "--json");
        Real(canary + "\n", "--keys", "add", "--label", "again");
        Real(string.Empty, "--keys", "list");
        Real(string.Empty, "--keys", "list", "--json");
        Real(string.Empty, "--keys", "relabel", "--id", id, "--label", "renamed");
        Real(string.Empty, "--keys", "disable", "--id", id);
        Real(string.Empty, "--keys", "enable", "--id", id);
        Real(string.Empty, "--keys", "scan", "--path", data.Path, "--json");
        Real(string.Empty, "--keys", "sync", "--json");
        Real($"{{\"label\":\"x\",\"token\":\"{canary}\"}}\n", "--keys", "import", "--json");
        Attack(canary + "\n", "--keys", "add", "--label", canary[..20]);
        Attack(canary + "\n", "--keys", "add", "--label", "x", "--token", canary);

        var notices = new List<ServerNotice>();
        var clock = new KeyClock(new DateTimeOffset(2026, 11, 24, 8, 0, 0, TimeSpan.Zero));
        var pool = new KeyPool(new KeyStoreAccess(data.Path, clock, notices.Add));
        var picked = pool.Pick().Should().BeOfType<PickResult.Picked>().Subject;
        seen.AppendLine(picked.ToString()).AppendLine(picked.Secret.ToString());
        seen.AppendLine(pool.Record(id, new KeyLaunchReport(KeyLaunchEnd.Limit, $"limit reached for {canary}, resets in 1h")).Value!.ToString());
        seen.AppendLine(pool.Record(id, new KeyLaunchReport(KeyLaunchEnd.AuthRefused, canary)).Value!.ToString());
        seen.AppendLine(pool.Record(id, new KeyLaunchReport(KeyLaunchEnd.Exposed)).Value!.ToString());
        seen.AppendLine(pool.Resume(id).ToString());
        clock.Advance(TimeSpan.FromHours(3));
        seen.AppendLine(pool.Pick().ToString());
        foreach (var notice in notices)
        {
            seen.AppendLine(ServerNoticeLine.Of(notice));
        }

        notices.Should().NotBeEmpty("the run reached the dead and exposed notices");
        argv.Should().NotContain(canary, "no mode takes the text on the command line; the runs that tried it (a label, a flag) were refused and said nothing of it");
        seen.ToString().Should().NotContain(KeyWorld.Sentinel, "no answer, sentence, notice or printed record holds any part of the key");
        var rest = Directory.GetFiles(data.Path, "*", SearchOption.AllDirectories).Where(file => !file.EndsWith(".key", StringComparison.Ordinal)).Select(File.ReadAllText);
        string.Join('\n', rest).Should().NotContain(KeyWorld.Sentinel, "no metadata, cursor or lock file holds any part of the key");

        // The companion: the same search, run over the same folder, does see the canary where it is.
        var hits = KeyScanner.Walk(data.Path, [new ClaudeKeySecret(id, canary)], excluded: string.Empty);
        hits.Hits.Select(hit => hit.Path).Should().Equal(Path.Combine(data.Path, "keys", "claude", id + ".key"));
        File.GetUnixFileMode(Path.Combine(data.Path, "keys", "claude", id + ".key")).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public void K13_TheLogsOfAStartThatSyncsASourceOfKeys_HoldCountsAndRules_NeverAValue()
    {
        using var world = new KeyWorld();
        var said = new List<string>();
        var log = new Serilog.LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(new Said(said)).CreateLogger();
        var file = Path.Combine(world.DataDir, "keys.env");
        File.WriteAllText(file, $"COAI_CLAUDE_KEY_good={KeyWorld.Token("a")}\nCOAI_CLAUDE_KEY_BAD LABEL={KeyWorld.Token("b")}\nnonsense {KeyWorld.Token("c")}\n");
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        KeyStartup.Sync(world.DataDir, name => name == "COAI_CLAUDE_KEYS_FILE" ? file : null, log);
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);
        KeyStartup.Sync(world.DataDir, name => name == "COAI_CLAUDE_KEYS_FILE" ? file : null, log);

        said.Should().NotBeEmpty();
        string.Join('\n', said).Should().NotContain(KeyWorld.Sentinel);
        string.Join('\n', said).Should().Contain("was not taken").And.Contain("readable by group or other");
    }

    [Fact]
    public void K3_AnExceptionOfTheDisk_IsARefusalWithNoPartOfTheKey()
    {
        using var world = new KeyWorld();
        File.WriteAllText(Path.Combine(world.DataDir, "keys"), "a file where the folder should be");

        var added = world.Admin.Add(new KeyInput(1, "one", KeyWorld.Token("a"), string.Empty, KeySources.Local));

        added.Ok.Should().BeFalse();
        added.Code.Should().Be(KeyExit.Io);
        added.Refusal.Should().NotContain(KeyWorld.Sentinel);
        world.Admin.Import([new KeyInput(1, "one", KeyWorld.Token("a"), string.Empty, KeySources.Local)]).Refusal.Should().NotContain(KeyWorld.Sentinel);
    }

    [Fact]
    public void K2_EveryRefusalOfTheShapeCheck_NamesTheRuleAndTheLength_AndNoCharacterOfTheInput()
    {
        var inputs = new[]
        {
            string.Concat("sk-ant-api03-", KeyWorld.Sentinel, new string('q', 50)),
            string.Concat("sk-ant-", "oat01-", KeyWorld.Sentinel, " ", new string('q', 50)),
            string.Concat("sk-ant-", "oat01-", KeyWorld.Sentinel),
            string.Concat("zz-", KeyWorld.Sentinel, new string('q', 50)),
            string.Concat("sk-ant-", "oat01-", KeyWorld.Sentinel, new string('q', 600)),
        };

        foreach (var input in inputs)
        {
            var refusal = KeyShape.Refusal(input);

            refusal.Should().NotBeEmpty();
            refusal.Should().NotContain(KeyWorld.Sentinel).And.NotContain("qqq");
        }
    }

    // ---------- structural scans, each with its companion ----------

    private static IEnumerable<(string File, string Text)> ProductionSources()
    {
        var root = Path.Combine(RepoPaths.Root(), "src_mcp");

        return new[] { "src", "core", "runners", "service_defaults", "storage" }
            .SelectMany(folder => Directory.GetFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(path => (path, File.ReadAllText(path)));
    }

    [GeneratedRegex(@"\.Reveal\(\s*\)")]
    private static partial Regex RevealCall();

    [GeneratedRegex(@"\.ReadSecret\(")]
    private static partial Regex ReadSecretCall();

    [GeneratedRegex(@"\b(log\w*|Log\w*|logger|Console\s*\.\s*\w+|Note|WriteLine|Warning|Information|Error|Debug|Verbose|Fatal)\b[^;]*?(\bClaudeKeySecret\b|\bReveal\s*\(|\bTokenText\b|\bsecret\b)", RegexOptions.IgnoreCase)]
    private static partial Regex LoggedSecret();

    /// <summary>The files of <paramref name="sources"/> that call the member <paramref name="call"/> matches, with the number of call sites.</summary>
    private static List<(string File, int Sites)> Callers(IEnumerable<(string File, string Text)> sources, Regex call) =>
        [.. sources.Select(source => (source.File, Sites: call.Matches(source.Text).Count)).Where(source => source.Sites > 0)];

    private static bool InKeysFolder(string path) => path.Replace('\\', '/').Contains("/src/Server/Keys/", StringComparison.Ordinal);

    [Fact]
    public void Scan_TheTextMemberHasAtMostOneCaller_TheLaunchRoad_AndNoneInsideTheKeysFolder()
    {
        var sources = ProductionSources().ToList();
        sources.Should().NotBeEmpty("the scan must see the sources");

        var reveal = Callers(sources, RevealCall());
        var read = Callers(sources, ReadSecretCall());

        reveal.Where(caller => InKeysFolder(caller.File)).Should().BeEmpty("the scrub and the search are members of the secret itself, so nothing in the pool needs the text");
        reveal.Sum(caller => caller.Sites).Should().BeLessThanOrEqualTo(1, "ONE caller: the launch road");
        reveal.Select(caller => Path.GetFileName(caller.File)).Should().BeSubsetOf(["RoleModelLauncher.cs"]);
        read.Where(caller => !InKeysFolder(caller.File)).Should().BeEmpty("only the pool and the store read a token out of a file");
    }

    [Fact]
    public void Scan_TheTextMemberScanSeesACallerWhenThereIsOne()
    {
        var planted = new[] { ("/x/src/Server/Other.cs", "var text = secret.Reveal();"), ("/x/src/Server/Keys/KeyPool.cs", "return session.ReadSecret(id);") };

        Callers(planted, RevealCall()).Should().ContainSingle().Which.File.Should().Be("/x/src/Server/Other.cs");
        Callers(planted, ReadSecretCall()).Should().ContainSingle().Which.File.Should().Be("/x/src/Server/Keys/KeyPool.cs");
        Callers(ProductionSources(), new Regex(@"\bpublic string Reveal\(")).Should().ContainSingle("the scan reads real source: the member it guards is declared once");
    }

    [Fact]
    public void Scan_NoLoggerOrConsoleCallTakesTheSecretType()
    {
        var sources = ProductionSources().Where(source => InKeysFolder(source.File)).ToList();
        sources.Should().NotBeEmpty();

        var offenders = sources.SelectMany(source => LoggedSecret().Matches(Regex.Replace(source.Text, @"///[^\n]*|//[^\n]*", string.Empty)).Select(match => $"{Path.GetFileName(source.File)}: {match.Value.Trim()[..Math.Min(80, match.Value.Trim().Length)]}")).ToList();

        offenders.Should().BeEmpty("a log or console call that takes the secret prints a key");
    }

    [Fact]
    public void Scan_TheLoggedSecretScanSeesALoggedSecretWhenThereIsOne()
    {
        LoggedSecret().IsMatch("log.Information(\"key {Key}\", secret);").Should().BeTrue();
        LoggedSecret().IsMatch("Console.Error.WriteLine(picked.Secret.Reveal());").Should().BeTrue();
        LoggedSecret().IsMatch("runtime.Note($\"--keys add: {input.TokenText}\");").Should().BeTrue();
        LoggedSecret().IsMatch("log.Warning(\"claude key source {Source} was not read: {Reason}\", source.Source, source.Refusal);").Should().BeFalse("the real call sites say counts and rules");
    }

    [Fact]
    public void Scan_NoTestOfTheKeyPoolHoldsATokenShapedLiteral_AndNoTestNameCanHoldOne()
    {
        var tests = Directory.GetFiles(Path.Combine(RepoPaths.Root(), "src_mcp", "tests"), "*.cs").Where(path => Path.GetFileName(path).StartsWith("AKey", StringComparison.Ordinal) || Path.GetFileName(path).StartsWith("Key", StringComparison.Ordinal))
            .Concat(Directory.GetFiles(Path.Combine(RepoPaths.Root(), "src_mcp", "tests_fakecli"), "PoolLockVerb.cs")).ToList();
        tests.Should().HaveCountGreaterThan(4, "the scan must see the key pool's own tests");

        var offenders = tests.Where(path => TokenShape().IsMatch(File.ReadAllText(path))).Select(Path.GetFileName);

        offenders.Should().BeEmpty("a token-shaped literal in source is a token in the repository; every made-up token is built at run time from parts");
        TokenShape().IsMatch(string.Concat("sk-ant-", "oat01-", "abcdefghijklmnop")).Should().BeTrue("the scan must see a token-shaped text");
    }

    [GeneratedRegex(@"sk-ant-(oat|api)\d{0,3}-[A-Za-z0-9_-]{8,}")]
    private static partial Regex TokenShape();

    [Fact]
    public void Scan_TheKeysFolderCallsNoNetworkAndStartsNoProcess_AndNamesTheCredsKeyNowhere()
    {
        var keys = ProductionSources().Where(source => InKeysFolder(source.File)).ToList();

        keys.Where(source => Regex.IsMatch(source.Text, @"HttpClient|Process\.Start|ProcessLauncher|TcpClient|WebRequest")).Should().BeEmpty();
        keys.Where(source => source.Text.Contains("CREDSFORDEVS_KEY", StringComparison.Ordinal)).Should().BeEmpty();
        keys.Should().HaveCountGreaterThan(10, "the scan must see the whole folder");
    }

    [Fact]
    public void K1_ARecordOfTheKeyList_HasNoMemberThatCouldHoldAText_AndTheOnlyJsonTypesAreMetadata()
    {
        var members = new[] { typeof(KeyRecord), typeof(PoolFile), typeof(KeyRow), typeof(KeyListAnswer), typeof(KeyAddedAnswer), typeof(KeyImportAnswer), typeof(KeyScanAnswer) }
            .SelectMany(type => type.GetProperties().Select(property => $"{type.Name}.{property.Name}"));

        members.Where(name => Regex.IsMatch(name, "Token|Secret|Text|Value|Password", RegexOptions.IgnoreCase)).Should().BeEmpty();
        members.Should().Contain("KeyRecord.Label", "the check must see the members");
    }

    [Fact]
    public void K1_TheMetadataOfAKey_IsPrintableWithAnyToStringAndSerializer_AndHoldsNoSentinel()
    {
        using var world = new KeyWorld();
        var row = world.AddKey("one", "a");

        var texts = new[]
        {
            row.ToString(),
            JsonSerializer.Serialize(row, KeysAnswerJson.Default.KeyRow),
            JsonSerializer.Serialize(world.Admin.List(false).Value!, KeysAnswerJson.Default.KeyListAnswer),
            File.ReadAllText(world.MetaFile("a")),
            File.ReadAllText(Path.Combine(world.KeysRoot, "pool.json")),
        };

        texts.Should().OnlyContain(text => !text.Contains(KeyWorld.Sentinel, StringComparison.Ordinal));
    }

    private sealed class Said(List<string> lines) : Serilog.Core.ILogEventSink
    {
        public void Emit(Serilog.Events.LogEvent logEvent) => lines.Add(logEvent.RenderMessage() + (logEvent.Exception is { } fault ? " :: " + fault.GetType().Name : string.Empty));
    }
}
