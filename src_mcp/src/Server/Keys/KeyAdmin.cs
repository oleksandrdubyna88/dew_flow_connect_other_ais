namespace CoaiMcp.Server.Keys;

/// <summary>
/// The changes a person (or a source) makes to the pool: add, relabel, disable, enable, remove, and the list that shows it — the same answers serve the Keys page (KEY_POOL_SPEC.md sections 4 and 8).
/// </summary>
/// <remarks>
/// <para><b>Write-only.</b> Nothing here answers a token: a list row has no text, an add answers the id, and the text goes to a file the pool reads only on a pick.</para>
/// <para><b>Every verb takes <c>pool.lock</c>.</b> A write that carries <c>expectedRevision</c> is refused as "changed elsewhere" when <c>adminRevision</c> has moved since the caller read it; the revision moves on a change by a person and never on a pick or a counter.</para>
/// </remarks>
internal sealed class KeyAdmin(KeyStoreAccess access)
{
    /// <summary>The pool keeps this many keys at most; the next add is refused.</summary>
    internal const int MaxKeys = 200;

    internal const string ChangedElsewhere = "changed elsewhere: the key list moved since you read it — read it again";

    private const string NoSuchKey = "no key has that id";

    /// <summary>The sentence an add, an import and a sync say on a shared VM instance, or when the setgid bit is there without the device setting: see <see cref="KeyFolder.SharedNote"/>. Empty on a private folder.</summary>
    public string SharedSentence => access.Folder.SharedNote;

    // -------------------------------------------------------------------------------------------------------------
    // list
    // -------------------------------------------------------------------------------------------------------------

    public KeyResult<KeyListAnswer> List(bool personSignInSwitch)
    {
        if (!access.Folder.Exists)
        {
            return KeyResult<KeyListAnswer>.Of(new KeyListAnswer(0, 0, personSignInSwitch, [], 0));
        }

        return access.Run(create: false, session =>
        {
            var now = access.NowUtc;
            var rows = session.Entries().Select(entry => KeyStoreAccess.RowOf(entry, now)).ToList();

            return KeyResult<KeyListAnswer>.Of(new KeyListAnswer(session.Pool().AdminRevision, rows.Count(row => row.Usable), personSignInSwitch, rows, session.Unreadable));
        });
    }

    // -------------------------------------------------------------------------------------------------------------
    // add
    // -------------------------------------------------------------------------------------------------------------

    /// <summary>Adds one key: the row, or the refusal. The same text twice is "already in the pool as &lt;label&gt;".</summary>
    public KeyResult<KeyRow> Add(KeyInput input) =>
        access.Run(create: true, session =>
        {
            var entries = session.Entries().ToList();
            var outcome = AddOne(session, entries, input);
            if (outcome is AddOutcome.Added added)
            {
                BumpRevision(session);

                return KeyResult<KeyRow>.Of(KeyStoreAccess.RowOf(added.Entry, access.NowUtc));
            }

            return outcome is AddOutcome.AlreadyThere there
                ? KeyResult<KeyRow>.Refused($"already in the pool as {there.Label}")
                : KeyResult<KeyRow>.Refused(((AddOutcome.Refused)outcome).Reason, ((AddOutcome.Refused)outcome).Code);
        });

    /// <summary>Adds many under one lock: counts, and the refused inputs by their line number and the rule — never the input.</summary>
    public KeyResult<KeyImportAnswer> Import(IReadOnlyList<KeyInput> inputs) =>
        access.Run(create: true, session =>
        {
            var entries = session.Entries().ToList();
            var (added, already, refused) = (0, 0, new List<KeyRefusedLine>());
            foreach (var input in inputs)
            {
                switch (AddOne(session, entries, input))
                {
                    case AddOutcome.Added:
                        added++;
                        break;
                    case AddOutcome.AlreadyThere:
                        already++;
                        break;
                    case AddOutcome.Refused rule:
                        refused.Add(new KeyRefusedLine(input.Line, rule.Reason));
                        break;
                }
            }

            if (added > 0)
            {
                BumpRevision(session);
            }

            return KeyResult<KeyImportAnswer>.Of(new KeyImportAnswer(added, already, 0, 0, 0, refused));
        });

    /// <summary>Adds one input to the folder and to <paramref name="entries"/> (the caller's list of what the pool now holds). A failure leaves no file behind.</summary>
    internal AddOutcome AddOne(KeySession session, List<KeyEntry> entries, KeyInput input, string replacing = "")
    {
        if (KeyShape.Refusal(input.TokenText) is { Length: > 0 } shape)
        {
            return new AddOutcome.Refused(shape, KeyExit.Data);
        }

        if (input.Account.Length > 0 && !KeySourceReader.IsAccount(input.Account))
        {
            // Never the account itself: the text may be a token that was typed in the wrong place.
            return new AddOutcome.Refused("the account must be an email address (an @, no space) that is not a secret; nothing was stored — if a key was typed as an account, revoke it", KeyExit.Data);
        }

        var id = KeyIds.Of(input.TokenText);
        if (session.Pool().ExposedIds.Contains(id, StringComparer.Ordinal))
        {
            return new AddOutcome.Refused(ExposedIsFinal, KeyExit.Data);
        }

        if (entries.FirstOrDefault(entry => entry.Record.Id == id) is { } same)
        {
            return new AddOutcome.AlreadyThere(same.Record.Label);
        }

        if (KeyLabels.Refusal(input.Label, entries.Where(entry => entry.Record.Id != replacing).Select(entry => entry.Record.Label)) is { Length: > 0 } label)
        {
            return new AddOutcome.Refused(label, KeyExit.Data);
        }

        return entries.Count >= MaxKeys
            ? new AddOutcome.Refused($"the pool holds {MaxKeys} keys, the most it keeps — remove one first", KeyExit.Data)
            : Written(session, entries, input, id);
    }

    private AddOutcome Written(KeySession session, List<KeyEntry> entries, KeyInput input, string id)
    {
        var record = new KeyRecord { Id = id, Label = input.Label, Account = input.Account, Source = input.Source, AddedUtc = access.NowUtc };
        try
        {
            var problem = session.SaveToken(id, input.TokenText);
            if (problem.Length == 0)
            {
                problem = session.SaveRecord(record, durable: true);
            }

            if (problem.Length > 0)
            {
                session.DeletePair(id);

                return new AddOutcome.Refused(KeyFolder.NotKeptSentence(access.Folder.Root), KeyExit.Io);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            session.DeletePair(id);

            return new AddOutcome.Refused($"the key store could not be written: {e.Message}", KeyExit.Io);
        }

        var entry = new KeyEntry(record, true, string.Empty);
        entries.Add(entry);

        return new AddOutcome.Added(entry);
    }

    // -------------------------------------------------------------------------------------------------------------
    // relabel, disable, enable, remove
    // -------------------------------------------------------------------------------------------------------------

    public KeyResult<KeyRow> Relabel(string id, string label, int? expectedRevision = null) =>
        Change(id, expectedRevision, (entries, entry) =>
            KeyLabels.Refusal(label, entries.Where(other => other.Record.Id != id).Select(other => other.Record.Label)) is { Length: > 0 } refusal
                ? KeyResult<KeyRecord>.Refused(refusal)
                : KeyResult<KeyRecord>.Of(entry.Record with { Label = label }));

    /// <summary>Stops NEW sessions picking the key; a session that has it ends its turns with it. A dead key stays dead.</summary>
    public KeyResult<KeyRow> Disable(string id, int? expectedRevision = null) =>
        Change(id, expectedRevision, (_, entry) =>
            KeyResult<KeyRecord>.Of(entry.Record.State == KeyStates.Dead ? entry.Record : entry.Record with { State = KeyStates.Disabled, SyncDisabled = false }));

    /// <summary>Makes the key usable: also ends <c>dead</c> and a cooldown — but never an EXPOSED key: its text was seen where it must never be, so only a new key can replace it.</summary>
    public KeyResult<KeyRow> Enable(string id, int? expectedRevision = null) =>
        Change(id, expectedRevision, (_, entry) =>
            entry.Record.DeadReason == KeyDeadReasons.Exposed
                ? KeyResult<KeyRecord>.Refused(ExposedIsFinal)
                : KeyResult<KeyRecord>.Of(entry.Record with { State = KeyStates.Enabled, DeadReason = string.Empty, SyncDisabled = false, CooldownUntilUtc = null, CooldownFrom = string.Empty, ConsecutiveCooldowns = 0 }));

    /// <summary>What a person is told when an exposed key is enabled or added again: the text is burnt.</summary>
    internal const string ExposedIsFinal = "this key's text was found where it must never be (its job's output or the coder's files): it is dead for good — revoke it at the vendor, remove it, and add a NEW key";

    private KeyResult<KeyRow> Change(string id, int? expectedRevision, Func<IReadOnlyList<KeyEntry>, KeyEntry, KeyResult<KeyRecord>> change)
    {
        if (!KeyIds.IsValid(id) || !access.Folder.Exists)
        {
            return KeyResult<KeyRow>.Refused(NoSuchKey);
        }

        return access.Run(create: false, session =>
        {
            var entries = session.Entries();
            if (Stale(session, expectedRevision))
            {
                return KeyResult<KeyRow>.Refused(ChangedElsewhere);
            }

            if (entries.FirstOrDefault(entry => entry.Record.Id == id) is not { } entry)
            {
                return KeyResult<KeyRow>.Refused(NoSuchKey);
            }

            var changed = change(entries, entry);
            if (changed.Value is not { } record)
            {
                return KeyResult<KeyRow>.Refused(changed.Refusal, changed.Code);
            }

            session.SaveRecord(record);
            BumpRevision(session);

            return KeyResult<KeyRow>.Of(KeyStoreAccess.RowOf(entry with { Record = record }, access.NowUtc));
        });
    }

    /// <summary>Deletes the pair, the key file first.</summary>
    public KeyResult<KeyRemovedAnswer> Remove(string id, int? expectedRevision = null)
    {
        if (!KeyIds.IsValid(id) || !access.Folder.Exists)
        {
            return KeyResult<KeyRemovedAnswer>.Refused(NoSuchKey);
        }

        return access.Run(create: false, session =>
        {
            if (Stale(session, expectedRevision))
            {
                return KeyResult<KeyRemovedAnswer>.Refused(ChangedElsewhere);
            }

            if (!session.Entries().Any(entry => entry.Record.Id == id))
            {
                return KeyResult<KeyRemovedAnswer>.Refused(NoSuchKey);
            }

            session.DeletePair(id);
            BumpRevision(session);

            return KeyResult<KeyRemovedAnswer>.Of(new KeyRemovedAnswer(id));
        });
    }

    private static bool Stale(KeySession session, int? expected) => expected is { } revision && revision != session.Pool().AdminRevision;

    internal static void BumpRevision(KeySession session)
    {
        var pool = session.Pool();
        session.SavePool(pool with { AdminRevision = pool.AdminRevision + 1 });
    }
}
