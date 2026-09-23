using System.Text.Json;

namespace CoaiMcp.Runners.Context;

/// <summary>One rule the resolver selected, and why.</summary>
/// <param name="Source">Repository-relative, and validated to be so — see <see cref="RuleManifest.Parse"/>.</param>
/// <param name="Reasons">
/// What selected it — <c>path:src/Foo.cs (**/*.cs)</c>, <c>task:implement</c>, <c>always</c>. Carried
/// into the prompt so a finding can say why its rule was shown.
/// </param>
public sealed record ManifestRule(string Id, string Source, int Bytes, string Hash, IReadOnlyList<string> Reasons);

/// <summary>What the shared resolver answered for one set of changed files.</summary>
public sealed record RuleManifest(IReadOnlyList<ManifestRule> Rules);

/// <summary>
/// The resolver's answer, or the reason there is not one. Never both, and never null.
/// </summary>
/// <remarks>
/// A union rather than a manifest-plus-flag because the two states must not be confusable: a bundle
/// built from a half-parsed manifest looks to a reviewer exactly like a bundle built from a complete
/// one, and the reviewer's silence about a rule it never saw reads as compliance.
/// </remarks>
public abstract record RuleSelection
{
    private RuleSelection() { }

    public sealed record Resolved(RuleManifest Manifest) : RuleSelection;

    public sealed record Unavailable(string Reason) : RuleSelection;
}

/// <summary>
/// Parsing, batching and merging the resolver's answers. Pure; launches nothing.
/// </summary>
public static class RuleManifests
{
    /// <summary>The resolver refuses more than this many <c>--file</c> arguments in one call.</summary>
    public const int FilesPerBatch = 256;

    /// <summary>
    /// The manifest, or the reason it is not one.
    /// </summary>
    /// <remarks>
    /// <para>Read with <see cref="JsonDocument"/> rather than a deserializer: the real manifest also
    /// carries <c>instructions</c>, <c>taskVocabulary</c> and <c>version</c>, which this code does not
    /// model and must not choke on, and a hand-written read gives a refusal that NAMES what was wrong
    /// instead of a type-mapping error.</para>
    /// <para><b>Three cases a code round insisted on separating.</b> A missing <c>rules</c> key is a
    /// failure — the script did not answer the question. An empty <c>rules</c> array is a valid ANSWER
    /// (nothing applies to these files) and resolves. A rule missing <c>id</c> or <c>source</c> is a
    /// failure, because the alternative is a bundle with a hole in it that reports as complete.</para>
    /// </remarks>
    public static RuleSelection Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new RuleSelection.Unavailable("the resolver wrote nothing");
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            return document.RootElement.ValueKind != JsonValueKind.Object
                ? new RuleSelection.Unavailable($"the resolver's answer is {document.RootElement.ValueKind}, not an object")
                : RulesOf(document.RootElement);
        }
        catch (JsonException error)
        {
            return new RuleSelection.Unavailable($"the resolver's answer is not JSON: {error.Message}");
        }
    }

    private static RuleSelection RulesOf(JsonElement root)
    {
        if (!root.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array)
        {
            return new RuleSelection.Unavailable("the resolver's answer has no `rules` array");
        }

        var parsed = new List<ManifestRule>();
        var index = 0;
        foreach (var rule in rules.EnumerateArray())
        {
            if (RuleAt(rule, index++) is { } failure)
            {
                return failure.Selection ?? new RuleSelection.Unavailable(failure.Reason);
            }
        }

        foreach (var rule in rules.EnumerateArray())
        {
            parsed.Add(Read(rule));
        }

        return new RuleSelection.Resolved(new RuleManifest(parsed));
    }

    private readonly record struct Rejection(string Reason, RuleSelection? Selection = null);

    /// <summary>Everything that makes one entry unusable, named by its position.</summary>
    private static Rejection? RuleAt(JsonElement rule, int index)
    {
        if (rule.ValueKind != JsonValueKind.Object)
        {
            return new Rejection($"rule {index} is {rule.ValueKind}, not an object");
        }

        if (Text(rule, "id") is not { Length: > 0 } id)
        {
            return new Rejection($"rule {index} has no `id`");
        }

        if (Text(rule, "source") is not { Length: > 0 } source)
        {
            return new Rejection($"rule {id} has no `source`");
        }

        return Contained(source) ? null : new Rejection($"rule {id} names a source outside the repository: {source}");
    }

    /// <summary>
    /// A source must be a repository-relative path, and this is where that is enforced.
    /// </summary>
    /// <remarks>
    /// The resolver is a script from the repository UNDER REVIEW. A change can edit it, and a manifest
    /// answering <c>../../../.ssh/id_ed25519</c> or <c>C:\secrets</c> would otherwise be read verbatim
    /// into a prompt that leaves this machine. Raised on the plan round; the containment belongs here,
    /// at the parse, rather than at the read, so an escaping path can never reach a caller at all.
    /// </remarks>
    private static bool Contained(string source) =>
        !Path.IsPathRooted(source)
        && !source.StartsWith('/')
        && !source.StartsWith('\\')
        && !source.Contains(':')
        && !source.Replace('\\', '/').Split('/').Contains("..");

    private static ManifestRule Read(JsonElement rule) =>
        new(Text(rule, "id")!,
            Text(rule, "source")!.Replace('\\', '/'),
            rule.TryGetProperty("bytes", out var bytes) && bytes.TryGetInt32(out var count) && count >= 0 ? count : 0,
            Text(rule, "hash") ?? string.Empty,
            [.. Strings(rule, "reasons")]);

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IEnumerable<string> Strings(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } text)
            {
                yield return text;
            }
        }
    }

    /// <summary>
    /// The changed paths, in deterministic batches the resolver will accept.
    /// </summary>
    /// <remarks>
    /// Sorted ordinally and de-duplicated first, so the same change produces the same batches on every
    /// machine — the whole plan this belongs to exists because selection stopped being reproducible.
    /// Nothing is dropped: a path too numerous for one call goes in the next one.
    /// </remarks>
    public static IReadOnlyList<IReadOnlyList<string>> Batches(IReadOnlyList<string> paths) =>
        [.. paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Chunk(FilesPerBatch)
            .Select(chunk => (IReadOnlyList<string>)chunk)];

    /// <summary>
    /// One manifest from several batches — or the reason there is not one.
    /// </summary>
    /// <remarks>
    /// <para><b>All or nothing.</b> If any batch failed, the whole resolution fails. Merging the
    /// successful ones would return a manifest that looks complete while the rules for a third of the
    /// change were never asked for — and a reviewer cannot tell the difference. Raised twice on the
    /// plan round, by two vendors.</para>
    /// <para>A rule met in two batches keeps the metadata of the FIRST batch that named it, and the
    /// UNION of its reasons — it was selected for both. A hash that disagrees between batches means the
    /// tree changed underneath the resolution, which is not something to average.</para>
    /// </remarks>
    public static RuleSelection Merge(IReadOnlyList<RuleSelection> answers)
    {
        if (answers.Count == 0)
        {
            return new RuleSelection.Resolved(new RuleManifest([]));
        }

        if (answers.OfType<RuleSelection.Unavailable>().FirstOrDefault() is { } failed)
        {
            return failed;
        }

        var byId = new Dictionary<string, ManifestRule>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var rule in answers.OfType<RuleSelection.Resolved>().SelectMany(answer => answer.Manifest.Rules))
        {
            if (!byId.TryGetValue(rule.Id, out var seen))
            {
                byId[rule.Id] = rule;
                order.Add(rule.Id);
                continue;
            }

            if (!string.IsNullOrEmpty(seen.Hash) && !string.IsNullOrEmpty(rule.Hash) && seen.Hash != rule.Hash)
            {
                return new RuleSelection.Unavailable(
                    $"rule {rule.Id} came back with two different hashes — the tree changed under the resolution");
            }

            byId[rule.Id] = seen with
            {
                Reasons = [.. seen.Reasons.Concat(rule.Reasons).Distinct(StringComparer.Ordinal)],
            };
        }

        return new RuleSelection.Resolved(new RuleManifest([.. order.Select(id => byId[id])]));
    }
}
