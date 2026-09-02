using System.Text.Json;
using System.Text.Json.Serialization;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;

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

/// <summary>What a person wrote back, and — when they pressed a button — what they chose.</summary>
public sealed record EscalationAnswer(string Id, string Answer, string AnsweredUtc)
{
    /// <summary><c>proceed</c> or <c>fix</c>. Absent for a typed answer, and absent is not a choice.</summary>
    public string? Decision { get; init; }
}

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
    /// <summary>
    /// Puts a question in front of a person WITHOUT waiting for the answer.
    /// </summary>
    /// <remarks>
    /// A <c>call_human</c> verdict is returned to the calling AI, and the AI decides whether to
    /// pass it on — so a gate that had exhausted its rounds could leave a person with nothing to
    /// see, which is what happened: the operator watched the panel all day and never learned the
    /// gate had asked for them. A verdict that says "a person decides" must reach that person
    /// whatever the AI does next. It does not block, because the round is already over; the file
    /// is the same shape as any other escalation, so the panel shows and answers it identically.
    /// </remarks>
    public void Notify(EscalationQuestion question)
    {
        try
        {
            // The directory, first: on a machine where nobody had used `ask_human` yet it does not
            // exist, and the catch below swallows DirectoryNotFoundException with every other IO
            // failure — so the verdict that says "a person must decide" reached nobody, silently.
            System.IO.Directory.CreateDirectory(Directory);
            WriteAtomic(QuestionPath(question.Id), JsonSerializer.Serialize(question, EscalationJsonContext.Default.EscalationQuestion));
        }
        catch (IOException)
        {
            // A notice nobody could write is not a round that failed.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

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

    /// <summary>
    /// This session's answered notice, if a person has answered one.
    /// </summary>
    /// <remarks>
    /// The reason this exists: a <c>call_human</c> notice is written by a round that then RETURNS,
    /// so nothing is polling for its answer the way <see cref="AskAsync"/> does. The panel wrote the
    /// answer file and no code on either side ever read it — a person could type a decision, watch
    /// the card disappear, and have changed nothing. That is a worse dead end than never being
    /// asked, because it looks like it worked.
    /// </remarks>
    private EscalationAnswer? AnsweredFor(string sessionId)
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return null;
        }

        // Newest first: a session asked twice is answered about the round it is in now.
        var questions = System.IO.Directory
            .EnumerateFiles(Directory, "*.json")
            .Where(p => !p.EndsWith(".answer.json", StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc);

        foreach (var path in questions)
        {
            if (ReadQuestion(path) is not { } question || question.SessionId != sessionId)
            {
                continue;
            }

            if (ReadAnswer(question.Id) is { } answer)
            {
                return answer;
            }
        }

        return null;
    }

    /// <summary>What the person chose for this session, or <see cref="HumanDecision.None"/>.</summary>
    public HumanDecision DecisionFor(string sessionId) =>
        // `?.Decision?` and not `?.Decision.`: a field absent from the JSON comes back NULL through
        // the source-generated deserializer whatever the property initializer says, so the
        // non-nullable declaration is a promise the wire does not keep. Found by the test for a
        // typed answer, which is the ordinary case.
        AnsweredFor(sessionId)?.Decision?.Trim().ToLowerInvariant() switch
        {
            "continue" => HumanDecision.Continue,
            "fix" => HumanDecision.Fix,
            "discuss" => HumanDecision.Discuss,
            _ => HumanDecision.None,
        };

    /// <summary>Their own words, whether or not they pressed a button. Never discarded.</summary>
    public string AnswerTextFor(string sessionId) => AnsweredFor(sessionId)?.Answer ?? string.Empty;

    private EscalationQuestion? ReadQuestion(string path)
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), EscalationJsonContext.Default.EscalationQuestion);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
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
