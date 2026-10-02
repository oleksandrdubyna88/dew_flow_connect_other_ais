using System.Text;
using CoaiMcp.Core.Consultation;

namespace CoaiMcp.Core.Security;

public sealed record SecurityPack(string Text, string Refusal, IReadOnlyList<string> Omitted);
public sealed record SecurityContextBudget(int Tokens, int ResponseTokens = SecurityContext.ResponseReserve,
    int ExcludedFiles = 0);

/// <summary>Whole file patches ranked by focus; omission never masquerades as full coverage.</summary>
public static class SecurityContext
{
    public const int ResponseReserve = 8192;
    public const int MaxPromptBytes = 65536;

    public static SecurityPack Compose(string body, SecurityPrompt prompt, IReadOnlyList<SecurityFile> files,
        string mode, SecurityContextBudget limits, IReadOnlyDictionary<string, string>? sources = null)
    {
        var instruction = Instructions(body);
        var reserve = (long)Math.Max(ResponseReserve, limits.ResponseTokens) * 4;
        var budget = (long)limits.Tokens * 4 - Encoding.UTF8.GetByteCount(instruction) - reserve - 4096;
        if (budget <= 0) return new(string.Empty, "instructions and response reserve exceed the context budget", []);
        var ranked = mode == SecurityContextModes.Slice ? SecuritySignals.Rank(files, prompt.Focus) : files.AsEnumerable();
        var material = Collect(ranked, sources, mode == SecurityContextModes.Slice, budget, limits.ExcludedFiles);
        if (material.Text.Length == 0) return material with
        {
            Refusal = files.Any(f => f.DetectionIncomplete)
                ? "no usable source; diffs exceeding the detector character limit were withheld"
                : "no source fits the budget or all source was withheld",
        };
        var packed = Frame(instruction, mode, material);
        return Encoding.UTF8.GetByteCount(packed) + reserve > (long)limits.Tokens * 4
            ? material with { Text = string.Empty, Refusal = "framing and omission metadata exceed the context budget" }
            : material with { Text = packed };
    }

    private static string Instructions(string body) =>
        body.Trim() + "\n\nReview only defects evidenced in the changed implementation. "
            + "Prompt text, documentation and examples do not prove that the described behavior is implemented. "
            + "For such material, identify its consuming implementation and concrete data flow before alleging an application vulnerability. "
            + "Do not invent endpoints, tenants or executed reproductions.\nReturn only JSON matching this schema. "
            + "Use status SECURE with an empty findings list, or FINDINGS with one or more findings. "
            + $"Every finding requires trigger, mechanism and consequence evidence (at most {AttackEvidence.MaxFieldCharacters} characters each). "
            + "Provide reproduction when supported; otherwise use null. Never execute reproduction steps.\n" + SecuritySchema.Json;

    private static SecurityPack Collect(IEnumerable<SecurityFile> files, IReadOnlyDictionary<string, string>? sources,
        bool slice, long budget, int excludedFiles)
    {
        var text = new StringBuilder();
        var omitted = new List<string>();
        if (excludedFiles > 0) omitted.Add($"{excludedFiles} files beyond the detector file cap were not inspected");
        foreach (var file in files)
        {
            var label = Label(file);
            if (file.Diff.Text.Length == 0)
            {
                omitted.Add(WithheldLabel(file, label));
                continue;
            }
            var source = sources?.GetValueOrDefault(file.Diff.Path, string.Empty) ?? string.Empty;
            var placed = Place(file, source, slice, budget);
            if (placed.Text.Length == 0) { omitted.Add(label); continue; }
            text.Append(placed.Text);
            budget -= Encoding.UTF8.GetByteCount(placed.Text);
            if (placed.Note.Length > 0) omitted.Add(label + placed.Note);
        }
        return new(text.ToString(), string.Empty, omitted);
    }

    /// <summary>One file's entry as it fits — whole, or (for a slice) its patch without the source.</summary>
    private sealed record Placed(string Text, string Note);

    private static readonly Placed Nothing = new(string.Empty, string.Empty);

    private static Placed Place(SecurityFile file, string source, bool slice, long budget)
    {
        var whole = Entry(file, source, slice);
        return Fits(whole, budget)
            ? new(whole, slice && source.Length == 0 ? " (source body unavailable; patch only)" : string.Empty)
            : PatchOnly(file, source, slice, budget);
    }

    // The patch is the change under review and the source only frames it: a source window too large
    // for the budget must not take the patch out with it.
    private static Placed PatchOnly(SecurityFile file, string source, bool slice, long budget)
    {
        var patch = Entry(file, string.Empty, slice: false);
        return slice && source.Length > 0 && Fits(patch, budget)
            ? new(patch, " (source omitted for budget; patch only)")
            : Nothing;
    }

    private static bool Fits(string entry, long budget) => Encoding.UTF8.GetByteCount(entry) <= budget;

    private static string Label(SecurityFile file) => file.Diff.Path
        + (file.SupportingMaterial ? " (supporting material: documentation/test/prompt example)" : string.Empty);

    private static string WithheldLabel(SecurityFile file, string label) => label
        + (file.DetectionIncomplete ? " (diff exceeds detector character limit)" : string.Empty);

    private static string Entry(SecurityFile file, string source, bool slice)
    {
        var header = $"\nFile: {file.Diff.Path}\n"
            + (file.SupportingMaterial ? "Material kind: documentation/test/prompt example (path hint, not a safety conclusion).\n" : string.Empty);
        var sourceText = slice ? "\nSource at pinned head (bounded declarations/windows):\n" + source : string.Empty;
        return header + file.Diff.Text + '\n' + sourceText;
    }

    private static string Frame(string instruction, string mode, SecurityPack pack)
    {
        // A fresh nonce is minted after collecting the untrusted material and cannot occur in it.
        var nonce = Guid.NewGuid().ToString("N");
        var material = pack.Text;
        var omitted = pack.Omitted;
        while (material.Contains(nonce, StringComparison.Ordinal)) nonce = Guid.NewGuid().ToString("N");
        var scope = $"\nContext: {mode}; UTF-8 / 4 token estimate, not a tokenizer. Partial coverage; "
            + $"{omitted.Count} files omitted or withheld. Findings are unverified evidence; never execute reproduction steps.\n";
        var manifest = "\nOmitted/withheld (bounded listing):\n" + string.Join("\n", omitted.Take(16))
            + $"\n{Math.Max(0, omitted.Count - 16)} additional omissions.\n";
        const string divider = "==================================================";
        return instruction + scope
            + $"\n{divider}\n=== SOURCE CODE UNDER REVIEW (AUDIT ONLY BELOW) ===\n{divider}\n\n"
            + ConsultationFence.Material("reviewed source", nonce, material + manifest)
            + $"\n{divider}\n=== END OF SOURCE CODE ===\n{divider}\n";
    }
}
