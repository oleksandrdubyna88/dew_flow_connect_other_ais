using System.Reflection;
using System.Text;
using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// The administrators, as read from one environment variable and compared in fixed time.
/// </summary>
/// <remarks>
/// <para>Two properties are load-bearing and neither is visible from the routes: what the variable
/// PARSES to, and that a comparison leaks nothing about the credential presented. The first is
/// ordinary input handling. The second cannot be asserted by timing a test — a wall clock on a
/// shared machine measures the machine — so it is asserted on the SHAPE of the code that makes the
/// guarantee: an array walked end to end with no early exit. That is a structural test, and it
/// names the whole condition rather than a fragment of it, so it cannot survive its own break.</para>
/// <para>What is deliberately NOT claimed: that the time is independent of how many administrators
/// are configured. N comparisons cost O(N), the count is in the operator's own secret store, and
/// nothing here pretends to hide it. The guarantee is about the key PRESENTED.</para>
/// </remarks>
public sealed class TheAdminKeysTests
{
    private const string Secret = "a-server-secret";

    /// <summary>No variable is no administrators, and that is a legitimate way to run the server.</summary>
    [Fact]
    public void AnAbsentVariableConfiguresNobody()
    {
        var admins = AdminKeys.Read(null, Secret);

        admins.None.Should().BeTrue();
        admins.Count.Should().Be(0);
        admins.Match("any-key-at-all", Secret).Should().BeOfType<AdminKeys.Presented.Unknown>(
            "an admin API with no administrators refuses everybody rather than failing to start");
    }

    /// <summary>A variable holding only comments is the same thing as an absent one.</summary>
    [Fact]
    public void AVariableOfOnlyCommentsConfiguresNobody()
    {
        AdminKeys.Read("# alice\n\n   \n# bob", Secret).None.Should().BeTrue();
    }

    /// <summary>
    /// A line per person, a comment saying whose it is, and neither the comment nor the blank lines
    /// become credentials.
    /// </summary>
    /// <remarks>
    /// The comment is the reason this format was chosen: a secret box holding four hex strings and
    /// nothing else is a secret box nobody dares edit. `\r\n` is here because a value pasted into a
    /// secret store from Windows carries them, and a carriage return left on the end of a key is a
    /// credential that silently never matches.
    /// </remarks>
    [Fact]
    public void BlankLinesAndCommentsAreNotCredentials()
    {
        var admins = AdminKeys.Read("# alice's key\r\nkey-one\r\n\r\n  key-two  \n# bob is away\n", Secret);

        admins.Count.Should().Be(2);
        admins.Match("key-one", Secret).Should().BeOfType<AdminKeys.Presented.Administrator>();
        admins.Match("key-two", Secret).Should().BeOfType<AdminKeys.Presented.Administrator>(
            "a key surrounded by spaces is the same key; the value came out of a text box");
        admins.Match("# alice's key", Secret).Should().BeOfType<AdminKeys.Presented.Unknown>();
        admins.Match(string.Empty, Secret).Should().BeOfType<AdminKeys.Presented.Unknown>();
    }

    /// <summary>The LAST configured key matches, which is what an early return would break.</summary>
    /// <remarks>
    /// A loop that accumulated into a variable but returned on the first comparison would pass every
    /// test that only ever presents the first key. This one presents the last of five.
    /// </remarks>
    [Fact]
    public void TheLastKeyInTheListIsFoundToo()
    {
        var admins = AdminKeys.Read("one\ntwo\nthree\nfour\nfive", Secret);

        admins.Count.Should().Be(5);
        admins.Match("five", Secret).Should().BeOfType<AdminKeys.Presented.Administrator>()
            .Which.Id.Should().Be(AdminId.Of(Corpus.HashOf("five", Secret)));
        admins.Match("one", Secret).Should().BeOfType<AdminKeys.Presented.Administrator>()
            .Which.Id.Should().Be(AdminId.Of(Corpus.HashOf("one", Secret)));
    }

    /// <summary>An unknown key is nobody, and says nothing about who is configured.</summary>
    [Fact]
    public void AKeyNobodyHoldsIsNobody()
    {
        AdminKeys.Read("one\ntwo", Secret)
            .Match("three", Secret)
            .Should().BeOfType<AdminKeys.Presented.Unknown>();
    }

    /// <summary>
    /// The server's secret is part of the comparison, so the variable's lines are not usable
    /// credentials on another server.
    /// </summary>
    /// <remarks>
    /// This is the test that proves the keys are HASHED rather than compared as text: a plaintext
    /// comparison would match whatever the secret was.
    /// </remarks>
    [Fact]
    public void TheSameKeyUnderADifferentSecretIsNobody()
    {
        var admins = AdminKeys.Read("one", Secret);

        admins.Match("one", Secret).Should().BeOfType<AdminKeys.Presented.Administrator>();
        admins.Match("one", "a-different-server-secret").Should().BeOfType<AdminKeys.Presented.Unknown>();
    }

    /// <summary>An administrator is named by its own key's hash, deterministically and with no table.</summary>
    [Fact]
    public void AnAdministratorIsNamedByItsOwnKey()
    {
        var admins = AdminKeys.Read("one\ntwo", Secret);

        var one = admins.Match("one", Secret).Should().BeOfType<AdminKeys.Presented.Administrator>().Subject;
        var two = admins.Match("two", Secret).Should().BeOfType<AdminKeys.Presented.Administrator>().Subject;

        one.Id.Value.Should().StartWith("admin-").And.HaveLength("admin-".Length + 8);
        one.Id.Should().NotBe(two.Id, "two administrators must not be one row in the audit");
        one.Id.Should().Be(
            AdminKeys.Read("one", Secret).Match("one", Secret)
                .Should().BeOfType<AdminKeys.Presented.Administrator>().Subject.Id,
            "the id is derived, so the same key is the same administrator on every start");
    }

    /// <summary>The only copy kept of an administrator's credential is one that cannot be reversed.</summary>
    /// <remarks>
    /// Read through reflection rather than trusted from the code, because "we hash it" is exactly
    /// the kind of claim a later refactor keeps in the comment and drops from the field. Every
    /// stored entry must be what <see cref="Corpus.HashOf"/> produces and none may be the key.
    /// </remarks>
    [Fact]
    public void NothingKeepsTheKeyItself()
    {
        var admins = AdminKeys.Read("alices-key\nbobs-key", Secret);

        var stored = admins.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(field => field.GetValue(admins))
            .OfType<byte[][]>()
            .Should().ContainSingle("the credentials live in exactly one field").Subject;

        stored.Should().HaveCount(2);
        foreach (var entry in stored)
        {
            var text = Encoding.UTF8.GetString(entry);
            text.Should().MatchRegex("^[0-9a-f]{64}$", "what is kept is the hash, not the key");
            text.Should().NotContain("alices-key").And.NotContain("bobs-key");
        }

        stored.Select(Encoding.UTF8.GetString).Should().BeEquivalentTo(
            [Corpus.HashOf("alices-key", Secret), Corpus.HashOf("bobs-key", Secret)]);
    }

    /// <summary>
    /// The comparison walks every configured hash, in fixed time, and leaves the loop only at its end.
    /// </summary>
    /// <remarks>
    /// <para>Structural on purpose: the property is about TIME, and a test that measured it would be
    /// measuring the machine it ran on. So it reads the source and pins the whole condition — the
    /// loop exists, it compares with <see cref="System.Security.Cryptography.CryptographicOperations.FixedTimeEquals"/>,
    /// and it contains no <c>return</c>, <c>break</c> or <c>continue</c>. If the method is rewritten
    /// into a different shape the anchors stop resolving and this goes red, which is correct: the
    /// new shape needs reading, not a test that quietly matches a fragment of it.</para>
    /// <para>The store is an array for the same reason. A set's <c>Contains</c> is a hash and a
    /// probe, and the bucket it reaches depends on the value, so its timing carries information
    /// about the credential presented.</para>
    /// </remarks>
    [Fact]
    public void TheComparisonWalksEveryHashAndLeavesOnlyAtTheEnd()
    {
        var source = File.ReadAllText(Path.Combine(Source(), "AdminKeys.cs"));

        const string loop = "foreach (var candidate in Probed)";
        const string after = "return found ? new Presented.Administrator(hash)";
        source.Should().Contain(loop, "the walk is what the guarantee is made of");
        source.Should().Contain(after, "the answer is carried out of the loop, not returned from inside it");
        // The field's DECLARED type is the pin, not the absence of the word "set" in the file: the
        // remarks in that file discuss `FrozenSet` at length to say why it is not one, so a text
        // search for it reads as a defect. A field declared `byte[][]` cannot be a set.
        source.Should().Contain(
            "private readonly byte[][] _hashes;",
            "an array, never a set: a set's probe is data-dependent and so is its timing");

        var body = source[source.IndexOf(loop, StringComparison.Ordinal)..source.IndexOf(after, StringComparison.Ordinal)];
        body.Should().Contain(
            "CryptographicOperations.FixedTimeEquals",
            "a byte-by-byte comparison that stops at the first difference leaks the prefix");

        // COMMENTS STRIPPED, because this must read the code and not the prose about it: the loop
        // body explains that it never returns from inside, and the word "returned" in that
        // sentence failed the assertion below. The same trap the `FrozenSet` check fell into.
        var code = string.Join(
            Environment.NewLine,
            body.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        code.Should().NotContain("return", "an early return makes the answer's timing say WHERE the key sat");
        code.Should().NotContain("break").And.NotContain("continue", "an early exit is the same defect");
        code.Should().NotContain("||", "a short-circuiting or would stop comparing at the first match");
    }

    /// <summary>
    /// A server with NO administrators does the same work as one with an administrator, so the
    /// difference cannot be timed.
    /// </summary>
    /// <remarks>
    /// <para>The gate answers an absent variable and a wrong credential with the same status and the
    /// same body, and a test compares those bytes. A code round found the half that bytes cannot
    /// show: <c>Match</c> returned early when nothing was configured, so it never computed the
    /// presented key's hash — a whole HMAC and hex formatting skipped — while a configured
    /// deployment paid for it on every attempt. Failed credentials are not rate-limited, so an
    /// attacker can average as many attempts as they like and read the difference. The identical
    /// body is then worth nothing.</para>
    /// <para>What is asserted is the WORK, not a duration: a wall clock on a shared machine measures
    /// the machine. With nothing configured the comparison list holds exactly one entry, so the path
    /// taken is the path of a deployment with one administrator and a wrong key. That is the
    /// guarantee the contract asks for — you learn nothing about whether administration is enabled
    /// — and it is all that can be given, because N configured keys cost N comparisons and the
    /// plan says the count is not a secret.</para>
    /// </remarks>
    [Fact]
    public void WithNoAdministratorsTheComparisonStillHappens()
    {
        var none = AdminKeys.Read(null, Secret);
        var one = AdminKeys.Read("the-only-administrator", Secret);

        none.Probed.Should().HaveCount(
            one.Probed.Count,
            "an empty configuration must walk the same number of entries as a single configured key, "
            + "or the two are told apart by how long the answer takes");
        none.Probed[0].Should().HaveCount(
            one.Probed[0].Length, "and compare the same number of bytes");
        none.Probed[0].Should().NotEqual(
            one.Probed[0], "the stand-in is not anybody's key, and no key can hash to it");
        none.Match("the-only-administrator", Secret).Should().BeOfType<AdminKeys.Presented.Unknown>(
            "doing the work must not make the answer yes");
    }

    /// <summary>The stand-in cannot be presented as a credential.</summary>
    /// <remarks>
    /// Matching it would need a key whose hash is that exact string, which is a preimage of
    /// SHA-256. Asserted anyway, because the stand-in is a constant somebody could one day replace
    /// with something derivable.
    /// </remarks>
    [Fact]
    public void TheStandInIsNotACredential()
    {
        var none = AdminKeys.Read("# nobody", Secret);
        var standIn = Encoding.UTF8.GetString(none.Probed[0]);

        none.Match(standIn, Secret).Should().BeOfType<AdminKeys.Presented.Unknown>();
        standIn.Should().NotBe(Corpus.HashOf(standIn, Secret), "it must not be its own hash either");
    }

    /// <summary>The server's source directory, found from the test binary.</summary>
    private static string Source()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src_bugs", "src");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("no src_bugs/src above the test binary");
    }
}
