using System.Diagnostics;
using System.Text.Json;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The panel's door onto <c>close_consult</c>, run as the PROCESS the panel runs.
/// </summary>
/// <remarks>
/// <para>The extension never speaks MCP to this binary — it drives one-shot modes selected from
/// <c>args[0]</c> before any transport opens. So the close control on a consultation card reaches
/// the server through <c>--close-consult</c> and through nothing else, and a unit test of
/// <see cref="ConsultationClosing"/> proves the rule while proving nothing about the flow. What is
/// exercised here is the argument parsing, the JSON on stdout and the EXIT CODES, against a real
/// record on disk. (issue #309; the missing bridge was named on the plan round.)</para>
/// <para>The exit codes are the part nothing else can check, because they are what the panel reads:
/// <b>0</b> recorded, <b>65</b> the request was refused or malformed. A refusal is an ANSWER — the
/// sentence is on stdout for the panel to show — which is why it is not the code a disk that would
/// not answer gets.</para>
/// <para><b>And never 64</b>, which this mode used to return for missing arguments. 64 is reserved,
/// by <c>.agents/PROJECT.md</c>, for "this binary has never heard of that mode" — it is how the
/// panel detects a server too old for a feature. A mode the binary HAS, answering it, sends the
/// panel to tell a person their server needs updating when the request was simply malformed.
/// <see cref="ARequestFaultIsNotAnOldBinaryTests"/> holds the rule; this holds it for this mode.
/// (codex SecurityReliability, the code round.)</para>
/// </remarks>
public sealed class CloseConsultCliScenarioTests : IDisposable
{
    private static string ServerExe
    {
        get
        {
            if (Environment.GetEnvironmentVariable("COAI_CONTRACT_EXE") is { Length: > 0 } published)
            {
                return published;
            }

            var configuration = AppContext.BaseDirectory.Contains("Release") ? "Release" : "Debug";

            return Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "src", "bin", configuration, "net10.0",
                OperatingSystem.IsWindows() ? "coai-mcp.exe" : "coai-mcp"));
        }
    }

    private readonly string _data = Directory.CreateTempSubdirectory("coai-close-cli-").FullName;
    private readonly string _repo = Directory.CreateTempSubdirectory("coai-close-repo-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var directory in new[] { _data, _repo })
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not a failing test.
            }
        }
    }

    private (int Code, string Out) Run(params string[] args)
    {
        var info = new ProcessStartInfo(ServerExe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        info.Environment["COAI_DATA_DIR"] = _data;
        using var process = Process.Start(info)!;

        // Both pipes drained concurrently, and a process past its deadline KILLED — the deadlock and
        // the orphan `LogCliScenarioTests` records, for the same reasons.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5_000);
            throw new TimeoutException("--close-consult did not answer and exit within a minute");
        }

        Task.WaitAll([stdout, stderr], 10_000).Should().BeTrue("the pipes close when the child does");

        return (process.ExitCode, stdout.Result);
    }

    /// <summary>A consultation on disk, as `consult` would have left it: answered, still open.</summary>
    private string Recorded(string outcome = "", string status = ConsultationStatuses.Open, string caller = "")
    {
        var store = new ConsultationStore(_data);
        var id = ConsultationStore.NewId();
        store.Write(new ConsultationRecord(
            id, caller.Length > 0 ? caller : $"repo:{_repo}", "claude", "s1", _repo, "main", "sha", "codex", "gpt-6", "codex",
            ConsultationMemories.VendorRemembers, 5, "2026-09-17T09:00:00.0000000Z")
        {
            Status = status,
            Outcome = outcome,
            // DATED, or the sweep this binary runs at startup finds it ancient and lapses it before
            // the close is even read — which is a true behaviour and not what this test is about.
            UpdatedUtc = ConsultationStore.Stamp(DateTime.UtcNow),
            Turns = [new("2026-09-17T09:00:10.0000000Z", "stuck", "try this", 10, 100, 20, null)],
        });

        return id;
    }

    private string OutcomeOf(string id) => new ConsultationStore(_data).Read(id)!.Outcome;

    private string OutcomeByOf(string id) => new ConsultationStore(_data).Read(id)!.OutcomeBy;

    [Fact]
    public void ThePanelsDoorRecordsAnOutcome()
    {
        var id = Recorded();

        var (code, said) = Run("--close-consult", "--repo", _repo, "--id", id, "--outcome", "solved",
            "--note", "the loop stopped one short");

        code.Should().Be(0, said);
        JsonDocument.Parse(said).RootElement.GetProperty("recorded").GetBoolean().Should().BeTrue();
        OutcomeOf(id).Should().Be("solved");
        // The door is part of the fact. "A person marked it closed" and "the AI said it worked" are
        // two different claims, and this is the one door that can say the first.
        OutcomeByOf(id).Should().Be("person");
    }

    /// <summary>
    /// The state the issue's own screenshot is in: a consultation nobody closed, which the clock ended.
    /// </summary>
    [Fact]
    public void ALapsedConsultationStillTakesAVerdictFromThePerson()
    {
        var id = Recorded(ConsultationOutcomes.Lapsed, ConsultationStatuses.Closed);

        var (code, said) = Run("--close-consult", "--repo", _repo, "--id", id, "--outcome", "not_solved");

        code.Should().Be(0, said);
        OutcomeOf(id).Should().Be("not_solved");
    }

    /// <summary>
    /// The person is NOT subject to the caller check — a session that has gone cannot close its own.
    /// </summary>
    [Fact]
    public void AConsultationOpenedByAnotherCallerIsStillClosableByHand()
    {
        var id = Recorded(caller: "repo:somewhere-else-entirely");

        var (code, said) = Run("--close-consult", "--repo", _repo, "--id", id, "--outcome", "abandoned");

        code.Should().Be(0, said);
        OutcomeOf(id).Should().Be("abandoned");
    }

    /// <summary>A malformed request is a DATA error, and never the code that means "too old".</summary>
    /// <remarks>
    /// Each of the three is a real way to get it wrong from the panel — no arguments at all, a repo
    /// without an id, an id without an outcome — and none of them says anything about the age of the
    /// binary that received it.
    /// </remarks>
    [Fact]
    public void MissingArgumentsAreADataError_NeverTheCodeThatMeansTooOld()
    {
        foreach (var malformed in new[]
        {
            new[] { "--close-consult" },
            ["--close-consult", "--repo", _repo],
            ["--close-consult", "--repo", _repo, "--id", Recorded()],
        })
        {
            Run(malformed).Code.Should().Be(
                65,
                "64 means 'this binary has never heard of --close-consult' and sends the panel to "
                + "tell a person to update a server that is fine");
        }
    }

    [Fact]
    public void ARefusalIsAnAnswerOnStdout_AndItsOwnExitCode()
    {
        var id = Recorded();

        var (code, said) = Run("--close-consult", "--repo", _repo, "--id", id, "--outcome", "probably_fine");

        code.Should().Be(65);
        said.Should().Contain("solved", "the refusal names the words that would work");
        OutcomeOf(id).Should().BeEmpty("a refused close writes nothing");
    }

    /// <summary>
    /// A consultation belonging to ANOTHER checkout is refused rather than closed under this one's lock.
    /// </summary>
    /// <remarks>
    /// The lock is taken by repository and the record is read by id. Those are two different things:
    /// a supplied path naming another checkout would take that one's lock and then write this record
    /// anyway, so a turn running in the REAL repository could overwrite the close. The path is
    /// checked rather than trusted. (codex, the first code round.)
    /// </remarks>
    [Fact]
    public void AConsultationFromAnotherCheckoutIsRefused_RatherThanClosedUnderTheWrongLock()
    {
        var id = Recorded();
        var elsewhere = Directory.CreateTempSubdirectory("coai-close-other-").FullName;
        try
        {
            var (code, said) = Run("--close-consult", "--repo", elsewhere, "--id", id, "--outcome", "solved");

            code.Should().Be(65);
            said.Should().Contain("close it from there");
            OutcomeOf(id).Should().BeEmpty("nothing was written under a lock that guards another tree");
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    /// <summary>
    /// And it does not TOUCH that other checkout's lock on the way to refusing.
    /// </summary>
    /// <remarks>
    /// <para>The residue of the finding above, raised again on the second code round and right the
    /// second time. Checking the path AFTER taking the supplied one's lock closes the data-loss half
    /// — nothing is written — and leaves two costs standing. The visible one is here: a close aimed
    /// at the wrong repository waits out the whole 30-second lock budget on a repository it has
    /// nothing to do with, and BLOCKS a consultation that is legitimately running there, before
    /// answering a refusal it could have given instantly from the record.</para>
    /// <para>The invisible one is worse and is why the fix is to take the RECORD's lock rather than
    /// to reorder two checks. Correctness rested on two normalisers agreeing about what one path is:
    /// <c>SamePath</c> resolves links (<c>DocumentReader.CanonicalRoot</c>), and
    /// <c>RepositoryLock.Normalise</c> does not — it is <c>GetFullPath</c> and a lowercase. Where
    /// those differ, and <c>SamePath</c>'s own remarks record that they DO on macOS, where
    /// <c>/var</c> is a link, two spellings of one checkout pass the path check and take two
    /// DIFFERENT locks: the close then writes while a turn in the same tree is running, which is the
    /// one thing the lock exists to prevent. Deriving the lock from the record's own
    /// <c>RepoPath</c> — the spelling git resolved — leaves nothing for two normalisers to disagree
    /// about. (codex Architecture, the second code round.)</para>
    /// </remarks>
    [Fact]
    public void AndItDoesNotWaitOnTheOtherCheckoutsLockToSayNo()
    {
        var id = Recorded();
        var elsewhere = Directory.CreateTempSubdirectory("coai-close-other-").FullName;
        // HELD, the way a consultation running there holds it — a FileShare.None handle in the data
        // directory, which is exactly what RepositoryLock opens.
        var theirs = RepositoryLock.PathFor(_data, elsewhere);
        Directory.CreateDirectory(Path.GetDirectoryName(theirs)!);
        using (var _ = new FileStream(theirs, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var started = Stopwatch.StartNew();

            var (code, said) = Run("--close-consult", "--repo", elsewhere, "--id", id, "--outcome", "solved");

            started.Stop();
            code.Should().Be(65, said);
            said.Should().Contain(
                "close it from there",
                "the refusal is decided from the RECORD, so a lock held in an unrelated checkout cannot change it");
            started.Elapsed.Should().BeLessThan(
                RepositoryLock.DefaultWait,
                "the close waited out another repository's lock budget before refusing — and for as long as it "
                + "waited it was also blocking whatever legitimately holds that lock");
        }

        Directory.Delete(elsewhere, recursive: true);
    }

    /// <summary>
    /// WHICH lock was taken, read off the filesystem rather than off a clock.
    /// </summary>
    /// <remarks>
    /// <para>Both halves in one case, and neither of them waits: taking a lock CREATES its file, so
    /// the question "whose lock did this close take" has a direct answer. A successful close leaves
    /// the consultation's own lock file behind — so the fix above was not bought by taking no lock at
    /// all — and a close aimed at another checkout leaves that checkout's nowhere, which is the
    /// statement the timing assertion above can only make indirectly.</para>
    /// <para>The alternative was to hold the consultation's own lock and assert the refusal, which
    /// proves the same thing and pays the full 30-second budget on every run of this suite for ever.
    /// A guard that makes the suite slower than the product is a guard people learn to skip.</para>
    /// </remarks>
    [Fact]
    public void TheLockFileItLeavesBehindIsTheConsultationsOwn()
    {
        var id = Recorded();
        var elsewhere = Directory.CreateTempSubdirectory("coai-close-other-").FullName;
        try
        {
            var ours = RepositoryLock.PathFor(_data, _repo);
            var theirs = RepositoryLock.PathFor(_data, elsewhere);
            theirs.Should().NotBe(ours, "the two checkouts must hash to different locks for this to say anything");

            Run("--close-consult", "--repo", elsewhere, "--id", id, "--outcome", "solved").Code.Should().Be(65);

            File.Exists(theirs).Should().BeFalse(
                "a close refused from the record took an unrelated repository's lock on the way to refusing");

            Run("--close-consult", "--repo", _repo, "--id", id, "--outcome", "solved").Code.Should().Be(0);

            File.Exists(ours).Should().BeTrue(
                "the close that succeeded took no lock at all, which is not what the fix was for");
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    [Fact]
    public void AConsultationThisServerNeverWroteIsRefused()
    {
        var (code, said) = Run("--close-consult", "--repo", _repo, "--id", new string('a', 32), "--outcome", "solved");

        code.Should().Be(65);
        said.Should().Contain("no consultation");
    }
}
