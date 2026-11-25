using System.Text;
using System.Text.Json;
using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Work;

namespace CoaiMcp.Tests;

/// <summary>A feature folder in memory that also lists its files with their git blob ids — what <c>WorkPack</c> reads (<see cref="IListedWorkFolder"/>).</summary>
internal sealed class ListedFolder(IReadOnlyDictionary<string, string> files) : IListedWorkFolder
{
    private readonly InMemoryWorkFolder _inner = new(files);

    public IReadOnlyList<WorkEntry> Files { get; } =
        [.. files.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new WorkEntry(pair.Key, BlobOf(pair.Value), Encoding.UTF8.GetByteCount(pair.Value)))];

    public IReadOnlyList<string> Folders(string relative) => _inner.Folders(relative);

    public WorkFile Read(string relative, int maxBytes) => _inner.Read(relative, maxBytes);

    internal static string BlobOf(string text) => BlobId.Sha1Of(Encoding.UTF8.GetBytes(text));

    /// <summary>Every regular file of a folder on disk, read as text.</summary>
    internal static ListedFolder OfDisk(string root) =>
        new(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(file => !new FileInfo(file).Attributes.HasFlag(FileAttributes.ReparsePoint))
            .ToDictionary(file => Path.GetRelativePath(root, file).Replace('\\', '/'), file => File.ReadAllText(file, new UTF8Encoding(false)), StringComparer.Ordinal));
}

/// <summary>The vectors of <c>shared/work-pack-vectors.json</c>.</summary>
internal static class PackVectors
{
    internal sealed record Dependency(string Node, string Title);

    internal sealed record Vector(string Name, string Covers, IReadOnlyDictionary<string, string> Files, string Scope, IReadOnlyList<string> Order, IReadOnlyList<Dependency> Dependencies, string Refused);

    internal static IReadOnlyList<Vector> All()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root(), "shared", "work-pack-vectors.json")));

        return [.. document.RootElement.GetProperty("vectors").EnumerateArray().Select(vector => new Vector(
            vector.GetProperty("name").GetString()!,
            vector.GetProperty("covers").GetString()!,
            vector.GetProperty("files").EnumerateObject().ToDictionary(file => file.Name, file => FileText(file.Value)),
            vector.GetProperty("scope").GetString()!,
            Strings(vector, "order"),
            vector.TryGetProperty("dependencies", out var dependencies)
                ? [.. dependencies.EnumerateArray().Select(item => new Dependency(item.GetProperty("node").GetString()!, item.GetProperty("title").GetString()!))]
                : [],
            vector.TryGetProperty("refused", out var refused) ? refused.GetString()! : string.Empty))];
    }

    private static IReadOnlyList<string> Strings(JsonElement vector, string name) =>
        vector.TryGetProperty(name, out var list) ? [.. list.EnumerateArray().Select(item => item.GetString()!)] : [];

    private static string FileText(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : value.GetProperty("prefix").GetString() + string.Concat(Enumerable.Repeat(value.GetProperty("line").GetString()!, value.GetProperty("times").GetInt32()));
}
