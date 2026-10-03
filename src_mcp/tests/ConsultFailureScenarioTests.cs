using System.Diagnostics;
using System.Text.Json;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A consultation turn that fails says WHICH failure it was and what to do about it — to the caller, on
/// the record, in the database and in the caller kind's health file — and a failure that was not the
/// caller's doing gives the call back.
/// </summary>
/// <remarks>
/// <para>Epic 2 of PLAN_the_consultant_works_on_every_vendor.md. Before it, every failed turn was one of a
/// handful of sentences, classified nowhere and persisted nowhere the panel could read; an empty answer
/// told the caller to "try once more with a sharper problem statement" whatever had actually happened,
/// and the call it had spent stayed spent.</para>
/// <para>The whole <c>consult</c> flow over the fake CLI standing in for codex (the shipped legacy map),
/// with the real launcher — a deadline is reproduced by a decorator that lifts the launch's OWN timeout,
/// so only the turn's deadline can end it, which is what the real launcher reports as a cancelled kill.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class ConsultFailureScenarioTests : ConsultScenarioBase
{
    private static void Fails(string stderr, int exit, string? stdout = null)
    {
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", null);
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", stdout);
        Environment.SetEnvironmentVariable("FAKECLI_STDERR", stderr);
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", exit.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private JsonElement RecordFile() =>
        JsonDocument.Parse(File.ReadAllText(Directory.EnumerateFiles(Path.Combine(_data, "consultations"), "*.json").Single())).RootElement;

    private static string Field(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private string HealthFile(string name) => Path.Combine(_data, "consultations", "health", name);

    [Fact]
    public async Task AnUnknownOption_IsVendorRefused_AndTheCureIsUpdateTheCli()
    {
        // claude 2.1.197, measured 2026-10-02, refusing the confined argv: `error: unknown option
        // '--restricted'`, exit 1. Any CLI too old for a flag this product sends says it this way.
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        Fails("error: unknown option '--restricted'", 1);

        var refused = Refusal(await Consult(Service(), "why is the count wrong"));

        refused.Should().Contain("(failure: vendor-refused)");
        refused.Should().Contain("update the CLI");
        Field(RecordFile(), "failureKind").Should().Be("vendor-refused");
    }

    [Fact]
    public async Task AVendorFailure_GivesTheCallBack_AndSaysSo()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var service = Service(callsPerSession: 1);
        Fails("codex: something went wrong", 3);

        var refused = Refusal(await Consult(service, "why is the count wrong"));

        refused.Should().Contain("(failure: exit)").And.Contain("was not counted");
        Answer("0198-second", Advice);
        var again = await Consult(service, "why is the count wrong, asked again");
        again.TryGetProperty("error", out var error).Should().BeFalse(error.ToString());
    }

    [Fact]
    public async Task ATreeTheConsultantChanged_KeepsTheCall()
    {
        // The consultant RAN and broke the one promise it runs under: that is not a call the vendor
        // failed to serve, and handing it back would make the cap free for a consultant that writes.
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var service = Service(callsPerSession: 1);
        var written = Path.Combine(_repo, "the-consultant-wrote-this.txt");
        Environment.SetEnvironmentVariable("FAKECLI_SIDE_EFFECT", written);
        try
        {
            Refusal(await Consult(service, "why is the count wrong")).Should().Contain("the working tree changed");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_SIDE_EFFECT", null);
        }

        Field(RecordFile(), "failureKind").Should().Be("tree-changed");
        Refusal(await Consult(service, "why is the count wrong, asked again")).Should().Contain("consult calls, the cap");
    }

    [Fact]
    public async Task ATurnEndedByItsOwnDeadline_IsADeadline_NotARecordFailure()
    {
        // The real launcher never throws on a cancelled token — it kills the tree and reports
        // `TimedOut: true, Cancelled: true` — and the snapshot after it then met a killed `git status`
        // and threw a ContextException, so a turn that ran out of time was told "the answer could not be
        // recorded". The snapshot now throws a cancellation for a cancelled token, and the turn's own
        // deadline reads as what it is.
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        Environment.SetEnvironmentVariable("FAKECLI_SLEEP_MS", "120000");
        var launcher = new LiftedTimeout(_launcher);
        var service = Service(launcher: launcher, reviewerTimeout: TimeSpan.FromSeconds(1));
        var clock = Stopwatch.StartNew();

        var refused = Refusal(await Consult(service, "why is the count wrong").WaitAsync(TimeSpan.FromSeconds(60)));

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(60));
        refused.Should().Contain("(failure: deadline)");
        refused.Should().NotContain("could not be recorded");
        Field(RecordFile(), "failureKind").Should().Be("deadline");
    }

    [Fact]
    public async Task TheLastFailure_AndTheLastAnswer_AreKeptPerCallerKind()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var service = Service();
        Fails("error: unknown option '--restricted'", 1);
        await Consult(service, "why is the count wrong");

        var failure = JsonDocument.Parse(File.ReadAllText(HealthFile("claude.failure.json"))).RootElement;
        Field(failure, "kind").Should().Be("vendor-refused");
        Field(failure, "cure").Should().Contain("update the CLI");
        Field(failure, "vendor").Should().Be("codex");
        File.Exists(HealthFile("claude.answer.json")).Should().BeFalse("nothing has answered yet");

        Answer("0198-second", Advice);
        await Consult(service, "why is the count wrong, asked again");

        var answer = JsonDocument.Parse(File.ReadAllText(HealthFile("claude.answer.json"))).RootElement;
        Field(answer, "vendor").Should().Be("codex");
        string.CompareOrdinal(Field(answer, "utc"), Field(failure, "utc")).Should().BePositive("the answer came after the failure");
        File.Exists(HealthFile("claude.failure.json")).Should().BeTrue("nothing is deleted: the reader decides which is current");
    }

    [Fact]
    public async Task AFailedConsultation_ReachesTheDatabase_WithItsKindAndCure()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        Fails("error: unknown option '--restricted'", 1);
        await Consult(Service(), "why is the count wrong");

        using var db = new SqliteConnection($"Data Source={Path.Combine(_data, CoaiMcp.Store.RoundsDb.FileName)};Mode=ReadOnly;Pooling=False");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT failure_kind, failure_cure, evidence FROM consultations";
        using var rows = read.ExecuteReader();

        rows.Read().Should().BeTrue("the failed consultation was projected");
        rows.GetString(0).Should().Be("vendor-refused");
        rows.GetString(1).Should().Contain("update the CLI");
    }

    [Fact]
    public async Task OldTemporaryHealthFiles_AndMonthOldEvidence_AreSweptByTheBeat()
    {
        var service = Service();
        var health = Directory.CreateDirectory(Path.Combine(_data, "consultations", "health")).FullName;
        var orphan = Path.Combine(health, "claude.failure.json.0123.tmp");
        var fresh = Path.Combine(health, "claude.answer.json.4567.tmp");
        File.WriteAllText(orphan, "{}");
        File.WriteAllText(fresh, "{}");
        File.SetLastWriteTimeUtc(orphan, DateTime.UtcNow.AddHours(-2));
        var evidence = Directory.CreateDirectory(Path.Combine(_data, "unparseable", "consultations")).FullName;
        var old = Path.Combine(evidence, "codex-20260801-120000.txt");
        var recent = Path.Combine(evidence, "codex-20261001-120000.txt");
        // The reviewers' evidence beside it — including a provider whose id happens to start "consult".
        var reviewers = Path.Combine(_data, "unparseable", "consult-o-Architecture-20260801-120000-000.txt");
        foreach (var file in (string[])[old, recent, reviewers])
        {
            File.WriteAllText(file, "transcript");
        }

        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-31));
        File.SetLastWriteTimeUtc(reviewers, DateTime.UtcNow.AddDays(-31));

        service.SweepConsultations();

        File.Exists(orphan).Should().BeFalse("an orphaned temporary file older than an hour is nobody's");
        File.Exists(fresh).Should().BeTrue("a temporary file this young may be another server's write in flight");
        File.Exists(old).Should().BeFalse("consultation evidence is kept for thirty days");
        File.Exists(recent).Should().BeTrue();
        File.Exists(reviewers).Should().BeTrue("a reviewer's evidence is not the consultation's to retire");
    }

    [Fact]
    public async Task TheSnapshot_ThrowsACancellation_ForACancelledToken_NotAGitFailure()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => new CoaiMcp.Runners.Consultation.FilesystemInvariant(_launcher).SnapshotAsync(_repo, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>("a killed `git status` is the token's doing, not git's");
    }

    [Fact]
    public async Task AnExitWithAConversation_EndsTheConsultation_AndOffersNoResume()
    {
        // Only a TRANSIENT failure is worth resuming (epic 2's review): a CLI that exited on an error of its
        // own will exit on it again, and "the consultant accepted the turn — call consult again" sent the
        // caller back into it.
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        Fails("codex: the model is not available on this plan", 1, stdout: "{\"type\":\"thread.started\",\"thread_id\":\"0198-kept\"}\n");

        var refused = Refusal(await Consult(Service(), "why is the count wrong"));

        refused.Should().Contain("(failure: exit)").And.NotContain("call consult again");
        refused.Should().Contain("do not retry this consultation", "a failure no retry cures says so");
        var record = new ConsultationStore(_data).All().Single();
        record.Status.Should().Be(ConsultationStatuses.Failed);
    }

    [Fact]
    public async Task ATransientFailureWithNoConversationOfItsOwn_IsNotResumable_OnTheRecordsOldHandle()
    {
        // The record's handle is the PREVIOUS turn's. A turn whose own launches named no conversation has
        // not been shown to have reached the vendor at all, so it is not declared resumable on that.
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var service = Service();
        var id = (await Consult(service, "why is the count wrong")).GetProperty("consultationId").GetString()!;
        Fails("codex: rate limit reached, try again in 2s", 1);

        var refused = Refusal(await Consult(service, "I checked, and it prints 3", id));

        refused.Should().Contain("(failure: rate-limited)").And.NotContain("call consult again");
        new ConsultationStore(_data).Read(id)!.Status.Should().Be(ConsultationStatuses.Failed);
    }

    [Fact]
    public async Task WhatAVendorSaid_IsRedacted_BeforeItIsWrittenAnywhere()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        const string Secret = "abcdefghijklmnopqrstuvwxyz0123456789";
        Fails($"error: unknown option '--restricted' (Authorization: Bearer {Secret})", 1);

        var refused = Refusal(await Consult(Service(), "why is the count wrong"));

        refused.Should().NotContain(Secret).And.Contain("[redacted]");
        File.ReadAllText(Directory.EnumerateFiles(Path.Combine(_data, "consultations"), "*.json").Single()).Should().NotContain(Secret);
        File.ReadAllText(HealthFile("claude.failure.json")).Should().NotContain(Secret);
        Directory.EnumerateFiles(Path.Combine(_data, "unparseable"), "*", SearchOption.AllDirectories)
            .Should().NotBeEmpty().And.OnlyContain(file => !File.ReadAllText(file).Contains(Secret));
    }

    [Fact]
    public async Task ARetentionDirectoryThatCannotBeRead_NeverStopsTheSweepEndingAStrandedRecord()
    {
        var service = Service();
        var anHourAgo = ConsultationStore.Stamp(DateTime.UtcNow.AddMinutes(-70));
        var stuck = new ConsultationRecord(
            ConsultationStore.NewId(), "caller-1", CallerIdentity.Claude, "no-session", _repo, "main", "0123abc",
            "codex", "gpt-5.6", "codex", ConsultationMemories.VendorRemembers, 5, anHourAgo)
        {
            Status = ConsultationStatuses.Asking,
            RunnerPid = Environment.ProcessId,
            UpdatedUtc = anHourAgo,
        };
        service.Consultations.Write(stuck);
        var health = Directory.CreateDirectory(Path.Combine(_data, "consultations", "health")).FullName;
        using var unreadable = Unreadable.Make(health);

        service.SweepConsultations().Should().Be(1, "the record's sweep is the job; a retention pass that cannot list a directory is not a reason to skip it");

        service.Consultations.Read(stuck.Id)!.Status.Should().Be(ConsultationStatuses.Failed);
    }

    /// <summary>A directory this process cannot list — an ACL deny on Windows, mode 000 elsewhere — undone on dispose.</summary>
    private sealed class Unreadable : IDisposable
    {
        private readonly string _dir;
        private readonly System.Security.AccessControl.FileSystemAccessRule? _rule;

        private Unreadable(string dir, System.Security.AccessControl.FileSystemAccessRule? rule)
        {
            _dir = dir;
            _rule = rule;
        }

        public static Unreadable Make(string dir)
        {
            if (OperatingSystem.IsWindows())
            {
                var rule = new System.Security.AccessControl.FileSystemAccessRule(
                    System.Security.Principal.WindowsIdentity.GetCurrent().User!,
                    System.Security.AccessControl.FileSystemRights.ListDirectory,
                    System.Security.AccessControl.AccessControlType.Deny);
                var info = new DirectoryInfo(dir);
                var acl = info.GetAccessControl();
                acl.AddAccessRule(rule);
                info.SetAccessControl(acl);
                Proves(dir);

                return new Unreadable(dir, rule);
            }

            File.SetUnixFileMode(dir, UnixFileMode.None);
            Proves(dir);

            return new Unreadable(dir, null);
        }

        /// <summary>A fixture the code does not reject proves nothing: as root a mode bites nobody.</summary>
        private static void Proves(string dir)
        {
            var bites = false;
            try
            {
                _ = Directory.EnumerateFiles(dir).ToList();
            }
            catch (UnauthorizedAccessException)
            {
                bites = true;
            }

            bites.Should().BeTrue("the directory must refuse a listing for this test to mean anything — is the suite running as root?");
        }

        public void Dispose()
        {
            if (OperatingSystem.IsWindows() && _rule is { } rule)
            {
                var info = new DirectoryInfo(_dir);
                var acl = info.GetAccessControl();
                acl.RemoveAccessRule(rule);
                info.SetAccessControl(acl);

                return;
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(_dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
    }

    /// <summary>The real launcher, with the consultant's own timeout lifted: only the turn's deadline can end it.</summary>
    private sealed class LiftedTimeout(IProcessLauncher inner) : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            inner.RunAsync(
                string.Equals(request.Executable, FakeCliExe, StringComparison.OrdinalIgnoreCase)
                    ? request with { Timeout = TimeSpan.FromMinutes(10) }
                    : request,
                ct);
    }
}
