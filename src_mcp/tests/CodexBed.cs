using System.Text.Json.Nodes;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A <see cref="CoderBed"/> made for the CODEX coder (epoch 3 E3 s5 t3): the job's folders lie under a folder of their own that holds another job's folder too (what the coder-jobs folder is), the job's <c>CODEX_HOME</c> holds a sign-in COPY (mode 0600, a placeholder in the place of the refresh
/// token, made-up tokens) and the <c>config.toml</c> and schema the vendor writes before every launch — the state in which the production road asks the preflight — and the program is FakeCli, steered through the steering file beside its binary.
/// </summary>
/// <remarks>
/// <para>Never a real codex and never a real sign-in: every token is made up here and is NOT a credential. In the serial <c>fakecli-env</c> collection, like every class that writes the steering file.</para>
/// <para>The steering is written through <see cref="FakeCliSteering"/> and removed with the bed.</para>
/// </remarks>
[Collection("fakecli-env")]
internal sealed class CodexBed : IAsyncDisposable
{
    /// <summary>The model every probe of a bed is asked for.</summary>
    internal const string Model = "gpt-6.1-terra";

    /// <summary>A token that is in the copy of a bed: no row, no log line and no record may hold it.</summary>
    internal const string AccessToken = "made-up-access-token-not-a-credential-7f31";

    internal const string IdToken = "made-up-id-token-not-a-credential-2c88";

    private IDisposable _steering = FakeCliSteering.Unwritten;

    private CodexBed(CoderBed inner, IReadOnlyList<string> arguments, string otherJobFile)
    {
        Coder = inner;
        Arguments = arguments;
        OtherJobFile = otherJobFile;
    }

    /// <summary>A file in the folder of ANOTHER job (the jobs folder holds two): a unit of this job must not read it.</summary>
    public string OtherJobFile { get; }

    public CoderBed Coder { get; }

    public CodexCoderVendor Vendor => CodexCoderVendor.Instance;

    public BoundaryJob Job => Coder.Job;

    /// <summary>The argv of the launch after the vendor prepared it: what the road hands the preflight.</summary>
    public IReadOnlyList<string> Arguments { get; }

    public string CodexHome => CodexCoderVendor.CodexHome(Job);

    public string AuthFile => Vendor.Home(Job).SignInFile;

    public string Worktree => Coder.Worktree;

    /// <summary>The bed, with the steering the fake answers by (the empty list: the fake holds everything).</summary>
    public static async Task<CodexBed> CreateAsync(params (string Name, string Value)[] steering)
    {
        var coder = await CoderBed.CreateAsync();
        // The real sign-in files of the other two vendors, made up in the stand-in for the real home: no row of a codex unit may find them (S42).
        Directory.CreateDirectory(Path.Combine(coder.RealHome, ".codex"));
        await File.WriteAllTextAsync(Path.Combine(coder.RealHome, ".codex", "auth.json"), "made-up-real-codex-sign-in-line\n");
        Directory.CreateDirectory(Path.Combine(coder.RealHome, ".gemini", "antigravity-cli"));
        await File.WriteAllTextAsync(Path.Combine(coder.RealHome, ".gemini", "antigravity-cli", "antigravity-oauth-token"), "made-up-real-agy-sign-in-line\n");
        var otherJobFile = coder.Inner.UseJobsFolderWithAnotherJob();
        var vendor = CodexCoderVendor.Instance;
        Directory.CreateDirectory(CodexCoderVendor.CodexHome(coder.Job));
        WriteCopy(vendor.Home(coder.Job).SignInFile, SignInCopy.Placeholder);
        var request = new ProcessRequest(FakeCliInvocations.Exe, vendor.FreshArgv(Model, coder.Worktree, new CoderLaunchFiles(Path.Combine(coder.Job.JobRoot, "report.txt"))), coder.Worktree);
        var prepared = vendor.PrepareLaunch(coder.Host, coder.Job, request);
        if (prepared.Problem.Length > 0)
        {
            throw new InvalidOperationException("the bed could not prepare the launch: " + prepared.Problem);
        }

        var bed = new CodexBed(coder, prepared.Request.Arguments, otherJobFile);
        bed.Steer(steering);

        return bed;
    }

    /// <summary>Writes the steering file with exactly these names (replacing what is there).</summary>
    public void Steer(params (string Name, string Value)[] steering)
    {
        _steering.Dispose();
        _steering = steering.Length == 0 ? FakeCliSteering.Unwritten : FakeCliSteering.Write(steering.ToDictionary(pair => pair.Name, pair => pair.Value));
    }

    /// <summary>The copy as coai makes it: compact JSON, a placeholder in place of the refresh token, mode 0600.</summary>
    public static void WriteCopy(string file, string refreshToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var copy = new JsonObject
        {
            ["auth_mode"] = "chatgpt",
            ["tokens"] = new JsonObject { ["id_token"] = IdToken, ["access_token"] = AccessToken, ["refresh_token"] = refreshToken, ["account_id"] = "made-up-account" },
            ["last_refresh"] = "2026-11-26T10:00:00Z",
        };
        File.Delete(file);
        File.WriteAllText(file, copy.ToJsonString());
        SetMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    /// <summary>Sets the mode of a file of the bed (a unit exists on Linux only).</summary>
    public static void SetMode(string file, UnixFileMode mode)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, mode);
        }
    }

    /// <summary>The boundary of the bed for the codex vendor, with an optional rewrite of every unit's argument list.</summary>
    public CoderBoundary Boundary(Func<IReadOnlyList<string>, IReadOnlyList<string>>? degrade = null, BoundaryHost? host = null) => Coder.Boundary(degrade, host).For(Vendor);

    /// <summary>Asks the preflight, the way the road asks it: the program of the launch and its own argv.</summary>
    public Task<BoundaryAdmission> AdmitAsync(CoderBoundary? boundary = null, BoundaryJob? job = null) =>
        (boundary ?? Boundary()).AdmitAsync(job ?? Job, FakeCliInvocations.Exe, TestContext.Current.CancellationToken, false, TimeSpan.FromSeconds(60) + UnitControl.Backstop);

    public CoderTurns Turns(CoderBoundary? boundary = null) => Coder.Turns(boundary ?? Boundary());

    public async ValueTask DisposeAsync()
    {
        _steering.Dispose();
        await Coder.DisposeAsync();
    }
}
