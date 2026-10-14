using System.Text;

namespace CoaiMcp.Runners.Processes;

/// <summary>
/// One stream's text, up to a ceiling, with the truncation said out loud.
/// </summary>
/// <remarks>
/// Not a <see cref="StringBuilder"/> with a check beside it, because the check has to hold at every
/// call site and there are two of them per launch. A bound that lives in the type cannot be
/// forgotten by the next reader of this file.
/// </remarks>
internal sealed class BoundedText(int maxChars)
{
    private readonly StringBuilder _text = new();
    private long _dropped;
    private bool _cutShort;

    /// <summary>True when what this holds is a HEAD rather than the whole stream.</summary>
    public bool Truncated => _dropped > 0 || _cutShort;

    /// <summary>True when the stream was still open at the drain grace — its OWN fact, distinct from the size ceiling: something outlived the child and holds the pipe.</summary>
    public bool HeldOpen => _cutShort;

    /// <summary>Take up to the ceiling, and count the rest rather than keeping it.</summary>
    /// <remarks>
    /// Reading continues past the ceiling in the caller — a pipe nobody drains blocks the child that
    /// is writing to it — so this goes on being called and goes on counting. The count is what makes
    /// the sentence below worth reading: "cut at 8 Mi" says nothing about whether one character was
    /// lost or two hundred megabytes.
    /// </remarks>
    public void Append(char[] buffer, int count)
    {
        var room = maxChars - _text.Length;
        if (count <= room)
        {
            _text.Append(buffer, 0, count);

            return;
        }

        _text.Append(buffer, 0, Math.Max(room, 0));
        _dropped += count - Math.Max(room, 0);
    }

    /// <summary>The stream was still open when the drain grace ran out.</summary>
    /// <remarks>
    /// A different fact from the ceiling and it deserves a different sentence: the ceiling is this
    /// launcher's decision about a runaway, while this is something that outlived the tree kill and
    /// is still holding the pipe. Reading them as one would send somebody to raise a limit that was
    /// never reached.
    /// </remarks>
    public void CutShort() => _cutShort = true;

    /// <summary>
    /// The text, with one sentence at the end when it is not all of it.
    /// </summary>
    /// <remarks>
    /// Composed HERE rather than appended when the ceiling was crossed, because the number worth
    /// printing — how much was dropped — is not known until the stream ends. It opens with a newline
    /// so it cannot run on from output that ended without one.
    /// </remarks>
    public override string ToString() => Truncated
        ? _text.ToString() + Sentence()
        : _text.ToString();

    private string Sentence() => _dropped > 0
        ? $"\n[coai: output truncated at {maxChars} characters; {_dropped} more were dropped]\n"
        : "\n[coai: output truncated — the stream was still open when the drain grace ran out]\n";
}
