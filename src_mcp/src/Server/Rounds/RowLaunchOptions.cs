using CoaiMcp.Core.Catalog;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>
/// What a review launch of one ROW ran with, labelled for its ledger line — story 3.4 of
/// todo/PLAN_every_round_is_counted.md, the local half.
/// </summary>
/// <remarks>
/// <para><b>Here, and not in the adapters.</b> An adapter's <c>Build</c> knows what it put on the argv
/// (<see cref="ReviewerInvocation.Sent"/>) and nothing about the row it was built for: a codex adapter is handed no effort
/// at all, so it cannot tell "the row asked for one and I never send it" from "the row asked for none". The roster holds
/// the row, so the labels are decided where both are in hand — once, for the round's reviewers and the security lane's.</para>
/// <para><b>The CLI's own configuration is read to LABEL, never to change the launch.</b> Only codex's is read today:
/// it is the one CLI whose effort and tier a row's launch can leave to its configuration — and did, which is the plan's
/// symptom 6. The read happens after the argv is built and touches nothing on it.</para>
/// </remarks>
internal static class RowLaunchOptions
{
    /// <summary>The launch with its <see cref="ReviewerInvocation.RanWith"/> filled, for <paramref name="provider"/>'s row.</summary>
    /// <param name="launch">The settings the launch was built with — its codex tier probe's answer names the CLI's release.</param>
    public static ReviewerInvocation Labelled(
        ReviewerInvocation built, ProviderSettings provider, IReviewerRuntime runtime, ReviewerSettings launch) =>
        built with { RanWith = Of(built.Sent, provider, runtime, launch, Environment.GetEnvironmentVariable) };

    /// <summary>The labels themselves, from what was sent, the row, and the environment the launch inherits — pure but for the file read.</summary>
    internal static LaunchOptions Of(
        SentOptions sent, ProviderSettings provider, IReviewerRuntime runtime, ReviewerSettings launch, Func<string, string?> env)
    {
        var cli = RuntimeResolution.NameOf(provider.Identity());
        var hasTier = FeatureAvailability.Builtin.RowHasFastTier(cli, provider.Model, provider.BaseUrl);

        return LaunchOptions.Of(
            cli,
            // The release the codex tier probe ALREADY read for this launch; every other launch asks no version.
            launch.CodexTier.Printed,
            new OptionChoice(sent.Effort, OptionSources.Row, Unsent(sent.Effort, provider.CliEffort.Length > 0)),
            // A tier the row has and asked for (Off or On) that the argv does not carry: the release could not take it.
            new OptionChoice(sent.Tier, OptionSources.Row, Unsent(sent.Tier, hasTier && provider.Fast != FastMode.Cli)),
            runtime is CodexRuntime ? CliDefaults.OfCodex(env, hasTier) : CliDefaults.None);
    }

    /// <summary><c>ignored</c> when a value was asked for and none was sent; nothing otherwise.</summary>
    private static string Unsent(string sent, bool asked) => sent.Length == 0 && asked ? OptionSources.Ignored : string.Empty;
}
