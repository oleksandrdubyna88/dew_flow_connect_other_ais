using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Vibe;

namespace CoaiMcp.Tests;

/// <summary>A filesystem in a dictionary: what was linked, and what was asked.</summary>
internal sealed class FakeSignInDisk
{
    public Dictionary<string, string> Links { get; } = [];

    public int LinkCalls { get; private set; }

    public int RemoveCalls { get; private set; }

    public FileReadResult Judged { get; set; } = new FileReadResult.Content("MISTRAL_API_KEY=sk-test-canary\n"u8.ToArray());

    public Exception? LinkThrows { get; set; }

    public Exception? RemoveThrows { get; set; }

    /// <summary>What a link reads back as, when it is not what was written.</summary>
    public string? ReadsBackAs { get; set; }

    /// <summary>The buffer the last judgement handed out — a COPY of <see cref="Judged"/> each time, as a descriptor read is: the launch control zeroes what it is given.</summary>
    public byte[] LastBuffer { get; private set; } = [];

    private FileReadResult Handed()
    {
        if (Judged is not FileReadResult.Content content)
        {
            return Judged;
        }

        LastBuffer = [.. content.Bytes];

        return new FileReadResult.Content(LastBuffer);
    }

    public SignInSteps Steps => new(
        _ => Handed(),
        (link, target) =>
        {
            LinkCalls++;
            Links[link] = target;
            if (LinkThrows is not null)
            {
                throw LinkThrows;
            }
        },
        link => ReadsBackAs ?? Links.GetValueOrDefault(link, string.Empty),
        link =>
        {
            RemoveCalls++;
            if (RemoveThrows is not null)
            {
                throw RemoveThrows;
            }

            Links.Remove(link);
        },
        Links.ContainsKey);
}
