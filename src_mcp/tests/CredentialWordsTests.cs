using System.Text;
using System.Text.Json;
using CoaiMcp.Core.Notices;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The server's half of the ONE list of words that mean a string is carrying a credential.
/// </summary>
/// <remarks>
/// <para><b>Why the server has a copy at all.</b> <c>server-notices.jsonl</c> is written by this
/// binary and read by the extension, and both halves redact before anything reaches disk. The words
/// they redact ON must be the same words: two redactors disagreeing about whether <c>sig</c> names a
/// credential is not a test failure anywhere — it is a secret in a file on one path and not the
/// other.</para>
/// <para><b>Why it is EMBEDDED and not read from <c>shared/</c>.</b> Two reviewers found this
/// independently on the plan round: a published Native-AOT binary runs where no <c>shared/</c>
/// directory exists, the read throws, and because every notice write is best-effort and swallows
/// its exceptions the redaction would then run with an EMPTY LIST and put raw credentials into the
/// file. So the JSON is embedded at build time, exactly as <c>shared/builtin-roles.json</c> already
/// is, and the loader FAILS CLOSED.</para>
/// </remarks>
public sealed class CredentialWordsTests
{
    /// <summary>
    /// The file both halves are generated from — through <see cref="SharedFixtures"/>, which every
    /// suite that reads <c>shared/</c> now uses instead of its own copy of the same directory walk.
    /// </summary>
    /// <remarks>
    /// Read from the REPOSITORY rather than from the embedded copy on purpose: comparing the
    /// embedded resource against itself would pass whatever it contained.
    /// </remarks>
    private static string SharedJson() => SharedFixtures.Text("credential-words.json");

    private static (string[] Anywhere, string[] WholePart) FromShared()
    {
        using var parsed = JsonDocument.Parse(SharedJson());

        return (
            [.. parsed.RootElement.GetProperty("anywhere").EnumerateArray().Select(e => e.GetString()!)],
            [.. parsed.RootElement.GetProperty("wholePart").EnumerateArray().Select(e => e.GetString()!)]);
    }

    [Fact]
    public void TheResourceIsEmbeddedInThisBuild_AndParsesIntoTheTwoListsTheFileHolds()
    {
        // WHAT THIS PROVES, said exactly, because the obvious name for it overclaims. The csproj
        // embeds `shared/credential-words.json` itself, so there is no SECOND copy on this side for
        // the first to drift from — comparing them is comparing the file with itself. What it does
        // prove is the failure that actually happens: the `EmbeddedResource` item missing or
        // renamed, so the published binary carries no list at all. Watched red before the item was
        // added, with the loader's own refusal sentence.
        //
        // The drift risk lives on the TypeScript side, where the module IS generated and can go
        // stale; `generate-credential-words.mjs --check` is the guard there.
        var (anywhere, wholePart) = FromShared();

        CredentialWords.Anywhere.Should().Equal(anywhere,
            "the resource this binary redacts with is not what shared/credential-words.json holds");
        CredentialWords.WholePart.Should().Equal(wholePart);
    }

    [Fact]
    public void TheTwoListsStayTwo_BecauseAUnionBringsBackMonkey()
    {
        // The defect that earned the distinction: matching `key`, `auth` and `sig` as substrings
        // made every one of these a credential, so a legitimate endpoint URL was refused and a
        // diagnostic parameter carrying nothing was redacted.
        foreach (var ordinary in new[]
                 { "author", "authors", "design", "assignee", "signal", "monkey", "keyboard-layout" })
        {
            CredentialWords.NamesACredential(ordinary).Should().BeFalse(ordinary);
        }
    }

    [Fact]
    public void TheSameShortWords_AreCredentialsWhenTheyAreAWholePart()
    {
        foreach (var named in new[] { "key", "api_key", "apiKey", "X-Api-Key", "API-KEY", "auth", "x-auth", "sig", "Sig" })
        {
            CredentialWords.NamesACredential(named).Should().BeTrue(named);
        }
    }

    [Fact]
    public void ACamelCaseNameIsSplitOnItsORIGINALCasing_WhichIsTheOnlyThingThatSeesTheBoundary()
    {
        // These are the cases the row above CANNOT see. `apiKey` lowers into `apikey`, which the
        // ANYWHERE list catches for an entirely different reason — so a split that lowered before
        // it cut would leave every camelCase case in that row green while this rule was broken.
        // Each of these has a whole part of `auth`, `key` or `sig` that ONLY exists before the
        // lowering, and none of them contains a word from the ANYWHERE list.
        foreach (var named in new[] { "xAuth", "requestSig", "myKey", "primaryKeyName" })
        {
            CredentialWords.NamesACredential(named).Should().BeTrue(named);
        }

        // And the other direction, so the boundary is not simply "split everywhere": these have no
        // such part and must stay ordinary.
        foreach (var ordinary in new[] { "xauthor", "designSignal", "monkeyBars" })
        {
            CredentialWords.NamesACredential(ordinary).Should().BeFalse(ordinary);
        }
    }

    [Fact]
    public void ARunTogetherSpelling_IsCaughtByTheCompoundRatherThanByLooseningTheRule()
    {
        // `apikey` has no boundary for `key` to sit on, so the whole-part rule cannot see it. That
        // cost is paid by naming the compound, not by loosening until `monkey` comes back.
        foreach (var named in new[] { "apikey", "myapikey", "accesskey", "privatekey" })
        {
            CredentialWords.NamesACredential(named).Should().BeTrue(named);
        }
    }

    [Fact]
    public void TheUnambiguousWords_MatchAnywhere()
    {
        foreach (var named in new[]
                 { "token", "refresh_token", "client_secret", "password", "passwd", "Authorization", "signature", "credential" })
        {
            CredentialWords.NamesACredential(named).Should().BeTrue(named);
        }
    }

    [Fact]
    public void AnOrdinaryParameterName_IsNotRedactedIntoNoise()
    {
        // A redaction that fires on `?api-version=2024-02-01` teaches people the mechanism is
        // noise, which is how a real one gets ignored.
        foreach (var ordinary in new[] { "api-version", "deployment", "page", "sort", "region" })
        {
            CredentialWords.NamesACredential(ordinary).Should().BeFalse(ordinary);
        }
    }

    /// <summary>Every row of the corpus, as values — read once and shared by the two tests below.</summary>
    private static IReadOnlyList<(string Name, bool Credential)> CorpusRows()
    {
        using var parsed = JsonDocument.Parse(SharedJson());

        return [.. parsed.RootElement.GetProperty("cases").EnumerateArray()
            .Select(one => (one.GetProperty("name").GetString()!, one.GetProperty("credential").GetBoolean()))];
    }

    public static TheoryData<string, bool> TheSharedCorpus()
    {
        var data = new TheoryData<string, bool>();
        foreach (var (name, credential) in CorpusRows())
        {
            data.Add(name, credential);
        }

        return data;
    }

    [Fact]
    public void TheSharedCorpus_StillCarriesTheCaseBothHalvesGotWrongFirst()
    {
        // A COMPANION to the theory, because a scan that matches nothing passes for ever. A corpus
        // reduced to its easy rows would leave the theory green while covering none of the boundary
        // the two splitters actually disagreed on.
        var cases = CorpusRows();

        cases.Should().HaveCountGreaterThanOrEqualTo(40, "the shared corpus has shrunk");
        cases.Should().Contain(("xAuth", true),
            "xAuth is the case only the whole-part rule on the ORIGINAL casing can answer, and it is "
            + "the one this story's first draft got wrong while every other case stayed green");
    }

    [Theory]
    [MemberData(nameof(TheSharedCorpus))]
    public void TheSharedCorpus_IsAnsweredTheSameWayHereAsInTheExtension(string name, bool credential)
    {
        // THE FINDING OF THIS STORY'S PLAN ROUND, and the one that mattered most. Until this, each
        // half was checked against its OWN hand-written table — so a C# splitter that disagreed
        // with the TypeScript one about `requestSig`, `auth-key` or `token2` would leave both
        // suites green while the extension and the server redacted different notices. That is a
        // secret on disk on one path and not the other, and no test anywhere would have said so.
        //
        // `shared/credential-words.json` carries the corpus and `credentialWords.test.ts` asserts
        // the same rows. Neither side owns it, and a case added to it has to be answered twice.
        CredentialWords.NamesACredential(name).Should().Be(credential,
            $"shared/credential-words.json says \"{name}\" is {(credential ? "" : "not ")}a credential, "
            + "and the extension is held to the same row");
    }

    [Fact]
    public void TheListIsLoadedWhenItIsASKEDFor_SoAStartupCanRefuseBeforeAnySecretArrives()
    {
        // Two reviewers worried that the fail-closed throw could be reached first from INSIDE a
        // best-effort write and swallowed there. It cannot be swallowed by that path — the writer's
        // catch names `IOException` and `UnauthorizedAccessException` and this is neither — but the
        // better answer is not to depend on the shape of a catch written in a later story. So the
        // load has an explicit trigger, and the writer's construction calls it: the refusal then
        // happens where a person is watching, not at the moment the first secret arrives.
        var loading = CredentialWords.EnsureLoaded;

        loading.Should().NotThrow("this build embeds the list, so the trigger is a no-op here");
        CredentialWords.Anywhere.Should().NotBeEmpty();
    }

    [Fact]
    public void TheResourceIsNamedExactlyWhatTheLoaderAsksFor()
    {
        // A LogicalName typo in the csproj is the failure that actually ships: the build succeeds,
        // the file is embedded under a name nothing looks for, and the refusal only appears at run
        // time. Asserted against the assembly's own manifest rather than against the constant.
        typeof(CoaiMcp.Core.Rounds.RoleCatalog).Assembly.GetManifestResourceNames()
            .Should().Contain("CoaiMcp.Core.credential-words.json",
                "the embedded name is what CredentialWords asks the assembly for, and a typo in "
                + "either one is only visible at run time");
    }

    [Theory]
    [InlineData(null, "it is not embedded in this build")]
    [InlineData("", "it parsed to nothing")]
    [InlineData("{ this is not json", "it parsed to nothing")]
    [InlineData("""{"anywhere":[],"wholePart":["key"]}""", "carries no words")]
    [InlineData("""{"anywhere":["token"],"wholePart":[]}""", "carries no words")]
    public void AListThatIsAbsentOrEmpty_FailsCLOSED(string? json, string saying)
    {
        // THE FINDING OF THE PLAN ROUND, as a test. The tempting shape is a loader that returns an
        // empty list when the resource is missing, so that "nothing breaks" — and what that produces
        // is a redactor that redacts NOTHING, on the published binary, silently, for ever. A
        // security measure that degrades to nothing on the one machine that matters is worse than
        // not having it, so this throws where the role catalog throws: a broken BUILD.
        var reading = () => CredentialWords.From(json is null ? null : new MemoryStream(Encoding.UTF8.GetBytes(json)));

        reading.Should().Throw<InvalidOperationException>().WithMessage($"*{saying}*");
    }
}
