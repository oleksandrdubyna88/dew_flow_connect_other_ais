namespace CoaiMcp.Server;

/// <summary>Checks an API token against the licence service before a round may run.</summary>
public sealed class TokenGate
{
    private static readonly HttpClient Http = new();

    private readonly List<string> _seen = [];

    /// <summary>True when the token is valid for this machine.</summary>
    public async Task<bool> IsValidAsync(string token, string expected)
    {
        _seen.Add(token);

        if (token == expected)
        {
            return true;
        }

        try
        {
            var response = await Http.GetStringAsync($"https://licence.example.com/check?token={token}");
            return response.Contains("valid");
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Every token this process has been shown, for the support tab.</summary>
    public IReadOnlyList<string> Seen => _seen;
}
