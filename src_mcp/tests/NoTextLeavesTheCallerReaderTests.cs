using System.Collections;
using System.Reflection;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E3.S5 of <c>todo/PLAN_a_round_records_who_wrote_the_code_and_how_much_E3.md</c>: an independent guard that nothing leaves the
/// caller-transcript reader as text. Over every fixture E1 cut — enumerated from the folder, compared with the manifest — no string
/// anywhere in an <see cref="ObservedSession"/> carries a byte of text, and every reason is a code of the one closed vocabulary.
/// </summary>
/// <remarks>
/// <para><b>The sentinel is E1's value</b>, read from <c>allowlist.json</c>; it is the string every text field of every fixture became.
/// A test that proves a negative needs the positive first: <see cref="Every_fixture_holds_the_sentinel_in_its_raw_bytes"/> shows the
/// fixtures WOULD leak if the reader read text, and <see cref="The_walk_reaches_every_string_it_is_asked_about"/> shows the reflection
/// walk reaches a string in every member.</para>
/// <para>The fixtures are materialised through <see cref="CallerTranscriptFixture.Materialise"/> — the one materialiser — and the
/// vocabulary test drives every mutation helper once (the table below), because a reason such as <c>torn meta</c> is produced only by a
/// fixture someone broke on purpose.</para>
/// </remarks>
public sealed class NoTextLeavesTheCallerReaderTests
{
    private const string Prefix = "testsToDelete-coai-notext-";
    private const string C02 = "message-as-several-lines";
    private const string C02Agent = "303b2866f0b3022b2";
    private const string C07 = "nested-agents";
    private const string C07Depth1 = "207bc560f3fb5a452";
    private const string C14 = "resumed-agent";
    private const string C14Agent = "2edf3ddec8861a853";

    /// <summary>The folder the case folders are enumerated from. The companion test fails when this finds none, or not the manifest's cases.</summary>
    private static readonly string CasesFolder = FixtureManifest.Folder;

    private sealed record Sample(string Fixture, Action<CallerTranscriptFixture> Apply, long? MaxLineBytes = null);

    /// <summary>
    /// One sample call per mutation helper of <see cref="CallerTranscriptFixture"/>. Reflection finds the helpers (a public instance method
    /// returning <c>void</c>) and cannot choose their arguments, so the arguments live here; a helper added later turns
    /// <see cref="Every_mutation_helper_has_a_sample"/> red until it has a row.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Sample> Samples = new Dictionary<string, Sample>
    {
        [nameof(CallerTranscriptFixture.TruncateLastLine)] = new(C02, f => f.TruncateLastLine(TranscriptFile.Main)),
        [nameof(CallerTranscriptFixture.TruncateLine)] = new(C02, f => f.TruncateLine(TranscriptFile.Agent(C02Agent), 23)),
        [nameof(CallerTranscriptFixture.TruncateBeforeStamp)] = new(C02, f => f.TruncateBeforeStamp(TranscriptFile.Agent(C02Agent), 22)),
        [nameof(CallerTranscriptFixture.DeleteMeta)] = new(C02, f => f.DeleteMeta(C02Agent)),
        [nameof(CallerTranscriptFixture.DeleteAgentFile)] = new(C02, f => f.DeleteAgentFile(C02Agent)),
        [nameof(CallerTranscriptFixture.CorruptMeta)] = new(C07, f => f.CorruptMeta(C07Depth1)),
        [nameof(CallerTranscriptFixture.EmptyAgentFile)] = new(C02, f => f.EmptyAgentFile(C02Agent)),
        [nameof(CallerTranscriptFixture.RemoveMetaKey)] = new(C02, f => f.RemoveMetaKey(C02Agent, "model")),
        [nameof(CallerTranscriptFixture.SetMetaKey)] = new(C07, f => f.SetMetaKey("2553a2f49d265a4e3", "parentAgentId", "0123456789abcdef0")),
        [nameof(CallerTranscriptFixture.RewriteEveryCwd)] = new(C14, f => f.RewriteEveryCwd("D:/fixture/repo-a")),
        [nameof(CallerTranscriptFixture.SetModelOnLine)] = new(C02, f => f.SetModelOnLine(TranscriptFile.Main, 1, "<synthetic>")),
        [nameof(CallerTranscriptFixture.SetRequestedModel)] = new(C02, f => f.SetRequestedModel(TranscriptFile.Main, 1, "a sentence with spaces")),
        [nameof(CallerTranscriptFixture.SetEffort)] = new(C02, f => f.SetEffort(TranscriptFile.Agent(C02Agent), 13, null)),
        [nameof(CallerTranscriptFixture.SetAgentId)] = new(C02, f => f.SetAgentId(TranscriptFile.Agent(C02Agent), 1, "ffffffffffffffff0")),
        [nameof(CallerTranscriptFixture.SetSidechain)] = new(C02, f => f.SetSidechain(TranscriptFile.Main, 1, true)),
        [nameof(CallerTranscriptFixture.SetStamp)] = new(C02, f => f.SetStamp(TranscriptFile.Agent(C02Agent), 13, f.StampOf(TranscriptFile.Agent(C02Agent), 14) - TimeSpan.FromHours(2.5))),
        [nameof(CallerTranscriptFixture.RemoveLineKey)] = new(C02, f => f.RemoveLineKey(TranscriptFile.Main, 1, "timestamp")),
        [nameof(CallerTranscriptFixture.SetLineKey)] = new(C02, f => f.SetLineKey(TranscriptFile.Main, 1, "message.id", "a sentence an id never is")),
        [nameof(CallerTranscriptFixture.PrependLine)] = new(C02, f => f.PrependLine(TranscriptFile.Main, "{\"type\":\"ai-title\"}")),
        [nameof(CallerTranscriptFixture.PutMultibyte)] = new(C14, f => f.PutMultibyte(TranscriptFile.Agent(C14Agent), 1)),
        [nameof(CallerTranscriptFixture.PutLongLine)] = new(C14, f => f.PutLongLine(TranscriptFile.Agent(C14Agent), 88, 64 * 1024), MaxLineBytes: 32 * 1024),
        [nameof(CallerTranscriptFixture.SetResultError)] = new("two-repositories-one-session", f => f.SetResultError(TranscriptFile.Main, f.CallIds(TranscriptFile.Main, "Edit")[0])),
        [nameof(CallerTranscriptFixture.DeleteResult)] = new("two-repositories-one-session", f => f.DeleteResult(TranscriptFile.Main, f.CallIds(TranscriptFile.Main, "Edit")[1])),
        [nameof(CallerTranscriptFixture.AnswerCall)] = new("two-repositories-one-session", f => f.AnswerCall(TranscriptFile.Main, f.CallIds(TranscriptFile.Main, "Edit")[1])),
        [nameof(CallerTranscriptFixture.CopyLineInto)] = new(C02, f => f.CopyLineInto(1, C02Agent)),
        [nameof(CallerTranscriptFixture.GrowOutputAcross)] = new(C02, f => f.GrowOutputAcross("8d01ee")),
        [nameof(CallerTranscriptFixture.CopyAgentUnderName)] = new(C02, f => f.CopyAgentUnderName(C02Agent, "some words here")),
    };

    private static IReadOnlyList<FixtureCase> CasesOnDisk() =>
        [.. Directory.EnumerateDirectories(CasesFolder).Select(path => Path.GetFileName(path)).Select(dir => FixtureManifest.Cases.Single(c => c.Dir == dir))];

    private static string MainFileOf(FixtureCase fixtureCase)
    {
        var projects = Path.Combine(CasesFolder, fixtureCase.Dir, "projects");

        return Directory.EnumerateDirectories(projects).SelectMany(dir => Directory.EnumerateFiles(dir, "*.jsonl")).Single();
    }

    // ---- the companion that keeps the scan alive --------------------------------------------------------------------------

    [Fact]
    public void The_fixture_scan_found_every_fixture_E1_cut()
    {
        var folders = Directory.EnumerateDirectories(CasesFolder).Select(path => Path.GetFileName(path)).ToList();

        folders.Should().NotBeEmpty("a scan that finds nothing passes forever");
        folders.Should().HaveCount(FixtureManifest.Cases.Count, "the folder holds exactly the manifest's cases");
        folders.Should().BeEquivalentTo(FixtureManifest.Cases.Select(c => c.Dir), "the two lists agree both ways");
    }

    // ---- the positive half first -----------------------------------------------------------------------------------------

    [Fact]
    public void Every_fixture_holds_the_sentinel_in_its_raw_bytes()
    {
        var withTranscript = CasesOnDisk().Where(c => c.HasTranscript).ToList();

        withTranscript.Should().HaveCount(FixtureManifest.Cases.Count(c => c.HasTranscript)).And.NotBeEmpty();
        foreach (var fixtureCase in withTranscript)
        {
            File.ReadAllText(MainFileOf(fixtureCase)).Should().Contain(FixtureManifest.Sentinel, $"{fixtureCase.Name} would leak if the reader read text");
        }
    }

    // ---- the guard -------------------------------------------------------------------------------------------------------

    [Fact]
    public void No_string_in_an_observed_session_holds_the_sentinel()
    {
        var sentinel = FixtureManifest.Sentinel;
        var cases = CasesOnDisk();
        cases.Should().NotBeEmpty();
        foreach (var fixtureCase in cases)
        {
            using var dir = TempDir.For(Prefix);
            var session = CallerTranscriptFixture.Materialise(fixtureCase.Name, dir).Read();

            var strings = StringWalk.Strings(session);

            strings.Should().NotContain(text => text.Contains(sentinel, StringComparison.Ordinal), fixtureCase.Name);
            session.State.Render().Should().NotContain(sentinel, fixtureCase.Name);
            session.ToString().Should().NotContain(sentinel, fixtureCase.Name);
            (fixtureCase.HasTranscript ? strings.Count : 1).Should().BeGreaterThan(0, "the walk found strings to inspect in " + fixtureCase.Name);
        }
    }

    [Fact]
    public void The_walk_reaches_every_string_it_is_asked_about()
    {
        var at = new DateTime(2000, 1, 1, 0, 0, 1, DateTimeKind.Utc);
        var session = new ObservedSession(
            new CallerSessionId("s-id", "s-source"),
            new ObservationState(ObservationKind.Partial, [new ObservationReason("r-code", 1)]),
            at,
            [new ObservedAgent("a-key", "a-parent", "a-tool", "a-type", "a-alias", at, at, true)],
            [new ObservedMessage("m-agent", "m-id", at, "m-requested", "m-model", "m-effort", new TokenUsage(1, 2, 3, 4), true)],
            [new ObservedWrite("w-agent", at, at, "w-tool", new WriteTarget(WriteTargetKind.Checkout, "w-where"))],
            [new UnobservedLaunch("u-parent", "u-tool", at, "u-reason")],
            ReadDiagnostics.None);

        var strings = StringWalk.Strings(session);

        strings.Should().Contain(["s-id", "s-source", "r-code", "a-key", "a-parent", "a-tool", "a-type", "a-alias", "m-agent", "m-id", "m-requested", "m-model", "m-effort", "w-agent", "w-tool", "w-where", "u-parent", "u-tool", "u-reason"]);
    }

    // ---- the vocabulary --------------------------------------------------------------------------------------------------

    [Fact]
    public void Every_mutation_helper_has_a_sample()
    {
        var helpers = typeof(CallerTranscriptFixture).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.ReturnType == typeof(void)).Select(method => method.Name).Distinct().ToList();

        helpers.Should().NotBeEmpty();
        helpers.Should().BeEquivalentTo(Samples.Keys, "a helper added to the materialiser needs a row in this table");
    }

    [Fact]
    public void Every_reason_is_from_the_closed_vocabulary()
    {
        var produced = FixtureCodes().Concat(HelperCodes()).Concat(VendorCodes()).ToList();

        produced.Should().NotBeEmpty();
        produced.Distinct().Where(code => !IsInVocabulary(code)).Should().BeEmpty("every reason is a const of ObservationReasons, or 'no reader for' a vendor of the closed list");
        produced.Should().Contain(ObservationReasons.TornMeta, "the CorruptMeta sample reaches a reason that only a broken fixture produces");
    }

    [Fact]
    public void The_vocabulary_check_refuses_a_code_that_is_not_in_the_class()
    {
        IsInVocabulary("meta without model").Should().BeFalse("it was a reason once and is a diagnostics count now");
        IsInVocabulary(FixtureManifest.Sentinel).Should().BeFalse();
        IsInVocabulary("no reader for " + FixtureManifest.Sentinel).Should().BeFalse();
        IsInVocabulary(ObservationReasons.NoReaderFor("this vendor")).Should().BeTrue();
        IsInVocabulary(ObservationReasons.TornLine).Should().BeTrue();
    }

    private static List<string> FixtureCodes()
    {
        var codes = new List<string>();
        foreach (var fixtureCase in CasesOnDisk())
        {
            using var dir = TempDir.For(Prefix);
            codes.AddRange(CodesOf(CallerTranscriptFixture.Materialise(fixtureCase.Name, dir).Read()));
        }

        return codes;
    }

    /// <summary>Each sample read twice: with no bound, and with a bound past every line (the gate and the head rule do their work).</summary>
    private static List<string> HelperCodes()
    {
        var codes = new List<string>();
        foreach (var sample in Samples.Values)
        {
            using var dir = TempDir.For(Prefix);
            var fixture = CallerTranscriptFixture.Materialise(sample.Fixture, dir);
            sample.Apply(fixture);
            var budget = new ReadBudget(long.MaxValue, TimeSpan.MaxValue, TimeProvider.System) { MaxLineBytes = sample.MaxLineBytes ?? 64L * 1024 * 1024 };
            codes.AddRange(CodesOf(fixture.Read(budget)).Concat(CodesOf(fixture.Read(budget, fixture.BeyondTheGuard))));
        }

        return codes;
    }

    private static IEnumerable<string> CodesOf(ObservedSession session) =>
        session.State.Reasons.Select(reason => reason.Code).Concat(session.UnobservedLaunches.Select(launch => launch.Reason));

    /// <summary>Every vendor the reader refuses by name, and one it does not know: each ends in a reason that holds the vendor word only from the closed list.</summary>
    private static IEnumerable<string> VendorCodes() =>
        CallerDeclaration.KnownVendors.Where(vendor => vendor != "claude").Append("not a vendor!")
            .Select(vendor => CallerTranscriptLocator.Locate(vendor, _ => null, ClaudeHome.Default))
            .Select(location => CallerTranscript.Read(location, ReadBudget.Unbounded, null))
            .SelectMany(CodesOf);

    private static bool IsInVocabulary(string code)
    {
        const string NoReader = "no reader for ";
        var constants = typeof(ObservationReasons).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string)).Select(field => (string?)field.GetRawConstantValue());

        return constants.Contains(code)
            || (code.StartsWith(NoReader, StringComparison.Ordinal) && (CallerDeclaration.KnownVendors.Contains(code[NoReader.Length..]) || code[NoReader.Length..] == "this vendor"));
    }

    /// <summary>Every <c>string</c> reachable from an object through public instance properties, recursing into records and sequences. The graph is a tree of records, so it has no cycle to guard.</summary>
    private static class StringWalk
    {
        public static List<string> Strings(object root)
        {
            var found = new List<string>();
            Walk(root, found);

            return found;
        }

        private static void Walk(object? node, List<string> found)
        {
            switch (node)
            {
                case string text:
                    found.Add(text);
                    break;
                case IEnumerable items:
                    items.Cast<object?>().ToList().ForEach(item => Walk(item, found));
                    break;
                case not null when IsOurs(node.GetType()):
                    WalkProperties(node, found);
                    break;
            }
        }

        private static bool IsOurs(Type type) => type.Assembly == typeof(ObservedSession).Assembly && !type.IsEnum;

        private static void WalkProperties(object node, List<string> found) =>
            node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.GetIndexParameters().Length == 0 && property.GetMethod is not null).ToList()
                .ForEach(property => Walk(property.GetValue(node), found));
    }
}
