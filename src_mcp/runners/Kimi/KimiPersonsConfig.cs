using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Kimi;

/// <summary>The person's Kimi Code OAuth slot as their own <c>config.toml</c> names it — the five values coai needs and nothing else.</summary>
/// <param name="Key">The slot's NAME (<c>oauth/kimi-code-env-x</c>), which names the credentials file — never a token.</param>
public sealed record KimiOauthSlot(string Type, string BaseUrl, string Storage, string Key, string OauthHost);

/// <summary>What reading the slot came to: the slot, or the sentence that says why not — a closed union, the <see cref="Files.Hold"/> shape.</summary>
public abstract record SlotRead
{
    private SlotRead()
    {
    }

    public sealed record Found(KimiOauthSlot Slot) : SlotRead;

    public sealed record Refused(string Reason) : SlotRead;
}

/// <summary>
/// The person's OAuth slot, READ from their <c>config.toml</c> and never copied (D19 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>;
/// E4.2 brief section 5).
/// </summary>
/// <remarks>
/// <para>Only the managed provider's two tables are read — <c>type</c> and <c>base_url</c> of <c>[providers."managed:kimi-code"]</c>,
/// <c>storage</c>, <c>key</c> and <c>oauth_host</c> of its <c>.oauth</c> table — through <see cref="CodexConfigFile"/>'s table
/// reader, which slices the text to ONE table before it looks for a key. The <c>moonshot-ai</c> row's <c>api_key</c>, the
/// <c>[[hooks]]</c> and the <c>[services.*]</c> never leave the file, and a malformed line elsewhere in it is never parsed, so
/// there is no parse error to quote it (decision 12: a canary in the person's config appears in nothing coai writes).</para>
/// <para>Two refusals D19 did not spell out (deviation 12): the provider must be <c>type = "kimi"</c> — the measured bytes — and the
/// slot <c>storage = "file"</c> — a slot stored anywhere but in files has no credentials folder to junction.</para>
/// </remarks>
public static class KimiPersonsConfig
{
    private const string Provider = "[providers.\"managed:kimi-code\"]";

    private const string Oauth = "[providers.\"managed:kimi-code\".oauth]";

    /// <summary>The slot in <paramref name="text"/>, or why it is not usable; <paramref name="describedAs"/> is how the sentence names the file.</summary>
    public static SlotRead Read(string text, string describedAs)
    {
        var slot = new KimiOauthSlot(
            CodexConfigFile.TableValue(text, Provider, "type"),
            CodexConfigFile.TableValue(text, Provider, "base_url"),
            CodexConfigFile.TableValue(text, Oauth, "storage"),
            CodexConfigFile.TableValue(text, Oauth, "key"),
            CodexConfigFile.TableValue(text, Oauth, "oauth_host"));

        return SignedIn(text, slot) ? Measured(slot) : new SlotRead.Refused(NotSignedIn(describedAs));
    }

    /// <summary>The slot in the file at <paramref name="path"/>; a file that is missing or cannot be read is "not signed in".</summary>
    public static SlotRead ReadFile(string path)
    {
        try
        {
            return Read(File.Exists(path) ? File.ReadAllText(path) : string.Empty, path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new SlotRead.Refused(NotSignedIn(path));
        }
    }

    /// <summary>Both tables there and the three values a launch cannot do without all set.</summary>
    private static bool SignedIn(string text, KimiOauthSlot slot) =>
        CodexConfigFile.HasTable(text, Provider) && CodexConfigFile.HasTable(text, Oauth)
        && new[] { slot.Key, slot.OauthHost, slot.BaseUrl }.All(value => value.Length > 0);

    private static SlotRead Measured(KimiOauthSlot slot) => slot switch
    {
        { Storage: not "file" } => new SlotRead.Refused(
            $"Kimi's sign-in on this machine is stored as '{(slot.Storage.Length > 0 ? slot.Storage : "(unset)")}', not as files (storage = \"file\") — coai links the sign-in's credential files into its own home, and there is no credentials folder to link (D19)"),
        { Type: not "kimi" } => new SlotRead.Refused(
            $"the managed:kimi-code provider is type '{(slot.Type.Length > 0 ? slot.Type : "(unset)")}', not 'kimi' — coai writes type = \"kimi\" into the owned config, so it would run Kimi's protocol against a service you set up for another kind of provider (D19)"),
        _ => new SlotRead.Found(slot),
    };

    private static string NotSignedIn(string describedAs) =>
        $"not signed in to Kimi Code — {describedAs} names no managed:kimi-code provider with an OAuth slot; run `kimi login` in a terminal and try again";
}
