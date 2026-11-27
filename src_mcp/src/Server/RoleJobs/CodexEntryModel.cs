using CoaiMcp.Core.Roles;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>What the model of a route entry came to when the job reached the entry (epoch 3 E3 s2 t5): the model every turn of the entry launches with, or the stop that says the entry cannot be used.</summary>
/// <param name="Launch">The model the entry launches with: the name as given, or — for a codex family — the newest model the installed CLI lists.</param>
/// <param name="Stop">Set when the entry cannot be used: nothing is launched, and the failover goes on (<see cref="RoleStopWords.ModelUnavailable"/>) or the job ends (a boundary stop, a limit that keeps its word).</param>
internal sealed record EntryModel(ModelRef Launch, RoleStop? Stop = null);

/// <summary>
/// The name of a codex entry, read ONCE when the job reaches the entry (epoch 3 E3 s2 t5; <c>SPEC_CHANGES.md</c> section Q1 and Q2; ledger entry 2.1, the owner, 2026-11-27): a codex name with no version (<c>terra</c>, <c>astra</c>, <c>sol</c>, <c>luna</c>) is a family and becomes the newest
/// <c>gpt-&lt;version&gt;-&lt;family&gt;</c> with visibility <c>list</c> of the live list of the installed CLI (<c>codex debug models</c>); a name of that shape given in full is used as given, if the list holds it. A name that cannot be used ends the entry <c>model-unavailable</c>, which fails over.
/// </summary>
/// <remarks>
/// <para><b>When.</b> When the job reaches the entry, never before: the first entry at the start of the job, a later one when the failover reaches it, so a route whose first model answers makes no codex call. The caller keeps the answer for the whole attempt — the repair turn, every fix turn and every
/// resumed session use the name it recorded and never ask again.</para>
/// <para><b>Which names ask.</b> A family, and a name of the exact shape. A codex name that is in <c>names</c> of the file and has neither (<c>gpt-5.5</c>, <c>gpt-5.4</c>) is used as given and is not looked up: nothing in the decision names it. Every other vendor is used as given.</para>
/// <para><b>What a failed read keeps.</b> The run that reads the list is read by the classifier of a launch first (<see cref="RoleFailover.StopOfLaunch"/>), so a missing CLI, a lost sign-in and a spent quota keep their own words and sentences, a boundary refusal keeps <c>coder-boundary</c>, and only what is left
/// — an exit that is not 0, the deadline, an answer that is not the list, an answer over the limit — is <c>model-unavailable</c>; that one skips the other models of the vendor, which would stop the same way.</para>
/// </remarks>
internal static class CodexEntryModel
{
    /// <summary>Whether the name of <paramref name="model"/> is a codex family or has the exact shape: then the installed CLI is asked when the job reaches the entry.</summary>
    public static bool AsksTheList(ModelRef model) =>
        model.Vendor == "codex" && (RoleModelNames.Current.IsCodexFamily(model.Model) || RoleModelNames.Current.IsCodexShaped(model.Model));

    /// <summary>The entry's model: asks the installed CLI for its live list when the name needs it, else the name as given.</summary>
    public static async Task<EntryModel> ResolveAsync(RoleModelLauncher launcher, ModelRef asked, string repoPath, string folder, CancellationToken ct) =>
        AsksTheList(asked) ? Read(asked, await launcher.ListCodexModelsAsync(asked, repoPath, folder, ct)) : new EntryModel(asked);

    /// <summary>What a finished read of the list makes of <paramref name="asked"/>. Pure, so every row of the table of Q2 is a unit test.</summary>
    internal static EntryModel Read(ModelRef asked, RoleLaunched listed)
    {
        // A launch that did not give an answer is read as any launch is: its own word first. A clean exit has no answer in the sense of a role (no output file), so it is never read that way.
        var ended = listed.OsStop is not null || listed.Refused is not null || listed.Launch?.Terminal is not null;
        if (ended && RoleFailover.StopOfLaunch(asked, listed) is { } stop)
        {
            return new EntryModel(asked, stop is { Word: RoleStopWords.Crash, SkipsVendor: false }
                ? new RoleStop(RoleStopWords.ModelUnavailable, $"the list of models of the installed codex CLI could not be read, so the model '{asked.Model}' cannot be used now ({stop.Reason}); the other models of the vendor are skipped", "model-list", SkipsVendor: true, Transient: false)
                : stop);
        }

        var list = CodexModelList.Read(listed.Launch?.Process?.StdOut ?? string.Empty);
        if (list is null)
        {
            return new EntryModel(asked, new RoleStop(RoleStopWords.ModelUnavailable, $"the list of models of the installed codex CLI could not be read (the answer was not the expected JSON, or it was over {CodexModelList.MostChars} characters), so the model '{asked.Model}' cannot be used now; the other models of the vendor are skipped", "model-list", SkipsVendor: true, Transient: false));
        }

        var table = RoleModelNames.Current;
        if (table.IsCodexFamily(asked.Model))
        {
            return CodexModelList.Newest(list, asked.Model, table.CodexFamilies) is { } newest
                ? new EntryModel(asked with { Model = newest })
                : new EntryModel(asked, new RoleStop(RoleStopWords.ModelUnavailable, $"the installed codex CLI lists no model of the family '{asked.Model}' (a name gpt-<version>-{asked.Model} with visibility list), so this entry cannot be used now; the next entry is tried", "no-model", SkipsVendor: false, Transient: false));
        }

        return CodexModelList.Holds(list, asked.Model)
            ? new EntryModel(asked)
            : new EntryModel(asked, new RoleStop(RoleStopWords.ModelUnavailable, $"the installed codex CLI does not list the model '{asked.Model}'; fix the name in {RoleRouter.ModelsSetting} (or COAI_ROLES_BANDS, or the model argument of start_role), or use its family name; the next entry is tried", "no-model", SkipsVendor: false, Transient: false));
    }
}
