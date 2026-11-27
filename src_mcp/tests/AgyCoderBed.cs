using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Toolchain;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What the tests of the agy coder share (epoch 3 E3 s6 t3 to t5): the table that makes agy launchable in a test, the vendor with FakeCli as its hook handler, the real usage text of agy as its help, a made-up sign-in file in the real shape (no token of anyone's), and the scripted launches of the fake as agy
/// (<c>agyTools</c> of the model map, see <c>tests_fakecli/AgyCoder.cs</c>). A real agy is never started and the real sign-in file is never read.
/// </summary>
[Collection("fakecli-env")]
internal sealed class AgyCoderBed
{
    internal const string Model = "gemini-3.8-flash-low";

    internal const string Access = "made-up-agy-access-token-one";

    internal const string Refresh = "made-up-agy-refresh-token-one";

    internal const string Conversation = "a1a1a1a1-1111-4111-8111-111111111111";

    internal const string Other = "b2b2b2b2-2222-4222-8222-222222222222";

    /// <summary>The usage text agy printed for an unknown flag (recorded in <c>fixtures/role-stops/agy-bad-flag.json</c>): the fake's answer to <c>--help</c>.</summary>
    internal static string Help()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "role-stops", "agy-bad-flag.json")));
        var stderr = fixture.RootElement.GetProperty("stderr").GetString()!;

        return stderr[stderr.IndexOf("Usage of agy:", StringComparison.Ordinal)..];
    }

    /// <summary>The sign-in file in the shape the measure saw (<c>token</c> with the access token, its type, the refresh token, the expiry and the lifetime; <c>auth_method</c>; <c>id_token_email</c>), every value made up.</summary>
    internal static string SignInFile(DateTime expires, string access = Access, bool realShape = false) =>
        new JsonObject
        {
            ["token"] = new JsonObject { ["access_token"] = access, ["token_type"] = "Bearer", ["refresh_token"] = Refresh, ["expiry"] = realShape ? RealShapeTime(expires) : expires.ToString("O"), ["expires_in"] = 3599 },
            ["auth_method"] = "oauth",
            ["id_token_email"] = "someone@example.invalid",
        }.ToJsonString();

    /// <summary>The time as the REAL file spells it (seen in the first real run): nine fraction digits and an offset with a plus sign — <c>2026-11-26T23:04:58.302489051+01:00</c>.</summary>
    internal static string RealShapeTime(DateTime utc) =>
        new DateTimeOffset(utc.ToUniversalTime()).ToOffset(TimeSpan.FromHours(1)).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'00'zzz", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A table in which agy ships and is launchable, with the one record of history that the product's table holds (a record, never a condition).</summary>
    internal static CoderBoundaryTable Table(bool launchable = true) =>
        CoderBoundaryTable.Parse($$"""
            { "version": 1, "judgement": "j.md", "shared": [],
              "vendors": [ { "id": "agy", "verdict": "ship", "results": "r.md", "launchable": {{(launchable ? "true" : "false")}},
                "measured": [ { "release": "1.5.18", "resultsHeading": "H", "judgementHeading": "J", "programs": [ { "file": "agy", "sha256": "0000000000000000000000000000000000000000000000000000000000000000" } ] } ],
                "properties": [], "pending": [] } ] }
            """);

    /// <summary>The agy vendor of a test: the hook handler is FakeCli, which runs the product's own guard code when it is called with <c>--agy-hook</c>.</summary>
    internal static AgyCoderVendor Vendor() => new(new HookHandler(RoleJobWorld.FakeCli, []));

    internal static CoderVendorSet Set(AgyCoderVendor? vendor = null) => CoderVendorSet.Of(Table(), vendor ?? Vendor());

    /// <summary>One tool call of the scripted agy: the name the hook sees, a path (relative to the worktree, or absolute) and what the tool writes.</summary>
    internal static IReadOnlyDictionary<string, object?> Tool(string tool, string path, string content = "", string target = "", string replacement = "", bool skipHook = false, bool noEvent = false, bool hookTwice = false, bool stayActive = false) =>
        new Dictionary<string, object?> { ["tool"] = tool, ["path"] = path, ["content"] = content, ["target"] = target, ["replacement"] = replacement, ["skipHook"] = skipHook, ["noEvent"] = noEvent, ["hookTwice"] = hookTwice, ["stayActive"] = stayActive };

    /// <summary>One launch of the fake as agy: its tools, the report it answers with and what it says on stderr.</summary>
    internal static IReadOnlyDictionary<string, object?> Launch(IReadOnlyList<IReadOnlyDictionary<string, object?>>? tools = null, string? answer = null, string conversation = "", string stderr = "", int exit = 0, int tokensIn = 100, int tokensOut = 10, int sleepMs = 0, int holdMs = 0, bool fifoRecords = false, bool fifoSignIn = false, string denied = "", IReadOnlyList<string>? reads = null, IReadOnlyList<string>? tries = null)
    {
        var entry = new Dictionary<string, object?>
        {
            ["agyTools"] = tools ?? [],
            ["agyAnswer"] = answer ?? CoderRig.Report(),
            ["agyTokensIn"] = tokensIn,
            ["agyTokensOut"] = tokensOut,
            ["exit"] = exit,
            ["sleepMs"] = sleepMs,
        };
        if (holdMs > 0)
        {
            entry["agyHoldMs"] = holdMs;
        }

        if (fifoRecords)
        {
            entry["agyFifoRecords"] = "1";
        }

        if (fifoSignIn)
        {
            entry["agyFifoSignIn"] = "1";
        }

        if (conversation.Length > 0)
        {
            entry["agyConversation"] = conversation;
        }

        if (stderr.Length > 0)
        {
            entry["stderr"] = stderr;
        }

        if (denied.Length > 0)
        {
            entry["agyDeniedActions"] = JsonNode.Parse(denied);
        }

        if (reads is not null)
        {
            entry["readFiles"] = reads;
        }

        if (tries is not null)
        {
            entry["tryWriteFiles"] = tries;
        }

        return entry;
    }

    /// <summary>The tool call that makes the fixture's checks pass (<see cref="CoderRig.Green"/>) or fail (<see cref="CoderRig.Red"/>) the way the claude fakes do: it writes the state file inside the worktree.</summary>
    internal static IReadOnlyDictionary<string, object?> Writes(string state) => Tool("write_file", CoderRig.StateFile, state + "\n");

    /// <summary>A rig for an agy coder: the vendor set with agy launchable, FakeCli as its CLI, the usage text and a release for its probes, and a sign-in file in the stand-in for the real home.</summary>
    internal static async Task<CoderRig> RigAsync(AgyCoderVendor? vendor = null, DateTime? expires = null, string? toolchain = null)
    {
        var rig = await CoderRig.CreateAsync(toolchain: toolchain);
        rig.Vendors = Set(vendor);
        rig.Rig.Setting("COAI_ROLE_AGY_CLI", RoleJobWorld.FakeCli);
        Environment.SetEnvironmentVariable("FAKECLI_HELP_STDOUT", Help());
        Environment.SetEnvironmentVariable("FAKECLI_VERSION_STDOUT", "1.5.18\n");
        rig.RealHomeFile(AgyCoderUnit.SignInRelative, SignInFile(expires ?? DateTime.UtcNow.AddHours(2)));

        return rig;
    }
}

/// <summary>The bed of a unit test: a job, a stand-in home with a made-up agy sign-in, the agy vendor with FakeCli as the hook handler, and the road of the turns.</summary>
internal sealed class AgyUnit : IAsyncDisposable
{
    private AgyUnit(CoderBed bed, AgyCoderVendor vendor, CoderTurns turns)
    {
        Bed = bed;
        Vendor = vendor;
        Turns = turns;
    }

    public CoderBed Bed { get; }

    public AgyCoderVendor Vendor { get; }

    public CoderTurns Turns { get; }

    public SignInJobs SignIns { get; } = new();

    public string Real => Path.Combine(Bed.RealHome, AgyCoderUnit.SignInRelative);

    public string Guard => AgyCoderUnit.GuardFolder(Bed.Job);

    public static async Task<AgyUnit> CreateAsync(DateTime? realExpiry = null, Func<IReadOnlyList<string>, IReadOnlyList<string>>? degrade = null, AgyCoderVendor? vendor = null, bool realShape = false)
    {
        Assert.SkipUnless(SystemdUser.Works(out var why), why);
        var bed = await CoderBed.CreateAsync();
        var chosen = vendor ?? AgyCoderBed.Vendor();
        var unit = new AgyUnit(bed, chosen, new CoderTurns(new CoderBoundary(bed.Launcher, bed.Host) { Vendor = chosen, Degrade = degrade }, bed.Evidence));
        unit.WriteSignIn(realExpiry ?? DateTime.UtcNow.AddHours(2), realShape: realShape);

        return unit;
    }

    public void WriteSignIn(DateTime expires, string access = AgyCoderBed.Access, bool realShape = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Real)!);
        File.WriteAllText(Real, AgyCoderBed.SignInFile(expires, access, realShape));
    }

    /// <summary>One launch of <paramref name="script"/> as the program, in the agy coder's unit: the sign-in copy, the guard folder and the preflight as the road makes them.</summary>
    public async Task<CoderTurnOutcome> RunAsync(string script, string part = "c1", ProcessRequest? asked = null)
    {
        var request = (asked ?? Bed.Shell(script)) with { WorkingDirectory = Guard };

        return await Turns.RunAsync(Bed.Job, request, part, Vendor, TestContext.Current.CancellationToken, SignIns);
    }

    public ValueTask DisposeAsync() => Bed.DisposeAsync();
}
