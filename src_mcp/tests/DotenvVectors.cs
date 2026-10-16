using System.Text.Json;

namespace CoaiMcp.Tests;

/// <summary>One vector of <c>shared/dotenv-name-vectors.json</c>: the bytes, the names python-dotenv bound from them, and DL2's verdict.</summary>
internal sealed record DotenvVector(string Id, byte[] Bytes, IReadOnlyList<string> Binds, IReadOnlyList<string> NoValue, string Verdict, int Line, string Kind, IReadOnlyList<string> MustNotEcho);

/// <summary>
/// The oracle file, read from the checked-in <c>shared/</c> folder — from the test binary up to the repository root (as
/// <see cref="CapabilityMatrixTests"/> does). A missing file fails every case loudly; nothing here skips for it.
/// </summary>
internal static class DotenvVectors
{
    public static IReadOnlyList<DotenvVector> All { get; } = Load();

    public static JsonDocument Document()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "shared", "dotenv-name-vectors.json"));

        return File.Exists(path)
            ? JsonDocument.Parse(File.ReadAllBytes(path))
            : throw new FileNotFoundException($"shared/dotenv-name-vectors.json is not at {path} — E1 of the Kimi-and-Vibe-on-Linux plan writes it; this suite never skips for it");
    }

    private static List<DotenvVector> Load()
    {
        using var document = Document();

        return [.. document.RootElement.GetProperty("vectors").EnumerateArray().Select(v => new DotenvVector(
            v.GetProperty("id").GetString()!,
            Convert.FromBase64String(v.GetProperty("inputBase64").GetString()!),
            Names(v, "binds"),
            Names(v, "noValue"),
            v.GetProperty("coai").GetProperty("verdict").GetString()!,
            v.GetProperty("coai").TryGetProperty("line", out var line) ? line.GetInt32() : 0,
            v.GetProperty("coai").TryGetProperty("why", out var why) ? why.GetString()! : string.Empty,
            Names(v, "mustNotEcho")))];
    }

    private static string[] Names(JsonElement vector, string property) => [.. vector.GetProperty(property).EnumerateArray().Select(n => n.GetString()!)];
}
