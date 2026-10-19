namespace CoaiMcp.Store;

/// <summary>
/// ONE read of a located session (E3.S4): list the files and metas, read main and then every agent file through the byte gate and the
/// shared meter, count each message, launch and write once, and say which state it reached. One instance serves one read and is
/// thrown away; nothing here is shared or persisted.
/// </summary>
/// <remarks>
/// <para><b>Order.</b> Files and metas are LISTED before any line is read, so a budget stop never leaves a launch unlisted. Main is read
/// first, then the agent files in key order — the order a stop truncates in.</para>
/// <para><b>Heads.</b> A file the read never opened (skipped by last-write, or after a stop) still has its HEAD read — up to its first
/// stamped line, off the budget — so <c>LaunchedUtc</c> is known and <c>TranscriptPresent</c> keeps its meaning. The main file is
/// never skipped by last-write (it holds the launching calls).</para>
/// <para>Each line is parsed into plain facts and its document disposed before the next line is asked for (C4).</para>
/// </remarks>
internal sealed class TranscriptRead
{
    private readonly CallerTranscriptLocation.Found _found;
    private readonly DateTime? _fromUtc;
    private readonly DateTime _bound;
    private readonly ReadTally _tally = new();
    private readonly ReadMeter _meter;
    private readonly TranscriptMessages _messages;
    private readonly LineIntake _intake;
    private readonly TranscriptLaunches _launches;
    private readonly List<FileRead> _files;

    public TranscriptRead(CallerTranscriptLocation.Found found, ReadBudget budget, DateTime? fromUtc)
    {
        _found = found;
        _fromUtc = fromUtc;
        _bound = fromUtc is { } from ? CallerTranscript.Before(from, CallerTranscript.StraddleGuard) : DateTime.MinValue;
        _meter = new ReadMeter(budget);
        _messages = new TranscriptMessages(_tally);
        _intake = new LineIntake(_tally, _messages);
        _launches = TranscriptLaunches.List(found.SessionFolder, _tally);
        _files = [new FileRead(0, "main", found.MainFile), .. _launches.FileKeys.Select((key, index) => new FileRead(index + 1, key, _launches.PathOf(key)))];
    }

    public ObservedSession Run()
    {
        _files.ForEach(ReadFile);
        _files.ForEach(Finish);

        return Assemble();
    }

    private void ReadFile(FileRead file)
    {
        if (_meter.Stopped)
        {
            return;
        }

        if (SkippedByLastWrite(file))
        {
            _tally.FilesSkippedByLastWrite++;

            return;
        }

        Scan(file);
    }

    /// <summary>A file last written before the bound cannot hold a line after it; agent files only (the main file holds the launching calls).</summary>
    private bool SkippedByLastWrite(FileRead file) =>
        !file.IsMain && _fromUtc is { } from && !TranscriptLines.WrittenSince(file.FullName, from);

    private void Scan(FileRead file)
    {
        _tally.FilesRead++;
        foreach (var bytes in TranscriptLines.ReadSharedFrom(file.FullName, _bound, _meter.Scan))
        {
            Ingest(file, bytes);
        }

        file.Completed = !_meter.Stopped;
        if (_meter.Stopped)
        {
            StopIn(file);
        }
    }

    private void Ingest(FileRead file, ReadOnlyMemory<byte> bytes)
    {
        if (!TranscriptLine.TryRead(bytes, out var line))
        {
            file.Tear();

            return;
        }

        file.LastLineTorn = false;
        _intake.Take(file, line);
    }

    /// <summary>S4-3: the budget stopped inside this file. Its ids that could still have had a line after the stop are dropped and counted.</summary>
    private void StopIn(FileRead file)
    {
        file.Stopped = true;
        _tally.Reason(ObservationReasons.ReadBudget);
        if (file.MaxStamp is { } stopStamp)
        {
            _messages.DropAfterStop(file.Index, stopStamp, _fromUtc);
        }
    }

    private void Finish(FileRead file)
    {
        if (file.FirstStamp is null && !file.Completed)
        {
            ReadHead(file);
        }

        CountTorn(file);
    }

    /// <summary>The head only, off the budget: lines up to and including the first stamped one (the gate's head rule, under a bound nothing can pass).</summary>
    private void ReadHead(FileRead file)
    {
        foreach (var bytes in TranscriptLines.ReadSharedFrom(file.FullName, DateTime.MaxValue, new ScanMeter()))
        {
            if (TakeHeadLine(file, bytes))
            {
                break;
            }
        }
    }

    private bool TakeHeadLine(FileRead file, ReadOnlyMemory<byte> bytes)
    {
        if (TranscriptLine.TryRead(bytes, out var line))
        {
            _intake.TakeHead(file, line);
        }

        return file.FirstStamp is not null;
    }

    /// <summary>
    /// S4-1: the LAST line a file yielded, when it failed to parse, is a torn TAIL — the CLI's append racing the read — counted and not
    /// partial. Any other torn line is corruption in the middle and is <c>partial: torn line</c>.
    /// </summary>
    private void CountTorn(FileRead file)
    {
        if (file.TornLines == 0)
        {
            return;
        }

        var tail = file.LastLineTorn ? 1 : 0;
        _tally.TornTails += tail;
        _tally.Reason(ObservationReasons.TornLine, file.TornLines - tail + (CallerTranscript.TornTailCountsAsPartial ? tail : 0));
    }

    private ObservedSession Assemble()
    {
        var main = _files[0];
        // After a stop, Finish has already read main's head off the budget, so a null here means main holds no stamped
        // line at all: empty, whether or not the budget stopped the read (S4-10).
        if (main.FirstStamp is null)
        {
            return Of(ObservationState.Unobserved(ObservationReasons.TranscriptEmpty), null, [], [], [], []);
        }

        var launches = _launches.Resolve(_files, _messages, _meter.Stopped, _tally);
        var messages = _messages.Project(alias => launches.Aliases.GetValueOrDefault(alias, string.Empty), _fromUtc);
        var writes = TranscriptWrites.Project(_files, _messages, _fromUtc, _tally);
        _tally.Reason(ObservationReasons.LineTooLong, _meter.Scan.LinesTooLong);
        _tally.Reason(ObservationReasons.ReadFailed, _meter.Scan.ReadFailures);

        return Of(StateOf(_tally.Reasons), main.FirstStamp, launches.Agents, messages, writes, launches.Unobserved);
    }

    private static ObservationState StateOf(IReadOnlyList<ObservationReason> reasons) =>
        reasons.Count == 0 ? ObservationState.Observed : ObservationState.Partial(reasons);

    private ObservedSession Of(
        ObservationState state,
        DateTime? firstLineUtc,
        IReadOnlyList<ObservedAgent> agents,
        IReadOnlyList<ObservedMessage> messages,
        IReadOnlyList<ObservedWrite> writes,
        IReadOnlyList<UnobservedLaunch> unobserved) =>
        new(_found.Session, state, firstLineUtc, agents, messages, writes, unobserved, Diagnostics());

    private ReadDiagnostics Diagnostics() => new(
        _tally.FilesRead,
        _tally.FilesSkippedByLastWrite,
        _meter.Scan.BytesScanned,
        _meter.SliceBytes,
        _meter.Scan.LinesSkipped,
        _meter.Scan.LinesTooLong,
        _meter.Scan.ReadFailures,
        _tally.TornTails,
        _tally.SyntheticLines,
        _tally.MetasWithoutModel,
        _tally.ValuesOffShape,
        _tally.WriteCallsRefused,
        _tally.WriteCallsUnanswered,
        _tally.MessagesWithoutUsage,
        _tally.MessagesDroppedAtBudget,
        _tally.AgentFilesEmpty);
}
