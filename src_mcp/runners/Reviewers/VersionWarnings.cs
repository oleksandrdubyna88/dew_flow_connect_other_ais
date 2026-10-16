using System.Text.RegularExpressions;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// ONE builder for the sentence "this CLI's version is not one coai measured", both runtimes' — Vibe's
/// (<c>VibeLaunch</c>) and Kimi's (<c>KimiLaunch</c>) — and the one reader of a version out of what a CLI printed (E4.2 of
/// <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>, decision 3 and brief section 12).
/// </summary>
/// <remarks>
/// <para>The owner's decision of 2026-10-13: an unmeasured version is WARNED about and run, never refused — "set it up once and it
/// must work if it can". The sentence is a string (<c>LaunchTurn.Warning</c>, the card, the reviewer line), so the carrier did
/// not change; what changed is that the two runtimes no longer build their own, so the opening cannot drift: both begin
/// <c>&lt;runtime&gt; &lt;running&gt; is not a version coai has measured (it measured &lt;measured&gt;); it is run anyway, but </c>.</para>
/// </remarks>
public static class VersionWarnings
{
    private static readonly Regex VersionPattern = new(@"\b(\d+\.\d+\.\d+)\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>The warning for <paramref name="runtime"/> <paramref name="running"/> when <paramref name="measured"/> is what was measured.</summary>
    /// <param name="numbersCover">What the measured version's numbers cover, said as a clause (it ends "…are 2.1.1's").</param>
    /// <param name="advised">What to re-run to measure the new version.</param>
    public static string Unmeasured(string runtime, string running, string measured, string numbersCover, string advised) =>
        $"{runtime} {running} is not a version coai has measured (it measured {measured}); it is run anyway, but {numbersCover} — {advised}";

    /// <summary>The version a CLI's <c>--version</c> printed (<c>kimi 2.1.1</c>, <c>python.exe …\vibe 2.26.1</c>), or empty.</summary>
    public static string VersionIn(string printed) =>
        VersionPattern.Match(printed) is { Success: true } found ? found.Groups[1].Value : string.Empty;
}
