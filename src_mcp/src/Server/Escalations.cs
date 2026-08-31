using System.Text.Json;
using System.Text.Json.Serialization;
using CoaiMcp.Core.Findings;

namespace CoaiMcp.Server;

/// <summary>
/// One question waiting for a person, as it sits on disk.
/// </summary>
/// <param name="Question">What to SHOW: in the configured language when that was possible.</param>
/// <param name="QuestionOriginal">As the AI wrote it — kept so nothing is ever lost in translation.</param>
/// <param name="Language">The language code the question was rendered into (or attempted in).</param>
/// <param name="TranslationNote">Empty when <paramref name="Question"/> is in that language;
/// otherwise why it is not, in words a person can act on.</param>
public sealed record EscalationQuestion(
    string Id,
    string SessionId,
    string RepoPath,
    string Branch,
    string Question,
    string QuestionOriginal,
    string Language,
    string TranslationNote,
    IReadOnlyList<Finding> OpenFindings,
    string AskedUtc);

/// <summary>What a person wrote back.</summary>
public sealed record EscalationAnswer(string Id, string Answer, string AnsweredUtc);

/// <summary>What waiting for a person produced. A closed union: silence is never an answer.</summary>
public abstract record EscalationOutcome
{
    public sealed record Answered(string Text) : EscalationOutcome;

    /// <summary>Nobody answered within the budget. The question FILE stays — it is not withdrawn.</summary>
    public sealed record NoAnswerYet(TimeSpan Waited) : EscalationOutcome;

    private EscalationOutcome() { }
}

/// <summary>
/// Reaching a person without opening a port: the server writes a question into the data directory
/// the extension already watches, and waits for an answer file beside it.
/// </summary>
/// <remarks>
/// <para><b>Why files rather than a socket.</b> Everything else in this product needed no channel
/// between the halves — settings ride one way in the copied `mcpServers` env block, and the rounds
/// view reads these same session files. Escalation is the one case that needs a channel, and a
/// directory both halves already use is a smaller thing to own than a port, a token, a lifetime
/// and a health story.</para>
/// <para><b>The answer file is written atomically</b> (temp + move) by the extension, and a
/// half-written or malformed one never resolves a question — the wait simply continues. A file
/// that cannot be parsed is not an answer, and treating it as one would unblock a round on
/// nothing.</para>
/// </remarks>
public sealed class Escalations(string dataDir, TimeSpan? pollInterval = null)
{
    private readonly TimeSpan _poll = pollInterval ?? TimeSpan.FromSeconds(2);

    public string Directory => Path.Combine(dataDir, "escalations");

    public string QuestionPath(string id) => Path.Combine(Directory, $"{id}.json");

    public string AnswerPath(string id) => Path.Combine(Directory, $"{id}.answer.json");

    /// <summary>Writes the question, then waits for its answer or the budget, whichever comes first.</summary>
    public async Task<EscalationOutcome> AskAsync(
        EscalationQuestion question,
        TimeSpan budget,
        CancellationToken ct = default)
    {
        System.IO.Directory.CreateDirectory(Directory);
        WriteAtomic(QuestionPath(question.Id), JsonSerializer.Serialize(question, EscalationJsonContext.Default.EscalationQuestion));

        var deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline)
        {
            if (ReadAnswer(question.Id) is { } answer)
            {
                return new EscalationOutcome.Answered(answer.Answer);
            }

            var remaining = deadline - DateTime.UtcNow;
            await Task.Delay(remaining < _poll ? remaining : _poll, ct);
        }

        // One last look: an answer written during the final wait must not be missed.
        return ReadAnswer(question.Id) is { } late
            ? new EscalationOutcome.Answered(late.Answer)
            : new EscalationOutcome.NoAnswerYet(budget);
    }

    /// <summary>The answer, or nothing — a malformed or half-written file is nothing, never an answer.</summary>
    internal EscalationAnswer? ReadAnswer(string id)
    {
        var path = AnswerPath(id);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var answer = JsonSerializer.Deserialize(File.ReadAllText(path), EscalationJsonContext.Default.EscalationAnswer);
            return answer is { Answer.Length: > 0 } ? answer : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null; // being written right now; the next poll will find it whole
        }
    }

    private static void WriteAtomic(string path, string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(EscalationQuestion))]
[JsonSerializable(typeof(EscalationAnswer))]
internal sealed partial class EscalationJsonContext : JsonSerializerContext;

/// <summary>
/// An empty directory for a reviewer that must have nowhere to explore.
/// </summary>
/// <remarks>
/// The plan stage runs here rather than in a checkout. A CLI given a repository reads it; a CLI
/// given an empty folder answers from the message, which is what a plan critique is.
/// </remarks>
public sealed class ScratchDirectory : IDisposable
{
    public ScratchDirectory() => Path = Directory.CreateTempSubdirectory("coai-plan-").FullName;

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException) { /* a straggling handle is not worth failing a completed round over */ }
        catch (UnauthorizedAccessException) { }
    }
}
