using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Server;

/// <summary>
/// Keeps the live <see cref="PanelService"/>, rebuilding it when the settings file underneath it
/// changes — so a threshold, a vendor or a model edited in the panel applies to the NEXT round
/// rather than to the next time somebody restarts their MCP client.
/// </summary>
/// <remarks>
/// <para>Settings used to be read exactly once, at startup. That is defensible for a daemon and
/// wrong for this: the panel writes the file the instant a person touches a control, the server
/// lives as long as the editor session, and the gap between them is silent — you change the model,
/// nothing happens, and there is no way to tell a setting that did not apply from a setting that
/// did nothing. The operator's own guess, unprompted: "maybe they only apply after a restart".
/// They did.</para>
/// <para>A wrapper rather than a rewrite of <see cref="PanelService"/>: the service is built from
/// its settings in its constructor, so the honest way to change them is to build another one. It
/// costs a few small objects per settings CHANGE — not per call, because an unchanged file is
/// recognised by its own timestamp and length and hands back the same instance.</para>
/// <para>Environment variables still win over the file, exactly as before: a variable set in the
/// client's config is more specific than a file any window may rewrite.</para>
/// </remarks>
public sealed class PanelServiceHost
{
    private readonly Func<string, string?> _env;
    private readonly VaultKeys _keys;
    private readonly DateTime _vaultReadUtc;
    private readonly IProcessLauncher _launcher;
    private readonly Serilog.ILogger _log;

    private readonly Noticing _noticing;
    private readonly Lock _gate = new();

    private PanelService _current;
    private (DateTime Written, long Length) _stamp;

    /// <param name="noticing">
    /// Where this host's services write their notices. It is REQUIRED and has no default, because a
    /// defaulted one is the trap story 2.3.2's plan round named: production takes the quiet path
    /// while every injected unit test passes. It is held rather than passed once, so that
    /// <see cref="Build"/> — which runs again whenever the settings file moves — hands the same
    /// instance to the rebuilt service instead of dropping it. (gemini, on the plan round.)
    /// </param>
    public PanelServiceHost(
        Func<string, string?> env,
        VaultKeys keys,
        DateTime vaultReadUtc,
        IProcessLauncher launcher,
        Serilog.ILogger log,
        Noticing noticing)
    {
        _env = env;
        _keys = keys;
        _vaultReadUtc = vaultReadUtc;
        _launcher = launcher;
        _log = log;
        _noticing = noticing;
        _stamp = Stamp();
        _current = Build();
    }

    /// <summary>The service to serve this call with — rebuilt only when the file actually moved.</summary>
    public PanelService Current
    {
        get
        {
            lock (_gate)
            {
                var stamp = Stamp();
                if (stamp == _stamp)
                {
                    return _current;
                }

                _stamp = stamp;
                _current = Build();
                _log.Information("settings reloaded — the panel's file changed on disk");
                return _current;
            }
        }
    }

    private PanelService Build()
    {
        var configuration = SettingsFile.Layer(
            SettingsFile.DataDirFrom(_env).Path,
            _env,
            // This rebuild runs on a stamp change rather than at startup, so it is the one place an
            // adoption could happen with nobody watching. It has a log; it uses it.
            note => _log.Warning("data directory: {Note}", note));
        return new PanelService(
            PanelSettings.FromEnvironment(configuration), _keys, _vaultReadUtc, _launcher, _log, _noticing);
    }

    /// <summary>
    /// The file's identity for change detection: when it was written and how long it is.
    /// </summary>
    /// <remarks>
    /// Deliberately not a content hash — this runs on every tool call, and two writes in the same
    /// filesystem timestamp tick that also preserve the exact length are not a case worth a read
    /// per call. A missing file stamps as default, so creating one counts as a change.
    /// </remarks>
    private (DateTime, long) Stamp()
    {
        try
        {
            var file = new FileInfo(SettingsFile.PathFor(SettingsFile.DataDirFrom(_env).Path));
            return file.Exists ? (file.LastWriteTimeUtc, file.Length) : default;
        }
        catch (IOException)
        {
            return _stamp; // unreadable for a moment is not changed
        }
        catch (UnauthorizedAccessException)
        {
            return _stamp;
        }
    }
}
