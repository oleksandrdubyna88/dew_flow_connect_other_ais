using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>
/// What the two consultant one-shot modes (<c>--consultants</c>, <c>--check-consultant</c>) and the background
/// <c>consultants.json</c> write are built from — composed the way <c>PanelService</c> composes a consultation's.
/// </summary>
/// <remarks>
/// <para>One composition for the three, because each needs the consultation's PREFLIGHT — the same refusals, in the
/// same words, as <c>consult</c> — and the check needs the executor, context, prompts and ledger a turn runs on. A
/// <c>PanelService</c> is not built: its constructor sweeps rounds, consultations and orphaned reviewers, which a
/// read or a probe has no business doing.</para>
/// <para>The <see cref="ConsultationService"/> here is asked for its preflight only; nothing asks it to consult.</para>
/// </remarks>
internal sealed class ConsultantParts
{
    /// <param name="noticing">Handed down from <c>Program</c>, which composes every one — the silent one for a one-shot mode.</param>
    public ConsultantParts(PanelSettings settings, IProcessLauncher launcher, Action<string> warn, Noticing noticing)
        : this(settings, launcher, warn, noticing, VaultKeys.None("this one-shot reads no vault"))
    {
    }

    /// <param name="keys">The vault, for a consultant that authenticates with a key — an api or endpoint row (E2.3).</param>
    public ConsultantParts(PanelSettings settings, IProcessLauncher launcher, Action<string> warn, Noticing noticing, VaultKeys keys)
    {
        Keys = keys;
        Settings = settings;
        Launcher = launcher;
        Warn = warn;
        Executor = new ReviewerExecutor(
            launcher,
            Path.Combine(settings.DataDir, "unparseable"),
            Path.Combine(settings.DataDir, "empty"),
            problem => warn($"evidence: {problem}"));
        Context = new ContextAssembler(launcher);
        Prompts = new RolePrompts(settings.DataDir);
        Ledger = new UsageLedger(settings.DataDir);
        Consultations = new ConsultationService(
            settings, launcher, Executor, Context, Prompts, Ledger, Serilog.Core.Logger.None,
            Environment.GetEnvironmentVariable, noticing, keys);
    }

    /// <summary>The vault this one-shot read — empty unless its mode needs a key.</summary>
    public VaultKeys Keys { get; }

    public PanelSettings Settings { get; }

    public IProcessLauncher Launcher { get; }

    public Action<string> Warn { get; }

    public ReviewerExecutor Executor { get; }

    public ContextAssembler Context { get; }

    public RolePrompts Prompts { get; }

    public UsageLedger Ledger { get; }

    public ConsultationService Consultations { get; }

    /// <summary>A consultant check's turn over these parts.</summary>
    public ConsultantCheckTurn Turn() => new(Settings, Launcher, Executor, Context, Prompts, Ledger, Warn, Keys);
}
