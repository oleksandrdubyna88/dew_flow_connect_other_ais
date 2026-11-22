using System.Text.Json;

namespace CoaiMcp.Server.Migrations;

/// <summary>
/// <c>--migrate-v2 [--apply]</c>: the v2 data migration of one data folder, as a one-shot mode, and the same migration as the automatic run at the start of
/// a v2 server (E1.S4, <c>v2/epoch-1/e1-gate-simplify/stories/s4-migrate-v2/SPEC.md</c>).
/// </summary>
/// <remarks>
/// <para><b>Stdout is the interface:</b> ONE JSON object (<see cref="MigrateV2Report"/>) whenever the mode runs — a plan, a result, or a refusal with its
/// <c>error</c> — and every sentence for a person goes to stderr. The mode always scans: the marker gates only the automatic run, so a person can
/// re-run it after a v1 <c>coai-mcp</c> that was still alive on the folder wrote a held session or a card after the marker.</para>
/// <para><b>Exit codes.</b> <b>0</b> a plan was printed or the apply completed (unreadable objects are reported, not blocking); <b>65</b> an argument it does not
/// know; <b>74</b> the data folder is missing, not writable, or cannot be resolved; <b>75</b> the lease is held by another migration, or objects were
/// deferred and no marker was written. <b>Never 64</b>: this binary KNOWS the mode, and 64 is how a caller detects an old binary
/// (<c>.agents/PROJECT.md</c>). A NAMED data folder that is confirmed absent is refused by the dispatch before this runs (<c>Program.WriterModes</c>).</para>
/// <para>A dry run reads and creates nothing: no <c>migrations/</c>, no lease, no log file.</para>
/// </remarks>
internal static class MigrateV2Mode
{
    internal const string Usage = "--migrate-v2 takes at most `--apply`, once, and nothing else";

    internal static int Run(string[] args, Func<string, string?> env, TextWriter output, Action<string> note) =>
        Run(args, env, output, note, TimeProvider.System, typeof(MigrateV2Mode).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");

    internal static int Run(string[] args, Func<string, string?> env, TextWriter output, Action<string> note, TimeProvider clock, string version)
    {
        var apply = args.Length == 2 && args[1] == "--apply";
        var dataDir = DataDirOf(env);
        if (args.Length > 2 || (args.Length == 2 && !apply))
        {
            // 65, NOT 64: the binary HAS this mode and is refusing the request.
            return Refused(output, note, dataDir ?? string.Empty, apply, Usage, 65);
        }

        if (dataDir is null)
        {
            return Refused(output, note, string.Empty, apply, "the data folder cannot be resolved from COAI_DATA_DIR and COAI_DATA_SIDE", 74);
        }

        var migration = new MigrationV2(dataDir, note, clock: clock, version: version);
        var report = apply ? migration.Apply() : migration.Plan();
        foreach (var step in report.Steps.Where(s => s.Action is "defer" or "leave"))
        {
            note($"v2 migration: {step.Kind} {step.Id}: {step.Detail}");
        }

        if (report.Error is { } error)
        {
            note(error);
        }

        output.WriteLine(JsonSerializer.Serialize(report, ServerJsonContext.Default.MigrateV2Report));

        return report.Status switch
        {
            "plan" or "done" => 0,
            "failed" => 74, // EX_IOERR
            _ => 75, // EX_TEMPFAIL: busy, or objects deferred
        };
    }

    /// <summary>The data folder as every mode resolves it, or null for a side name the resolver refuses.</summary>
    private static string? DataDirOf(Func<string, string?> env)
    {
        try
        {
            return PanelSettings.DataDirectoryFor(env).Path;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static int Refused(TextWriter output, Action<string> note, string dataDir, bool apply, string why, int code)
    {
        note(why);
        var report = new MigrateV2Report("migrate-v2", apply, dataDir, "failed", new MigrateV2Counts(0, 0, 0, 0, 0, 0, 0), [], []) { Error = why };
        output.WriteLine(JsonSerializer.Serialize(report, ServerJsonContext.Default.MigrateV2Report));

        return code;
    }

    /// <summary>
    /// The migration a v2 server runs itself, once, before it serves: only when the build is a v2 build and the marker is absent or was made by other tables.
    /// NEVER throws into <c>ServeAsync</c> and never writes stdout — a lease held by another process, a read-only folder or deferred objects are a warning,
    /// the server serves, and the next start tries again.
    /// </summary>
    /// <remarks>
    /// The catch-all is HERE and only here, at the boundary between a migration and a server that must start: this method passes no fault seam, so there is
    /// nothing for it to hide from a test (<see cref="MigrationV2.AtBoundary"/> is set only by tests that call <see cref="MigrationV2.Apply"/> themselves).
    /// </remarks>
    internal static void RunAutomatic(string dataDir, Version assembly, Action<string> warn)
    {
        try
        {
            var tables = MigrationV2Tables.Default;
            if (!MigrationV2.ShouldRunAutomatically(assembly, MigrationV2.MarkerHash(dataDir), tables.Hash))
            {
                return;
            }

            var report = new MigrationV2(dataDir, warn, tables, version: assembly.ToString(3)).Apply();
            foreach (var hold in report.AnsweredHolds)
            {
                warn($"v2 migration: session {hold.Session} was held and a person had answered it (card {hold.Card}, decision '{hold.Decision}'); v2 never waits, so it was released as good_enough");
            }

            if (report.Status != "done")
            {
                warn($"v2 migration did not finish ({report.Status}{(report.Error is { } why ? ": " + why : string.Empty)}); the server starts anyway and the next start tries again");
            }
        }
        catch (Exception e)
        {
            warn($"v2 migration could not run ({e.GetType().Name}: {e.Message}); the server starts anyway and the next start tries again");
        }
    }
}
