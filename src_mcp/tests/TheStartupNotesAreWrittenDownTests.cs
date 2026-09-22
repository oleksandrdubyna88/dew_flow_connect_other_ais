using CoaiMcp.Core.Notices;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What a run says about itself at startup reaches the page, each note under a subject it groups on.
/// </summary>
/// <remarks>
/// <para><b>What was broken.</b> Three things were said at startup and said only to Serilog: a
/// legacy settings file this side adopted, every setting whose value this build could not use, and
/// what it found on the disk. The codes existed since story 1.2 and nothing wrote them. The plan
/// records the cost once already — a configuration that had been applied, read and reloaded
/// correctly looked broken for twenty minutes, and the one line that would have ended it was in a
/// file the panel does not read.</para>
///
/// <para><b>The subjects are the story.</b> The extension keys repeats on <c>(code, subject)</c>, so
/// a subject left empty collapses every malformed setting there has ever been into one row. The key
/// is typed at the SOURCE rather than parsed out of the sentence — each of the three places that
/// builds one already knows its variable — and a storage note carries its place as well as its kind,
/// because two loose databases under different roots would otherwise be one row with a count of two
/// and no way to see which (gemini, on the plan round).</para>
/// </remarks>
public sealed class TheStartupNotesAreWrittenDownTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("coai-startup-");

    private readonly List<ServerNotice> _written = [];

    private readonly List<string> _said = [];

    public void Dispose() => _dir.Dispose();

    private Noticing Collecting => new(notice => { _written.Add(notice); return true; }, Watching());

    private Serilog.ILogger Watching() =>
        new Serilog.LoggerConfiguration().WriteTo.Sink(new Sink(_said)).CreateLogger();

    /// <summary>A path <see cref="Path.GetFullPath(string)"/> refuses: it holds a NUL.</summary>
    private static readonly string Refused = "C:/a" + (char)0 + "b/settings.json";

    private static PanelSettings With(params UnrecognisedSetting[] settings) =>
        new() { UnrecognisedSettings = [.. settings] };

    [Fact]
    public void TwoMalformedSettings_AreTwoSubjects()
    {
        StartupNotices.Record(
            With(new UnrecognisedSetting("COAI_RETRY_BACKOFF", "cannot read that as seconds"),
                 new UnrecognisedSetting("COAI_CODE_WORKSPACE", "does not know that workspace")),
            [],
            Collecting, Watching());

        _written.Select(notice => notice.Subject)
            .Should().Equal(["COAI_RETRY_BACKOFF", "COAI_CODE_WORKSPACE"],
                "two settings are two rows; one subject for both would be one row nobody can read");
        _written.Should().OnlyContain(notice => notice.Code == ServerNoticeCodes.UnrecognisedSetting);
    }

    [Fact]
    public void TwoRefusedRowsOfOneSetting_ShareOneSubject()
    {
        // The accepted cost, asserted so that it is written down rather than discovered. Two bad
        // rows of COAI_ROLES are two lines with one subject, which the page shows as one row with a
        // count — the same bargain story 2.2 took for two refusal branches in one method.
        StartupNotices.Record(
            With(new UnrecognisedSetting("COAI_ROLES", "row 1 has no id"),
                 new UnrecognisedSetting("COAI_ROLES", "row 2 has no stage")),
            [],
            Collecting, Watching());

        _written.Should().HaveCount(2);
        _written.Select(notice => notice.Subject).Distinct().Should().ContainSingle();
    }

    [Fact]
    public void TwoLooseDatabases_AreTwoSubjects()
    {
        // gemini, on the plan round: with the KIND alone as the subject, a machine running two sides
        // reports two loose databases as one row with a count of two, and the person cannot see
        // which. The place is part of the key.
        StartupNotices.Record(With(), [
            new StorageNote(StorageNote.LooseDatabase, "C:/one", "a database in C:/one"),
            new StorageNote(StorageNote.LooseDatabase, "C:/two", "a database in C:/two"),
        ], Collecting, Watching());

        _written.Select(notice => notice.Subject).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void EachStorageKind_CarriesTheClassThatMatchesIt()
    {
        // codex: a new-directory emitted as `stand-down`, or a loose database as `outcome`, would
        // pass every other test here while the extension shows the wrong severity. The taxonomy is
        // asserted rather than intended.
        StartupNotices.Record(With(), [
            new StorageNote(StorageNote.LooseDatabase, "C:/root", "a database this side is not using"),
            new StorageNote(StorageNote.NewDirectory, "C:/root/side", "this side starts with no history"),
        ], Collecting, Watching());

        _written.Should().SatisfyRespectively(
            loose => loose.Class.Should().Be("stand-down",
                "the server stood down from a database somebody put there"),
            created => created.Class.Should().Be("outcome",
                "a directory being created is something that happened, not something refused"));
    }

    [Fact]
    public void EveryStorageKindThereIs_HasBeenGivenAClass()
    {
        // The census, not a repetition of the map: it reads the kinds off `StorageNote` itself, so
        // a THIRD one added tomorrow fails here on the day it is added rather than reaching the
        // page as an outcome. A switch with a default would have answered every one of them
        // silently — story 2.3.2 learned this about reviewer endings and the same reflex applies.
        var kinds = typeof(StorageNote)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field is { IsLiteral: true, FieldType: var type } && type == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        kinds.Should().HaveCountGreaterThan(1, "the census is worthless if it found nothing to count");
        kinds.Should().OnlyContain(kind => StartupNotices.ClassByKind.ContainsKey(kind));
    }

    [Fact]
    public void AStorageKindNobodyDecidedAbout_DoesNotReachThePageAsAnOutcome()
    {
        // codex: the cost of the default branch, stated as behaviour. A permission refusal invented
        // here stands in for whatever the third kind turns out to be.
        StartupNotices.Record(
            With(), [new StorageNote("permission-denied", "C:/root", "this side cannot write there")],
            Collecting, Watching());

        _written.Should().BeEmpty("silence is recoverable; a refusal shown as a success is not");
        Losses().Should().ContainSingle(
            "and the loss is said out loud, which is the boundary story 2.3.2 built");
        _said.Should().Contain(line => line.Contains("cannot write there", StringComparison.Ordinal),
            "while the terminal still gets the sentence itself — losing the page is not losing both");
    }

    [Fact]
    public void AnUnrecognisedSetting_IsAStandDown()
    {
        StartupNotices.Record(With(new UnrecognisedSetting("COAI_ROLES", "not JSON")), [], Collecting, Watching());

        _written.Should().ContainSingle().Which.Class.Should().Be("stand-down",
            "this build fell back on something the person did not choose");
    }

    [Fact]
    public void TheAdoptionSentence_IsAnOutcomeUnderTheSettingsPath()
    {
        var settings = Path.Combine(_dir, "settings.json");

        StartupNotices.Adopted("a settings.json in the root was adopted", settings, Collecting, Watching());

        var notice = _written.Should().ContainSingle().Subject;

        notice.Code.Should().Be(ServerNoticeCodes.SettingsAdopted);
        notice.Class.Should().Be("outcome");
        notice.Subject.Should().Be(Path.GetFullPath(settings), "the resource the sentence is about");
    }

    [Fact]
    public void OnePathSpeltTwoWays_IsOneSubject()
    {
        // The plan round: the same settings file reached once with backslashes and once with forward
        // slashes would otherwise be two rows of history rather than one with a count.
        StartupNotices.Adopted(
            "adopted", Path.Combine(_dir, "a", "..", "settings.json"), Collecting, Watching());
        StartupNotices.Adopted("adopted", Path.Combine(_dir, "settings.json"), Collecting, Watching());

        _written.Select(notice => notice.Subject).Distinct().Should().ContainSingle(
            "one file is one row, however the path to it was spelled");
    }

    [Fact]
    public void APathThisProcessCannotCanonicalise_IsStillNamed()
    {
        // A NUL in the path is what `Path.GetFullPath` refuses, and the trade this catch exists for
        // is stated as behaviour: the notice still goes out, under the path exactly as it arrived.
        // Losing an adoption over the SPELLING of its subject would be the wrong way round.
        //
        // Spelled by NUMBER because the first draft wrote the escape and the byte reached disk:
        // `NoSourceFileCarriesAControlByteTests` caught it, which is the whole reason that guard
        // is in this suite — git treats such a file as binary, with no diff and no three-way merge.
        StartupNotices.Adopted("adopted", Refused, Collecting, Watching());

        _written.Should().ContainSingle().Which.Subject.Should().Be(Refused);
    }

    [Fact]
    public void NothingUnrecognisedAndNothingOnTheDisk_WritesNothing()
    {
        StartupNotices.Record(With(), [], Collecting, Watching());

        _written.Should().BeEmpty("empty is the normal state — a value nobody set is not a mismatch");
    }

    [Fact]
    public void AWriterThatThrows_DoesNotStopStartup()
    {
        // The boundary from story 2.3.2, exercised from its SECOND caller — codex asked for both
        // methods, because an adoption that throws is a startup that dies before the host exists.
        var exploding = new Noticing(_ => throw new InvalidOperationException("no"), Watching());

        var recording = () => StartupNotices.Record(
            With(new UnrecognisedSetting("COAI_ROLES", "not JSON")),
            [new StorageNote(StorageNote.NewDirectory, "C:/x", "new")],
            exploding, Watching());
        var adopting = () => StartupNotices.Adopted("adopted", "C:/x/settings.json", exploding, Watching());

        recording.Should().NotThrow();
        adopting.Should().NotThrow();
        Losses().Should().HaveCount(3, "and each loss is said out loud rather than swallowed");
    }

    [Fact]
    public void ASecretInASentenceOrASubject_DoesNotReachTheLine()
    {
        // codex: a data directory such as `C:\token=ghp_…` makes the SUBJECT the place a secret
        // lives, not just the sentence. Every string field goes through the redactor — this asserts
        // it on this road, for both.
        const string secret = "ghp_0123456789abcdefghij0123456789abcdef";

        StartupNotices.Record(
            With(new UnrecognisedSetting("COAI_CODE_WORKSPACE", $"COAI_CODE_WORKSPACE is 'token={secret}'")),
            [new StorageNote(StorageNote.NewDirectory, $"C:/token={secret}", $"C:/token={secret} is new")],
            Collecting, Watching());

        var lines = _written.Select(ServerNoticeLine.Of).ToList();

        lines.Should().HaveCount(2);
        lines.Should().OnlyContain(line => !line.Contains(secret, StringComparison.Ordinal));
    }

    [Fact]
    public void ASubjectTooLongForTheLine_IsCutWhereTheRecordIsBuilt()
    {
        // The same rule the title has had since story 2.2, and for the same reason: a subject is a
        // PATH, a path can be any length, and 256 queued notices each holding a long one is process
        // held because a share stopped answering. The writer cuts at the same limit on the way out,
        // so the grouping key is unchanged by moving the cut earlier. (gemini, on the code round.)
        StartupNotices.Record(
            With(),
            [new StorageNote(StorageNote.NewDirectory, "C:/" + new string('d', 5_000), "new")],
            Collecting, Watching());

        _written.Should().ContainSingle().Which.Subject!.Length
            .Should().BeLessThanOrEqualTo(Redaction.TitleLimit);
    }

    [Fact]
    public void ASentenceTooLongForATitle_KeepsItsRemedyAndSaysItWasCut()
    {
        // codex, on the code round: an unbounded value sits at the FRONT of these sentences and the
        // instruction at the back, so a cut takes the actionable half and leaves no sign it did.
        var sentence = new string('x', Redaction.TitleLimit + 50)
            + " — check COAI_DATA_DIR for a typo before recording into it.";

        StartupNotices.Record(With(new UnrecognisedSetting("COAI_DATA_DIR", sentence)), [], Collecting, Watching());

        var notice = _written.Should().ContainSingle().Subject;

        notice.Title.Should().EndWith("…", "a reader can otherwise not tell the sentence was cut");
        notice.Detail.Should().EndWith("before recording into it.",
            "and the remedy — the half a front-loaded cut always takes — survives in the detail");
    }

    [Fact]
    public void ADetailTooLongForTheLine_IsCutWhereTheRecordIsBuiltToo()
    {
        // CodeRabbit, on the pull request, and it is right: the subject and the title were bounded
        // where the record is built and the DETAIL was handed the whole sentence — which is the
        // field that carries the overflow, so the bound the queue exists for was defeated by the
        // very change that added it. 256 queued notices each holding 20 kB is 5 MB of process held
        // because a share stopped answering.
        StartupNotices.Record(
            With(new UnrecognisedSetting("COAI_ROLES", new string('x', 20_000))), [],
            Collecting, Watching());

        _written.Should().ContainSingle().Which.Detail!.Length
            .Should().BeLessThanOrEqualTo(Redaction.DetailLimit);
    }

    [Fact]
    public void ASettingSavedMidSession_ReachesThePageWithoutARestart()
    {
        // The hole this story's own plan wrote down as owed, and which five reviewers across three
        // vendors refused to leave owed: `PanelServiceHost.Build()` re-layers the settings whenever
        // the file moves, which is what a person editing the panel DOES. Saving a bad value there
        // used to reach the log and nothing else, so the page stayed silent until a restart —
        // and mid-session is the likeliest moment for a bad value to appear at all.
        // The bad value is there BEFORE the host exists, which is what makes the first assertion
        // mean anything: startup has already said this one, and the host's own first build must
        // not say it again. (Written the other way round first, where an empty file made it pass
        // whatever the code did — the plant that was supposed to turn it red stayed green.)
        Saved("""{"COAI_RETRY_BACKOFF":"soon"}""");
        var host = Hosting();

        _written.Should().BeEmpty(
            "the host's FIRST build is startup's own, and Program has already said those — a "
            + "second copy would make every start report each mismatch twice");

        Saved("""{"COAI_RETRY_BACKOFF":"soon","COAI_CODE_WORKSPACE":"somewhere"}""");
        _ = host.Current;

        _written.Select(notice => notice.Subject).Should()
            .Equal(["COAI_RETRY_BACKOFF", "COAI_CODE_WORKSPACE"],
                "each under the key it is about, so ten saves of one typo stay one row with a count");
    }

    /// <summary>
    /// The log lines that report a LOST notice, apart from the diagnostics themselves.
    /// </summary>
    /// <remarks>
    /// Both now reach the same sink, because one call writes the log and the page. Counting every
    /// line would make these assertions go up by one whenever a diagnostic is added, which is the
    /// opposite of what they are for.
    /// </remarks>
    private IEnumerable<string> Losses() =>
        _said.Where(line => line.Contains("could not be written down", StringComparison.Ordinal));

    /// <summary>What the panel does when a person touches a control: rewrite the settings file.</summary>
    private void Saved(string json) =>
        File.WriteAllText(Path.Combine(_dir, SettingsFile.Name), json);

    [Fact]
    public void TheServiceReadAgainWithoutASettingsChange_WritesNothingMore()
    {
        // `Current` is read on EVERY tool call. If the notice rode there rather than on the rebuild,
        // one permanently-bad setting would write a line per call — the volume ceiling story 2.2 set
        // is bounded by settings EDITS, and this is what keeps it that way.
        var host = Hosting();

        Saved("""{"COAI_RETRY_BACKOFF":"soon"}""");
        _ = host.Current;
        _ = host.Current;
        _ = host.Current;

        _written.Should().ContainSingle("a rebuild is a settings change, not a call");
    }

    [Fact]
    public void TheSettingsReload_SaysItToTheLogAsWellAsThePage()
    {
        var host = Hosting();

        Saved("""{"COAI_RETRY_BACKOFF":"soon"}""");
        _ = host.Current;

        _said.Should().Contain(said => said.Contains("COAI_RETRY_BACKOFF", StringComparison.Ordinal),
            "the terminal keeps what it had on this road too — the same decision startup made");
    }

    /// <summary>A real host over the temp directory, writing its notices into this test's list.</summary>
    /// <remarks>
    /// A REAL one, because what is being asserted is that the rebuild path — which only the host
    /// runs, and only when the file's stamp moves — says what startup says. A fake would assert the
    /// method exists, which is not the thing that was missing.
    /// </remarks>
    private PanelServiceHost Hosting() =>
        new(name => name == "COAI_DATA_DIR" ? _dir.Path : null,
            VaultKeys.None("no vault in this test"), default, new ProcessLauncher(),
            Watching(), Collecting);

    [Fact]
    public void EveryStartupNote_ReachesTheLogAndThePageFromOneCall()
    {
        // codex asked for this twice, and the second answer is better than the first. On the PLAN
        // round: an implementation can emit the panel notice and drop the `log.Warning`, and every
        // behavioural test stays green while terminal operators silently lose what they had — so a
        // structural scan held the two calls beside each other in Program.cs. On the CODE round he
        // named what that scan cannot do: prove that each DIAGNOSTIC reaches both sinks. A note
        // added to one of two enumerations is visible in the panel and absent from the terminal.
        // So there is one enumeration now, writing both, and this asserts it on the sentences
        // rather than on the source text.
        StartupNotices.Record(
            With(new UnrecognisedSetting("COAI_ROLES", "COAI_ROLES is not JSON this server reads")),
            [new StorageNote(StorageNote.NewDirectory, "C:/x", "C:/x did not exist and is new")],
            Collecting, Watching());

        _written.Should().HaveCount(2);
        _written.Select(notice => notice.Title).Should().OnlyContain(
            title => _said.Any(line => line.Contains(title!, StringComparison.Ordinal)),
            "an operator reading a terminal and a person reading the panel are different people, "
            + "and one call writing both is what stops a future note reaching only one of them");
    }

    [Fact]
    public void TheStartupNotes_HaveTwoCallersAndNoMore()
    {
        var serving = ProductionSources.CodeOf("src_mcp/src/Program.cs");

        serving.Should().Contain("StartupNotices.Record(settings, storage, noticing, log);",
            "the startup block writes them, and the log argument is what makes that one call");
        serving.Should().Contain(
            "StartupNotices.Adopted(note, SettingsFile.PathFor(dataDir), noticing, log));",
            "including the adoption, which happens inside the layering callback");
        ProductionSources.FilesMentioning("StartupNotices.").Keys.Order().Should()
            .Equal(
                ["src_mcp/src/Program.cs", "src_mcp/src/Server/PanelServiceHost.cs"],
                "two callers and no more: the startup block, and the settings RELOAD — which is "
                + "where a person's own edit lands, and which said nothing until the code round");
    }

    [Fact]
    public void TheKeyANoticeGroupsOn_IsTheVariableItsSentenceNamesAsWell()
    {
        // Not a defect — a drift guard. Each of these variables is spelled where the value is read,
        // where the refusal is decided and inside the sentence, and the grouping key made a fourth.
        // A rename that misses the sentence groups the page under the new variable while telling
        // the person about the old one. (codex, on the code round; the keys are one constant each
        // now, and this is what keeps them so.)
        var settings = PanelSettings.FromEnvironment(name => name switch
        {
            "COAI_RETRY_BACKOFF" => "soon",
            "COAI_ON_EXHAUSTED" => "carry-on",
            "COAI_CODE_WORKSPACE" => "somewhere",
            "COAI_ROLES" => "not json",
            _ => null,
        });

        settings.UnrecognisedSettings.Should().HaveCount(4, "one per bad value, and no more");
        settings.UnrecognisedSettings.Should().OnlyContain(
            one => one.Sentence.Contains(one.Key, StringComparison.Ordinal));
    }

    private sealed class Sink(List<string> said) : Serilog.Core.ILogEventSink
    {
        public void Emit(Serilog.Events.LogEvent logEvent)
        {
            lock (said)
            {
                said.Add(logEvent.RenderMessage());
            }
        }
    }
}
