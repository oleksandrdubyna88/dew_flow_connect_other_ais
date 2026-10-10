using System.Text.Json;
using System.Text.RegularExpressions;
using CoaiServer;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The ledger lines the deployed Team server actually WRITES — three of them, copied from its live
/// <c>usage.jsonl</c> on 2026-10-10 and redacted (story 1.2 of <c>todo/PLAN_every_round_is_counted.md</c>).
/// </summary>
/// <remarks>
/// <para>One <c>ok</c>, one withdrawal the server wrote as <c>TimedOut</c> with zero tokens (8.4 s — the client
/// gave up, the vendor was not slow; <c>ProcessLauncher</c> marks it <c>Cancelled</c> and <c>ReviewerExecutor</c>
/// reads only <c>TimedOut</c>), one <c>RateLimited</c>. They are history, not a shape this suite composed: a
/// reader change in E3 is tested against what is on disk, and the three defects E3 fixes are visible in them —
/// no cached or reasoning count, no usage note, a withdrawal filed as a timeout.</para>
/// <para><b>The repository is public.</b> Every line keeps only the ledger's own keys, its one address is a
/// placeholder, and nothing in it names a host. The tests below hold that for every file in the folder, so a
/// fourth fixture pasted from the live ledger is refused until it is redacted the same way.</para>
/// </remarks>
public sealed partial class UsageLedgerFixtureTests
{
    /// <summary>The one address a fixture may carry, standing where the person's email was.</summary>
    internal const string Placeholder = "person@example.invalid";

    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "fixtures", "usage");

    /// <summary>The ledger's own keys (<c>UsageEntry</c> in Runners, camel-cased) — and nothing else.</summary>
    private static readonly IReadOnlySet<string> AllowedKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "utc", "provider", "model", "role", "stage", "seconds", "tokensIn", "tokensOut", "tokensCached",
        "tokensReasoning", "costUsd", "outcome", "kind", "usageNote", "costNote", "email",
    };

    /// <summary>The day the lines were written, as the reader's half-open window.</summary>
    private static readonly UsageRange TheDay = new(
        new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero));

    public static TheoryData<string> Fixtures =>
        [.. Directory.EnumerateFiles(Folder, "*.jsonl").Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];

    [Fact]
    public void TheThreeShapesThePlanAskedFor_ArePresent() =>
        // The companion of every scan below: a folder the copy rule forgot would make the theories pass
        // over nothing. The three names are the three stories E3 reads them in.
        Fixtures.Select(row => row.Data).Should().BeEquivalentTo(
            ["ok.jsonl", "rate-limited.jsonl", "timed-out-withdrawal.jsonl"]);

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EveryLine_CarriesOnlyTheLedgersOwnKeys(string file)
    {
        foreach (var line in Lines(file))
        {
            using var document = JsonDocument.Parse(line);
            document.RootElement.EnumerateObject().Select(property => property.Name)
                .Should().BeSubsetOf(AllowedKeys, $"{file} may carry only what the ledger writes");
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EveryLine_CarriesNoAddressButThePlaceholder(string file)
    {
        foreach (var line in Lines(file))
        {
            line.Count(c => c == '@').Should().Be(Placeholder.Count(c => c == '@'), $"{file}: the only @ is the placeholder's");
            line.Should().Contain(Placeholder, $"{file}: a server line names who spent it, so the placeholder stands there");
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EveryLine_NamesNoHostAndNoAddress(string file)
    {
        foreach (var line in Lines(file))
        {
            var rest = line.Replace(Placeholder, string.Empty, StringComparison.Ordinal);
            Ipv4().IsMatch(rest).Should().BeFalse($"{file} must not carry an IP address");
            HostLike().IsMatch(rest).Should().BeFalse($"{file} must not carry a host name");
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EveryLine_ReadsThroughTheServersOwnReader(string file)
    {
        var dataDir = Directory.CreateTempSubdirectory("coai-usage-fixture-").FullName;
        try
        {
            File.Copy(Path.Combine(Folder, file), Path.Combine(dataDir, "usage.jsonl"));

            var scan = new UsageReader(dataDir).Read(TheDay);

            scan.Unreadable.Should().Be(0, $"{file} is a line the deployed server wrote; the reader must take it");
            scan.Lines.Should().ContainSingle().Which.Should().Match<UsageLine>(read =>
                read.Email == Placeholder && read.Kind == JobKind.Review && read.Outcome == OutcomeOf(file));
        }
        finally
        {
            Directory.Delete(dataDir, recursive: true);
        }
    }

    [Fact]
    public void TheWithdrawal_IsWrittenTimedOutWithNothingCounted_WhichIsTheDefectE3Names()
    {
        // What the deployed server writes TODAY, named honestly rather than fixed here: a launch the client
        // withdrew after 8.4 s is filed as the vendor's own timeout, and its usage as a free 0/0 instead of
        // "not captured". Story 3.2 changes the outcome; this test changes with it, on purpose.
        using var document = JsonDocument.Parse(Lines("timed-out-withdrawal.jsonl").Single());
        var line = document.RootElement;

        line.GetProperty("outcome").GetString().Should().Be("TimedOut");
        line.GetProperty("seconds").GetDouble().Should().BeLessThan(10, "a withdrawal, not a vendor that took its whole budget");
        line.GetProperty("tokensIn").GetInt64().Should().Be(0);
        line.GetProperty("tokensOut").GetInt64().Should().Be(0);
        line.TryGetProperty("usageNote", out _).Should().BeFalse("the deployed server writes no usage note yet");
    }

    private static IReadOnlyList<string> Lines(string file) =>
        [.. File.ReadAllLines(Path.Combine(Folder, file)).Where(line => line.Trim().Length > 0)];

    /// <summary>The outcome each file is named for — read from the name, so a mislabelled fixture is caught.</summary>
    private static string OutcomeOf(string file) => file switch
    {
        "ok.jsonl" => "ok",
        "rate-limited.jsonl" => "RateLimited",
        "timed-out-withdrawal.jsonl" => "TimedOut",
        _ => throw new ArgumentException($"no outcome is named for fixture '{file}'", nameof(file)),
    };

    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b")]
    private static partial Regex Ipv4();

    /// <summary>A label, a dot, two or more letters — a host name's shape; a model id's <c>5.6</c> is digits after the dot and does not match.</summary>
    [GeneratedRegex(@"\b[a-z0-9-]+\.[a-z]{2,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex HostLike();
}
