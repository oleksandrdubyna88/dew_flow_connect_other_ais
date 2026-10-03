using System.Text.Json.Serialization;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>
/// <c>coai-mcp --consultants</c>'s answer, and <c>consultations/health/consultants.json</c>'s contents: one row per
/// caller kind, taken at <see cref="Utc"/> on <see cref="Side"/>.
/// </summary>
/// <remarks>
/// The time and the side travel WITH the answer because the file is read by the other side too — a plain Windows
/// window reading a WSL store through <c>coai.alsoWatchDataDirectories</c> — which has to say when the facts were
/// taken and on which machine (cadence consultation 435b1b25).
/// </remarks>
public sealed record ConsultantsAnswer
{
    public string Utc { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary><c>windows</c>, <c>wsl</c>, <c>linux</c>, <c>macos</c> or <c>other</c>.</summary>
    public string Side { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>The WSL distribution (<c>WSL_DISTRO_NAME</c>) on a WSL side; empty everywhere else.</summary>
    public string Distro { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>
    /// The staleness a reader across the seam judges a check's heartbeat by (<see cref="ConsultCheckRecord.HeartbeatStaleAfterSeconds"/>)
    /// — beside the rows too, so the file the other side reads first says it as well.
    /// </summary>
    public int HeartbeatStaleAfterSeconds => (int)ConsultCheckState.HeartbeatStaleAfter.TotalSeconds;

    public IReadOnlyList<ConsultantRowReport> Consultants { get => field ?? []; init; } = [];
}

/// <summary>One caller kind's consultant — what it resolves to and everything known about it, or why it cannot be had.</summary>
public sealed record ConsultantRowReport
{
    public string CallerKind { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>A consultation could be had right now — <c>ConsultationService.Preflight</c>'s answer, the tool's own.</summary>
    public bool Available { get; init; }

    /// <summary>Empty when available; otherwise exactly the sentence <c>consult</c> would refuse with.</summary>
    public string Reason { get => field ?? string.Empty; init; } = string.Empty;

    public string Vendor { get => field ?? string.Empty; init; } = string.Empty;

    public string Runtime { get => field ?? string.Empty; init; } = string.Empty;

    public string Model { get => field ?? string.Empty; init; } = string.Empty;

    public string ExecutablePath { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>What the CLI said about itself — never whether anyone is signed in.</summary>
    public ConsultantCliReport Cli { get => field ?? ConsultantCliReport.NotProbed; init; } = ConsultantCliReport.NotProbed;

    /// <summary>For claude: <c>restricted</c>, <c>no-restricted</c> or <c>unknown</c> — what its <c>--help</c> said. Empty otherwise.</summary>
    public string Capability { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>What this consultant can be kept from reading on THIS side (<c>shared/consultant-limitations.json</c>).</summary>
    public ConsultantLimitationReport? Limitation { get; init; }

    /// <summary>For antigravity: where agy reads its allow rules on this side, and on Linux/WSL the rule with its warning.</summary>
    public ConsultantAgyReport? Agy { get; init; }

    public ConsultHealthAnswer? LastAnswer { get; init; }

    public ConsultHealthFailure? LastFailure { get; init; }

    /// <summary>
    /// The last failure is THIS row's consultant's current state (<see cref="ConsultHealth.Current"/>) — the one rule; the
    /// panel shows this verdict and decides nothing about it.
    /// </summary>
    public bool FailureCurrent { get; init; }

    /// <summary>
    /// Why a health file of this caller kind could not be read — empty when both read or are absent. A row with this set
    /// is never shown as healthy: its outcome is not known (the whole-branch review, A4).
    /// </summary>
    public string HealthUnreadable { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>The last check, settled by its lock (<see cref="ConsultCheckState.Settled"/>) — null when none was ever run.</summary>
    public ConsultCheckRecord? Check { get; init; }
}

/// <param name="Probed">The CLI was asked — false for a row that is not available, where nothing was run.</param>
/// <param name="Found">The executable could be STARTED (<see cref="VendorHealth.CliFound"/>).</param>
/// <param name="AuthSource"><c>own auth</c>, <c>vault key</c> or <c>unavailable</c> — where a credential would come from, not a sign-in.</param>
public sealed record ConsultantCliReport(bool Probed, bool Found, string Version, string AuthSource, string Note)
{
    public static ConsultantCliReport NotProbed { get; } = new(false, false, string.Empty, string.Empty, string.Empty);
}

/// <param name="Standing"><c>confined</c>, <c>unconfined</c>, <c>default-deny</c> or <c>unmeasured</c>.</param>
/// <param name="Evidence"><c>measured</c> or <c>source</c> — which of the two kinds of backing the row has.</param>
public sealed record ConsultantLimitationReport(
    string Standing,
    string Text,
    string Capability,
    string Evidence,
    string MeasuredDate,
    string MeasuredCliVersion,
    string MeasuredCells,
    string MeasuredDocument,
    string Source);

/// <param name="SettingsPath">Where agy reads <c>permissions.allow</c> on this side, with the home directory filled in.</param>
/// <param name="Snippet">A rule to paste — on Linux/WSL only, where a prefix rule was measured to work; empty elsewhere.</param>
/// <param name="SnippetWarning">The sentence that travels with the snippet: a prefix rule is not read-only.</param>
public sealed record ConsultantAgyReport(string SettingsPath, string Snippet, string SnippetWarning);

/// <summary>
/// One caller kind as the survey found it, the IO done — a closed union, so "resolves to nothing", "a vendor
/// <c>consult</c> would refuse" and "probed" are three cases rather than a row of nullable members.
/// </summary>
/// <remarks>
/// The whole-branch review of PLAN_the_consultant_works_on_every_vendor.md (2026-10-03), finding I: the facts used to be
/// one record with five nullable members whose meaningful combinations nothing named.
/// </remarks>
public abstract record SurveyedConsultant(string CallerKind, ConsultPreflight Preflight)
{
    /// <summary>The entry resolves to no vendor, or the routing cannot be read.</summary>
    public sealed record Unresolved(string CallerKind, ConsultPreflight Preflight) : SurveyedConsultant(CallerKind, Preflight);

    /// <summary>A vendor <c>consult</c> would refuse — reported with the refusal, nothing run for it.</summary>
    /// <param name="Capability">What its CLI would have been asked — <see cref="CliCapability.NotAsked"/> or <see cref="CliCapability.NotApplicable"/>.</param>
    public sealed record Refused(string CallerKind, ConsultPreflight Preflight, ProviderSettings Vendor, CliCapability Capability)
        : SurveyedConsultant(CallerKind, Preflight);

    /// <summary>An available vendor, its CLI's probe, and what its CLI said about the flags its argv depends on.</summary>
    public sealed record Probed(string CallerKind, ConsultPreflight Preflight, ProviderSettings Vendor, VendorHealth Health, CliCapability Capability)
        : SurveyedConsultant(CallerKind, Preflight);
}

/// <summary>What a consultant's CLI said about the flags its argv depends on — beyond its version.</summary>
public abstract record CliCapability
{
    private CliCapability() { }

    /// <summary>This runtime's argv does not depend on its installed CLI (<see cref="IConsultantRuntime.CapabilityExecutable"/> is empty).</summary>
    public sealed record NotApplicable : CliCapability;

    /// <summary>It does, and nobody asked: the row is not available, so nothing was run. Reported as <c>unknown</c>.</summary>
    public sealed record NotAsked : CliCapability;

    /// <summary>claude's <c>--help</c>, as it answered.</summary>
    public sealed record Claude(ClaudeCapability Said) : CliCapability;

    /// <summary>The word a limitation row is qualified by — empty for anything a claude row cannot be chosen by.</summary>
    public string Qualifier => this is Claude claude ? claude.Said.Qualifier : string.Empty;

    /// <summary>The row's <c>capability</c> field: the qualifier, <c>unknown</c> when it could not be told, empty when it does not apply.</summary>
    public string Reported => this switch
    {
        NotApplicable => string.Empty,
        _ when Qualifier.Length > 0 => Qualifier,
        _ => "unknown",
    };
}

/// <summary>What is on disk about one caller kind's outcomes: the last answer, the last failure, the last check.</summary>
public sealed record ConsultantOutcomes(HealthOnDisk<ConsultHealthAnswer> LastAnswer, HealthOnDisk<ConsultHealthFailure> LastFailure, CheckOnDisk Check)
{
    public static ConsultantOutcomes Nothing { get; } =
        new(new HealthOnDisk<ConsultHealthAnswer>.None(), new HealthOnDisk<ConsultHealthFailure>.None(), new CheckOnDisk.None());
}

/// <summary>A row's outcome fields as the wire carries them — computed in ONE place for the survey and every refresh.</summary>
/// <param name="HealthUnreadable">Why a health file could not be read — empty when both read (or were absent).</param>
public sealed record ConsultantRowOutcomes(ConsultHealthAnswer? LastAnswer, ConsultHealthFailure? LastFailure, bool FailureCurrent, string HealthUnreadable);

/// <summary>How one caller kind's facts become its row. Pure — the host and the home directory are handed in.</summary>
public static class ConsultantsReport
{
    public const string Antigravity = "antigravity";

    /// <param name="host">The side the limitations are looked up for — <see cref="HostKinds.Current"/> in production.</param>
    /// <param name="expandHome">Fills <c>%USERPROFILE%</c> / <c>~</c> in agy's settings path with this side's home.</param>
    public static ConsultantRowReport Row(SurveyedConsultant surveyed, ConsultantOutcomes outcomes, HostKind host, Func<string, string> expandHome) =>
        surveyed switch
        {
            SurveyedConsultant.Probed probed => Resolved(surveyed, outcomes, probed.Vendor, probed.Capability, host, expandHome) with { Cli = CliOf(probed.Health) },
            SurveyedConsultant.Refused refused => Resolved(surveyed, outcomes, refused.Vendor, refused.Capability, host, expandHome),
            // A row that names nobody: no failure can be ITS failure, so none is current — the files are still shown.
            _ => Common(surveyed, outcomes, new ConsultantIdentity(string.Empty, string.Empty)),
        };

    /// <summary>
    /// The outcome fields of the row that names <paramref name="row"/> — the files as they are, the ONE rule
    /// (<see cref="ConsultHealth.Current"/>) for whether the failure is current, and why a file could not be read.
    /// </summary>
    public static ConsultantRowOutcomes Outcomes(HealthOnDisk<ConsultHealthAnswer> answer, HealthOnDisk<ConsultHealthFailure> failure, ConsultantIdentity row) =>
        new(answer.ValueOrNull, failure.ValueOrNull, ConsultHealth.Current(answer, failure, row),
            string.Join("; ", new[] { answer.WhyUnreadable, failure.WhyUnreadable }.Where(why => why.Length > 0)));

    /// <summary>What every row carries, resolved or not: availability, the health files, the check.</summary>
    private static ConsultantRowReport Common(SurveyedConsultant surveyed, ConsultantOutcomes outcomes, ConsultantIdentity identity)
    {
        var said = Outcomes(outcomes.LastAnswer, outcomes.LastFailure, identity);

        return new()
        {
            CallerKind = surveyed.CallerKind,
            Available = surveyed.Preflight.Available,
            Reason = surveyed.Preflight.Reason,
            LastAnswer = said.LastAnswer,
            LastFailure = said.LastFailure,
            FailureCurrent = said.FailureCurrent,
            HealthUnreadable = said.HealthUnreadable,
            Check = CheckOf(surveyed.CallerKind, outcomes.Check),
        };
    }

    /// <summary>A row whose entry resolves to a vendor: who it is, what its failure is, and what it can be kept from reading here.</summary>
    private static ConsultantRowReport Resolved(
        SurveyedConsultant surveyed, ConsultantOutcomes outcomes, ProviderSettings vendor, CliCapability capability, HostKind host, Func<string, string> expandHome)
    {
        var runtime = RuntimeResolution.NameOf(vendor.Identity());
        // A claude nobody could probe has no qualifier, matches no claude row and reads `unmeasured` — never a guess.
        var limitation = ConsultantLimitations.Lookup(host, runtime, capability.Qualifier);

        return Common(surveyed, outcomes, new ConsultantIdentity(vendor.Provider, vendor.Model)) with
        {
            Vendor = vendor.Provider,
            Runtime = runtime,
            Model = vendor.Model,
            ExecutablePath = vendor.ExecutablePath,
            Capability = capability.Reported,
            Limitation = Report(limitation),
            Agy = runtime == Antigravity ? AgyOf(limitation, expandHome) : null,
        };
    }

    private static ConsultantCliReport CliOf(VendorHealth health) =>
        new(true, health.CliFound, health.Version, health.Auth, health.Note);

    /// <summary>The check as the wire carries it: the judged record, a record saying it is unreadable, or none.</summary>
    private static ConsultCheckRecord? CheckOf(string callerKind, CheckOnDisk check) => check switch
    {
        CheckOnDisk.Found found => found.Record,
        CheckOnDisk.Unreadable unreadable => new ConsultCheckRecord { CallerKind = callerKind, State = ConsultCheckStates.Unreadable, Reason = unreadable.Why },
        _ => null,
    };

    private static ConsultantLimitationReport Report(ConsultantLimitation row) => row.Evidence switch
    {
        LimitationEvidence.Measured m => new(ConsultantLimitations.Word(row.Standing), row.Text, row.Capability, "measured",
            m.Date, m.CliVersion, m.Cells, m.Document, string.Empty),
        LimitationEvidence.Sourced s => new(ConsultantLimitations.Word(row.Standing), row.Text, row.Capability, "source",
            string.Empty, string.Empty, string.Empty, string.Empty, s.Sentence),
        _ => throw new InvalidOperationException("the union is closed"),
    };

    /// <summary>The settings path for this side, and the snippet with its warning exactly where the row carries one.</summary>
    /// <remarks>The loader admits a snippet on linux/wsl rows only, so a Windows or macOS side never gets one here.</remarks>
    private static ConsultantAgyReport AgyOf(ConsultantLimitation row, Func<string, string> expandHome) =>
        new(row.SettingsPath.Length > 0 ? expandHome(row.SettingsPath) : string.Empty, row.Snippet, row.SnippetWarning);
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(ConsultantsAnswer))]
internal sealed partial class ConsultantsJsonContext : JsonSerializerContext;
