using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>What one row came to before any launch: admitted with everything its launch reads, or refused with a word and a sentence.</summary>
public abstract record RowAdmission
{
    /// <param name="Runtime">A consultant runtime — every row resolves to one (<see cref="QuestionResolution.For"/>), which is
    /// what lets its turn go through <see cref="ConsultantTurn"/> with no other road to keep.</param>
    /// <param name="Plan">The planner's fragments for runtime × grant — D13's flag rides on it.</param>
    public sealed record Admitted(
        QuestionRow Row,
        QuestionPromptDefinition Prompt,
        ProviderSettings Provider,
        IConsultantRuntime Runtime,
        Confinement.Planned Plan) : RowAdmission;

    /// <param name="Status"><see cref="RowOutcomes.Blocked"/> for a pair that cannot run (A3), <see cref="RowOutcomes.Disabled"/> for a row that is off.</param>
    public sealed record Refused(QuestionRow Row, QuestionPromptDefinition? Prompt, string Status, string Reason) : RowAdmission;

    private RowAdmission() { }
}

/// <summary>
/// The admission of one row (PLAN_question_consultant.md, §4): its prompt, its vendor as a launchable row,
/// the runtime that answers for it, and the planner's verdict on runtime × capability — in that order, each
/// refusal a sentence naming its cure, and NOTHING launched by this class.
/// </summary>
/// <remarks>
/// Pure over its inputs. The matrix is asked through <see cref="ConfinementPlanner.Plan"/>, so A3's "refused by
/// the server" is the same rule the extension's <c>capabilityAdmission.ts</c> answers — and a pair the table
/// does not admit never reaches a runtime, whatever the file says.
/// </remarks>
public static class QuestionAdmission
{
    public const string Section = "ConnectOtherAIs > Question consultant (COAI_QCONSULT_ROWS)";

    public static RowAdmission Admit(QuestionRow row, QuestionConsultSettings settings, IReadOnlyList<ProviderSettings> reviewers, Core.Api.ApiOverrides overrides)
    {
        if (!row.Enabled)
        {
            return new RowAdmission.Refused(row, settings.Prompts.Find(row.Prompt), RowOutcomes.Disabled, "the row is switched off");
        }

        if (settings.Prompts.Find(row.Prompt) is not { } prompt)
        {
            return new RowAdmission.Refused(row, null, RowOutcomes.Blocked,
                $"the row '{row.Id}' names the prompt '{row.Prompt}', which is not one this installation has — the prompts are: {settings.Prompts.Spelled}; pick one in {Section}");
        }

        return QuestionRowResolver.Resolve(row, reviewers) switch
        {
            ResolvedQuestionRow.Unavailable no => new RowAdmission.Refused(row, prompt, RowOutcomes.Blocked, no.Why),
            ResolvedQuestionRow.Vendor vendor => OnTheVendor(row, prompt, vendor.Provider, settings, overrides),
            _ => throw new InvalidOperationException("the union is closed"),
        };
    }

    /// <summary>The checks that need the row: a runtime that answers, a module that accepts the api row's settings, and the plan.</summary>
    private static RowAdmission OnTheVendor(QuestionRow row, QuestionPromptDefinition prompt, ProviderSettings provider, QuestionConsultSettings settings, Core.Api.ApiOverrides overrides)
    {
        var identity = provider.Identity();
        if (QuestionResolution.For(identity) is not { } runtime)
        {
            return new RowAdmission.Refused(row, prompt, RowOutcomes.Blocked, QuestionResolution.CannotAnswer(identity));
        }

        if (runtime is ApiConsultant && ApiRowView.Of(provider, overrides).Refusal is { Length: > 0 } refusal)
        {
            return new RowAdmission.Refused(row, prompt, RowOutcomes.Blocked, refusal);
        }

        return ConfinementPlanner.Plan(RuntimeResolution.NameOf(identity), Grant(prompt.Capability, settings.Roots)) switch
        {
            Confinement.Planned plan => Planned(row, prompt, provider, runtime, plan),
            Confinement.Refused refused => new RowAdmission.Refused(row, prompt, RowOutcomes.Blocked, refused.Reason),
            _ => throw new InvalidOperationException("the union is closed"),
        };
    }

    /// <summary>
    /// D13 on the SERVER, revised by the operator on 2026-10-03: a pair the planner admits but FLAGS — the runtime can
    /// read this machine whatever it is told — is admitted with its flag. There is nothing to acknowledge: codex has no
    /// setting that limits what it reads, so a tick would confine nothing. The flag travels with every answer.
    /// </summary>
    private static RowAdmission.Admitted Planned(QuestionRow row, QuestionPromptDefinition prompt, ProviderSettings provider, IConsultantRuntime runtime, Confinement.Planned plan) =>
        new(row, prompt, provider, runtime, plan);

    /// <summary>The grant a prompt's capability is: the roots travel with <c>disk</c> alone (<see cref="CapabilityGrant"/>).</summary>
    private static CapabilityGrant Grant(Capability capability, IReadOnlyList<string> roots) => capability switch
    {
        Capability.Disk => new CapabilityGrant(Capability.Disk, roots),
        Capability.Web => CapabilityGrant.Web,
        _ => CapabilityGrant.None,
    };
}

/// <summary>A question row's vendor as a launchable row, or the sentence it is refused with.</summary>
public abstract record ResolvedQuestionRow
{
    public sealed record Vendor(ProviderSettings Provider) : ResolvedQuestionRow;

    public sealed record Unavailable(string Why) : ResolvedQuestionRow;

    private ResolvedQuestionRow() { }
}

/// <summary>
/// What a row's vendor MEANS: a definition is itself; a bare id borrows the reviewer row of that id, or is
/// a runtime's own name — the consultant's three materialisers (<see cref="ConsultantResolver"/>), reused,
/// with the question row's own key name and its own refusal sentences.
/// </summary>
/// <remarks>
/// No allowlist is applied here: which runtimes answer a question is <see cref="QuestionResolution"/>'s
/// list, asked by <see cref="QuestionAdmission"/> right after — one list, in the place that launches.
/// </remarks>
public static class QuestionRowResolver
{
    public static ResolvedQuestionRow Resolve(QuestionRow row, IReadOnlyList<ProviderSettings> reviewers)
    {
        var choice = new ConsultantChoice(row.Vendor, row.Model, row.Runtime, row.BaseUrl, row.ExecutablePath, row.Row);

        // The catalog row the extension wrote beside it (C2): read by the reviewer row's parser, refused by name when it
        // does not read — never a quiet launch without the options the person set.
        return ConsultantResolver.OptionsOf(choice) is { } options
            ? Resolved(row, choice, options, reviewers)
            : new ResolvedQuestionRow.Unavailable(
                $"the row '{row.Id}' carries model settings that could not be read — open its model on the Models tab and save "
                + $"it again, or switch the row off, in {QuestionAdmission.Section}");
    }

    private static ResolvedQuestionRow Resolved(QuestionRow row, ConsultantChoice choice, ProviderSettings options, IReadOnlyList<ProviderSettings> reviewers)
    {
        if (choice.IsDefinition)
        {
            return new ResolvedQuestionRow.Vendor(ConsultantResolver.WithOptions(ConsultantResolver.AsProvider(choice), options) with
            {
                VaultKey = row.Key.Length > 0 ? row.Key : options.VaultKey,
            });
        }

        if (reviewers.FirstOrDefault(r => ConsultantResolver.SameId(r.Provider, row.Vendor)) is { } reviewer)
        {
            // The reviewer row's endpoint, path, dialect, price and per-model settings — a question row
            // naming a reviewer means THAT reviewer; its key name wins only when the row spells one.
            return new ResolvedQuestionRow.Vendor(ConsultantResolver.FromRow(choice, reviewer) with
            {
                VaultKey = row.Key.Length > 0 ? row.Key : reviewer.VaultKey,
                Dialect = reviewer.Dialect,
                Price = reviewer.Price,
                Api = reviewer.Api,
            });
        }

        return QuestionResolution.Answering.FirstOrDefault(runtime => ConsultantResolver.SameId(runtime, row.Vendor)) is { } named
            ? new ResolvedQuestionRow.Vendor(ConsultantResolver.FromRuntime(choice, named) with { VaultKey = row.Key })
            : new ResolvedQuestionRow.Unavailable(
                $"the row '{row.Id}' names the vendor '{row.Vendor}', which is neither a reviewer row nor a runtime this build can "
                + $"launch a question on ({string.Join(", ", QuestionResolution.Answering)}) — give the row a runtime of its own, "
                + $"or name a reviewer, in {QuestionAdmission.Section}");
    }
}
