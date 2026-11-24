namespace CoaiMcp.Server.Keys;

/// <summary>
/// The start of a server: sync the pool from the two generic sources (the <c>COAI_CLAUDE_KEY_*</c> variables and the keys file) so a rotation is "edit and restart" (KEY_POOL_SPEC.md, the owner's correction of
/// 2026-11-23). A start with no variable, no file path and no key folder does not touch the disk.
/// </summary>
/// <remarks>
/// What is logged is counts, a source name (a path the person chose) and the rule a line broke — never a value. A fault here is a warning, never a start that fails: the keys already in the pool are still the pool.
/// </remarks>
internal static class KeyStartup
{
    public static void Sync(string dataDir, Func<string, string?> settings, Serilog.ILogger log)
    {
        try
        {
            var variables = KeySourceSync.ProcessVariables().ToList();
            var access = new KeyStoreAccess(dataDir, TimeProvider.System, _ => { });
            if (!KeySourceSync.Configured(settings, variables) && !access.Folder.Exists)
            {
                return;
            }

            foreach (var source in KeySourceSync.Run(access, settings, variables).Sources)
            {
                Say(source, log);
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Only the type: a sentence built from a fault must not be able to carry a value.
            log.Warning("claude key sync failed: {Fault}", e.GetType().Name);
        }
    }

    private static void Say(KeySourceResult source, Serilog.ILogger log)
    {
        if (source.Counts is not { } counts)
        {
            log.Warning("claude key source {Source} was not read: {Reason}", source.Source, source.Refusal);

            return;
        }

        foreach (var line in counts.Refused)
        {
            log.Warning("claude key source {Source}: line {Line} was not taken: {Reason}", source.Source, line.Line, line.Reason);
        }

        if (counts.Added + counts.Replaced + counts.Disabled + counts.Enabled > 0)
        {
            log.Information("claude key source {Source}: added {Added}, replaced {Replaced}, disabled {Disabled}, enabled {Enabled}", source.Source, counts.Added, counts.Replaced, counts.Disabled, counts.Enabled);
        }
    }
}
