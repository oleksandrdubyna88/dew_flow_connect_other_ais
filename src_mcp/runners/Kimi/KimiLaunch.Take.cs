using System.ComponentModel;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Vibe;

namespace CoaiMcp.Runners.Kimi;

/// <summary>The launch itself (E4.2 brief section 10): the checks in order, the two holds, the version, the shape twice.</summary>
public static partial class KimiLaunch
{
    private const string InstallFallback = "install Kimi Code: https://www.kimi.com/code/docs/en/kimi-code-cli/guides/getting-started";

    /// <summary>What the checks before the turn concluded: a refusal, or the config bytes the home must hold.</summary>
    private sealed record Prepared(string Refusal, byte[] Expected)
    {
        public static Prepared Refuse(string refusal) => new(refusal, []);
    }

    /// <summary>
    /// The whole check, in order, ending holding the home's turn and the sign-in's gate — or a refusal with nothing held.
    /// </summary>
    /// <remarks>
    /// <para><b>Before the turn</b>, nothing is taken and nothing started: the host (D18), the tools, the model (D10), the built
    /// environment, the person's OAuth slot, their credentials folder, the model block, the rendered config, and the home made
    /// (E4.1's UNC, chained-link and fresh-path refusals come through <see cref="KimiHome.Made"/>). <b>Then</b> the home's
    /// in-process turn — narrow, cheap — and the sign-in's gate — wide — both held until the launch has been read, and given back in
    /// reverse on EVERY way out: a refusal, a throw, a cancellation while the gate or the version waits. <b>Inside both</b>: the
    /// home's shape, the installed version in the same built environment on the owned home, the shape AGAIN (the <c>--version</c>
    /// process is Kimi's own and runs on the home), and the verdict — an unmeasured version is a warning, never a refusal (the owner's
    /// decision of 2026-10-13).</para>
    /// <para>Never reads <c>SpecialFolder.UserProfile</c> or an environment variable of its own: every path arrives in
    /// <paramref name="places"/>, resolved once in the unredirected parent (decision 4).</para>
    /// </remarks>
    public static async Task<LaunchTurn> TakeAsync(HostKind host, ProcessRequest request, KimiPlaces places, IProcessLauncher launcher, CancellationToken ct)
    {
        var prepared = Prepare(host, request, places);
        if (prepared.Refusal.Length > 0)
        {
            return LaunchTurn.Refused(prepared.Refusal);
        }

        var turn = await VibeHomeTurns.TakeAsync(places.OwnedRoot, ct);
        var gate = await GateAsync(places, turn, ct);
        if (gate is not Hold.Taken taken)
        {
            turn.Dispose();

            return LaunchTurn.Refused(LockRefusal((Hold.Unusable)gate, places.Credentials));
        }

        return await InsideAsync(places, prepared.Expected, new Attempt(turn, taken.Stream), launcher, ct);
    }

    /// <summary>The gate, taken with the home's turn already held: a throw or a cancellation gives the turn back before it propagates.</summary>
    private static async Task<Hold> GateAsync(KimiPlaces places, IDisposable turn, CancellationToken ct)
    {
        try
        {
            return await KimiCredentialsLock.TakeAsync(places.Credentials, ct);
        }
        catch
        {
            turn.Dispose();
            throw;
        }
    }

    /// <summary>Inside both holds: any throw gives them back, the stream first and then the turn.</summary>
    private static async Task<LaunchTurn> InsideAsync(KimiPlaces places, byte[] expected, Attempt attempt, IProcessLauncher launcher, CancellationToken ct)
    {
        try
        {
            return await VerdictAsync(places, expected, attempt, launcher, ct);
        }
        catch
        {
            attempt.Dispose();
            throw;
        }
    }

    /// <summary>The shape, the version, the shape again — a refusal gives both holds back — then the go, with the version warning when it was not measured.</summary>
    private static async Task<LaunchTurn> VerdictAsync(KimiPlaces places, byte[] expected, Attempt attempt, IProcessLauncher launcher, CancellationToken ct)
    {
        var shape = KimiHome.Refusal(places, expected);
        if (shape.Length > 0)
        {
            return Given(attempt, shape);
        }

        var asked = await VersionAsync(places, launcher, ct);
        var after = asked.Refusal.Length > 0 ? asked.Refusal : KimiHome.Refusal(places, expected);

        return after.Length > 0 ? Given(attempt, after) : LaunchTurn.Holding(attempt, WarningFor(asked.Version), "kimi " + asked.Version);
    }

    private static LaunchTurn Given(IDisposable attempt, string refusal)
    {
        attempt.Dispose();

        return LaunchTurn.Refused(refusal);
    }

    /// <summary>Everything that can be said before any lock is taken, in order — the first refusal wins; the config bytes the home must hold otherwise.</summary>
    private static Prepared Prepare(HostKind host, ProcessRequest request, KimiPlaces places)
    {
        var first = FirstOf(
            () => HostRefusal(host, places.Executable),
            () => places.ToolsRefusal,
            () => ModelRefusal(ModelOf(request)),
            () => EnvironmentRefusal(request, places));
        if (first.Length > 0)
        {
            return Prepared.Refuse(first);
        }

        var slot = KimiPersonsConfig.ReadFile(places.PersonsConfig);

        return slot is SlotRead.Found found ? Rendered(host, ModelOf(request), places, found.Slot) : Prepared.Refuse(((SlotRead.Refused)slot).Reason);
    }

    /// <summary>The slot is read: the credentials folder, the model block and the render, then the home made.</summary>
    private static Prepared Rendered(HostKind host, string model, KimiPlaces places, KimiOauthSlot slot)
    {
        if (!Directory.Exists(places.Credentials))
        {
            return Prepared.Refuse($"not signed in to Kimi Code — {places.PersonsHome} has no credentials folder; run `kimi login` in a terminal and try again");
        }

        var expected = ExpectedConfig(model, places, slot);
        if (expected.Refusal.Length > 0)
        {
            return expected;
        }

        var made = KimiHome.Made(host, places, expected.Expected);

        return made.Length > 0 ? Prepared.Refuse(made) : expected;
    }

    private static Prepared ExpectedConfig(string model, KimiPlaces places, KimiOauthSlot slot)
    {
        if (KimiModelBlocks.Builtin is not BlocksLoad.Loaded loaded)
        {
            return Prepared.Refuse(((BlocksLoad.Broken)KimiModelBlocks.Builtin).Reason);
        }

        var block = KimiModelBlocks.For(model, ReadPersons(places.PersonsConfig), loaded.Blocks);

        return block is BlockRead.Found found ? Render(slot, found.Block) : Prepared.Refuse(((BlockRead.Refused)block).Reason);
    }

    private static Prepared Render(KimiOauthSlot slot, KimiModelBlock block)
    {
        try
        {
            return new Prepared(string.Empty, KimiConfig.Bytes(KimiConfig.Render(slot, block)));
        }
        catch (ArgumentException e)
        {
            return Prepared.Refuse($"your Kimi sign-in or model block holds a value coai will not write into an owned config.toml unescaped: {e.Message} (D19)");
        }
    }

    private static string ReadPersons(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    /// <summary>The installed Kimi's version, asked in the launch's own built environment on the owned home — or why it could not be.</summary>
    private static async Task<(string Version, string Refusal)> VersionAsync(KimiPlaces places, IProcessLauncher launcher, CancellationToken ct)
    {
        try
        {
            var probe = Built(new ProcessRequest(places.Executable, ["--version"], places.Home) { Timeout = VersionTimeout }, places);
            var asked = await launcher.RunAsync(probe, ct);
            var version = VersionWarnings.VersionIn(asked.StdOut);

            return asked.ExitCode == 0 && version.Length > 0
                ? (version, string.Empty)
                : (string.Empty, $"`{places.Executable} --version` answered no version (exit {asked.ExitCode}): {OneLine(asked.StdOut + " " + asked.StdErr)}");
        }
        catch (Win32Exception e)
        {
            return (string.Empty, $"'{places.Executable}' could not be started: {e.Message} — {VendorDiagnosis.InstallCure("kimi") ?? InstallFallback}");
        }
    }

    /// <summary>Why the gate could not be taken: the key sentence when the folder cannot be named by its handle, else the lock's own.</summary>
    private static string LockRefusal(Hold.Unusable unusable, string target) =>
        KimiCredentialsLock.PathFor(target) is { Length: > 0 } path
            ? $"the Kimi sign-in lock {path} cannot be taken ({unusable.Cause.Message}) — fix the folder's permissions"
            : unusable.Cause.Message;

    /// <summary>The first non-empty sentence of the checks, run in order — later ones are not asked.</summary>
    private static string FirstOf(params Func<string>[] checks) =>
        checks.Select(check => check()).FirstOrDefault(said => said.Length > 0) ?? string.Empty;

    private static string OneLine(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>What a started launch holds: the sign-in's gate and the home's turn, given back in reverse, once, after the read.</summary>
    private sealed class Attempt(IDisposable turn, FileStream gate) : IDisposable
    {
        private int _given;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _given, 1) == 1)
            {
                return;
            }

            try
            {
                gate.Dispose();
            }
            finally
            {
                turn.Dispose();
            }
        }
    }
}
