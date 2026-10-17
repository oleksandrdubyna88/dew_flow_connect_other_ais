using System.Text.Json;
using CoaiMcp.Runners.Platform;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>One vector of <c>shared/wsl-banner-vectors.json</c>: a <c>/proc/version</c> banner and the WSL generation it names.</summary>
internal sealed record WslBannerVector(string Id, string Banner, WslGeneration Generation);

/// <summary>
/// The shared banner rule, read from the checked-in <c>shared/</c> folder (from the test binary up to the repository root, as
/// <see cref="CapabilityMatrixTests"/> does). A missing file fails every case loudly; nothing here skips for it.
/// </summary>
internal static class WslBannerVectors
{
    public static IReadOnlyList<WslBannerVector> All { get; } = Load();

    public static TheoryData<string> Ids() => [.. All.Select(vector => vector.Id)];

    public static WslBannerVector Named(string id) => All.Single(vector => vector.Id == id);

    private static List<WslBannerVector> Load()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "shared", "wsl-banner-vectors.json"));
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));

        return [.. document.RootElement.GetProperty("vectors").EnumerateArray().Select(vector => new WslBannerVector(
            vector.GetProperty("id").GetString()!,
            vector.GetProperty("banner").GetString()!,
            Enum.Parse<WslGeneration>(vector.GetProperty("generation").GetString()!, ignoreCase: true)))];
    }
}