using System.Text.RegularExpressions;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Runners.Worktrees;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s7 t1 — the vendor seam of the coder (judgement S5, S6; SPEC C2 and C6). d1: one type per vendor gives the probe, the argv, the unit's properties, its working directory, its environment, its home, its sign-in recipe, its program and the id of its
/// prompt, and claude's values are what they were. d2: a handle carries the vendor. d3 and d4: <c>shared/coder-boundary-releases.json</c> is the one table, and it is filled only from the judgement file. d5: the seam is built for every combination of verdicts.
/// </summary>
/// <remarks>
/// No CLI is started and no unit is made: the seam is values, and a vendor of these tests (<see cref="SeamProbeVendor"/>) stands in for codex and agy, which have no type yet. The real repository files are read as the product's two reference documents.
/// </remarks>
public sealed class ACoderVendorSeamTests
{
    private const string Session = "67289235-65f7-40b5-9532-e63515d90f30";
    private const string Job = "job-a";
    private const string W = "/w/coai-coder-job-a";
    private const string M = "/w/main";
    private const string J = "/w/job";
    private const string Guard = "/w/job/coder/guard";

    private static BoundaryHost NoHome() => new(BoundaryPlatform.Linux, "/nonexistent/home", string.Empty, string.Empty, "systemd-run", "systemctl", string.Empty, string.Empty);

    private static BoundaryJob TheJob() => new("job-a", new CoderTree(W, M, "gitdir: /w/main/.git/worktrees/job-a\n"), J, NetworkChoice.None("t"), NetworkChoice.None("t"), 2000);

    private static readonly CoderProgram Program = new("/usr/bin/true", string.Empty, string.Empty);

    private static string Json(string verdict, string releases = "[]") =>
        $$"""
        { "version": 1, "judgement": "j.md",
          "shared": [ { "id": "U1", "class": "pre" } ],
          "vendors": [ { "id": "probe", "verdict": "{{verdict}}", "results": "r.md", "releases": {{releases}}, "properties": [ { "id": "PR-1", "class": "launch" } ], "pending": [] } ] }
        """;

    private const string OneRelease = """[ { "release": "1.0.0", "resultsHeading": "H", "judgementHeading": "J", "sha256": "0000000000000000000000000000000000000000000000000000000000000000", "stream": { "mode": "x" } } ]""";

    private static CoderVendorSet SetWith(string verdict, string releases, ICoderVendor? vendor = null) => CoderVendorSet.Of(CoderBoundaryTable.Parse(Json(verdict, releases)), vendor ?? new SeamProbeVendor());

    // ---------- d1: claude is what it was ----------

    private static string[] Measured(string tree) =>
    [
        "-p", "--output-format", "json", "--restricted", "--tools", "Read,Edit,Write,Glob,Grep", "--permission-mode", "acceptEdits",
        "--permission-prompts", "none", "--strict-mcp-config", "--add-dir", tree,
    ];

    [Fact]
    public void D1_TheClaudeVendor_GivesTheMeasuredArgv_FreshAndResumed_AsLiterals()
    {
        var claude = CoderVendors.Claude;

        claude.FreshArgv(string.Empty, W).Should().Equal(Measured(W));
        claude.FreshArgv("claude-opus-5", W).Should().Equal([.. Measured(W), "--model", "claude-opus-5"]);
        claude.ResumedArgv(string.Empty, W, Session).Should().Equal([.. Measured(W), "--resume", Session]);
        claude.ResumedArgv("claude-opus-5", W, Session).Should().Equal([.. Measured(W), "--resume", Session, "--model", "claude-opus-5"]);
    }

    [Fact]
    public void D1_TheClaudeVendor_UnitValues_AreTheListsE2Recorded()
    {
        var claude = CoderVendors.Claude;
        string[] e2 =
        [
            "ProtectSystem=strict", "ProtectHome=tmpfs", "TemporaryFileSystem=/run/user", "TemporaryFileSystem=/mnt", "TemporaryFileSystem=/dev/shm", "InaccessiblePaths=-/run/WSL",
            $"BindReadOnlyPaths={M}", "BindReadOnlyPaths=/w", $"BindPaths={W}", $"BindReadOnlyPaths={W}/.git", $"TemporaryFileSystem={J}:ro", $"BindPaths={J}/coder", $"BindPaths={J}/coder/tmp:{BoundaryUnit.UnitTemp}", $"BindPaths={J}/coder/vartmp:{BoundaryUnit.UnitVarTemp}",
            "NoNewPrivileges=yes", "PrivateUsers=yes", "RestrictSUIDSGID=yes", "LockPersonality=yes", "ProtectKernelTunables=yes", "ProtectControlGroups=yes", "Nice=10", "LimitCORE=0",
            "PrivateNetwork=no",
            "RuntimeMaxSec=300", "TimeoutStopSec=10", "MemoryMax=2000M", "MemorySwapMax=0",
        ];

        claude.UnitProperties(NoHome(), TheJob(), Program, TimeSpan.FromMinutes(5)).Should().Equal(e2);
        claude.UnitProperties(NoHome(), TheJob(), Program, TimeSpan.FromMinutes(5)).Should().Equal(CoderUnit.Properties(NoHome(), TheJob(), Program, TimeSpan.FromMinutes(5)));
        claude.WorkingDirectory(TheJob()).Should().Be(W, "claude's unit starts in the worktree");
        claude.Environment(NoHome(), TheJob()).Select(pair => pair.Key).Should().Equal("PATH", "HOME", "TMPDIR", "LANG", "TERM", "NO_COLOR", "GIT_TERMINAL_PROMPT");
        claude.Environment(NoHome(), TheJob()).Should().Equal(CoderUnit.Environment(NoHome(), TheJob()));
        var home = claude.Home(TheJob());
        (home.Home, home.SignInFile).Should().Be(($"{J}/coder/home", $"{J}/coder/home/.claude/.credentials.json"));
        home.Folders.Should().Equal(TheJob().CoderFolders);
        claude.SignIn.Should().Be(new CoderSignInRecipe.BindRealFile(".claude/.credentials.json"));
        claude.PromptId.Should().Be("coder");
        claude.Id.Should().Be("claude");
    }

    [Fact]
    public void D1_TheClaudeVendor_ProgramIsTheOneThePrograms_TypeResolved()
    {
        CoderVendors.Claude.Program("/bin/true", string.Empty, TheJob()).Should().Be(CoderProgram.Resolve("/bin/true", string.Empty, TheJob()));
    }

    [Fact]
    public async Task D1_TheClaudeConsultant_BuildsTheArgvFromTheSeam_AndTheProbeIsTheSeams()
    {
        var coder = new LaunchConfinement.Coder(Job, W) { Resume = CoderHandle.Bind(new LaunchConfinement.Coder(Job, W), Session) };
        var launch = new ConsultantLaunch(Path.GetFullPath("main"), "make the change", string.Empty, Path.GetFullPath("answers"), new ReviewerSettings("claude") { Model = "m", ClaudeCli = ClaudeCapability.WithRestricted }) { Confinement = coder };

        new ClaudeConsultant(new ClaudeRuntime()).Build(launch).Request.Arguments.Should().Equal(CoderVendors.Claude.ResumedArgv("m", W, Session));

        var probed = await CoderVendors.Claude.ProbeAsync(new WatchedLauncher(new ProcessLauncher(), _ => new ProcessResult(1, string.Empty, "no\n", false)), "/nonexistent/claude", W, TestContext.Current.CancellationToken);
        probed.Should().BeOfType<CoderCapability.Refused>("the probe is claude's own write capability, asked through the seam");
    }

    // ---------- d1: another vendor's seam changes what the unit is made of ----------

    [Fact]
    public void D1_AVendor_CanHideTheMainCheckoutAndTheCoderRoot_InsteadOfBindingThemReadOnly()
    {
        var hiding = new SeamProbeVendor();

        var properties = new CoderBoundary(new ProcessLauncher(), NoHome()) { Vendor = hiding }.PropertiesFor(TheJob(), "/bin/true", TimeSpan.FromMinutes(5));

        properties.Should().Contain($"InaccessiblePaths={M}").And.Contain("InaccessiblePaths=/w");
        properties.Should().NotContain($"BindReadOnlyPaths={M}", "the main checkout is hidden, not read-only, where the vendor says so");
        properties.Should().NotContain("BindReadOnlyPaths=/w");
        new CoderBoundary(new ProcessLauncher(), NoHome()).PropertiesFor(TheJob(), "/bin/true", TimeSpan.FromMinutes(5)).Should().Contain($"BindReadOnlyPaths={M}", "the default vendor is claude's, unchanged");
    }

    [Fact]
    public void D1_TheUnitIsStartedWhereTheVendorSays_WithTheVendorsEnvironment()
    {
        var vendor = new SeamProbeVendor();
        var crossing = CoderUnit.Cross(new ProcessRequest("/bin/true", [], W), _ => null);

        var arguments = CoderUnit.Wrapped("coai-coder-job-a-c1", vendor.WorkingDirectory(TheJob()), vendor.UnitProperties(NoHome(), TheJob(), Program, TimeSpan.FromMinutes(5)), vendor.Environment(NoHome(), TheJob()), Program, ["--go"], crossing);

        arguments.SkipWhile(argument => argument != "--working-directory").Skip(1).First().Should().Be(Guard, "the vendor's working directory is the unit's, never forced to the worktree");
        arguments.Should().Contain("PROBE_ONLY=1").And.NotContain("TERM=dumb", "the environment allow-list is the vendor's");
        arguments.Should().Contain("-p").And.Contain($"InaccessiblePaths={M}");
    }

    [Fact]
    public async Task D1_ALaunchIsRefusedWhereTheVendorDoesNotStart_AndOneThatIsRightGoesOnToTheBoundary()
    {
        var launcher = new WatchedLauncher(new ProcessLauncher(), _ => new ProcessResult(0, string.Empty, string.Empty, false));
        using var worktree = TempDir.For("coai-seam-wt-");
        var mine = new CoderTurns(new CoderBoundary(launcher, NoHome() with { Platform = BoundaryPlatform.Windows }) { Vendor = new SeamProbeVendor { Guard = worktree.Path } }, new FilesystemInvariant(launcher));
        var elsewhere = new CoderTurns(new CoderBoundary(launcher, NoHome() with { Platform = BoundaryPlatform.Windows }) { Vendor = new SeamProbeVendor() }, new FilesystemInvariant(launcher));

        var right = await mine.RunAsync(TheJob(), new ProcessRequest("/bin/true", [], worktree.Path), "c1", TestContext.Current.CancellationToken);
        var wrong = await elsewhere.RunAsync(TheJob(), new ProcessRequest("/bin/true", [], worktree.Path), "c1", TestContext.Current.CancellationToken);

        right.Should().BeOfType<CoderTurnOutcome.Refused>().Which.Sentence.Should().Contain("Windows", "the launch passed the working-directory check and reached the platform refusal");
        wrong.Should().BeOfType<CoderTurnOutcome.Refused>().Which.Sentence.Should().Contain(Guard).And.Contain("starts");
        launcher.Requests.Should().BeEmpty("nothing was started in either case");
    }

    [Fact]
    public void D1_AVendorWithNoPromptOfItsOwn_CannotBeLaunchedAsACoder_EvenWhenItsTableRowShips()
    {
        var set = SetWith("ship", OneRelease, new SeamProbeVendor { Prompt = string.Empty });

        var resolved = set.Resolve("probe");

        resolved.Should().BeOfType<CoderVendorResolution.Refused>().Which.Sentence.Should().Contain("no coder prompt of its own").And.Contain("claude");
        set.Launchable.Should().Equal("claude");
        SetWith("ship", OneRelease).Resolve("probe").Should().BeOfType<CoderVendorResolution.Launchable>();
    }

    // ---------- d2: the handle carries the vendor ----------

    [Fact]
    public void D2_AHandleCarriesTheVendorBesideTheJobAndTheWorktree()
    {
        var handle = CoderHandle.Bind(new LaunchConfinement.Coder(Job, W), Session);
        var probes = CoderHandle.Bind(new LaunchConfinement.Coder(Job, W) { Vendor = "probe" }, Session);

        handle.Vendor.Should().Be("claude");
        probes.Vendor.Should().Be("probe");
        handle.Should().NotBe(probes);
    }

    [Fact]
    public async Task D2_AHandleOfAnotherVendor_IsRefusedBeforeAProcessStarts()
    {
        var set = SetWith("ship", OneRelease);
        var theirs = CoderHandle.Bind(new LaunchConfinement.Coder(Job, W) { Vendor = "probe" }, Session);
        var mine = new LaunchConfinement.Coder(Job, W);

        CoderLaunches.WhyNot(mine with { Resume = theirs }, set).Should().Contain("belongs to the probe CLI").And.Contain("claude");
        CoderLaunches.WhyNot(new LaunchConfinement.Coder(Job, W) { Vendor = "probe", Resume = CoderHandle.Bind(mine, Session) }, set).Should().Contain("belongs to the claude CLI");
        CoderLaunches.WhyNot(new LaunchConfinement.Coder(Job, W) { Vendor = "probe", Resume = theirs }, set).Should().BeEmpty("the vendor's own handle resumes");

        var launcher = new WatchedLauncher(new ProcessLauncher(), _ => new ProcessResult(0, string.Empty, string.Empty, false));
        var launch = new ConsultantLaunch(Path.GetFullPath("main"), "p", string.Empty, Path.GetFullPath("answers"), new ReviewerSettings("claude") { ClaudeCli = ClaudeCapability.WithRestricted }) { Confinement = mine with { Resume = theirs } };
        var prepared = await new ClaudeConsultant(new ClaudeRuntime()).PrepareAsync(launch, launcher, TestContext.Current.CancellationToken);

        prepared.Should().BeOfType<ConsultantPreparation.Refused>().Which.Failure.Should().BeOfType<ConsultFailure.LaunchRefused>().Which.Said.Should().Contain("another vendor");
        launcher.Requests.Should().BeEmpty("the handle is checked before the CLI is asked anything");
    }

    [Fact]
    public void D2_AHandleOfAnotherJobOrWorktree_IsStillRefused_WhateverTheVendor()
    {
        var set = SetWith("ship", OneRelease);
        var other = CoderHandle.Bind(new LaunchConfinement.Coder("job-b", "/w/coai-coder-job-b") { Vendor = "probe" }, Session);

        CoderLaunches.WhyNot(new LaunchConfinement.Coder(Job, W) { Vendor = "probe", Resume = other }, set).Should().Contain("another job");
        CoderLaunches.WhyNot(new LaunchConfinement.Coder(Job, W) { Vendor = "probe", Resume = other with { JobId = Job } }, set).Should().Contain("another worktree");
    }

    // ---------- d3: the table ----------

    [Fact]
    public void D3_TheShippedTable_HoldsTheVerdictShipAndNoReleaseForCodexAndAgy_AndAReleaseIsARecordNotACondition()
    {
        var table = CoderBoundaryTable.Shipped;

        table.Vendors.Select(row => row.Id).Should().Equal("codex", "agy");
        table.Vendors.Should().OnlyContain(row => row.Verdict == "ship" && row.Releases.Count == 0 && row.Ships && row.LastMeasured == null, "a vendor ships by its verdict alone (owner decision of 2026-11-26): the list of measured releases is history");
        table.ShippedVendors.Should().Equal("claude", "codex", "agy");
        CoderVendors.Shipped.Launchable.Should().Equal(["claude"], "the table ships them; what launches them is a TYPE of its own (s5, s6), and none exists yet");
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("agy")]
    public void D3_AVendorThatShipsButHasNoTypeYet_IsRefusedForThatReason_NotForALackOfARelease(string vendor)
    {
        CoderBoundaryTable.Shipped.WhyNot(vendor).Should().BeEmpty("the table says nothing against it");
        var said = CoderVendors.Shipped.Resolve(vendor).Should().BeOfType<CoderVendorResolution.Refused>().Subject.Sentence;

        said.Should().Contain($"no launch built for '{vendor}'").And.Contain("shipped are claude").And.NotContain("release");
        CoderBoundaryTable.Shipped.WhyNot("claude").Should().BeEmpty();
        CoderBoundaryTable.Shipped.WhyNot("gemini-cli").Should().Contain("has no row");
    }

    [Fact]
    public void D3_AVendorWhoseVerdictIsRefuse_IsRefusedWhateverItsReleases_AndAShippedOneWhateverItsReleaseIs()
    {
        var refused = CoderBoundaryTable.Parse(Json("refuse", OneRelease));
        refused.WhyNot("probe").Should().Contain("the verdict for 'probe' is refuse");
        CoderBoundaryTable.Parse(Json("ship", "[]")).WhyNot("probe").Should().BeEmpty("no release is measured, and none is needed");
        CoderBoundaryTable.Parse(Json("ship", OneRelease)).Row("probe")!.LastMeasured!.Release.Should().Be("1.0.0", "the last measured release is what a job's record names beside the installed one");
    }

    [Fact]
    public void D3_ARefusedVendor_IsRefusedWhateverItsReleases()
    {
        var table = CoderBoundaryTable.Parse(Json("refuse", OneRelease));

        table.Row("probe")!.Ships.Should().BeFalse();
        table.WhyNot("probe").Should().Contain("verdict for 'probe' is refuse").And.Contain("claude");
    }

    [Fact]
    public void D3_AShippedVendorWithAMeasuredRelease_Ships_AndCarriesTheReleaseValues()
    {
        var table = CoderBoundaryTable.Parse(Json("ship", OneRelease));

        table.Row("probe")!.Ships.Should().BeTrue();
        table.ShippedVendors.Should().Equal("claude", "probe");
        table.Row("probe")!.Releases.Single().Should().BeEquivalentTo(new CoderRelease("1.0.0", "H", "J", new string('0', 64), new Dictionary<string, string> { ["mode"] = "x" }));
        table.PropertiesOf("probe").Select(property => property.Id).Should().Equal("U1", "PR-1");
    }

    [Theory]
    [InlineData("""{ "version": 2, "judgement": "j", "shared": [], "vendors": [] }""", "version")]
    [InlineData("""{ "version": 1, "judgement": "j", "shared": [ { "id": "U1", "class": "sometimes" } ], "vendors": [] }""", "class")]
    [InlineData("""{ "version": 1, "judgement": "j", "shared": [], "vendors": [ { "id": "claude", "verdict": "ship", "results": "r" } ] }""", "claude is never in the table")]
    [InlineData("""{ "version": 1, "judgement": "j", "shared": [], "vendors": [ { "id": "x", "verdict": "maybe", "results": "r" } ] }""", "verdict")]
    [InlineData("""{ "version": 1, "judgement": "j", "shared": [], "vendors": [ { "id": "x", "verdict": "ship", "results": "r", "releases": [ { "release": "1", "resultsHeading": "h", "judgementHeading": "j", "sha256": "abc" } ] } ] }""", "sha256")]
    [InlineData("""{ "version": 1, "judgement": "j", "shared": [ { "id": "U1", "class": "pre" }, { "id": "U1", "class": "pre" } ], "vendors": [] }""", "twice")]
    public void D3_ATableThatIsNotWellFormed_FailsClosed(string json, string saying)
    {
        var parse = () => CoderBoundaryTable.Parse(json);

        parse.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain(saying);
    }

    /// <summary>What reads the table, as source: a read of its file, a load of it, a row type or the list of shipped vendors.</summary>
    private static readonly Regex ReadsTheTable = new(@"(ReadAllText|ReadAllLines|OpenRead|GetManifestResourceStream)\([^\n]*coder-boundary-releases|CoderBoundaryTable\.(Shipped|Parse)|CoderVendorRow|\.ShippedVendors\b", RegexOptions.CultureInvariant);

    /// <summary>What states a verdict, as source: a verdict word as a string literal.</summary>
    private static readonly Regex StatesAVerdict = new("\"(ship|refuse)\"", RegexOptions.CultureInvariant);

    private static bool Decides(string source) => ReadsTheTable.IsMatch(source) || (StatesAVerdict.IsMatch(source) && Regex.IsMatch(source, @"\b(codex|agy)\b"));

    private static string Repo() => Path.GetDirectoryName(Path.GetDirectoryName(SharedFixtures.PathOf("coder-boundary-releases.json")))!;

    private static IReadOnlyList<string> PlacesThatDecide(string root) =>
    [
        .. Directory.EnumerateFiles(Path.Combine(root, "src_mcp"), "*.cs", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => Decides(NoComments(File.ReadAllText(path))))
            .Select(path => Path.GetFileName(path))
            .Order(StringComparer.Ordinal),
    ];

    private static string NoComments(string source) => string.Join('\n', source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal) && !line.TrimStart().StartsWith("///", StringComparison.Ordinal)));

    [Fact]
    public void D3_NoSecondPlaceDecidesWhichVendorShips()
    {
        PlacesThatDecide(Repo()).Should().Equal(["CoderBoundaryTable.cs", "CoderVendors.cs"], "the table's reader and the set built on it are the only places that read a verdict, a release or the file; everything else asks the set");
    }

    [Fact]
    public void D3_TheScansCompanion_ProvesTheScanMatchesTheOnePlace_AndASecondPlaceWouldBeFound()
    {
        // The positive control: the scan finds the two places that read the table, and each way a second place would decide.
        foreach (var one in new[] { "CoderBoundaryTable.cs", "CoderVendors.cs" })
        {
            Decides(NoComments(File.ReadAllText(Path.Combine(Repo(), "src_mcp", "runners", "Consultation", one)))).Should().BeTrue($"the scan finds {one}");
        }

        foreach (var second in new[] { "var row = CoderBoundaryTable.Shipped;", "var text = File.ReadAllText(\"shared/coder-boundary-releases.json\");", "var x = CoderBoundaryTable.Parse(text);", "CoderVendorRow row;", "var n = table.ShippedVendors;", "if (vendor == \"codex\" && verdict == \"ship\") { }" })
        {
            Decides(second).Should().BeTrue(second);
        }

        Decides("var route = Narrow(models); // ship it").Should().BeFalse("an ordinary line is not a decision");
        Directory.EnumerateFiles(Path.Combine(Repo(), "shared"), "*.json").Where(path => Regex.IsMatch(File.ReadAllText(path), "\"verdict\"\\s*:\\s*\"(ship|refuse)\"")).Select(Path.GetFileName)
            .Should().Equal(["coder-boundary-releases.json"], "no other shared file holds a verdict");
    }

    // ---------- d4: the table is filled only from the judgement file ----------

    private static string Section(string text, string heading)
    {
        var lines = text.Split('\n');
        var at = Array.FindIndex(lines, line => line.StartsWith('#') && line.TrimStart('#').Trim() == heading);
        if (at < 0)
        {
            return string.Empty;
        }

        var level = lines[at].TakeWhile(c => c == '#').Count();
        var end = Array.FindIndex(lines, at + 1, line => line.StartsWith('#') && line.TakeWhile(c => c == '#').Count() <= level);

        return string.Join('\n', lines[at..(end < 0 ? lines.Length : end)]);
    }

    private static IReadOnlyDictionary<string, string> JudgementIds(string judgement)
    {
        var section8 = judgement.Split("\n## 8. ")[1].Split("\n## 9. ")[0];

        return Regex.Matches(section8, @"^\| \*{0,2}([A-Z]+(?:-[A-Z]+)?-?\d+)\*{0,2} \| .* \| ([^|]+) \| [^|]*\|$", RegexOptions.Multiline | RegexOptions.CultureInvariant)
            .ToDictionary(match => match.Groups[1].Value, match => Regex.Match(match.Groups[2].Value.Trim(), "^(pre|launch|turn|release)").Value);
    }

    private static string JudgementVerdict(string judgement, string vendor)
    {
        var row = judgement.Split('\n').FirstOrDefault(line => line.StartsWith($"| {vendor} |", StringComparison.Ordinal)) ?? string.Empty;

        return row.Contains("**ship", StringComparison.Ordinal) ? "ship" : row.Contains("**refuse", StringComparison.Ordinal) ? "refuse" : string.Empty;
    }

    /// <summary>Everything wrong between the table and the two documents it is filled from: results (one text per vendor) and the judgement. Empty when there is nothing.</summary>
    private static IReadOnlyList<string> Problems(CoderBoundaryTable table, Func<string, string> resultsOf, string judgement)
    {
        var problems = new List<string>();
        var ids = JudgementIds(judgement);
        foreach (var property in table.Shared.Concat(table.Vendors.SelectMany(row => row.Properties)))
        {
            if (!ids.TryGetValue(property.Id, out var kind))
            {
                problems.Add($"the id {property.Id} is not listed by the judgement");
            }
            else if (kind != property.Class)
            {
                problems.Add($"the id {property.Id} has the class {property.Class} and the judgement says {kind}");
            }
        }

        foreach (var row in table.Vendors)
        {
            if (JudgementVerdict(judgement, row.Id) != row.Verdict)
            {
                problems.Add($"the verdict of {row.Id} is {row.Verdict} and the judgement says '{JudgementVerdict(judgement, row.Id)}'");
            }

            foreach (var release in row.Releases)
            {
                var results = resultsOf(row.Id);
                problems.AddRange(ReleaseProblems(row.Id, release, results, judgement));
            }

            problems.AddRange(row.Pending.Where(id => !ids.ContainsKey(id)).Select(id => $"the pending id {id} is not listed by the judgement"));
        }

        return problems;
    }

    private static IEnumerable<string> ReleaseProblems(string vendor, CoderRelease release, string results, string judgement)
    {
        var heard = Section(results, release.ResultsHeading);
        if (heard.Length == 0)
        {
            yield return $"{vendor} {release.Release}: the results heading '{release.ResultsHeading}' is not there";
        }
        else if (!heard.Contains(release.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            yield return $"{vendor} {release.Release}: the sha256 is not under the results heading";
        }

        var judged = Section(judgement, release.JudgementHeading);
        if (judged.Length == 0)
        {
            yield return $"{vendor} {release.Release}: the judgement heading '{release.JudgementHeading}' is not there";
        }
        else if (!string.Join('\n', judged.Split('\n').Skip(1)).Contains(release.Release, StringComparison.Ordinal))
        {
            yield return $"{vendor} {release.Release}: the judgement section does not name the release";
        }
    }

    [Fact]
    public void D4_TheRepositorysTable_AgreesWithTheJudgementFileAndTheResultsFiles()
    {
        var judgement = File.ReadAllText(Path.Combine(Repo(), CoderBoundaryTable.Shipped.Judgement));

        Problems(CoderBoundaryTable.Shipped, vendor => File.ReadAllText(Path.Combine(Repo(), CoderBoundaryTable.Shipped.Row(vendor)!.Results)), judgement).Should().BeEmpty();
        JudgementIds(judgement).Count.Should().BeGreaterThan(50, "the reading of the judgement's section 8 finds its ids; an empty reading would pass everything");
        CoderBoundaryTable.Shipped.Vendors.SelectMany(row => row.Properties.Select(property => property.Id).Concat(row.Pending)).Concat(CoderBoundaryTable.Shipped.Shared.Select(property => property.Id)).Order()
            .Should().Equal(JudgementIds(judgement).Keys.Order(), "every id the judgement lists is in the table or pending, and nothing else");
    }

    private const string Judgement = """
        # Judgement
        ## 1. Verdicts
        | Vendor | Release | Verdict |
        |---|---|---|
        | probe | 1.0.0 | **ship** |
        ## 8. Properties
        | Id | Property | Class | Rows |
        |---|---|---|---|
        | U1 | the worktree | pre | B1 |
        | PR-1 | something | launch | B2 |
        ## 9. End
        ## Judgement of 1.0.0
        probe 1.0.0 ships.
        """;

    private static string Results(string sha) => $"# Results\n## Measure of 1.0.0\nthe program {sha}\n## Next\n";

    private static CoderBoundaryTable TableOf(string verdict = "ship", string heading = "Measure of 1.0.0", string judging = "Judgement of 1.0.0", string sha = null!) =>
        CoderBoundaryTable.Parse(Json(verdict, $$"""[ { "release": "1.0.0", "resultsHeading": "{{heading}}", "judgementHeading": "{{judging}}", "sha256": "{{sha ?? new string('a', 64)}}" } ]"""));

    [Fact]
    public void D4_ATableThatMatchesTheDocuments_HasNoProblem()
    {
        Problems(TableOf(), _ => Results(new string('a', 64)), Judgement).Should().BeEmpty();
    }

    [Fact]
    public void D4_ARelease_WhoseResultsHeadingIsNotThere_IsAProblem() =>
        Problems(TableOf(heading: "A heading nobody wrote"), _ => Results(new string('a', 64)), Judgement).Should().ContainSingle().Which.Should().Contain("results heading");

    [Fact]
    public void D4_ARelease_WhoseSha256IsNotUnderItsResultsHeading_IsAProblem() =>
        Problems(TableOf(), _ => Results(new string('b', 64)), Judgement).Should().ContainSingle().Which.Should().Contain("sha256");

    [Fact]
    public void D4_ARelease_WhoseJudgementHeadingOrNameIsNotThere_IsAProblem()
    {
        Problems(TableOf(judging: "No such judgement"), _ => Results(new string('a', 64)), Judgement).Should().ContainSingle().Which.Should().Contain("judgement heading");
        Problems(TableOf(), _ => Results(new string('a', 64)), Judgement.Replace("probe 1.0.0 ships.", "probe ships.")).Should().ContainSingle().Which.Should().Contain("does not name the release");
    }

    [Fact]
    public void D4_AVerdictTheJudgementDoesNotSay_IsAProblem()
    {
        Problems(TableOf("refuse"), _ => Results(new string('a', 64)), Judgement).Should().ContainSingle().Which.Should().Contain("verdict of probe is refuse");
        Problems(TableOf(), _ => Results(new string('a', 64)), Judgement.Replace("**ship**", "**refuse**")).Should().ContainSingle().Which.Should().Contain("judgement says 'refuse'");
    }

    [Fact]
    public void D4_AnIdTheJudgementDoesNotList_AndAClassThatDiffers_AreProblems()
    {
        Problems(TableOf(), _ => Results(new string('a', 64)), Judgement.Replace("| PR-1 | something | launch | B2 |\n", string.Empty)).Should().ContainSingle().Which.Should().Contain("PR-1 is not listed");
        Problems(TableOf(), _ => Results(new string('a', 64)), Judgement.Replace("| something | launch |", "| something | turn |")).Should().ContainSingle().Which.Should().Contain("PR-1 has the class launch and the judgement says turn");
    }

    // ---------- d5: built for every combination of verdicts ----------

    [Theory]
    [InlineData("ship", false, true)]
    [InlineData("ship", true, true)]
    [InlineData("refuse", false, false)]
    [InlineData("refuse", true, false)]
    public void D5_TheSeamIsBuiltForEveryCombination_AndARefusedVendorIsNeverLaunchable(string verdict, bool released, bool launchable)
    {
        var set = SetWith(verdict, released ? OneRelease : "[]");

        (set.Resolve("probe") is CoderVendorResolution.Launchable).Should().Be(launchable);
        set.Launchable.Should().Equal(launchable ? ["claude", "probe"] : ["claude"]);
        set.Resolve("claude").Should().BeOfType<CoderVendorResolution.Launchable>("claude never depends on the table");
        if (!launchable)
        {
            set.Resolve("probe").Should().BeOfType<CoderVendorResolution.Refused>().Which.Sentence.Should().Contain("claude");
        }
    }

    [Fact]
    public void D5_WithBothVendorsRefused_TheTableRefusesThemByData_AndClaudeStillLaunches()
    {
        var table = CoderBoundaryTable.Parse("""
            { "version": 1, "judgement": "j", "shared": [], "vendors": [
              { "id": "codex", "verdict": "refuse", "results": "r" }, { "id": "agy", "verdict": "refuse", "results": "r" } ] }
            """);
        var set = CoderVendorSet.Of(table);

        set.Launchable.Should().Equal("claude");
        set.Resolve("codex").Should().BeOfType<CoderVendorResolution.Refused>();
        set.Resolve("agy").Should().BeOfType<CoderVendorResolution.Refused>();
        CoderLaunches.WhyNot(new LaunchConfinement.Coder(Job, W) { Vendor = "codex" }, set).Should().Contain("refuse");
        CoderLaunches.WhyNot(new LaunchConfinement.Coder(Job, W), set).Should().BeEmpty();
    }

    /// <summary>A vendor that stands in for codex and agy: its unit HIDES the main checkout and the coder root, it starts in a guard folder, and it has its own environment and prompt.</summary>
    private sealed class SeamProbeVendor : ICoderVendor
    {
        public string Guard { get; init; } = ACoderVendorSeamTests.Guard;

        public string Prompt { get; init; } = "coder.probe";

        public string Id => "probe";

        public string PromptId => Prompt;

        public string PromptResource => "CoaiMcp.coder-prompt.md";

        public CoderSignInRecipe SignIn { get; } = new CoderSignInRecipe.CopyForJob(".probe/auth.json", ["access"], new Dictionary<string, string> { ["refresh"] = "PLACEHOLDER" }, string.Empty, "PR-1");

        public IConsultantRuntime Runtime => new ClaudeConsultant(new ClaudeRuntime(), "probe");

        public string FixClause => "you have a probe";

        public bool PointsAtInstructionBlock => true;

        public CoderTurnVerdict CheckTurn(ProcessResult process, string expectedSession) => new CoderTurnVerdict.Fine();

        public Task<CoderCapability> ProbeAsync(IProcessLauncher launcher, string executable, string workingDirectory, CancellationToken ct) => Task.FromResult<CoderCapability>(new CoderCapability.Ready("1.0.0"));

        public IReadOnlyList<string> FreshArgv(string model, string worktree) => ["exec", "--model", model, "--cd", worktree];

        public IReadOnlyList<string> ResumedArgv(string model, string worktree, string sessionId) => ["exec", "resume", sessionId, "--model", model];

        public IReadOnlyList<string> UnitProperties(BoundaryHost host, BoundaryJob job, CoderProgram program, TimeSpan runtimeMax) =>
        [
            $"InaccessiblePaths={job.Tree.MainCheckout}", $"InaccessiblePaths={job.CoderRoot}", $"BindPaths={job.Tree.Root}", "PrivateNetwork=no",
        ];

        public string WorkingDirectory(BoundaryJob job) => Guard;

        public IReadOnlyList<KeyValuePair<string, string>> Environment(BoundaryHost host, BoundaryJob job) => [new("PATH", BoundaryHost.SystemPath), new("PROBE_ONLY", "1")];

        public CoderHomeLayout Home(BoundaryJob job) => new(job.CoderHomeFolder, Path.Combine(job.CoderHomeFolder, ".probe", "auth.json"), job.CoderFolders);

        public CoderProgram Program(string executable, string path, BoundaryJob job) => CoderProgram.Resolve(executable, path, job);
    }
}
