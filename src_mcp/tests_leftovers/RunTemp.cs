using System.Text.RegularExpressions;
using CoaiMcp.Tests.Leftovers;

namespace CoaiMcp.Tests;

/// <summary>
/// The test process's temp folder: ONE fresh, claimed, owner-form folder per run, made before any test code, that everything a run makes in "temp" lands in — and the
/// gate that REFUSES a run which does not have it (<c>todo/PLAN_test_leftovers_E5.md</c>, S3; the folder first came with E2).
/// </summary>
/// <remarks>
/// <para><b>Two parts, one per project.</b> <see cref="Start"/> is called by each test assembly's own module initializer (<c>RunTempStart.cs</c>: a module initializer runs only when
/// ITS assembly loads, so the library cannot have one for all four), and <see cref="Gate"/> by its own <c>Main</c> (<c>TestEntryPoint.cs</c>: xUnit's generated body, unchanged,
/// behind the gate). <c>Start</c> makes the folder and records any failure, never throws (a throw from a module initializer kills the host with exit 134); <c>Gate</c> runs before
/// MTP is built — before discovery, any fixture, any <c>MemberData</c>, any test — and a run it refuses ends with exit <see cref="RefusedExitCode"/> and ONE stderr line.</para>
/// <para><b>No order is assumed between test collections</b> (they run in parallel): the gate is the guarantee that nothing ran before the folder was proven. A host that bypasses
/// <c>Main</c> (an IDE's own entry) is caught by <see cref="RunTempFixture"/>, the fallback.</para>
/// <para><b>No fallback to the machine's temp.</b> The E2 behaviour — one warning and a run in the machine temp — is WITHDRAWN: a run that cannot make its folder, claim it, or point
/// TEMP, TMP and TMPDIR at it does not start. The information flags (<c>--help</c>, <c>--version</c>, <c>--info</c>), when they are the WHOLE command line, run no test and no discovery and pass; beside any other argument
/// (<c>@@</c>, <c>-automated</c>, a filter) they are gated, and <c>--list-tests</c> runs every <c>MemberData</c> and is gated.</para>
/// <para><b>The end.</b> <see cref="EndOfRun"/> runs from <see cref="RunTempFixture"/>'s disposal AND from the process-exit handler registered here, so a run that never builds a fixture
/// (an information flag, a filter that matches nothing) leaves nothing either. A KILLED run leaves exactly one folder plus its claim, which the owner-aware sweep collects.</para>
/// </remarks>
public static class RunTemp
{
    /// <summary>EX_CONFIG, as <c>src_mcp/src/Api/ProbeApiMode.cs</c> answers a configuration fault; outside Microsoft.Testing.Platform's own 0-13.</summary>
    public const int RefusedExitCode = 78;

    /// <summary>Every refusal line starts with this.</summary>
    public const string RefusalPrefix = "coai tests: no test ran — ";

    /// <summary>
    /// Test-only seams, read from the environment; each makes the run FAIL CLOSED, so a stray value is harmless. Values: <c>fail-claim</c> (the claim cannot be written),
    /// <c>drop-variables</c> (TEMP goes back to what it was), <c>mismatch-temp</c>, <c>mismatch-tmp</c>, <c>mismatch-tmpdir</c> (that ONE variable names another folder),
    /// <c>bypass-gate</c> (<see cref="Gate"/> still runs every check and refuses what <see cref="Refusal()"/> refuses, but does not set <see cref="IsActive"/>, as a host that bypassed <c>Main</c> would leave it — so <see cref="RunTempFixture"/>, the fallback, is what refuses).
    /// </summary>
    public const string SelfTestVariable = "COAI_RUNTEMP_SELFTEST";

    private static RunTempFolder _run = RunTempFolder.None;
    private static Exception? _failure;
    private static int _started;
    private static volatile bool _active;

    /// <summary>The flags that run no test and no discovery: they pass the gate.</summary>
    public static IReadOnlyList<string> InformationFlags { get; } = ["--help", "--version", "--info"];

    /// <summary>The machine's temp root, trailing separator trimmed — for the tests that mean IT, and for the start-of-run sweep.</summary>
    public static string Real { get; private set; } = string.Empty;

    /// <summary>This run's folder, inside <see cref="Real"/>. EMPTY when no folder was made.</summary>
    public static string Folder { get; private set; } = string.Empty;

    /// <summary>True only after <see cref="Gate"/> checked and passed a test run.</summary>
    public static bool IsActive => _active;

    /// <summary>What <see cref="Refusal(RunState, Func{string, string?}, string)"/> judges: the run's state, as plain values so a test can hand it any.</summary>
    internal sealed record RunState(string Real, string Folder, Exception? Failure, bool Started);

    /// <summary>
    /// Makes, claims and registers the run folder and points TEMP, TMP and TMPDIR at it. First call only (idempotent); NEVER throws — every exception is kept for
    /// <see cref="Refusal()"/>, because a throw from a module initializer kills the host.
    /// </summary>
    public static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        try
        {
            Real = Trimmed(Owned.TempRoot());
            var selfTest = Environment.GetEnvironmentVariable(SelfTestVariable);
            var before = RunTempFolder.Variables.ToDictionary(name => name, name => Environment.GetEnvironmentVariable(name));
            _run = RunTempFolder.Create(Real, Seams(selfTest));
            Folder = _run.Folder;
            ApplySelfTestAfterCreate(selfTest, before);
        }
        catch (Exception e)
        {
            // EVERY exception: PlatformNotSupportedException, a CheckWhat ArgumentException and a failed macOS layout included.
            _failure = e;
        }
    }

    /// <summary>
    /// The machine's temp path without its trailing separator — but never trimmed to nothing or to a bare drive letter: a temp folder that IS a root (<c>/</c>, <c>C:</c>) keeps it,
    /// or <c>Path.Combine("", name)</c> would make the run folder a RELATIVE path.
    /// </summary>
    internal static string Trimmed(string path)
    {
        var root = Path.GetPathRoot(path) ?? string.Empty;

        return path.Length <= root.Length ? path : path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>Null when a test run may go on; else the ONE line that says why it may not, naming the executable.</summary>
    public static string? Refusal() =>
        Refusal(new RunState(Real, Folder, _failure, Volatile.Read(ref _started) == 1), Environment.GetEnvironmentVariable, Environment.ProcessPath ?? "this test executable");

    /// <summary>
    /// The checks, in order — the first that fails wins: the folder could not be made; <see cref="Start"/> never ran; the folder is gone; its leaf is not the owner-form run name;
    /// TEMP, TMP or TMPDIR is not exactly the folder. ONE line, each line break of a message becoming a space, always starting with <see cref="RefusalPrefix"/>.
    /// </summary>
    internal static string? Refusal(RunState state, Func<string, string?> getVariable, string exe) =>
        FirstFailure(state, getVariable, exe) is { } line ? RefusalPrefix + OneLine(line) : null;

    private static string? FirstFailure(RunState state, Func<string, string?> getVariable, string exe)
    {
        if (state.Failure is { } failure)
        {
            return $"the run folder could not be made in '{state.Real}' ({failure.GetType().Name}: {failure.Message}). "
                + $"Make '{state.Real}' writable, or point TMPDIR, TEMP and TMP at a folder you can write, then run {exe} again.";
        }

        if (!state.Started)
        {
            return $"this process made no run folder (the test assembly's module initializer did not run). Run {exe}.";
        }

        if (!IsADirectory(state.Folder))
        {
            return $"the run folder '{state.Folder}' this process made is gone. Run {exe} again.";
        }

        if (!RunLeaf().IsMatch(Path.GetFileName(state.Folder)))
        {
            return $"'{state.Folder}' is not an owner-form run folder. Run {exe}.";
        }

        var values = RunTempFolder.Variables.Select(name => (Name: name, Value: getVariable(name))).ToList();
        if (values.Any(v => !string.Equals(v.Value, state.Folder, StringComparison.Ordinal)))
        {
            return $"TEMP, TMP and TMPDIR must all name this run's folder '{state.Folder}' ({string.Join(", ", values.Select(v => $"{v.Name}='{v.Value}'"))}). "
                + $"Start the suite as its own executable: {exe}.";
        }

        return null;
    }

    private static Regex RunLeaf() =>
        new("^" + Regex.Escape(Owned.NameBase + "run-") + "[A-Za-z0-9]{6}\\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static bool IsADirectory(string folder)
    {
        try
        {
            return Native.IdentityOf(folder) is { IsDirectory: true };
        }
        catch (Exception)
        {
            return false; // an identity that cannot be read is no proof that the folder is there: refuse
        }
    }

    private static string OneLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ');

    /// <summary>
    /// The gate, in front of the platform: <c>Main</c> calls it with the arguments and xUnit's generated body. A start that is not an information request (the information flags as the whole command line) and has no proven run
    /// folder is refused (stderr, <see cref="RefusedExitCode"/>) BEFORE the platform is built — so before discovery, any fixture, any <c>MemberData</c> and any test.
    /// </summary>
    public static int Gate(string[] args, Func<string[], int> run)
    {
        // An information request is the WHOLE command line: --help, --version, --info and nothing beside them. Beside a response file (@@), -automated, a filter or
        // --list-tests it is just one more argument of a run that discovers and runs, so it is gated like any run.
        var informationOnly = args.Length > 0 && args.All(arg => InformationFlags.Contains(arg));
        if (informationOnly)
        {
            return run(args); // runs no test and no discovery; the initializer's folder, if made, is removed at exit like any run's
        }

        if (Refusal() is { } line)
        {
            WriteRefusal(Console.OpenStandardError(), line);

            return RefusedExitCode;
        }

        // A host that bypassed Main (the self-test): every check above still ran. Only IsActive stays false, so RunTempFixture — the fallback — is what refuses.
        if (Environment.GetEnvironmentVariable(SelfTestVariable) != "bypass-gate")
        {
            _active = true;
        }

        return run(args);
    }

    /// <summary>
    /// Writes the refusal line as explicit UTF-8 BYTES (no byte order mark) to <paramref name="stderr"/> and flushes. Never through <c>Console.Error</c>: on Windows that writes in the
    /// console's code page, which maps the em dash of <see cref="RefusalPrefix"/> to a '-' — the plan's exact prefix would reach a reader as another text.
    /// </summary>
    internal static void WriteRefusal(Stream stderr, string line)
    {
        stderr.Write(new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(line + Environment.NewLine));
        stderr.Flush();
    }

    /// <summary>What the end-of-run audit reads: every folder the helper still holds, without this run's own folder, which <see cref="EndOfRun"/> ends right after the audit (D9).</summary>
    internal static IReadOnlyList<OwnedDir> RegistryWithoutTheRunFolder() =>
        [.. OwnedDir.Unreclaimed().Where(dir => !ReferenceEquals(dir, _run.Dir))];

    /// <summary>Restores the three variables and removes the folder and its claim. Idempotent: whichever of the fixture and the process exit comes first does the work.</summary>
    internal static void EndOfRun() => _run.Restore();

    private static RunTempSeams Seams(string? selfTest) =>
        new(static (name, value) => Environment.SetEnvironmentVariable(name, value), static name => Environment.GetEnvironmentVariable(name))
        {
            RegisterRestore = static run => AppDomain.CurrentDomain.ProcessExit += (_, _) => run.Restore(),
            WithholdInheritedRun = static () => new InheritedRunWithheld(),
            BeforeClaimWrite = selfTest == "fail-claim" ? static _ => throw new IOException("E5 S3 self-test: the claim cannot be written") : null,
        };

    /// <summary>The real environment with <c>TESTSTODELETE_RUN</c> taken out for the length of the claim, and put back exactly as it was (the module initializer is single-threaded: nothing else reads it meanwhile).</summary>
    private sealed class InheritedRunWithheld : IDisposable
    {
        private const string Variable = "TESTSTODELETE_RUN";

        private readonly string? _inherited = Environment.GetEnvironmentVariable(Variable);

        public InheritedRunWithheld() => Environment.SetEnvironmentVariable(Variable, null);

        public void Dispose() => Environment.SetEnvironmentVariable(Variable, _inherited);
    }

    /// <summary>The self-test values that break the variables AFTER a successful <c>Create</c>: the gate must then refuse. Any other value is ignored.</summary>
    private static void ApplySelfTestAfterCreate(string? selfTest, IReadOnlyDictionary<string, string?> before)
    {
        switch (selfTest)
        {
            case "drop-variables":
                Environment.SetEnvironmentVariable("TEMP", before["TEMP"]);
                break;
            case "mismatch-temp":
                Environment.SetEnvironmentVariable("TEMP", Folder + "-mismatch");
                break;
            case "mismatch-tmp":
                Environment.SetEnvironmentVariable("TMP", Folder + "-mismatch");
                break;
            case "mismatch-tmpdir":
                Environment.SetEnvironmentVariable("TMPDIR", Folder + "-mismatch");
                break;
        }
    }
}
