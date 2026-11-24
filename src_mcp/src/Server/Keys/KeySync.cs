namespace CoaiMcp.Server.Keys;

/// <summary>
/// Syncing the pool from a source (KEY_POOL_SPEC.md, the owner's correction of 2026-11-23): a key the source lists that the pool lacks is added, one whose text changed is replaced, one the source no longer lists is
/// DISABLED (never deleted). Only the keys of THIS source are touched; the keys a person added by hand, and those of other sources, stay.
/// </summary>
/// <remarks>
/// <para><b>Each key keeps its id</b> (the hash of its text) whatever its source; a changed text is a new key with the old key's label, added before the old pair is deleted, so a fault in between never loses both.</para>
/// <para><b>A key that comes back</b> is enabled again only when a sync disabled it (<see cref="KeyRecord.SyncDisabled"/>); a person's own disable stays.</para>
/// <para>A sync that changed anything moves <c>adminRevision</c>, because the set of keys changed under a person's list.</para>
/// </remarks>
internal sealed class KeySync(KeyStoreAccess access, KeyAdmin admin)
{
    public KeyResult<KeyImportAnswer> Apply(string source, SourceRead read)
    {
        if (read.Inputs.Count == 0 && !access.Folder.Exists)
        {
            return KeyResult<KeyImportAnswer>.Of(new KeyImportAnswer(0, 0, 0, 0, 0, read.Refused));
        }

        return access.Run(create: true, session =>
        {
            var entries = session.Entries().ToList();
            var counts = new Counts();
            var refused = new List<KeyRefusedLine>(read.Refused);
            foreach (var input in read.Inputs)
            {
                One(session, entries, source, input, counts, refused);
            }

            Sweep(session, entries, source, read.SeenLabels, counts);
            if (counts.Changed)
            {
                KeyAdmin.BumpRevision(session);
            }

            return KeyResult<KeyImportAnswer>.Of(new KeyImportAnswer(counts.Added, counts.Already, counts.Replaced, counts.Disabled, counts.Enabled, refused));
        });
    }

    private void One(KeySession session, List<KeyEntry> entries, string source, KeyInput input, Counts counts, List<KeyRefusedLine> refused)
    {
        var mine = entries.FirstOrDefault(entry => entry.Record.Source == source && entry.Record.Label.Equals(input.Label, StringComparison.OrdinalIgnoreCase));
        if (mine is not null && mine.Record.Id == KeyIds.Of(input.TokenText))
        {
            Unchanged(session, entries, mine, input, counts);

            return;
        }

        var outcome = admin.AddOne(session, entries, input, mine?.Record.Id ?? string.Empty);
        switch (outcome)
        {
            case AddOutcome.Added when mine is not null:
                session.DeletePair(mine.Record.Id);
                entries.Remove(mine);
                counts.Replaced++;
                break;
            case AddOutcome.Added:
                counts.Added++;
                break;
            case AddOutcome.AlreadyThere:
                counts.Already++;
                break;
            case AddOutcome.Refused rule:
                refused.Add(new KeyRefusedLine(input.Line, rule.Reason));
                break;
        }
    }

    /// <summary>The key is the same: its account may have changed, and a key a sync disabled comes back.</summary>
    private static void Unchanged(KeySession session, List<KeyEntry> entries, KeyEntry mine, KeyInput input, Counts counts)
    {
        var record = mine.Record;
        var back = record.SyncDisabled && record.State == KeyStates.Disabled;
        var updated = record with
        {
            Account = input.Account,
            State = back ? KeyStates.Enabled : record.State,
            SyncDisabled = back ? false : record.SyncDisabled,
        };
        if (updated == record)
        {
            counts.Already++;

            return;
        }

        session.SaveRecord(updated);
        entries[entries.IndexOf(mine)] = mine with { Record = updated };
        counts.Changed = true;
        counts.Enabled += back ? 1 : 0;
        counts.Already += back ? 0 : 1;
    }

    private static void Sweep(KeySession session, List<KeyEntry> entries, string source, IReadOnlySet<string> seen, Counts counts)
    {
        foreach (var entry in entries.Where(entry => entry.Record.Source == source && entry.Record.State == KeyStates.Enabled && !seen.Contains(entry.Record.Label)).ToList())
        {
            session.SaveRecord(entry.Record with { State = KeyStates.Disabled, SyncDisabled = true });
            counts.Disabled++;
        }
    }

    private sealed class Counts
    {
        public int Added { get; set; }

        public int Already { get; set; }

        public int Replaced { get; set; }

        public int Disabled { get; set; }

        public int Enabled { get; set; }

        private bool _changed;

        public bool Changed
        {
            get => _changed || Added + Replaced + Disabled + Enabled > 0;
            set => _changed = value;
        }
    }
}
