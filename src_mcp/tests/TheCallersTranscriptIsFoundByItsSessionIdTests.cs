using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E3.S2 of <c>todo/PLAN_a_round_records_who_wrote_the_code_and_how_much_E3.md</c>: "who is calling" becomes either a located
/// transcript or a NAMED reason it cannot be observed. The id comes from the Claude session registry, then the session variable,
/// then a stored id; the file is found as <c>&lt;projects&gt;/*/&lt;id&gt;.jsonl</c> with no slug computed and no recursion.
/// </summary>
/// <remarks>
/// Every fixture here is built against the REAL validator (testing.md, a fixture the code rejects proves nothing): the ids are
/// version-4 GUIDs that pass <c>Guid.TryParseExact(.., "D")</c>, and the first test asserts <c>Found</c>, so a fixture the locator
/// declines cannot make the refusals pass. No test reads a developer's profile: every home is a <c>TempDir</c>.
/// </remarks>
public sealed class TheCallersTranscriptIsFoundByItsSessionIdTests
{
    private const string A = "11111111-1111-4111-8111-111111111111";
    private const string B = "22222222-2222-4222-8222-222222222222";
    private const string Pid = "52044";

    private static Func<string, string?> Env(params (string Name, string Value)[] pairs) =>
        name => pairs.Where(p => p.Name == name).Select(p => p.Value).FirstOrDefault();

    private static ClaudeHome HomeIn(TempDir dir)
    {
        Directory.CreateDirectory(dir.At("projects"));
        Directory.CreateDirectory(dir.At("sessions"));

        return new ClaudeHome(dir.At("projects"), dir.At("sessions"));
    }

    private static string Plant(ClaudeHome home, string folder, string id)
    {
        var dir = Directory.CreateDirectory(Path.Combine(home.ProjectsDir, folder)).FullName;
        var file = Path.Combine(dir, id + ".jsonl");
        File.WriteAllText(file, "{}\n");

        return file;
    }

    private static void Register(ClaudeHome home, string pid, string json) => File.WriteAllText(Path.Combine(home.SessionsDir, pid + ".json"), json);

    private static string RegistryEntry(string id) => "{\"pid\":52044,\"sessionId\":\"" + id + "\",\"kind\":\"interactive\"}";

    private static CallerTranscriptLocation.Found Found(CallerTranscriptLocation result)
    {
        result.Should().BeOfType<CallerTranscriptLocation.Found>();

        return (CallerTranscriptLocation.Found)result;
    }

    private static CallerTranscriptLocation.NotObservable Refused(CallerTranscriptLocation result)
    {
        result.Should().BeOfType<CallerTranscriptLocation.NotObservable>();

        return (CallerTranscriptLocation.NotObservable)result;
    }

    [Fact]
    public void The_registry_wins_over_the_variable_and_says_so()
    {
        using var dir = TempDir.For("testsToDelete-coai-locator-registry-");
        var home = HomeIn(dir);
        var main = Plant(home, "any-folder", A);
        Plant(home, "any-folder", B);
        Register(home, Pid, RegistryEntry(A));

        var found = Found(CallerTranscriptLocator.Locate("claude", Env(("CLAUDE_PID", Pid), ("CLAUDE_CODE_SESSION_ID", B)), home));

        found.Session.Should().Be(new CallerSessionId(A, CallerSessionId.Registry));
        found.MainFile.Should().Be(main);
        found.SessionFolder.Should().Be(Path.Combine(home.ProjectsDir, "any-folder", A), "the folder of subagents and metas; it need not exist");
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("{\"sessionId\":\"22222222-2222")]
    [InlineData("{\"pid\":52044}")]
    [InlineData("{\"sessionId\":5}")]
    [InlineData("[\"not an object\"]")]
    [InlineData("{\"sessionId\":\"../../etc/passwd\"}")]
    public void An_unreadable_or_absent_registry_falls_back_to_the_variable(string registry)
    {
        using var dir = TempDir.For("testsToDelete-coai-locator-fallback-");
        var home = HomeIn(dir);
        Plant(home, "any-folder", B);
        if (registry != "absent")
        {
            Register(home, Pid, registry);
        }

        var found = Found(CallerTranscriptLocator.Locate("claude", Env(("CLAUDE_PID", Pid), ("CLAUDE_CODE_SESSION_ID", B)), home));

        found.Session.Should().Be(new CallerSessionId(B, CallerSessionId.Environment));
    }

    [Fact]
    public void A_registry_file_over_64_KiB_is_not_read()
    {
        using var dir = TempDir.For("testsToDelete-coai-locator-big-");
        var home = HomeIn(dir);
        Plant(home, "any-folder", A);
        Plant(home, "any-folder", B);
        Register(home, Pid, RegistryEntry(A) + new string(' ', 70 * 1024));

        var found = Found(CallerTranscriptLocator.Locate("claude", Env(("CLAUDE_PID", Pid), ("CLAUDE_CODE_SESSION_ID", B)), home));

        found.Session.Source.Should().Be(CallerSessionId.Environment, "an oversized file is refused, so the variable answers");
    }

    [Fact]
    public void The_registry_is_chosen_by_the_pid_never_by_a_listing()
    {
        // A listing implementation would find <otherpid>.json, and a `<pid>.<hash>.key` sits beside every real registry file.
        using var dir = TempDir.For("testsToDelete-coai-locator-bypid-");
        var home = HomeIn(dir);
        Plant(home, "any-folder", A);
        Plant(home, "any-folder", B);
        Register(home, "77777", RegistryEntry(A));
        File.WriteAllText(Path.Combine(home.SessionsDir, Pid + ".0123abcd.key"), "not json");

        var found = Found(CallerTranscriptLocator.Locate("claude", Env(("CLAUDE_PID", Pid), ("CLAUDE_CODE_SESSION_ID", B)), home));

        found.Session.Should().Be(new CallerSessionId(B, CallerSessionId.Environment));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("12345678901")]
    [InlineData("..\\x")]
    [InlineData("1/2")]
    public void A_pid_that_is_not_one_to_ten_digits_is_never_made_into_a_file_name(string pid)
    {
        using var dir = TempDir.For("testsToDelete-coai-locator-pid-");
        var home = HomeIn(dir);
        Plant(home, "any-folder", A);
        Plant(home, "any-folder", B);
        Register(home, "1", RegistryEntry(A));

        var found = Found(CallerTranscriptLocator.Locate("claude", Env(("CLAUDE_PID", pid), ("CLAUDE_CODE_SESSION_ID", B)), home));

        found.Session.Source.Should().Be(CallerSessionId.Environment);
    }

    [Fact]
    public void A_stated_session_alone_is_a_stated_id()
    {
        // The id is a valid GUID with a transcript on disk, and it still names nothing: an operator's string says nothing about
        // whose client this is (parent §16 D9).
        using var dir = TempDir.For("testsToDelete-coai-locator-stated-");
        var home = HomeIn(dir);
        Plant(home, "any-folder", A);

        var refused = Refused(CallerTranscriptLocator.Locate("unknown", Env(("COAI_CALLER_SESSION", A)), home));

        refused.Reason.Should().Be(CallerTranscriptLocation.StatedId);
        refused.Session.Should().Be(CallerSessionId.None);
    }

    [Fact]
    public void Reusing_the_round_callers_ladder_would_read_a_stated_id_and_that_is_the_point()
    {
        // The refuted approach, in shape: CallerIdentity.From puts COAI_CALLER_SESSION FIRST and hands back its value as an id. A
        // locator built on it would open the transcript of whatever string the operator typed.
        var env = Env(("COAI_CALLER_SESSION", A));

        var ladder = CallerIdentity.From(env);

        ladder.Id.Should().Be(A, "the round caller's ladder treats the stated string as the caller's id");
        ladder.Vendor.Should().Be(CallerIdentity.Stated);
        using var dir = TempDir.For("testsToDelete-coai-locator-ladder-");
        var home = HomeIn(dir);
        Plant(home, "any-folder", A);
        // CallerDeclaration resolves a stated id to the vendor "unknown", and that is what the locator is told.
        Refused(CallerTranscriptLocator.Locate(CallerIdentity.Unknown, env, home)).Reason.Should().Be(CallerTranscriptLocation.StatedId);
    }

    [Fact]
    public void Nothing_set_is_no_session_id()
    {
        using var dir = TempDir.For("testsToDelete-coai-locator-none-");

        Refused(CallerTranscriptLocator.Locate("claude", Env(), HomeIn(dir))).Reason.Should().Be(CallerTranscriptLocation.NoSessionId);
        Refused(CallerTranscriptLocator.Locate("claude", Env(("COAI_CALLER_SESSION", "  ")), HomeIn(dir))).Reason
            .Should().Be(CallerTranscriptLocation.NoSessionId, "a blank stated value states nothing");
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("gemini")]
    [InlineData("antigravity")]
    public void A_non_claude_caller_has_no_reader(string vendor)
    {
        // The vendor decides FIRST, with a valid Claude variable present and its transcript on disk. 6 of 405 Windows rounds
        // have a codex caller (E1 Q5), so this is a real, frequent state.
        using var dir = TempDir.For("testsToDelete-coai-locator-vendor-");
        var home = HomeIn(dir);
        Plant(home, "any-folder", A);

        var refused = Refused(CallerTranscriptLocator.Locate(vendor, Env(("CLAUDE_CODE_SESSION_ID", A)), home));

        refused.Reason.Should().Be("no reader for " + vendor).And.Be(CallerTranscriptLocation.NoReaderFor(vendor));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("unknown")]
    [InlineData("")]
    public void Claude_unknown_and_empty_vendors_are_read(string vendor)
    {
        using var dir = TempDir.For("testsToDelete-coai-locator-vendorok-");
        var home = HomeIn(dir);
        Plant(home, "any-folder", A);

        Found(CallerTranscriptLocator.Locate(vendor, Env(("CLAUDE_CODE_SESSION_ID", A)), home)).Session.Id.Should().Be(A);
    }

    [Theory]
    [InlineData("Some Editor/../x")]
    [InlineData("a-name-that-is-far-too-long-for-a-vendor")]
    [InlineData("Codex")]
    [InlineData("-lead")]
    public void A_vendor_word_off_the_closed_shape_is_this_vendor(string vendor)
    {
        // The vendor text becomes part of a reason, and a reason must never carry anything arbitrary.
        using var dir = TempDir.For("testsToDelete-coai-locator-vendorword-");

        Refused(CallerTranscriptLocator.Locate(vendor, Env(("CLAUDE_CODE_SESSION_ID", A)), HomeIn(dir))).Reason
            .Should().Be("no reader for this vendor");
    }

    [Theory]
    [InlineData("..\\..\\x")]
    [InlineData("../sentinel")]
    [InlineData("a/b")]
    [InlineData("")]
    [InlineData("   ")]
    public void An_id_that_is_not_a_uuid_never_reaches_a_path(string id)
    {
        using var dir = TempDir.For("testsToDelete-coai-locator-notuuid-");
        var home = HomeIn(dir);
        // Where a naive `<projects>/*/<id>.jsonl` built from `../sentinel` would land.
        File.WriteAllText(Path.Combine(home.ProjectsDir, "sentinel.jsonl"), "{}\n");
        Directory.CreateDirectory(Path.Combine(home.ProjectsDir, "any-folder"));
        Register(home, Pid, "{\"sessionId\":\"" + id.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"}");

        var fromVariable = Refused(CallerTranscriptLocator.Locate("claude", Env(("CLAUDE_CODE_SESSION_ID", id)), home));
        var fromRegistry = Refused(CallerTranscriptLocator.Locate("claude", Env(("CLAUDE_PID", Pid)), home));
        var stored = Refused(CallerTranscriptLocator.LocateStored(id, home));

        fromVariable.Reason.Should().Be(CallerTranscriptLocation.NoSessionId);
        fromRegistry.Reason.Should().Be(CallerTranscriptLocation.NoSessionId);
        stored.Reason.Should().Be(CallerTranscriptLocation.NoSessionId);
    }

    [Fact]
    public void The_transcript_is_found_in_any_project_folder_without_a_slug()
    {
        using var dir = TempDir.For("testsToDelete-coai-locator-slug-");
        var home = HomeIn(dir);
        Plant(home, "zz-unrelated-to-any-cwd", A);
        Plant(home, "another-folder", B);

        var found = Found(CallerTranscriptLocator.Locate("claude", Env(("CLAUDE_CODE_SESSION_ID", A)), home));

        found.MainFile.Should().EndWith(Path.Combine("zz-unrelated-to-any-cwd", A + ".jsonl"));
    }

    [Fact]
    public void An_uppercase_id_finds_the_lowercase_file()
    {
        using var dir = TempDir.For("testsToDelete-coai-locator-case-");
        var home = HomeIn(dir);
        Plant(home, "any-folder", A);

        Found(CallerTranscriptLocator.Locate("claude", Env(("CLAUDE_CODE_SESSION_ID", A.ToUpperInvariant())), home)).Session.Id.Should().Be(A);
    }

    [Fact]
    public void A_transcript_in_a_subfolder_of_a_project_folder_is_not_found()
    {
        // No recursion: only <projects>/<dir>/<id>.jsonl counts.
        using var dir = TempDir.For("testsToDelete-coai-locator-norecursion-");
        var home = HomeIn(dir);
        Plant(home, Path.Combine("any-folder", "nested"), A);

        Refused(CallerTranscriptLocator.Locate("claude", Env(("CLAUDE_CODE_SESSION_ID", A)), home)).Reason
            .Should().Be(CallerTranscriptLocation.TranscriptNotFound);
    }

    [Fact]
    public void Two_project_folders_holding_the_id_is_ambiguous_not_a_guess()
    {
        using var dir = TempDir.For("testsToDelete-coai-locator-ambiguous-");
        var home = HomeIn(dir);
        Plant(home, "folder-one", A);
        Plant(home, "folder-two", A);

        Refused(CallerTranscriptLocator.Locate("claude", Env(("CLAUDE_CODE_SESSION_ID", A)), home)).Reason
            .Should().Be(CallerTranscriptLocation.TranscriptAmbiguous);
    }

    [Fact]
    public void Persistence_off_is_transcript_not_found()
    {
        using var dir = TempDir.For("testsToDelete-coai-locator-persistoff-");
        var home = HomeIn(dir);
        Plant(home, "any-folder", B);

        var refused = Refused(CallerTranscriptLocator.Locate("claude", Env(("CLAUDE_CODE_SESSION_ID", A)), home));

        refused.Reason.Should().Be(CallerTranscriptLocation.TranscriptNotFound);
        refused.Session.Should().Be(new CallerSessionId(A, CallerSessionId.Environment), "the id is known; only the file is missing");
    }

    [Fact]
    public void A_missing_projects_folder_is_transcript_not_found_not_a_throw()
    {
        using var dir = TempDir.For("testsToDelete-coai-locator-noprojects-");
        var home = new ClaudeHome(dir.At("nowhere"), dir.At("also-nowhere"));

        Refused(CallerTranscriptLocator.Locate("claude", Env(("CLAUDE_CODE_SESSION_ID", A)), home)).Reason
            .Should().Be(CallerTranscriptLocation.TranscriptNotFound);
    }

    [Fact]
    public void A_registry_entry_whose_transcript_is_missing_says_registry_and_does_not_fall_back()
    {
        // The /clear shape (parent §16 D9, rule 7): the variable is inherited stale and its transcript exists, the registry names
        // the live session, which has none. The registry wins, so this is NOT found — and Source says whose answer it was.
        using var dir = TempDir.For("testsToDelete-coai-locator-clear-");
        var home = HomeIn(dir);
        Plant(home, "any-folder", B);
        Register(home, Pid, RegistryEntry(A));

        var refused = Refused(CallerTranscriptLocator.Locate("claude", Env(("CLAUDE_PID", Pid), ("CLAUDE_CODE_SESSION_ID", B)), home));

        refused.Reason.Should().Be(CallerTranscriptLocation.TranscriptNotFound);
        refused.Session.Should().Be(new CallerSessionId(A, CallerSessionId.Registry));
    }

    [Fact]
    public void COAI_AGENT_LOG_DIR_moves_the_transcripts_not_the_registry()
    {
        using var dir = TempDir.For("testsToDelete-coai-locator-logdir-");
        var home = HomeIn(dir);
        var moved = home.With(dir.At("elsewhere"));
        Plant(moved, "any-folder", A);
        Register(moved, Pid, RegistryEntry(A));

        var found = Found(CallerTranscriptLocator.Locate("claude", Env(("CLAUDE_PID", Pid)), moved));

        moved.ProjectsDir.Should().Be(dir.At("elsewhere"));
        moved.SessionsDir.Should().Be(home.SessionsDir, "the registry folder does not move");
        found.Session.Source.Should().Be(CallerSessionId.Registry);
        home.With(string.Empty).Should().Be(home, "an empty override changes nothing");
    }

    [Fact]
    public void A_stored_id_is_found_with_source_stored()
    {
        using var dir = TempDir.For("testsToDelete-coai-locator-stored-");
        var home = HomeIn(dir);
        Plant(home, "any-folder", A);

        Found(CallerTranscriptLocator.LocateStored(A, home)).Session.Should().Be(new CallerSessionId(A, CallerSessionId.Stored));
    }

    [Fact]
    public void A_repo_caller_is_no_session_id()
    {
        using var dir = TempDir.For("testsToDelete-coai-locator-repocaller-");

        Refused(CallerTranscriptLocator.LocateStored("repo:dew_flow_connect_other_ais", HomeIn(dir))).Reason
            .Should().Be(CallerTranscriptLocation.NoSessionId);
    }

    [Fact]
    public void A_registry_that_grows_after_the_length_check_is_still_bounded()
    {
        // The stream says it is small and then delivers more than 64 KiB, as a file appended to after the check would. A read
        // that trusts Length parses it whole; the read must be bounded by what it READS.
        var json = "{\"sessionId\":\"" + A + "\",\"pad\":\"" + new string('x', 70 * 1024) + "\"}";
        using var stream = new UnderstatedLengthStream(System.Text.Encoding.UTF8.GetBytes(json));

        CallerTranscriptLocator.ReadRegistry(stream).Should().Be(CallerSessionId.None);
    }

    [Fact]
    public void A_ten_digit_pid_is_accepted()
    {
        using var dir = TempDir.For("testsToDelete-coai-locator-pid10-");
        var home = HomeIn(dir);
        Plant(home, "any-folder", A);
        Register(home, "1234567890", RegistryEntry(A));

        Found(CallerTranscriptLocator.Locate("claude", Env(("CLAUDE_PID", "1234567890")), home)).Session
            .Should().Be(new CallerSessionId(A, CallerSessionId.Registry));
    }

    [Theory]
    [InlineData(64 * 1024, true)]
    [InlineData(64 * 1024 + 1, false)]
    public void A_registry_of_exactly_64_KiB_is_read_and_one_byte_more_is_refused(int size, bool read)
    {
        using var dir = TempDir.For("testsToDelete-coai-locator-edge-");
        var home = HomeIn(dir);
        Plant(home, "any-folder", A);
        Plant(home, "any-folder", B);
        var entry = RegistryEntry(A);
        Register(home, Pid, entry + new string(' ', size - entry.Length));

        var found = Found(CallerTranscriptLocator.Locate("claude", Env(("CLAUDE_PID", Pid), ("CLAUDE_CODE_SESSION_ID", B)), home));

        found.Session.Source.Should().Be(read ? CallerSessionId.Registry : CallerSessionId.Environment);
    }

    [Theory]
    [InlineData("abcdefghijklmnopqrst", "no reader for abcdefghijklmnopqrst")]
    [InlineData("abcdefghijklmnopqrstu", "no reader for this vendor")]
    [InlineData("a", "no reader for a")]
    public void A_vendor_word_of_one_to_twenty_letters_is_kept(string vendor, string reason)
    {
        using var dir = TempDir.For("testsToDelete-coai-locator-vendoredge-");

        Refused(CallerTranscriptLocator.Locate(vendor, Env(("CLAUDE_CODE_SESSION_ID", A)), HomeIn(dir))).Reason.Should().Be(reason);
    }

    /// <summary>Reports a small <see cref="Length"/> and delivers all its bytes — a file that grew after it was measured.</summary>
    private sealed class UnderstatedLengthStream(byte[] content) : MemoryStream(content)
    {
        public override long Length => 10;
    }

    [Fact]
    public void The_registry_wins_even_when_only_the_variables_transcript_exists()
    {
        // E3.S2's fixture check, moved to E3.S4's materialiser (plan C7): E1's real registry bytes for the /clear shape.
        using var dir = TempDir.For("testsToDelete-coai-locator-c12-");
        var fixture = CallerTranscriptFixture.Materialise("registry-and-variable-disagree", dir);
        var registry = Directory.EnumerateFiles(fixture.Home.SessionsDir, "*.json").Single();
        var pid = Path.GetFileNameWithoutExtension(registry);

        var refused = Refused(CallerTranscriptLocator.Locate("claude", Env(("CLAUDE_PID", pid), ("CLAUDE_CODE_SESSION_ID", fixture.Rounds.CallerSessionId)), fixture.Home));

        refused.Reason.Should().Be(CallerTranscriptLocation.TranscriptNotFound);
        refused.Session.Source.Should().Be(CallerSessionId.Registry);
        refused.Session.Id.Should().NotBe(fixture.Rounds.CallerSessionId, "the registry names another id, which is the one with no transcript");
        Found(CallerTranscriptLocator.LocateStored(fixture.Rounds.CallerSessionId, fixture.Home)).MainFile.Should().Be(fixture.MainFile, "the variable's id DOES have a transcript");
    }

    [Fact]
    public void Persistence_off_is_transcript_not_found_from_a_real_registry_entry()
    {
        using var dir = TempDir.For("testsToDelete-coai-locator-c10-");
        var fixture = CallerTranscriptFixture.Materialise("persistence-off", dir);
        var pid = Path.GetFileNameWithoutExtension(Directory.EnumerateFiles(fixture.Home.SessionsDir, "*.json").Single());

        var refused = Refused(CallerTranscriptLocator.Locate("claude", Env(("CLAUDE_PID", pid)), fixture.Home));

        Directory.Exists(fixture.Home.ProjectsDir).Should().BeFalse("persistence was off: no projects folder at all");
        refused.Reason.Should().Be(CallerTranscriptLocation.TranscriptNotFound);
        refused.Session.Source.Should().Be(CallerSessionId.Registry);
    }

    [Fact]
    public void No_test_reads_the_developers_profile()
    {
        // Parent §16 D17: the assembly fixture (TestClaudeHome) points the one default at an empty temp home for the whole run.
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var real = Path.Combine(profile, ".claude");

        ClaudeHome.Default.ProjectsDir.Should().NotStartWith(real).And.Contain("testsToDelete-");
        ClaudeHome.Default.SessionsDir.Should().NotStartWith(real);
        AgentLog.DefaultProjectsDir.Should().Be(ClaudeHome.Default.ProjectsDir, "one default, not two");

        // The positive half: a sentinel transcript planted in that home is what a default read finds.
        var id = Guid.NewGuid().ToString("D");
        var planted = Plant(ClaudeHome.Default, "testsToDelete-sentinel-folder", id);
        try
        {
            Found(CallerTranscriptLocator.LocateStored(id, ClaudeHome.Default)).MainFile.Should().Be(planted);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(planted)!, recursive: true);
        }
    }
}
