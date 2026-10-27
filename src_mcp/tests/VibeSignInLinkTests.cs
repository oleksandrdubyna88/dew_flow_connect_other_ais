using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Vibe;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The maker of the ONE link an owned Vibe home may hold (DL2 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>; A1 spec §3.2; S-V3, S-V5,
/// S-V6, S-V14, S-V15): judged through a descriptor, names scanned, the buffer zeroed, the link made at a path that does not exist and read
/// back RAW — and a failure leaves NOTHING at the name and never a copy.
/// </summary>
/// <remarks>
/// The first half runs on every host over <see cref="SignInSteps"/> stand-ins (a dictionary for a filesystem), so a Windows machine that
/// cannot make a symbolic link proves the maker's decisions. The second half runs the REAL steps on a Linux file and skips elsewhere.
/// </remarks>
public sealed class VibeSignInLinkTests : IDisposable
{
    private const string Persons = "/home/p/.vibe/.env";

    private readonly TempDir _home = TempDir.For("testsToDelete-vsl-");

    public void Dispose() => _home.Dispose();

    private string Link => Path.Combine(_home, ".env");

    private static VibeSignIn SignIn => new(Persons);

    private SignInLink Make(FakeSignInDisk disk) => VibeSignInLink.Make(SignIn, _home, disk.Steps);

    [Fact]
    public void AnAdmittedFile_IsLinked_ByItsExactAbsoluteTarget_AndReadBack()
    {
        var disk = new FakeSignInDisk();

        Make(disk).Should().BeOfType<SignInLink.Linked>();

        disk.Links.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>(Link, Persons), "the link is the person's file by its exact spelling, never a copy");
    }

    [Fact]
    public void TheSameHomesSecondTurn_FindsItsLink_AndMakesNothing()
    {
        var disk = new FakeSignInDisk();
        Make(disk);

        Make(disk).Should().BeOfType<SignInLink.Linked>();

        disk.LinkCalls.Should().Be(1);
        disk.RemoveCalls.Should().Be(0);
    }

    /// <summary>
    /// The A1 spec §3.2 step 2 said the right link on a second turn is `Linked` with nothing touched. It is not safe: the person's file can
    /// change between turns (a variable appended, chmod 666, replaced by a FIFO) and the second turn would run un-judged. The target is
    /// judged again on EVERY turn; the link is not remade.
    /// </summary>
    [Theory]
    [InlineData("content", "VIBE_MCP_SERVERS=[]\nMISTRAL_API_KEY=sk-test-canary\n", "line 1")]
    [InlineData("content", "MISTRAL_API_KEY=sk-test-canary\nVIBE_X=1\n", "line 2")]
    [InlineData("unreadable", "writable by group or others (mode 666)", "chmod 600")]
    [InlineData("unreadable", "not a regular file (a FIFO)", "Vibe itself can read a FIFO sign-in")]
    [InlineData("unreadable", "a symbolic link, never followed", "never chains links")]
    public void TheSameHomesSecondTurn_JudgesTheTargetAgain_AndRefusesWhatChangedUnderIt(string how, string what, string said)
    {
        var disk = new FakeSignInDisk();
        Make(disk).Should().BeOfType<SignInLink.Linked>();
        disk.Judged = how == "content" ? new FileReadResult.Content(System.Text.Encoding.UTF8.GetBytes(what)) : new FileReadResult.Unreadable(what);

        var second = Make(disk);

        second.Should().BeOfType<SignInLink.Refused>("a second turn ran on a sign-in nobody judged: the person's file changed between the turns").Which.Sentence.Should().Contain(said);
        disk.LinkCalls.Should().Be(1, "the link is not remade");
        disk.RemoveCalls.Should().Be(0, "and not removed: it is the person's file that changed, not coai's link");
    }

    [Fact]
    public void TheSameHomesSecondTurn_AdmitsAKeyTheyRotated_WithoutRemakingTheLink_AndZeroesTheBuffer()
    {
        var disk = new FakeSignInDisk();
        Make(disk);
        disk.Judged = new FileReadResult.Content(System.Text.Encoding.UTF8.GetBytes("MISTRAL_API_KEY=sk-test-rotated\n"));

        Make(disk).Should().BeOfType<SignInLink.Linked>("legitimate key rotation stays admitted");

        disk.LinkCalls.Should().Be(1);
        disk.LastBuffer.Should().NotBeEmpty().And.OnlyContain(b => b == 0, "the second turn's buffer is zeroed too");
    }

    [Fact]
    public void TheSameHomesSecondTurn_WhenThePersonsFileIsGone_FallsThroughSignedOut()
    {
        var disk = new FakeSignInDisk();
        Make(disk);
        disk.Judged = new FileReadResult.Missing();

        Make(disk).Should().BeOfType<SignInLink.NoSignIn>();
    }

    /// <summary>S-V3, the maker's half: the shape check owns every other `.env`; one the maker meets is not its to delete.</summary>
    [Fact]
    public void ALinkThatIsAlreadyThere_ToAnotherTarget_IsRefused_AndNothingIsDeleted()
    {
        var disk = new FakeSignInDisk();
        disk.Links[Link] = "/elsewhere/.env";

        var made = Make(disk);

        made.Should().BeOfType<SignInLink.Refused>("a .env link to /elsewhere/.env should refuse the launch, but it was admitted");
        disk.RemoveCalls.Should().Be(0);
        disk.Links.Should().ContainKey(Link);
    }

    [Fact]
    public void AMissingSignIn_FallsThrough_NamingThePath_AndMakesNoLink()
    {
        var disk = new FakeSignInDisk { Judged = new FileReadResult.Missing() };

        var made = Make(disk);

        made.Should().BeOfType<SignInLink.NoSignIn>().Which.Reason.Should().Contain(Persons);
        made.Note.Should().Contain("vibe --setup").And.Contain("signed out");
        disk.LinkCalls.Should().Be(0);
    }

    /// <summary>S-V5: a refusal is the line and the rule — never the name found, never a byte of a value.</summary>
    [Fact]
    public void AFileNamingAnotherVariable_Refuses_ByLineNumberOnly()
    {
        var disk = new FakeSignInDisk { Judged = new FileReadResult.Content("VIBE_MCP_SERVERS=[]\nMISTRAL_API_KEY=sk-test-canary\n"u8.ToArray()) };

        var made = Make(disk);

        var sentence = made.Should().BeOfType<SignInLink.Refused>("a .env naming VIBE_MCP_SERVERS should refuse").Which.Sentence;
        sentence.Should().Contain("line 1").And.Contain(Persons);
        sentence.Should().NotContain("VIBE_MCP_SERVERS", "the refusal must not name VIBE_MCP_SERVERS — a found name may be value text").And.NotContain("sk-test", "the refusal must not contain the value 'sk-test-…'");
        disk.LinkCalls.Should().Be(0, "a refused file is never linked");
    }

    /// <summary>S-V6 and S-V14, on every host: what the descriptor judge answers, the sentence carries with the cure that reason has.</summary>
    [Theory]
    [InlineData("writable by group or others (mode 620)", "chmod 600")]
    [InlineData("a symbolic link, never followed", "never chains links")]
    [InlineData("not a regular file (a FIFO)", "Vibe itself can read a FIFO sign-in")]
    [InlineData("not a regular file (a socket)", "check the file's owner and type")]
    [InlineData("owned by uid 1000, not uid 1001", "check the file's owner and type")]
    [InlineData("larger than 65536 bytes", "keep only `MISTRAL_API_KEY=…`")]
    public void ATargetThatIsNotAFileCoaiLinks_IsRefused_WithTheCureItsReasonHas(string reason, string cure)
    {
        var disk = new FakeSignInDisk { Judged = new FileReadResult.Unreadable(reason) };

        var made = Make(disk);

        made.Should().BeOfType<SignInLink.Refused>().Which.Sentence.Should().Contain(reason).And.Contain(cure).And.Contain(Persons);
        disk.LinkCalls.Should().Be(0);
    }

    /// <summary>S-V15, the maker's half: nothing at the name, no copy, the reason carried.</summary>
    [Fact]
    public void AFailingLink_LeavesNothingAtTheHome_AndCarriesTheReason()
    {
        var disk = new FakeSignInDisk { LinkThrows = new IOException("Operation not supported") };

        var made = Make(disk);

        made.Should().BeOfType<SignInLink.NotMade>().Which.Reason.Should().Contain("Operation not supported");
        made.Note.Should().Contain("Operation not supported").And.Contain("never copies");
        disk.Links.Should().BeEmpty("a half-made .env was left in the home");
        Directory.GetFileSystemEntries(_home).Should().BeEmpty("a copy of .env was made as a fallback");
    }

    [Fact]
    public void ALinkThatReadsBackAsSomethingElse_IsRemoved_AndTheLaunchGoesOnWithout()
    {
        var disk = new FakeSignInDisk { ReadsBackAs = "/elsewhere" };

        var made = Make(disk);

        made.Should().BeOfType<SignInLink.NotMade>().Which.Reason.Should().NotContain("/elsewhere", "a link target is a path, but not one coai wrote");
        disk.Links.Should().BeEmpty();
        disk.RemoveCalls.Should().Be(1);
    }

    [Fact]
    public void AnUnremovableLeftover_RefusesTheLaunch_NamingTheHome_BecauseAnUnknownEnvIsNeverLaunchedOn()
    {
        var disk = new FakeSignInDisk { LinkThrows = new IOException("disk full"), RemoveThrows = new UnauthorizedAccessException("denied") };

        Make(disk).Should().BeOfType<SignInLink.Refused>().Which.Sentence.Should().Contain(_home.Path).And.Contain("could not remove");
    }

    [Fact]
    public void AnEmptySignIn_IsAProgrammingError_NotAnExpectedFailure()
    {
        var made = () => VibeSignInLink.Make(VibeSignIn.None, _home, new FakeSignInDisk().Steps);

        made.Should().Throw<ArgumentException>();
    }

    /// <summary>I4: no sentence the maker can produce holds a byte of any value — swept over every vector, every outcome.</summary>
    [Fact]
    public void NoSentence_HoldsAByteOfTheValue_OverEveryVector()
    {
        var canaries = DotenvVectors.All.SelectMany(v => v.MustNotEcho).Where(word => word.StartsWith("sk-test-", StringComparison.Ordinal)).Distinct().ToList();
        canaries.Should().NotBeEmpty("the sweep must have canaries to look for");

        foreach (var vector in DotenvVectors.All)
        {
            var disk = new FakeSignInDisk { Judged = new FileReadResult.Content([.. vector.Bytes]) };
            var made = Make(disk);

            var said = made switch
            {
                SignInLink.Refused refused => refused.Sentence,
                _ => made.Note,
            };
            canaries.Where(canary => said.Contains(canary, StringComparison.Ordinal)).Should().BeEmpty($"vector '{vector.Id}': a value reached a sentence");
        }
    }

    [Fact]
    public void TheBuffer_IsZeroedAfterTheScan_WhateverTheVerdict()
    {
        foreach (var text in new[] { "MISTRAL_API_KEY=sk-test-canary\n", "VIBE_X=sk-test-canary\n" })
        {
            var disk = new FakeSignInDisk { Judged = new FileReadResult.Content(System.Text.Encoding.UTF8.GetBytes(text)) };

            Make(disk);

            disk.LastBuffer.Should().NotBeEmpty().And.OnlyContain(b => b == 0, $"the file's bytes must not outlive the scan ({text.Split('=')[0]})");
        }
    }

    // ------------------------------------------------------------------------ the real steps, on a Linux file

    private static void OnLinux() => Assert.SkipUnless(OperatingSystem.IsLinux(), "the real steps judge a Linux file through a descriptor: runs on the Linux legs");

    private string PersonsFile(string text, UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite)
    {
        var path = Path.Combine(Directory.CreateDirectory(Path.Combine(_home, "person")).FullName, ".env");
        File.WriteAllText(path, text);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, mode);
        }

        return path;
    }

    private string OwnedHome() => Directory.CreateDirectory(Path.Combine(_home, "owned")).FullName;

    [Fact]
    public void RealSteps_MakeTheLinkAsTheFileItself_AndTheNextTurnFindsIt()
    {
        OnLinux();
        var persons = PersonsFile("MISTRAL_API_KEY=sk-test-canary\n");
        var signIn = new VibeSignIn(persons);
        var home = OwnedHome();

        VibeSignInLink.Make(signIn, home).Should().BeOfType<SignInLink.Linked>();

        new FileInfo(Path.Combine(home, ".env")).LinkTarget.Should().Be(persons, "the home's .env is a symlink to the person's file, by its absolute spelling");
        File.ReadAllText(persons).Should().Be("MISTRAL_API_KEY=sk-test-canary\n", "the person's file is untouched");
        VibeSignInLink.Make(signIn, home).Should().BeOfType<SignInLink.Linked>("the same home's second turn");
    }

    [Theory]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupWrite, "chmod 600")]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherWrite, "chmod 600")]
    public void RealSteps_RefuseAGroupWritableTarget_WithTheCure_AndMakeNoLink(UnixFileMode mode, string cure)
    {
        OnLinux();
        var home = OwnedHome();

        var made = VibeSignInLink.Make(new VibeSignIn(PersonsFile("MISTRAL_API_KEY=x\n", mode)), home);

        made.Should().BeOfType<SignInLink.Refused>().Which.Sentence.Should().Contain(cure);
        OwnedPaths.IsLinkIfThere(Path.Combine(home, ".env")).Should().BeFalse();
    }

    [Fact]
    public void RealSteps_RefuseATargetThatIsItselfALink_NeverChaining()
    {
        OnLinux();
        var real = PersonsFile("MISTRAL_API_KEY=x\n");
        var chained = Path.Combine(_home, "chained-env");
        File.CreateSymbolicLink(chained, real);
        var home = OwnedHome();

        VibeSignInLink.Make(new VibeSignIn(chained), home).Should().BeOfType<SignInLink.Refused>().Which.Sentence.Should().Contain("never chains links");
    }

    [Fact]
    public void RealSteps_FallThrough_WhenThePersonHasNoSignInFile()
    {
        OnLinux();

        VibeSignInLink.Make(new VibeSignIn(Path.Combine(_home, "no-such", ".env")), OwnedHome()).Should().BeOfType<SignInLink.NoSignIn>();
    }

    [Fact]
    public void RealSteps_AFileNamingAnotherVariable_IsRefused_AndNothingIsLinked()
    {
        OnLinux();
        var home = OwnedHome();

        var made = VibeSignInLink.Make(new VibeSignIn(PersonsFile("VIBE_MCP_SERVERS=[]\n")), home);

        made.Should().BeOfType<SignInLink.Refused>().Which.Sentence.Should().Contain("line 1").And.NotContain("VIBE_MCP_SERVERS");
        OwnedPaths.IsLinkIfThere(Path.Combine(home, ".env")).Should().BeFalse();
    }
}
