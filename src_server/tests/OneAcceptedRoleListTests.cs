using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// There is ONE answer to "which review roles does this server run", and this fails when a second
/// one appears.
/// </summary>
/// <remarks>
/// <para><b>Why a structural scan rather than trust.</b> <c>RoleCatalog.Builtin.Roles</c> was
/// enumerated in two places before this plan — the unknown-role refusal and the missing-role refusal
/// — each answering the question in its own words, and neither knowing the other existed. Both were
/// correct, because the answer was a constant. The moment an operator can configure the answer, a
/// second enumeration is a server that refuses a role in one gate and lists it in the other.</para>
/// <para>Plan 2 is why this is a test and not a note: it shipped with THREE independent counts of
/// "which code roles will run", and the one nobody updated promised four reviewers under a page
/// drawing five boxes. Nothing caught it for a whole plan, because nothing was looking.</para>
/// <para>The scan is over SOURCE, which is blunt — but the thing being prevented is somebody writing
/// the list again, and that is a source-level act. <see cref="AcceptedRoles"/> is the one place
/// allowed to enumerate the catalog; every other file must ask it.</para>
/// </remarks>
public sealed class OneAcceptedRoleListTests
{
    /// <summary>The file that is allowed to build the list, because building it is its whole job.</summary>
    private const string TheOnePlace = "AcceptedRoles.cs";

    /// <summary>
    /// Reaching the shipped catalog at all — which is the shape any second answer would take.
    /// </summary>
    /// <remarks>
    /// Deliberately <c>RoleCatalog.Builtin</c> rather than <c>RoleCatalog.Builtin.Roles</c>: the
    /// narrower token let a gate ask <c>RoleCatalog.Builtin.ById(role)</c> — which is exactly the
    /// membership check this type exists to own — and pass the scan. Two reviewers named the
    /// alias-shaped evasion; this closes the one spelling of it that is a real method on a real type.
    /// Nothing outside <see cref="AcceptedRoles"/> reaches the catalog today, so the tightening costs
    /// nothing and refuses a whole family rather than one phrase. (gemini and codex, story 2's plan
    /// round.)
    /// </remarks>
    private const string Enumeration = "RoleCatalog.Builtin";

    [Fact]
    public void OnlyAcceptedRolesEnumeratesTheShippedCatalog()
    {
        var offenders = SourceFiles()
            .Where(file => File.ReadAllText(file).Contains(Enumeration, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Where(name => name != TheOnePlace)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            "which roles this server runs is AcceptedRoles' answer; a second enumeration of "
            + $"{Enumeration} is a gate that can disagree with the other one about a configured role");
    }

    [Fact]
    public void TheScanIsLookingAtSomething()
    {
        // A scan over an empty file list passes for the wrong reason, and would go on passing after
        // a rename moved the server's source somewhere it does not look.
        SourceFiles().Should().HaveCountGreaterThan(10, "the server's own source");
        SourceFiles().Select(Path.GetFileName).Should().Contain(TheOnePlace);
        File.ReadAllText(SourceFiles().Single(f => Path.GetFileName(f) == TheOnePlace))
            .Should().Contain(Enumeration, "the one place that IS allowed to, so the scan can see it");
    }

    /// <summary>The server's own C# — not its tests, and not anything generated into obj/bin.</summary>
    private static List<string> SourceFiles()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here is not null && !Directory.Exists(Path.Combine(here.FullName, "src_server", "src")))
        {
            here = here.Parent;
        }

        here.Should().NotBeNull("the repository root is above the test binary");

        return [.. Directory.EnumerateFiles(
            Path.Combine(here!.FullName, "src_server", "src"), "*.cs", SearchOption.AllDirectories)];
    }
}
