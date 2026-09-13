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
    /// <summary>
    /// The file that is allowed to build the list, because building it is its whole job.
    /// </summary>
    /// <remarks>
    /// A PATH, not a base name. Exempting every file called <c>AcceptedRoles.cs</c> would exempt a
    /// second one added anywhere under the server — which is precisely the thing being prevented,
    /// wearing the name of the thing that prevents it. (codex, story 2's code round.)
    /// </remarks>
    private static readonly string TheOnePlace = Path.Combine("Jobs", "AcceptedRoles.cs");

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
            .Where(file => !Under(file).Equals(TheOnePlace, StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file).Contains(Enumeration, StringComparison.Ordinal))
            .Select(Under)
            .OrderBy(path => path, StringComparer.Ordinal)
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
        SourceFiles().Select(Under).Should().Contain(TheOnePlace);
        File.ReadAllText(SourceFiles().Single(f => Under(f) == TheOnePlace))
            .Should().Contain(Enumeration, "the one place that IS allowed to, so the scan can see it");
    }

    [Fact]
    public void TheScanDoesNotWalkGeneratedOutput()
    {
        // `src_server/src/obj` fills with generated C# on every build — the regex source generator's
        // output, assembly attributes, editor artefacts. None of it is anybody's code, all of it can
        // change with a toolchain, and a token appearing there would fail this test for a reason no
        // reader could act on. (local, story 2's code round.)
        SourceFiles().Select(Under).Should().OnlyContain(path => !Generated(path));
    }

    /// <summary>A file's path under `src_server/src`, which is how the exemption is spelled.</summary>
    private static string Under(string file) =>
        Path.GetRelativePath(Root(), file);

    /// <summary>The server's own C# — not its tests, and not anything generated into obj/bin.</summary>
    private static List<string> SourceFiles() =>
    [
        .. Directory.EnumerateFiles(Root(), "*.cs", SearchOption.AllDirectories)
            .Where(file => !Generated(Path.GetRelativePath(Root(), file))),
    ];

    /// <summary>Build output rather than anybody's code: the regex generator's, the SDK's, the editor's.</summary>
    private static bool Generated(string relative)
    {
        var top = relative.Split(Path.DirectorySeparatorChar)[0];

        return top.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || top.Equals("bin", StringComparison.OrdinalIgnoreCase);
    }

    private static string Root()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here is not null && !Directory.Exists(Path.Combine(here.FullName, "src_server", "src")))
        {
            here = here.Parent;
        }

        here.Should().NotBeNull("the repository root is above the test binary");

        return Path.Combine(here!.FullName, "src_server", "src");
    }
}
