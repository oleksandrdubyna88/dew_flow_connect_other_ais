using System.Text.Json;
using CoaiMcp.Core.Commands;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Every order the gate hands back is, byte for byte, what it was before its text became data (issue #467).
/// </summary>
/// <remarks>
/// <para>The texts moved out of <c>GateCommands.cs</c> into <c>shared/commands/*.md</c>, where a person can
/// override them. A person who overrides nothing must not be able to tell: the orders are read by the AI
/// that called the gate and matched by the bench, and a changed comma is a changed order.</para>
/// <para><b>The oracle is a recording, not a second copy of the code.</b>
/// <c>fixtures/gate-commands-before-467.json</c> was written by the code as it stood BEFORE the move, over
/// every combination of the switches, every plan size, both gate scopes and both kinds of model pair:
/// each distinct order once, and each combination as the indices of the orders it produced.</para>
/// </remarks>
public sealed class TheOrdersAreWhatTheyWereTests
{
    private const string Fixture = "gate-commands-before-467.json";

    [Fact]
    public void EveryOrderForEveryCombination_IsWhatItWasBeforeTheTextsBecameData()
    {
        var recorded = JsonSerializer.Deserialize<Recording>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", Fixture)))!;
        var now = Record();

        now.Preamble.Should().Be(recorded.Preamble, "the preamble is part of every reply that carries orders");
        now.Contexts.Keys.Should().BeEquivalentTo(recorded.Contexts.Keys, "the combinations are the same ones");
        foreach (var (key, indices) in recorded.Contexts)
        {
            now.Contexts[key].Select(i => now.Texts[i]).Should().Equal(
                indices.Select(i => recorded.Texts[i]), $"the orders for {key} must not have changed");
        }
    }

    /// <summary>Every combination the orders depend on, and what the code says for each.</summary>
    internal static Recording Record()
    {
        var texts = new List<string>();
        var contexts = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var context in Combinations())
        {
            contexts[KeyOf(context.Value)] = [.. GateCommands.For(context.Value).Select(order => IndexOf(texts, order))];
        }

        return new Recording(GateCommands.Preamble, texts, contexts);
    }

    private static int IndexOf(List<string> texts, string order)
    {
        var at = texts.IndexOf(order);
        if (at >= 0)
        {
            return at;
        }
        texts.Add(order);

        return texts.Count - 1;
    }

    private static IEnumerable<KeyValuePair<string, CommandContext>> Combinations()
    {
        bool[] both = [false, true];
        return
            from autonomous in both
            from split in both
            from strongest in both
            from planStage in both
            from first in both
            from scope in new[] { GateScope.Epic, GateScope.Task }
            from plan in Plans
            from models in Pairs
            let context = new CommandContext(autonomous, split, strongest, plan.Value, planStage, first)
            {
                GatePer = scope,
                Models = models.Value,
            }
            select new KeyValuePair<string, CommandContext>(plan.Key + "/" + models.Key, context);
    }

    private static string KeyOf(CommandContext c) =>
        $"a{B(c.Autonomous)} s{B(c.SplitPlan)} f{B(c.SplitWithFable)} p{B(c.PlanStage)} r{B(c.FirstPlanRound)} "
        + $"{c.GatePer} {SizeOf(c.PlanText)} {(c.Models == CommandModels.ClaudeCode ? "claude" : "unnamed")}";

    private static string B(bool value) => value ? "1" : "0";

    private static string SizeOf(string plan) => PlanShapeReader.Of(plan).Verdict.ToString();

    private static readonly Dictionary<string, string> Plans = new()
    {
        ["as-it-is"] = "# PLAN — a nit\n\nOne paragraph. Change `panelView.ts`.\n",
        ["small"] = PlanOf(lines: 121, steps: 2),
        ["medium"] = PlanOf(lines: 351, steps: 6),
        ["large"] = PlanOf(lines: 601, steps: 9),
        ["huge"] = PlanOf(lines: 901, steps: 12),
    };

    private static readonly Dictionary<string, ModelPair> Pairs = new()
    {
        ["claude"] = CommandModels.ClaudeCode,
        ["unnamed"] = new ModelPair(string.Empty, string.Empty),
    };

    private static string PlanOf(int lines, int steps)
    {
        var text = new System.Text.StringBuilder("# PLAN — generated\n\n## Build order\n\n");
        for (var i = 1; i <= steps; i++)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{i}. do the {i}th thing\n");
        }
        text.Append("\n## Notes\n\n- touches `file0.cs`\n- under `src_mcp/`\n");
        while (text.ToString().Split('\n').Length < lines)
        {
            text.Append("filler\n");
        }

        return text.ToString();
    }

    internal sealed record Recording(string Preamble, List<string> Texts, SortedDictionary<string, int[]> Contexts);
}
