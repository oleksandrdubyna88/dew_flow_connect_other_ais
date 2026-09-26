using CoaiMcp.Runners.Processes;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// A directory reached through a LINK — the shape every macOS temp directory has (<c>/var</c> is a link to
/// <c>/private/var</c>), and any checkout a person moved and linked back.
/// </summary>
/// <remarks>
/// <para>Made by the test itself, so a comparison that cannot see a link fails on every platform rather
/// than only on the one runner whose temp directory happens to sit behind one: a junction on Windows (no
/// privilege needed, where a symbolic link needs Developer Mode — and git resolves a junction exactly as
/// macOS resolves <c>/var</c>), a symbolic link everywhere else.</para>
/// <para>It was a private helper inside <c>AReviewRootBehindALinkTests</c>; the feature stage's
/// top-level check needed the same one, and the reuse rule's second move — extract the shared half — is
/// cheaper than a copy that drifts.</para>
/// </remarks>
internal static class DirectoryLink
{
    /// <summary>Makes <paramref name="link"/> a link to the existing directory <paramref name="target"/>.</summary>
    public static async Task MakeAsync(IProcessLauncher launcher, string link, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            var made = await launcher.RunAsync(
                new ProcessRequest("cmd", ["/c", "mklink", "/J", link, target], Path.GetDirectoryName(link)!));
            made.ExitCode.Should().Be(0, $"a junction is how this test reaches its directory: {made.StdErr}");
        }
        else
        {
            Directory.CreateSymbolicLink(link, target);
        }
    }

    /// <summary>
    /// Drops the link ITSELF — never recursively, so a delete can never walk through it into the
    /// directory it points at.
    /// </summary>
    public static void Remove(string link)
    {
        if (Directory.Exists(link))
        {
            Directory.Delete(link);
        }
    }
}
