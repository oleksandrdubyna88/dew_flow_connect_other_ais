using CoaiMcp.Runners.Files;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The Windows rules of <see cref="PrivateFolder"/> (D7 of <c>todo/PLAN_vibe_live_round_fixes.md</c>) as pure functions of a DACL:
/// what a private folder's DACL is, whether a given one is that, who could use a folder that is not, and every refusal sentence.
/// Nothing here needs Windows — the facts are plain values — so these run on Linux too, which is where SonarCloud measures them.
/// </summary>
public sealed class PrivateAclTests
{
    private static readonly Sid Me = new("S-1-5-21-1111-2222-3333-1001");
    private static readonly Sid Everyone = new("S-1-1-0");
    private static readonly Sid Users = new("S-1-5-32-545");

    private const int Read = 0x1200A9;
    private const int Modify = 0x1301BF;

    /// <summary>The names the real shell would resolve; anything else reads as its own text.</summary>
    private static string NameOf(Sid sid) => sid.Value switch
    {
        "S-1-1-0" => "Everyone",
        "S-1-5-18" => @"NT AUTHORITY\SYSTEM",
        "S-1-5-32-545" => @"BUILTIN\Users",
        "" => "unknown",
        _ => sid.Value,
    };

    private static AceFact Ace(Sid sid, int mask = PrivateAcl.FullControl, bool allow = true, bool inherited = false, AceInheritance inheritance = PrivateAcl.Inheriting) =>
        new(sid, allow, inherited, mask, inheritance);

    private static AclFacts Facts(Sid owner, bool isProtected, params AceFact[] aces) => new(owner, isProtected, true, aces);

    private static AclFacts Ours() => PrivateAcl.Wanted(Me);

    [Fact]
    public void TheWantedDacl_IsExactlyThePersonSystemAndAdministrators_ProtectedFullControlAndInheriting()
    {
        var wanted = PrivateAcl.Wanted(Me);

        wanted.Protected.Should().BeTrue("inheritance from the parent is cut");
        wanted.Aces.Select(ace => ace.Sid).Should().Equal(Me, Sid.System, Sid.Administrators);
        wanted.Aces.Should().OnlyContain(ace => ace.Allow && !ace.Inherited && ace.Mask == 0x1F01FF && ace.Inheritance == (AceInheritance.ContainerInherit | AceInheritance.ObjectInherit));
        wanted.Owner.Should().Be(Sid.None, "the owner is not set: the creating token's default stands");
        PrivateAcl.IsExactlyOurs(wanted, Me).Should().BeTrue("what the folder is made with is what it is later judged against");
    }

    private static readonly Dictionary<string, AclFacts> NotOurs = new()
    {
        ["an extra Everyone entry"] = Facts(Sid.None, true, [.. Ours().Aces, Ace(Everyone, Read)]),
        ["only two of the three"] = Facts(Sid.None, true, Ace(Me), Ace(Sid.System)),
        ["not protected"] = Facts(Sid.None, false, [.. Ours().Aces]),
        ["an inherited entry"] = Facts(Sid.None, true, Ace(Me), Ace(Sid.System), Ace(Sid.Administrators, inherited: true)),
        ["a Deny entry in place of an Allow"] = Facts(Sid.None, true, Ace(Me), Ace(Sid.System), Ace(Sid.Administrators, allow: false)),
        ["a mask short of full control"] = Facts(Sid.None, true, Ace(Me), Ace(Sid.System), Ace(Sid.Administrators, Modify)),
        ["an entry that does not reach the files"] = Facts(Sid.None, true, Ace(Me), Ace(Sid.System), Ace(Sid.Administrators, inheritance: AceInheritance.ContainerInherit)),
        ["an inherit-only entry"] = Facts(Sid.None, true, Ace(Me), Ace(Sid.System), Ace(Sid.Administrators, inheritance: PrivateAcl.Inheriting | AceInheritance.InheritOnly)),
        ["the person twice, Administrators never"] = Facts(Sid.None, true, Ace(Me), Ace(Sid.System), Ace(Me)),
        ["another person in place of the person"] = Facts(Sid.None, true, Ace(new Sid("S-1-5-21-9-9-9-1002")), Ace(Sid.System), Ace(Sid.Administrators)),
    };

    public static TheoryData<string> NotExactlyOurs() => [.. NotOurs.Keys];

    [Theory]
    [MemberData(nameof(NotExactlyOurs))]
    public void ADaclThatIsNotExactlyTheThree_IsNotOurs(string why)
    {
        PrivateAcl.IsExactlyOurs(NotOurs[why], Me).Should().BeFalse(why);
    }

    [Fact]
    public void TheThreeEntries_AreOursInAnyOrder()
    {
        PrivateAcl.IsExactlyOurs(Facts(Sid.None, true, Ace(Sid.Administrators), Ace(Me), Ace(Sid.System)), Me).Should().BeTrue();
    }

    [Theory]
    [InlineData("S-1-5-21-1111-2222-3333-1001", true)]
    [InlineData("S-1-5-32-544", true)]
    [InlineData("S-1-5-18", false)]
    [InlineData("S-1-1-0", false)]
    [InlineData("", false)]
    public void TheOwnerRule_AdmitsThePersonAndAdministrators_AndNoOneElse(string owner, bool ours)
    {
        PrivateAcl.OwnerIsOurs(Facts(new Sid(owner), true), Me).Should().Be(ours);
    }

    [Fact]
    public void AChildOwnedBySomeoneElse_IsRefusedByName_AndThePersonAndAdministratorsAreNot()
    {
        const string child = @"C:\homes\coai-vibe\id\h\transcript.jsonl";

        PrivateAcl.OwnerProblem(child, Facts(Sid.System, false), Me, NameOf)
            .Should().Contain(child).And.Contain(@"NT AUTHORITY\SYSTEM").And.Contain("WRITE_DAC").And.Contain("COAI_CONSULTANT_HOMES");
        PrivateAcl.OwnerProblem("x", Facts(Everyone, false), Me, NameOf).Should().NotBeEmpty();
        PrivateAcl.OwnerProblem("x", Facts(Me, false), Me, NameOf).Should().BeEmpty();
        PrivateAcl.OwnerProblem("x", Facts(Sid.Administrators, false), Me, NameOf).Should().BeEmpty("an elevated session owns what it makes as Administrators");
    }

    [Fact]
    public void ADescendantNeedsAResetWhenItIsProtectedOrHoldsAnExplicitEntry_AndOnlyThen()
    {
        PrivateAcl.NeedsReset(Facts(Me, true, Ace(Me, inherited: true))).Should().BeTrue("protected");
        PrivateAcl.NeedsReset(Facts(Me, false, Ace(Everyone, Read))).Should().BeTrue("an explicit entry");
        PrivateAcl.NeedsReset(Facts(Me, false, Ace(Me, inherited: true), Ace(Everyone, Read, inherited: true))).Should().BeFalse("everything inherited: propagation already replaced it");
        PrivateAcl.NeedsReset(Facts(Me, false)).Should().BeFalse();
    }

    [Fact]
    public void AResetDescendant_InheritsExactlyTheThree_OrItDoesNot()
    {
        AceFact[] three = [Ace(Me, inherited: true), Ace(Sid.System, inherited: true), Ace(Sid.Administrators, inherited: true)];

        PrivateAcl.Inherited(Facts(Me, false, three), Me).Should().BeTrue();
        PrivateAcl.Inherited(Facts(Me, true, three), Me).Should().BeFalse("still protected");
        PrivateAcl.Inherited(Facts(Me, false, [.. three, Ace(Everyone, Read, inherited: true)]), Me).Should().BeFalse("a fourth SID came down");
        PrivateAcl.Inherited(Facts(Me, false, [.. three[..2], Ace(Sid.Administrators)]), Me).Should().BeFalse("an explicit entry stayed");
        PrivateAcl.Inherited(Facts(Me, false, three[..2]), Me).Should().BeFalse("one of the three is missing");
    }

    [Fact]
    public void Exposure_NamesWhoCouldReadOrModify_AndGroupsOneNameOnce()
    {
        var facts = Facts(Me, false, [.. Ours().Aces, Ace(Everyone, Read), Ace(Users, Read), Ace(Users, Modify)]);

        var said = PrivateAcl.Exposure(facts, Me, NameOf);

        said.Should().Be(@"Everyone could read it; BUILTIN\Users could modify it");
    }

    [Fact]
    public void Exposure_IgnoresDenyEntriesAndTheThree_AndSaysSoWhenNobodyElseIsListed()
    {
        PrivateAcl.Exposure(Facts(Me, false, Ace(Me), Ace(Everyone, Read, allow: false)), Me, NameOf)
            .Should().Contain("nobody else is listed");
    }

    [Fact]
    public void Exposure_OfANullDacl_SaysEveryoneCouldModifyIt()
    {
        PrivateAcl.Exposure(new AclFacts(Me, false, false, []), Me, NameOf).Should().Contain("no access list").And.Contain("modify");
    }

    [Fact]
    public void EveryRefusalSentence_NamesThePathAndTheSettingThatCuresIt()
    {
        const string path = @"C:\homes\coai-vh";
        string[] sentences =
        [
            PrivateAcl.NotTheFolder(path),
            PrivateAcl.ForeignOwner(path, @"NT AUTHORITY\SYSTEM", "Everyone could read it"),
            PrivateAcl.NotMadePrivate(path, "Everyone could read it"),
            PrivateAcl.ChildCouldNotBeMade(path, "access denied"),
            PrivateAcl.NotOpened(path, 5, isLink: true),
            PrivateAcl.NotOpened(path, PrivateAcl.SharingViolation, isLink: false),
            PrivateAcl.NotOpened(path, 5, isLink: false),
        ];

        sentences.Should().OnlyContain(said => said.Contains(path, StringComparison.Ordinal) && said.Contains("COAI_CONSULTANT_HOMES", StringComparison.Ordinal));
        PrivateAcl.ForeignOwner(path, @"NT AUTHORITY\SYSTEM", "Everyone could read it").Should().Contain(@"owner NT AUTHORITY\SYSTEM").And.Contain("Everyone could read it");
        PrivateAcl.MadePrivate(path, "Everyone could read it").Should().Be($"made {path} private; Everyone could read it until now");
    }

    [Fact]
    public void AnOpenThatFailed_SaysWhyAccordingToWhatItWas()
    {
        const string path = @"C:\homes\coai-vh";

        PrivateAcl.NotOpened(path, 5, isLink: true).Should().Contain("is a link").And.Contain("error 5");
        PrivateAcl.NotOpened(path, 32, isLink: false).Should().Contain("open elsewhere").And.Contain("renamed or deleted");
        PrivateAcl.NotOpened(path, 5, isLink: false).Should().Contain("could not be opened").And.Contain("error 5").And.NotContain("is a link");
    }
}
