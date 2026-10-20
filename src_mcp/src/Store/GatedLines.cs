namespace CoaiMcp.Store;

/// <summary>
/// The byte gate in front of the splitter: which lines are handed on, and what each one costs the meter.
/// </summary>
/// <remarks>
/// <para><b>The head</b> — every line up to and including the first whose bytes hold <c>"timestamp":</c> — is yielded whatever its
/// stamp: it is what gives <c>FirstLineUtc</c> and <c>LaunchedUtc</c>, and a file can start with an <c>ai-title</c>, which has none.
/// <b>The tail</b> — an unterminated last line — is always yielded. Every other line is yielded only when
/// <see cref="TranscriptLines.StampAtOrAfter(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/> says so; otherwise it is counted
/// skipped and its bytes are never touched again.</para>
/// <para><b>Charge, ask, then yield.</b> A line that passes is charged to <see cref="ScanMeter.BytesYielded"/> (its bytes plus
/// its terminator), then <see cref="ScanMeter.Stop"/> is asked, and only then is it handed on. When the budget says stop, that
/// line is charged and not yielded, so the slice never holds a line past the budget (plan gate finding 8).</para>
/// </remarks>
internal sealed class GatedLines(Stream stream, byte[] boundStamp, ScanMeter meter)
{
    private readonly LineSplitter _lines = new(stream, meter);
    private bool _headDone;

    public bool TryNext(out ReadOnlyMemory<byte> line)
    {
        line = default;
        while (_lines.TryNext(out var raw))
        {
            if (!Passes(raw))
            {
                meter.LinesSkipped++;
                continue;
            }

            meter.BytesYielded += raw.Text.Length + raw.Terminator;
            line = raw.Text;

            return !meter.Stop();
        }

        return false;
    }

    private bool Passes(RawLine raw)
    {
        var text = raw.Text.Span;
        var inHead = InHead(text);

        return inHead || raw.Terminator == 0 || TranscriptLines.StampAtOrAfter(text, boundStamp);
    }

    private bool InHead(ReadOnlySpan<byte> text)
    {
        if (_headDone)
        {
            return false;
        }

        _headDone = TranscriptLines.HasStampKey(text);

        return true;
    }
}
