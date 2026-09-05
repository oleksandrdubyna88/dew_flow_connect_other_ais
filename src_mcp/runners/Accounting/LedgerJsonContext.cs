using System.Text.Json.Serialization;

namespace CoaiMcp.Runners.Accounting;

/// <summary>
/// The ledger's own serializer, and the only difference that matters is <b>not</b> indented.
/// </summary>
/// <remarks>
/// <c>usage.jsonl</c> is JSON Lines: one entry, one line. The tool answers share a context with
/// <c>WriteIndented = true</c> because a person reads them, and writing a ledger entry through it
/// spread every entry over fifteen lines — which the line-based reader then discards, so the chart
/// would have shown nothing at all. Caught by a reviewer on the commit that added the ledger,
/// before a single entry had been written.
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(UsageEntry))]
public sealed partial class LedgerJsonContext : JsonSerializerContext;
