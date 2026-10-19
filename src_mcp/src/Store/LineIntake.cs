namespace CoaiMcp.Store;

/// <summary>
/// Routes the facts of one parsed line to what counts them (E3.S4 steps 4, 5 and 7): the file's stamps, the message table, the file's
/// calls and the file's results. It decides nothing about states — it only records what each line said, once.
/// </summary>
internal sealed class LineIntake(ReadTally tally, TranscriptMessages messages)
{
    /// <summary>One parsed line of <paramref name="file"/>.</summary>
    public void Take(FileRead file, LineFacts line)
    {
        file.NoteStamp(line.Stamp);
        CheckAgentId(file, line);
        switch (line.Kind)
        {
            case LineKind.Assistant:
                TakeAssistant(file, line);
                break;
            case LineKind.User:
                TakeResults(file, line);
                break;
        }
    }

    /// <summary>The head of a file read on its own (a file skipped by last-write, or one a budget stop never reached): stamps and the agent id only.</summary>
    public void TakeHead(FileRead file, LineFacts line)
    {
        file.NoteStamp(line.Stamp);
        CheckAgentId(file, line);
    }

    /// <summary>
    /// S4-4 and step 5: the first line that carries an <c>agentId</c> and every assistant line that does must name the file's own key,
    /// or the transcript is inconsistent — ONCE per file. A line of the main file that is a sidechain line names its own agent and never disagrees.
    /// </summary>
    private void CheckAgentId(FileRead file, LineFacts line)
    {
        if (line.AgentId.Length == 0 || !IsChecked(file, line))
        {
            return;
        }

        file.AgentIdChecked = true;
        if (Disagrees(file, line))
        {
            Disagree(file);
        }
    }

    private static bool IsChecked(FileRead file, LineFacts line) => line.Kind == LineKind.Assistant || !file.AgentIdChecked;

    private static bool Disagrees(FileRead file, LineFacts line) => !(file.IsMain && line.Sidechain) && line.AgentId != file.Key;

    private void Disagree(FileRead file)
    {
        if (file.AgentIdDisagreed)
        {
            return;
        }

        file.AgentIdDisagreed = true;
        tally.Reason(ObservationReasons.InconsistentTranscript);
    }

    private void TakeAssistant(FileRead file, LineFacts line)
    {
        if (line.Values.Synthetic)
        {
            tally.SyntheticLines++;

            return;
        }

        if (!IsUsable(line))
        {
            tally.Reason(ObservationReasons.InconsistentTranscript);

            return;
        }

        var key = AgentKeyOf(file, line);
        if (key.Length == 0)
        {
            tally.Reason(ObservationReasons.SidechainWithoutAgentId);

            return;
        }

        Record(file, key, line);
    }

    /// <summary>An assistant line counts only with a parseable stamp and a message id: without either it cannot be placed or de-duplicated.</summary>
    private static bool IsUsable(LineFacts line) => line.Stamp is not null && line.MessageId.Length > 0;

    /// <summary>The file wins (S4-4); a sidechain line in the MAIN file belongs to the agent its <c>agentId</c> names, or to none (S4-12).</summary>
    private static string AgentKeyOf(FileRead file, LineFacts line) =>
        !file.IsMain ? file.Key : line.Sidechain ? line.AgentId : file.Key;

    private void Record(FileRead file, string key, LineFacts line)
    {
        tally.ValuesOffShape += line.Values.OffShape;
        messages.Absorb(file.Index, key, line.Stamp.GetValueOrDefault(), line);
        foreach (var block in line.Blocks)
        {
            file.NoteCall(block, line.MessageId);
        }
    }

    private static void TakeResults(FileRead file, LineFacts line)
    {
        foreach (var block in line.Blocks)
        {
            file.NoteResult(block, line.Stamp);
        }
    }
}
