using System.Text;
using CoaiMcp.Core.Consultation;

namespace CoaiMcp.Core.Security;

public sealed record SecurityPack(string Text, string Refusal, IReadOnlyList<string> Omitted);

/// <summary>Whole file patches ranked by focus; omission never masquerades as full coverage.</summary>
public static class SecurityContext
{
    public const int ResponseReserve = 8192;
    public const int MaxPromptBytes = 65536;

    public static SecurityPack Compose(string body, SecurityPrompt prompt, IReadOnlyList<SecurityFile> files,
        string mode, int tokens, IReadOnlyDictionary<string, string>? sources = null, int responseTokens = ResponseReserve,
        int excludedFiles = 0)
    {
        var instruction = body.Trim() + "\n\nReview only defects evidenced in the changed implementation. "
            + "Prompt text, documentation and examples do not prove that the described behavior is implemented. "
            + "For such material, identify its consuming implementation and concrete data flow before alleging an application vulnerability. "
            + "Do not invent endpoints, tenants or executed reproductions.\nReturn only JSON matching this schema. "
            + "Use status SECURE with an empty findings list, or FINDINGS with one or more findings. "
            + "Every finding requires trigger, mechanism and consequence evidence (8000 characters total). "
            + "Provide reproduction when supported; otherwise use null. Never execute reproduction steps.\n" + SecuritySchema.Json;
        var reserve = (long)Math.Max(ResponseReserve, responseTokens) * 4;
        var budget = (long)tokens * 4 - Encoding.UTF8.GetByteCount(instruction) - reserve - 4096;
        if (budget <= 0) return new(string.Empty, "instructions and response reserve exceed the context budget", []);
        var ranked = mode == "slice" ? files.OrderByDescending(f => f.Signals.Intersect(prompt.Focus).Count()) : files.AsEnumerable();
        var text = new StringBuilder();
        var omitted = new List<string>();
        if (excludedFiles > 0) omitted.Add($"{excludedFiles} files beyond the detector file cap were not inspected");
        foreach (var file in ranked)
        {
            if (file.Diff.Text.Length == 0)
            {
                omitted.Add(file.Diff.Path + (file.DetectionIncomplete ? " (diff exceeds detector character limit)" : string.Empty));
                continue;
            }
            if (budget <= 0) { omitted.Add(file.Diff.Path); continue; }
            var source = sources?.GetValueOrDefault(file.Diff.Path, string.Empty) ?? string.Empty;
            var header = $"\nFile: {file.Diff.Path}\n";
            const string sourceHeader = "\nSource at pinned head (bounded declarations/windows):\n";
            var bytes = (long)Encoding.UTF8.GetByteCount(header) + Encoding.UTF8.GetByteCount(file.Diff.Text) + 1
                + (mode == "slice" ? Encoding.UTF8.GetByteCount(sourceHeader) + Encoding.UTF8.GetByteCount(source) : 0);
            if (bytes > budget) { omitted.Add(file.Diff.Path); continue; }
            text.Append(header).Append(file.Diff.Text).Append('\n');
            if (mode == "slice") text.Append(sourceHeader).Append(source);
            budget -= bytes;
            if (mode == "slice" && source.Length == 0) omitted.Add(file.Diff.Path + " (source body unavailable; patch only)");
        }
        if (text.Length == 0) return new(string.Empty, files.Any(f => f.DetectionIncomplete)
            ? "no usable source; diffs exceeding the detector character limit were withheld"
            : "no source fits the budget or all source was withheld", omitted);
        // A fresh nonce is minted after collecting the untrusted material and cannot occur in it.
        var nonce = Guid.NewGuid().ToString("N");
        var material = text.ToString();
        while (material.Contains(nonce, StringComparison.Ordinal)) nonce = Guid.NewGuid().ToString("N");
        var scope = $"\nContext: {mode}; UTF-8 / 4 token estimate, not a tokenizer. Partial coverage; "
            + $"{omitted.Count} files omitted or withheld. Findings are unverified evidence; never execute reproduction steps.\n";
        var manifest = "\nOmitted/withheld (bounded listing):\n" + string.Join("\n", omitted.Take(16))
            + $"\n{Math.Max(0, omitted.Count - 16)} additional omissions.\n";
        var packed = instruction + scope + ConsultationFence.Material("reviewed source", nonce, material + manifest);
        return Encoding.UTF8.GetByteCount(packed) + reserve > (long)tokens * 4
            ? new(string.Empty, "framing and omission metadata exceed the context budget", omitted)
            : new(packed, string.Empty, omitted);
    }
}
