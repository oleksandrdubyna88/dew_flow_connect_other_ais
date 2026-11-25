namespace CoaiMcp.Server.Keys;

/// <summary>
/// The start of a server: sync the pool from the two generic sources (the <c>COAI_CLAUDE_KEY_*</c> variables and the keys file) so a rotation is "edit and restart" (KEY_POOL_SPEC.md, the owner's correction of
/// 2026-11-23). A start with no variable, no file path and no key folder does not touch the disk.
/// </summary>
/// <remarks>
/// <para>What is logged is counts, a source name (a path the person chose) and the rule a line broke — never a value. A fault here is a warning, never a start that fails: the keys already in the pool are still the pool.</para>
/// <para><b>The variables leave the process once they are read</b> (the security judgement of s7 part 1, finding 1): after the sync every <c>COAI_CLAUDE_KEY_*</c> and <c>COAI_CLAUDE_ACCOUNT_*</c> is removed from this process's own environment, so no child that inherits it — and
/// the launcher also strips the whole family from every child — can carry a key. The pool has them from here on.</para>
/// </remarks>
internal static class KeyStartup
{
    /// <param name="variablesOf">The environment the sources are read from; the process's own by default (a test passes its own).</param>
    /// <param name="unset">Removes one variable from the process environment after the sync; the real removal by default (a test passes a recorder).</param>
    public static void Sync(string dataDir, Func<string, string?> settings, Serilog.ILogger log, Func<IEnumerable<KeyValuePair<string, string>>>? variablesOf = null, Action<string>? unset = null)
    {
        var variables = (variablesOf ?? KeySourceSync.ProcessVariables)().ToList();
        try
        {
            var access = new KeyStoreAccess(dataDir, TimeProvider.System, _ => { }, sharedSetting: settings(KeyFolder.SharedSetting) == "1");
            if (!KeySourceSync.Configured(settings, variables) && !access.Folder.Exists)
            {
                return;
            }

            Warn(access, log);
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
        finally
        {
            Remove(variables, unset ?? (name => Environment.SetEnvironmentVariable(name, null)));
        }
    }

    /// <summary>Removes the variables that hold a key or an account from the process environment — by name, never by value.</summary>
    private static void Remove(IReadOnlyList<KeyValuePair<string, string>> variables, Action<string> unset)
    {
        foreach (var name in variables.Select(pair => pair.Key).Where(name => name.StartsWith(KeySourceReader.KeyPrefix, StringComparison.Ordinal) || name.StartsWith(KeySourceReader.AccountPrefix, StringComparison.Ordinal)))
        {
            unset(name);
        }
    }

    /// <summary>The sentence a start says when the data folder is shared (every member of its group reads every key) or has the setgid bit without the device setting (then it is NOT shared).</summary>
    private static void Warn(KeyStoreAccess access, Serilog.ILogger log)
    {
        if (access.Folder.SharedNote is { Length: > 0 } note)
        {
            log.Warning("claude keys: {Note}", note);
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
