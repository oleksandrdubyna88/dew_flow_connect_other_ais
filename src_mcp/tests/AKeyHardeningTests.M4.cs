using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server.Keys;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>The part of the hardening tests (the security judgement of s7 part 1) that covers: M4.</summary>
public sealed partial class AKeyHardeningTests
{
    // ---------- must 4: an account is an email, never a token ----------

    [Theory]
    [InlineData("token")]
    [InlineData("plain-name")]
    public void M4_AnAccountThatIsNotAnEmail_IsRefusedByAdd_AndStoredNowhere(string kind)
    {
        using var world = new KeyWorld();
        var account = kind == "token" ? KeyWorld.Token("acct") : "plain-name";

        var added = world.Admin.Add(new KeyInput(1, "one", KeyWorld.Token("a"), account, KeySources.Local));

        added.Ok.Should().BeFalse();
        added.Refusal.Should().NotContain(KeyWorld.Sentinel);
        world.Admin.List(false).Value!.Rows.Should().BeEmpty("a refused add leaves nothing");
        AllText(world.DataDir).Should().NotContain(KeyWorld.Sentinel);
    }

    [Fact]
    public void M4_ATokenInTheAccount_IsRefusedByEverySource_AndIsNowhereOnDiskOrInAnAnswer()
    {
        using var world = new KeyWorld();
        var token = KeyWorld.Token("a");
        var asAccount = KeyWorld.Token("acct");
        var run = new KeysRun(world);
        var seen = new StringBuilder();

        void Take((int Code, string Out, string Err) result) => seen.AppendLine(result.Out).AppendLine(result.Err);

        Take(run.Do($"{{\"label\":\"x\",\"token\":\"{token}\",\"account\":\"{asAccount}\"}}\n", "import", "--json"));
        Take(run.Do(token + "\n", "add", "--label", "y", "--account", asAccount));
        Take(run.Do($"COAI_CLAUDE_KEY_z={token}\nCOAI_CLAUDE_ACCOUNT_z={asAccount}\n", "import", "--format", "env", "--json"));
        Take(run.Do($"{{\"version\":1,\"keys\":[{{\"label\":\"w\",\"account\":\"{asAccount}\",\"token\":\"{token}\"}}]}}", "import", "--format", "creds-json", "--json"));
        run.Variables["COAI_CLAUDE_KEY_v"] = token;
        run.Variables["COAI_CLAUDE_ACCOUNT_v"] = asAccount;
        Take(run.Do(string.Empty, "sync", "--json"));
        Take(run.Do(string.Empty, "list", "--json"));

        run.List().GetProperty("rows").GetArrayLength().Should().Be(0, "no source took a key with a token for its account");
        seen.ToString().Should().NotContain(KeyWorld.Sentinel + "acct", "no answer repeats the account");
        AllText(world.DataDir).Should().NotContain(KeyWorld.Sentinel + "acct");
    }

    // ---------- the answers a sentence of a verb may print ----------

    [Fact]
    public void M4_NoAnswerOfAnyVerb_PrintsTheAccountItWasGiven()
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world);
        var account = "someone@example.com";

        var added = run.Do(KeyWorld.Token("a") + "\n", "add", "--label", "one", "--account", account, "--json");

        added.Code.Should().Be(0);
        added.Out.Should().NotContain(account);
        JsonDocument.Parse(run.Do(string.Empty, "list", "--json").Out).RootElement.GetProperty("rows")[0].GetProperty("account").GetString().Should().Be(account, "the list shows the account: it is an email, which the check proved is not a token");
    }
}
