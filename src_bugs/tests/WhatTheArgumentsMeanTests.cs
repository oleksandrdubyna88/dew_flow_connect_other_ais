using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// Which arguments this binary answers, and which one makes it exit 64.
/// </summary>
/// <remarks>
/// <para><b>`TheBuiltBinariesTests` already proves this over the real executable, and that is not
/// enough.</b> It spawns a PROCESS, and a coverage run cannot see inside one — so the rule that
/// decides whether a caller learns "this binary is too old" or gets a web server it never asked for
/// was exercised and measured as untouched. Both suites earn their place: that one proves the exit
/// code really reaches the shell, this one can be run against every shape cheaply.</para>
/// <para>The rule itself, from `.agents/PROJECT.md`: <b>64 means "never heard of that mode"</b> and
/// is how a caller detects an old binary and falls back. A mode this binary KNOWS answers some other
/// non-zero code when its arguments are wrong. `coai-bugs --rotate-the-moon` used to start Kestrel
/// and listen for ever, which is neither.</para>
/// </remarks>
public sealed class WhatTheArgumentsMeanTests
{
    [Theory]
    [InlineData("--rotate-the-moon")]
    [InlineData("--issue-keys")]          // a near-miss of a real mode
    [InlineData("--help")]                // never implemented here; the Team server has one
    public void AnArgumentNamingNoModeIsUnknown(string argument) =>
        Program.Unknown([argument]).Should().Be(
            argument, "64 is what tells a caller it is talking to a binary too old for the ask");

    /// <summary>The host's own arguments are not this binary's to refuse.</summary>
    /// <remarks>
    /// `--urls` is how the unit tells it where to listen. Refusing it would make the service
    /// unstartable, which is a considerably worse failure than the one this check prevents.
    /// </remarks>
    [Theory]
    [InlineData("--urls")]
    [InlineData("--environment")]
    [InlineData("--contentRoot")]
    [InlineData("--applicationName")]
    public void TheHostsOwnArgumentsPassThrough(string argument) =>
        Program.Unknown([argument, "whatever"]).Should().BeEmpty();

    /// <summary>`--urls=x` and `--urls x` are the same argument to the host.</summary>
    [Fact]
    public void AnEqualsFormIsTheSameArgument() =>
        Program.Unknown(["--urls=http://127.0.0.1:8110"]).Should().BeEmpty();

    /// <summary>No arguments at all is the ordinary case: it serves.</summary>
    [Fact]
    public void NoArgumentsIsNotAnUnknownMode() =>
        Program.Unknown([]).Should().BeEmpty();

    /// <summary>Something that is not a flag is not a mode either, and is left to the host.</summary>
    [Fact]
    public void ABareWordIsNotJudgedHere() =>
        Program.Unknown(["serve"]).Should().BeEmpty();

    [Theory]
    [InlineData("--issue-key")]
    [InlineData("--revoke")]
    [InlineData("--promote")]
    [InlineData("--waiting")]
    public void EveryAdminModeIsKnown(string mode)
    {
        Program.Unknown([mode]).Should().BeEmpty("`Admin.Knows` takes it before this is reached");
        Admin.Knows([mode]).Should().BeTrue();
    }

    /// <summary>And the registry is what both of them read.</summary>
    /// <remarks>
    /// Derived from `Admin.Modes` rather than retyped: the whole reason that field exists is that
    /// the predicate and the dispatch switch were two hand-maintained lists.
    /// </remarks>
    [Fact]
    public void TheRegistryIsTheOnlyListOfModes()
    {
        Admin.Modes.Should().NotBeEmpty();
        foreach (var mode in Admin.Modes)
        {
            Admin.Knows([mode]).Should().BeTrue($"{mode} is in the registry");
            Admin.Knows([mode + "-not"]).Should().BeFalse();
        }
    }

    /// <summary>
    /// The keyword list comes out of the binary, and an operator can override it with a file.
    /// </summary>
    /// <remarks>
    /// The embedded path is what every ordinary start uses and is covered by
    /// `TheKeywordsTravelInTheBinaryTests`. THIS covers the override, which exists so a list can be
    /// corrected on a running deployment without waiting for a release — and which, being the branch
    /// nobody takes, is exactly the one that rots.
    /// </remarks>
    [Fact]
    public void AFileNamedByTheEnvironmentReplacesTheEmbeddedList()
    {
        var file = Path.Combine(Path.GetTempPath(), $"kw-{Guid.NewGuid():N}.txt");
        File.WriteAllText(file, "[CSharp]\nlock\nreturn\n");
        var was = Environment.GetEnvironmentVariable("COAI_BUGS_KEYWORDS");
        try
        {
            Environment.SetEnvironmentVariable("COAI_BUGS_KEYWORDS", file);

            Program.Keywords().Should().Contain("[CSharp]").And.Contain("lock");
        }
        finally
        {
            Environment.SetEnvironmentVariable("COAI_BUGS_KEYWORDS", was);
            File.Delete(file);
        }
    }

    /// <summary>With no override it is the embedded one, and it is the real list.</summary>
    [Fact]
    public void WithNoOverrideItIsTheEmbeddedList()
    {
        var was = Environment.GetEnvironmentVariable("COAI_BUGS_KEYWORDS");
        try
        {
            Environment.SetEnvironmentVariable("COAI_BUGS_KEYWORDS", null);

            var keywords = CoaiMcp.Core.Collecting.SkeletonKeywords.From(Program.Keywords());

            keywords.Keys.Should().Contain(["CSharp", "TypeScript", "JavaScript"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COAI_BUGS_KEYWORDS", was);
        }
    }
}
