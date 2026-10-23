using CoaiMcp.Runners.Kimi;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The person's OAuth slot, READ from their own <c>config.toml</c> and never copied (E4.2 of
/// <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>, S2.1–S2.3): only the managed provider's own two tables are read — the
/// <c>moonshot-ai</c> row's <c>api_key</c>, the hooks and the services never leave the file.
/// </summary>
public sealed class KimiPersonsConfigTests
{
    [Fact]
    public void TheSlot_IsReadFromThePersonsConfig_AndNothingElseLeavesIt()
    {
        var read = KimiPersonsConfig.Read(KimiFixture.OwnerConfig(), "your config.toml");

        var found = read.Should().BeOfType<SlotRead.Found>(read is SlotRead.Refused refused ? $"the owner's own shape must be read, got: '{refused.Reason}'" : string.Empty).Subject;
        found.Slot.Should().Be(new KimiOauthSlot("kimi", KimiFixture.BaseUrl, "file", KimiFixture.SlotKey, KimiFixture.OauthHost));
        found.Slot.ToString().Should().NotContain(KimiFixture.ApiKeyCanary, "the moonshot-ai row's api_key never leaves the file")
            .And.NotContain("a-hook-the-person-wrote");
    }

    public static TheoryData<string, string> NotSignedIn => new()
    {
        { "no managed provider", "default_model = \"x\"\n[providers.\"moonshot-ai\"]\ntype = \"kimi\"\nbase_url = \"https://x\"\n" },
        { "no oauth table", "[providers.\"managed:kimi-code\"]\ntype = \"kimi\"\nbase_url = \"https://x\"\n" },
        { "an empty key", KimiFixture.OwnerConfig().Replace($"key = \"{KimiFixture.SlotKey}\"", "key = \"\"") },
    };

    [Theory]
    [MemberData(nameof(NotSignedIn))]
    public void NoManagedProvider_NoOauth_OrAnEmptyKey_IsNotSignedIn(string row, string text)
    {
        var read = KimiPersonsConfig.Read(text, "your config.toml");

        read.Should().BeOfType<SlotRead.Refused>(row).Which.Reason.Should().Contain("not signed in to Kimi Code").And.Contain("kimi login");
    }

    [Fact]
    public void AMissingFile_IsNotSignedIn()
    {
        var read = KimiPersonsConfig.ReadFile(Path.Combine(Path.GetTempPath(), "testsToDelete-kimi-nowhere", "config.toml"));

        read.Should().BeOfType<SlotRead.Refused>().Which.Reason.Should().Contain("not signed in to Kimi Code").And.Contain("kimi login");
    }

    [Fact]
    public void AKeyringStorage_IsRefusedBecauseThereAreNoFilesToLink()
    {
        var read = KimiPersonsConfig.Read(KimiFixture.OwnerConfig().Replace("storage = \"file\"", "storage = \"keyring\""), "your config.toml");

        read.Should().BeOfType<SlotRead.Refused>().Which.Reason.Should().Contain("'keyring'").And.Contain("storage = \"file\"").And.Contain("no credentials folder to link").And.NotContain("unmeasured", "the reason is that a keyring sign-in has no files to link (impossible), not that nobody measured it");
    }

    [Fact]
    public void AnotherProviderType_IsRefusedBecauseCoaiWouldRunKimisProtocolAgainstIt()
    {
        var text = KimiFixture.OwnerConfig().Replace("[providers.\"managed:kimi-code\"]\ntype = \"kimi\"", "[providers.\"managed:kimi-code\"]\ntype = \"openai\"");

        var read = KimiPersonsConfig.Read(text, "your config.toml");

        read.Should().BeOfType<SlotRead.Refused>().Which.Reason.Should().Contain("type 'openai'").And.Contain("not 'kimi'").And.Contain("coai writes type = \"kimi\" into the owned config").And.Contain("set up for another kind of provider").And.NotContain("unmeasured", "the reason is that coai would run Kimi's protocol against another provider's endpoint, not that nobody measured it");
    }

    [Fact]
    public void AFileOnDisk_IsRead()
    {
        using var person = new KimiFixture();

        KimiPersonsConfig.ReadFile(person.PersonsConfig).Should().BeOfType<SlotRead.Found>();
    }
}
