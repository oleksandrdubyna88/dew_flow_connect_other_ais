using CoaiMcp.Core.Feature;
using CoaiMcp.Core.Notices;
using CoaiMcp.Runners.Context;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What the source resolver refuses BY NAME, before git is asked anything — and what it must NOT
/// refuse, because the trial's reviewers were refused ordinary code files (plan D15, narrowed by the
/// operator on 2026-09-26).
/// </summary>
/// <remarks>
/// <para><b>The narrowing is the point of the positive tests.</b> The plan's first D15 applied the
/// redaction words of <c>shared/credential-words.json</c> to file NAMES; on 21 real features that
/// refused nine ordinary code files — <c>providers/credentials.ts</c>, <c>Auth.cs</c>,
/// <c>TokenIdentity.cs</c>, <c>tokens.rs</c>. The words stay where they belong, on file CONTENT
/// (<see cref="Redaction"/>); a NAME is refused only for the fixed shapes below.</para>
/// <para>Every list here is DERIVED from the pattern table, never retyped (testing.md — a test that
/// repeats a list the code also holds will not notice the third entry), and each scan has its
/// companion: the known instance the pattern still matches.</para>
/// </remarks>
public sealed class ACredentialLookingNameIsRefusedTests
{
    // --------------------------------------------------------------------------------------------
    // Credential-looking names: the fixed shapes, and nothing else.
    // --------------------------------------------------------------------------------------------

    /// <summary>One example per pattern, derived from the pattern itself — a `*` becomes `x`, so the list cannot go stale.</summary>
    public static TheoryData<string> AnExamplePerCredentialPattern
    {
        get
        {
            var examples = new TheoryData<string>();
            foreach (var pattern in CredentialFiles.Patterns)
            {
                examples.Add("config/" + pattern.Replace("*", "x", StringComparison.Ordinal));
                examples.Add(pattern.Replace("*", string.Empty, StringComparison.Ordinal));
            }

            return examples;
        }
    }

    [Theory]
    [MemberData(nameof(AnExamplePerCredentialPattern))]
    public void EachCredentialShape_IsRefused(string path)
    {
        CredentialFiles.LooksLikeOne(path).Should().BeTrue($"'{path}' is a credential shape");
        CredentialFiles.WhichPattern(path).Should().NotBeEmpty("the refusal names the shape it matched");
    }

    /// <summary>The companion of the scan: the known instances, spelled out, so a pattern table that stopped matching goes red here.</summary>
    [Theory]
    [InlineData(".env", ".env*")]
    [InlineData(".env.local", ".env*")]
    [InlineData("deploy/.env.production", ".env*")]
    [InlineData("certs/server.pem", "*.pem")]
    [InlineData("certs/server.key", "*.key")]
    [InlineData("certs/client.pfx", "*.pfx")]
    [InlineData("certs/client.p12", "*.p12")]
    [InlineData(".ssh/id_rsa", "id_rsa*")]
    [InlineData(".ssh/id_rsa.pub", "id_rsa*")]
    [InlineData(".ssh/id_ed25519", "id_ed25519*")]
    [InlineData(".ssh/id_ecdsa", "id_ecdsa*")]
    [InlineData("CERTS/SERVER.PEM", "*.pem")]
    [InlineData("deploy\\.ENV", ".env*")]
    public void AKnownCredentialFile_IsRefused_NamingItsPattern(string path, string pattern)
    {
        CredentialFiles.LooksLikeOne(path).Should().BeTrue();
        CredentialFiles.WhichPattern(path).Should().Be(pattern);
    }

    /// <summary>
    /// The trial's false refusals: ordinary code whose NAME carries a redaction word is served.
    /// </summary>
    /// <remarks>
    /// Nine of 21 features had a file like these refused. The words are still applied — to the file's
    /// CONTENT — which is where a credential would be.
    /// </remarks>
    [Theory]
    [InlineData("providers/credentials.ts")]
    [InlineData("src/Auth.cs")]
    [InlineData("src/TokenIdentity.cs")]
    [InlineData("src/tokens.rs")]
    [InlineData("src/secrets/SecretStore.cs")]
    [InlineData("docs/environment.md")]
    [InlineData("src/keyboard.ts")]
    [InlineData("src/pemberton.rs")]
    [InlineData("src/rsa_keys.cs")]
    [InlineData("src/keyring.ts")]
    public void AnOrdinaryCodeFile_IsNotRefusedByItsName(string path)
    {
        CredentialFiles.LooksLikeOne(path).Should().BeFalse($"'{path}' is code, not a credential file");
        CredentialFiles.WhichPattern(path).Should().BeEmpty();
    }

    /// <summary>
    /// The pattern table is what the plan fixed, and every entry is a SHAPE anchored at one end —
    /// never a word found anywhere in a name, which is what refused nine ordinary code files in the trial.
    /// </summary>
    [Fact]
    public void ThePatterns_AreTheFixedShapes_AndNoneIsAWordFoundAnywhere()
    {
        CredentialFiles.Patterns.Should().BeEquivalentTo(
            [".env*", "*.pem", "*.key", "*.pfx", "*.p12", "id_rsa*", "id_ed25519*", "id_ecdsa*"]);
        CredentialFiles.Patterns.Should().OnlyContain(
            pattern => pattern.StartsWith('*') != pattern.EndsWith('*'),
            "a prefix or a suffix is a shape; `*word*` would be the redaction list applied to names again");
        foreach (var word in CredentialWords.Anywhere)
        {
            CredentialFiles.Patterns.Should().NotContain(
                pattern => pattern.Contains(word, StringComparison.OrdinalIgnoreCase),
                "'{0}' is a redaction word for CONTENT, and the trial refused Auth.cs and tokens.rs on words like it", word);
        }
    }

    // --------------------------------------------------------------------------------------------
    // Lock files and build output: the same list the diff already excludes, answered for one path.
    // --------------------------------------------------------------------------------------------

    /// <summary>One example per exclusion glob, derived from the glob — nested under two directories and at the root.</summary>
    public static TheoryData<string, string> AnExamplePerExclusion
    {
        get
        {
            var examples = new TheoryData<string, string>();
            foreach (var glob in DiffExclusions.Default)
            {
                var tail = glob["**/".Length..].Replace("/**", "/deep/file.txt", StringComparison.Ordinal)
                    .Replace("*", "x", StringComparison.Ordinal);
                examples.Add("a/b/" + tail, glob);
                examples.Add(tail, glob);
            }

            return examples;
        }
    }

    [Theory]
    [MemberData(nameof(AnExamplePerExclusion))]
    public void EachExclusionGlob_MatchesItsOwnExample(string path, string glob)
    {
        DiffExclusions.Excludes(path).Should().BeTrue($"'{path}' is what '{glob}' excludes from every diff");
        DiffExclusions.WhichExcludes(path).Should().Be(glob);
    }

    /// <summary>Every glob in the table starts with the one prefix the derivation above assumes — so a new shape fails here, loudly.</summary>
    [Fact]
    public void EveryExclusionGlob_IsRootedAtAnyDepth() =>
        DiffExclusions.Default.Should().OnlyContain(glob => glob.StartsWith("**/", StringComparison.Ordinal));

    /// <summary>A path component that merely CONTAINS an excluded name is not excluded — `bin` is not `binary`.</summary>
    [Theory]
    [InlineData("src/Cart.cs")]
    [InlineData("src/binary/Reader.cs")]
    [InlineData("src/output/Writer.cs")]
    [InlineData("src/distribution.ts")]
    [InlineData("docs/roadmap.md")]
    [InlineData("package.json")]
    [InlineData("src/node_modules_shim.ts")]
    public void AnOrdinaryPath_IsNotExcluded(string path)
    {
        DiffExclusions.Excludes(path).Should().BeFalse($"'{path}' is source");
        DiffExclusions.WhichExcludes(path).Should().BeEmpty();
    }

    /// <summary>Every exclusion matcher carries a match ceiling — derived from the table the product builds, never retyped.</summary>
    [Fact]
    public void EveryExclusionMatcher_HasAMatchCeiling()
    {
        DiffExclusions.Matchers.Select(m => m.Glob).Should().Equal(DiffExclusions.Default,
            "one matcher per glob, so a scan of the matchers is a scan of the whole table");

        DiffExclusions.Matchers.Should().OnlyContain(
            m => m.Matcher.MatchTimeout != System.Text.RegularExpressions.Regex.InfiniteMatchTimeout,
            "a backtracking pattern with no ceiling can be made to search for as long as a path is long");
    }

    /// <summary>
    /// An exclusion match that times out FAILS CLOSED: the path is withheld, and the refusal names the glob.
    /// </summary>
    /// <remarks>
    /// Reached through the matcher seam rather than by hoping a path finds the ceiling — a test that
    /// needed a real timeout would be asserting a performance figure. The tempting shape is to treat a
    /// match that gave up as "not excluded", and that is the one outcome that must never happen: these
    /// globs decide what a reviewer is NOT shown.
    /// </remarks>
    [Fact]
    public void AnExclusionMatchThatTimesOut_WithholdsThePath()
    {
        Func<string, bool> timesOut = _ => throw new System.Text.RegularExpressions.RegexMatchTimeoutException(
            "a path shaped to make the engine search", "a glob", TimeSpan.Zero);
        Func<string, bool> never = _ => false;

        DiffExclusions.FirstExcluding("src/Cart.cs", [("**/never/**", never), ("**/slow/**", timesOut)])
            .Should().Be("**/slow/**", "a match that could not finish does not know the path is safe to show");
        DiffExclusions.FirstExcluding("src/Cart.cs", [("**/never/**", never)])
            .Should().BeEmpty("the seam itself excludes nothing a matcher did not");
    }

    // --------------------------------------------------------------------------------------------
    // File CONTENT goes through the notices redaction — layout kept, nothing cut, fail closed.
    // --------------------------------------------------------------------------------------------

    [Fact]
    public void AKeyShapedStringInsideSource_IsRedacted_AndTheLayoutIsKept()
    {
        const string source = "const live = \"sk-abcdefghijklmnop1234\";\n\tconst next = 1;\r\nend\n";

        var safe = Redaction.SafeSource(source);

        safe.Should().Be("const live = \"sk-[redacted]\";\n\tconst next = 1;\r\nend\n",
            "the vendor-key shape is taken out and every tab, LF and CR stays where it was");
    }

    [Fact]
    public void ALabelledSecretInsideSource_IsRedacted_OnItsOwnLineOnly()
    {
        const string source = "password = hunter2secret\nvalue = 42\n";

        Redaction.SafeSource(source).Should().Be("password = [redacted]\nvalue = 42\n");
    }

    /// <summary>A control byte that is not layout is dropped, as the notices contract drops it — and the size of a served file is bounded elsewhere, so nothing is cut.</summary>
    [Fact]
    public void SourceIsNeverCut_AndAStrayControlCharacterIsDropped()
    {
        var long_ = new string('a', Redaction.DetailLimit * 3) + "\u0007\n";

        var safe = Redaction.SafeSource(long_);

        safe.Should().Be(new string('a', Redaction.DetailLimit * 3) + "\n");
        safe.Should().NotContain(Redaction.Truncated);
    }

    [Fact]
    public void ASourceWhoseRedactionCannotFinish_IsNotServedAsItArrived() =>
        Redaction.WhenRedactionTimesOut(() => throw new System.Text.RegularExpressions.RegexMatchTimeoutException("x", "y", TimeSpan.Zero))
            .Should().Be(Redaction.Redacted, "text nobody can vouch for does not reach a reviewer either");
}
