namespace CoaiMcp.Store;

/// <summary>One line cut from a stream: its bytes without the terminator, and how many terminator bytes it had (0 for a tail).</summary>
internal readonly record struct RawLine(ReadOnlyMemory<byte> Text, int Terminator);

/// <summary>
/// Splits a stream into lines on raw bytes, in blocks, without ever decoding one (E3.S1, <see cref="TranscriptLines.ReadSharedFrom"/>).
/// </summary>
/// <remarks>
/// <para><b>Bounded by construction.</b> A line longer than <see cref="ScanMeter.MaxLineBytes"/> is never buffered whole: once the
/// line under assembly passes the cap the held bytes are dropped and the rest of it is scanned to its newline without being
/// kept (plan gate finding 5). A read that fails after the open ends the file and is counted, never thrown (finding 9), and an
/// unterminated remainder is then NOT a tail — it is a line the failure cut, and it is dropped with the file.</para>
/// <para><b>Not a line:</b> an empty one. A blank line is not a torn line, and counting it as one would put
/// <c>partial: torn line</c> on a clean transcript.</para>
/// </remarks>
internal sealed class LineSplitter(Stream stream, ScanMeter meter)
{
    private const int Block = 64 * 1024;
    private const long StopEvery = 1024 * 1024;

    private byte[] _buffer = new byte[2 * Block];
    private int _start;
    private int _end;

    /// <summary>How many bytes after <c>_start</c> are known to hold no newline, so a long line is not searched again from its start.</summary>
    private int _probed;
    private bool _discarding;
    private bool _atEnd;
    private bool _clean = true;
    private bool _atOffsetZero = true;
    private long _sinceStopCheck;

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    public bool TryNext(out RawLine line)
    {
        while (!TryCut(out line))
        {
            if (_atEnd || !Fill())
            {
                return Tail(out line);
            }
        }

        return true;
    }

    /// <summary>The next kept line already in the buffer. False when the buffer holds no further newline.</summary>
    private bool TryCut(out RawLine line)
    {
        while (FindEnd(out var length))
        {
            if (TakeLine(length, out line))
            {
                return true;
            }
        }

        DropOverlong();
        line = default;
        return false;
    }

    private bool FindEnd(out int length)
    {
        var from = _start + _probed;
        var at = _buffer.AsSpan(from, _end - from).IndexOf((byte)'\n');
        _probed = at < 0 ? _end - _start : _probed;
        length = _probed + Math.Max(at, 0);

        return at >= 0;
    }

    /// <summary>Consumes the line of <paramref name="length"/> bytes plus its newline. True when it is a line to hand on.</summary>
    private bool TakeLine(int length, out RawLine line)
    {
        var text = TextLength(length);
        var wasDiscarding = _discarding;
        _discarding = false;
        var keep = !wasDiscarding && !TooLong(length) && text > 0;
        line = keep ? new RawLine(_buffer.AsMemory(_start, text), length - text + 1) : default;
        _start += length + 1;
        _probed = 0;

        return keep;
    }

    /// <summary>The line's length without a <c>\r</c> before the newline.</summary>
    private int TextLength(int length) => length > 0 && _buffer[_start + length - 1] == (byte)'\r' ? length - 1 : length;

    private bool TooLong(int length)
    {
        if (length <= meter.MaxLineBytes)
        {
            return false;
        }

        meter.LinesTooLong++;
        return true;
    }

    /// <summary>No newline in what is held: past the cap this line is dropped, counted once, and its rest is scanned unkept.</summary>
    private void DropOverlong()
    {
        if (!_discarding && _end - _start > meter.MaxLineBytes)
        {
            _discarding = true;
            meter.LinesTooLong++;
        }

        if (_discarding)
        {
            _start = _end;
            _probed = 0;
        }
    }

    /// <summary>One block more. False when the file ended, the read failed or the meter said stop.</summary>
    private bool Fill()
    {
        MakeRoom();
        var read = ReadBlock();
        if (read <= 0)
        {
            return End(clean: read == 0);
        }

        _end += read;
        meter.BytesScanned += read;
        SkipBom();
        if (AskedToStop(read))
        {
            return End(clean: false);
        }

        return true;
    }

    /// <summary>
    /// A UTF-8 byte order mark at stream offset 0 is not part of the first line: <c>StreamReader</c> (so <c>ReadShared</c>) drops it
    /// and <c>JsonDocument.Parse</c> over bytes rejects it, which would call line 1 torn. The bytes stay counted as scanned.
    /// </summary>
    private void SkipBom()
    {
        if (!_atOffsetZero)
        {
            return;
        }

        _atOffsetZero = false;
        if (_buffer.AsSpan(_start, _end - _start).StartsWith(Utf8Bom))
        {
            _start += Utf8Bom.Length;
        }
    }

    private bool End(bool clean)
    {
        _atEnd = true;
        _clean = clean;

        return false;
    }

    private int ReadBlock()
    {
        try
        {
            return stream.Read(_buffer.AsSpan(_end, Block));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            meter.ReadFailures++;
            return -1;
        }
    }

    /// <summary>The hook is asked after every MiB scanned, so a file of stale lines — which yields nothing — still meets the budget.</summary>
    private bool AskedToStop(int read)
    {
        _sinceStopCheck += read;
        if (_sinceStopCheck < StopEvery)
        {
            return false;
        }

        _sinceStopCheck = 0;

        return meter.Stop();
    }

    private void MakeRoom()
    {
        if (_buffer.Length - _end >= Block)
        {
            return;
        }

        if (_start > 0)
        {
            _buffer.AsSpan(_start, _end - _start).CopyTo(_buffer);
            _end -= _start;
            _start = 0;
        }

        if (_buffer.Length - _end < Block)
        {
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _end + Block));
        }
    }

    /// <summary>A last line with no newline: yielded, because a torn tail is exactly what the caller must still see.</summary>
    private bool Tail(out RawLine line)
    {
        var held = _end - _start;
        var keep = _clean && !_discarding && held > 0;
        line = keep ? new RawLine(_buffer.AsMemory(_start, held), 0) : default;
        _start = _end;
        _probed = 0;

        return keep;
    }
}
