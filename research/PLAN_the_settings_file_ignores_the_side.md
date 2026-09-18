# PLAN — the settings file and the logs ignore the side that partitions everything else

> Status: **IMPLEMENTED, 2026-09-18.** Scope:
> `src_mcp/src/Server/SettingsFile.cs`, `src_mcp/src/Server/PanelSettings.cs`, `src_mcp/tests`, and
> the shared fixture [shared/data-side-vectors.json](../shared/data-side-vectors.json).
>
> Related docs: [module_server.md](module_server.md),
> [PLAN_the_data_directory_moves_and_each_side_keeps_its_own.md](PLAN_the_data_directory_moves_and_each_side_keeps_its_own.md).
>
> Found while building
> [PLAN_the_install_asks_where_the_data_lives.md](PLAN_the_install_asks_where_the_data_lives.md)
> and deliberately left out of it: this is the SERVER's half, it needs a server release, and it is
> wrong today whether or not anything from that plan ships.
>
> **It is the declared prerequisite of S8** of
> [PLAN_every_message_is_written_down.md](../todo/PLAN_every_message_is_written_down.md), whose own text
> reads *"Until this lands, S8 must not be built."* It is being built now because the release it
> needs — `coai-mcp 0.29.0` — went out on 2026-09-18.
>
> **Through its plan round, 2026-09-18: two reviewers, seven findings, all seven accepted**, and one
> consultation that changed a decision the round declined to make. Both are folded in below; the
> places they changed are marked *(round)* and *(consultant)*.

## The symptom

`COAI_DATA_SIDE` partitions a chosen data directory so two installations sharing one NAS keep their
own database, sessions and sign-ins. It partitions *almost* everything.

`SettingsFile.DataDirFrom` ([SettingsFile.cs:74-75](../src_mcp/src/Server/SettingsFile.cs)) is a bare
`COAI_DATA_DIR` read:

```csharp
public static string DataDirFrom(Func<string, string?> environment) =>
    environment("COAI_DATA_DIR") is { Length: > 0 } dir ? dir : PanelSettings.DefaultDataDir;
```

No side, and no trim — where `PanelSettings.ResolveDataDir`
([PanelSettings.cs:513](../src_mcp/src/Server/PanelSettings.cs), currently `private static`) applies
both, treats whitespace as unset, and refuses an unusable side name loudly rather than falling back
to the shared root.

**It has seven callers**, and each of them is a place where the two halves of the product disagree:

| Caller | What it decides |
|---|---|
| `Program.cs:350`, `Program.cs:418` | one-shot modes that read settings |
| `Program.cs:1353-1354` | the LOG ROOT, via `CoaiLogPath.RootFor(DataDirFrom(…))` |
| `Program.cs:1360` | the `--providers` settings layer |
| `PanelServiceHost.cs:75` | the settings layer the server runs on |
| `PanelServiceHost.cs:91` | the file a change watcher stats |

**So on a side-partitioned installation, two sides share one settings file and one log directory** —
the opposite of what the person asked for, and silent. Two sides configured differently overwrite
each other's settings, and the extension's own `settings.lock` is contended between machines that
were meant to be independent. `' '` as a value is a configured directory to this function and an
unset one to the other, so they disagree about a whitespace variable too.

## What must be true when this is done

**1. One rule.** `DataDirFrom` answers exactly what `ResolveDataDir` answers. The intended shape is
to widen `ResolveDataDir`'s visibility and CALL it — a second implementation of a resolver is the
defect this plan is about, so copying its logic would be committing it again.

**2. The extension agrees.** `coaiDataDir()` already resolves the side, and the settings file it
writes must land where the server reads it. Today they diverge and the divergence is invisible until
two sides are configured differently.

**3. A migration that loses nothing, and cannot be half-done.** *(round, three findings)*

**4. The shared vectors cover it, and a LIVE test proves the two runtimes agree.** *(round)*

## The logs are partitioned, and that is a consequence rather than a change *(consultant)*

The plan asked its round to decide whether `logs/` should be partitioned at all. The round did not
decide it; a consultation did, and it **refuted the argument this plan was leaning on**.

- The plan said the file name already disambiguates writers, since it carries the app and the pid.
  That is true **only within one machine**. `CoaiLogPath.For`
  ([CoaiLogPath.cs:30-35](../src_mcp/service_defaults/CoaiLogPath.cs)) is
  `Path.Combine(logsRoot, utcNow.ToString("yyyy-MM-dd"), $"{appName}-{utcNow:HH-mm-ss}-{pid}.log")` —
  deterministic on exactly those four inputs, with **no machine component anywhere in it**, and
  [CoaiLogging.cs:55](../src_mcp/service_defaults/CoaiLogging.cs) opens it with `shared: false`. Two
  machines on one NAS root, both `coai-mcp`, both starting in the same UTC second with the same pid,
  target one file neither is prepared to share. A condition, not an observation — but it removes the
  argument, because side attribution is then carried by nothing at all.
- **And there is no call site to change.** `Program.cs:1353-1354` is already
  `CoaiLogPath.RootFor(SettingsFile.DataDirFrom(…))`, so the log root flows through the resolver this
  plan corrects. The plan claimed "one call site either way, `Program.cs:715-716`" — stale line
  numbers and a stale premise, neither re-read before the round. **That inverts which option is
  cheap**: leaving the logs shared would mean ADDING a deliberate exception.
- The settings-corruption asymmetry the plan offered as its reason **does no work here** and is
  dropped. It is about the other half of the change.

**Historical logs stay at `<root>/logs/`** and are documented rather than moved: the side of an old
run cannot be recovered from its name, so moving them would be a guess written to disk.

## The migration, in the order it must happen *(round: three findings, two Blocking)*

An installation running partitioned already has a `settings.json` in the root that both sides have
been reading. Three ways to get this wrong were named, and all three are avoided by one protocol:

| Way to get it wrong | What happens | What this plan does |
|---|---|---|
| Side A **moves** the root file | side B starts next, finds neither file, and silently reverts to defaults | the root file is **never removed** |
| A **copy** is published directly | a kill mid-write leaves a partial file, and the existence check then skips adoption for ever | written to a temporary sibling, validated by PARSING it, then published by rename |
| Two sides start **at once** on a NAS | both read and write the root file with no lock | adoption happens under the same `settings.lock` the rest of the product uses |

The protocol, exactly:

1. If `<root>/<side>/settings.json` exists, nothing happens. This is the steady state and it costs
   one `File.Exists`.
2. Otherwise, under `settings.lock`: read `<root>/settings.json`. If it is absent, nothing happens
   and the side starts on defaults, which is a genuinely new installation.
3. Parse what was read. **A file that does not parse is not adopted** — it is named and left alone,
   because publishing an unreadable file to a new location would be copying a defect into a second
   place.
4. Write it to `<root>/<side>/settings.json.<pid>.tmp`, then `File.Move(tmp, dest, overwrite: false)`.
   **Not overwriting** is what makes a concurrent winner safe: the loser's move throws, it says so,
   and the winner's file stands.
5. Say it happened, once, naming both paths. An adoption nobody is told about is the silent default
   this plan exists to prevent.

**A fresh side adopts the root file too, and that is deliberate.** It is what that side would have
read yesterday, so adopting preserves behaviour rather than changing it; starting on defaults would
be the surprise. The root file being retained means this stays true for every side, in any order.

## `--providers` is the same seam, not a neighbouring one *(round)*

`--providers` can run **before** any normal startup, read an absent side file, and report defaults
for an installation whose configuration exists. Since `Program.cs:1360` resolves through
`DataDirFrom`, the adoption has to sit where every caller reaches it rather than in the server's
startup path — so it belongs in the settings READ, not in `Main`.

The consultant named the check for it directly: **invoke `--providers` first with only root settings
present, then start normally; both must read the same intended side settings.**

## Test plan (RED first)

| # | Test | RED symptom expected |
|---|---|---|
| 1 | two sides given the same `COAI_DATA_DIR` resolve DIFFERENT settings paths | both resolve to `<root>/settings.json` |
| 2 | a whitespace `COAI_DATA_DIR` means "unset" here exactly as in `PanelSettings` | it is treated as a configured directory |
| 3 | an unusable `COAI_DATA_SIDE` refuses here too | it resolves, and the server starts on a name the rest of the product refuses |
| 4 | the LOG ROOT is partitioned, through the same resolver and with no rule of its own *(round)* | it is `<root>/logs` for every side |
| 5 | a side with no settings file of its own adopts the root's, once, and says so | it starts on defaults and nobody is told |
| 6 | the root file SURVIVES adoption, and a second side adopts it too *(round)* | side B finds nothing and starts on defaults |
| 7 | a kill between writing the temp file and publishing leaves adoption still possible *(round)* | a partial destination blocks it for ever |
| 8 | two adopters racing: one publishes, the other is refused and says so, and neither file is lost *(round)* | last writer wins silently |
| 9 | a root file that does not PARSE is named and not adopted *(round)* | it is copied to the side and the defect with it |
| 10 | `--providers` run FIRST on a legacy root-only installation reads the same settings a normal start would *(round, consultant)* | it reports defaults for an installation that has configuration |
| 11 | the shared vectors carry a `settingsPath` and a `logsPath` per case, asserted by both suites | each half is self-consistent and blind to the other |
| 12 | a LIVE check: the extension's runner executes the built `coai-mcp` and both report the same resolved directory for the same environment *(round)* | two implementations agree with a JSON file and not with each other |

Test 12 is the one the conventions require and the one a fixture cannot replace: *"a green suite is
not evidence about a contract with TWO IMPLEMENTATIONS"*. Path separators, normalisation and
environment reading differ between runtimes, and a static vector file cannot see it.

## The boundary with the notifications plan *(round: it must be a table)*

Reciprocal of the *Who builds what* table in
[PLAN_every_message_is_written_down.md](../todo/PLAN_every_message_is_written_down.md) — a boundary named
once is not a boundary.

| Item | Built by | What the other one's part is |
|---|---|---|
| `DataDirFrom` answering what `ResolveDataDir` answers | **this plan** | S8 consumes it and adds nothing to it |
| The settings-file adoption and its lock | **this plan** | S8 never writes `settings.json` |
| The log root moving with the side | **this plan** | S8's file is not a log |
| `server-notices.jsonl`: its path, its writer, its redaction, its caps | **S8** | this plan neither creates the directory for it nor names it |
| The seam vectors' `settingsPath` / `logsPath` entries | **this plan** | S8 adds its own entries beside them and reads neither |

**Disjoint**: this plan touches no notice, no ledger and no serialiser; S8 touches no settings file,
no log root and no migration. The single thing they share is the resolver, and this plan is the one
that fixes it.

**This plan goes FIRST.** S8's own text says so.

## What shipped, and where it differs from the plan

Built on `fix/the-settings-file-knows-its-side`, 2026-09-18. The shape above is what landed; four
things are NOT what this document said before the code round, and they are the useful part of the
record.

**1. `Layer` has a required sink, which this plan never asked for.** The plan said the adoption
should return what a person needs told, and the first build did — then dropped it on the floor, while
the comment beside it claimed the startup path logged what came back. Nothing anywhere called
`AdoptRootSettings`. Six reviewers across three vendors found the same thing independently in the
code round, and they were describing the exact defect
[PLAN_every_message_is_written_down.md](../todo/PLAN_every_message_is_written_down.md) exists for: a
sentence composed and shown to nobody. `Layer(dataDir, environment, Action<string> said)` now takes a
sink with **no default**, so the compiler asks every caller where its sentences go — `ServeAsync` and
`PanelServiceHost.Build` pass the log, the two one-shot modes pass `Note`, which is stderr, because
their stdout carries JSON. Planting the discard turns two tests red with *the collection is empty*.

**2. The race test did not test the race.** As first written it created the destination BEFORE
calling the adoption, so the existence check returned early and `File.Move(overwrite: false)` — the
guarantee the case is named after — was never reached. It would have passed with the guard deleted,
which is the same failure as the role-deletion nonce test. It drives a hook now
(`SettingsFile.BetweenTheCheckAndThePublication`), so the other window publishes in the one instant
between this side's check and its rename. Watched failing with `overwrite: true` planted: *the loser
of the race overwrote the winner's file*.

**3. The resolver property covers CLASSES of input, and agreement includes refusing alike.** The
first version crossed five directories with five side names, all of them valid; a reviewer was right
that the interesting inputs are the ones where the two could disagree by one THROWING and the other
answering. It now crosses six directory shapes with eleven side shapes — `.`, `..`, separators,
upper case, padding — and compares the refusal message when there is one.

**4. Four reviewers asked why the adoption takes no lock, and the answer went into the code rather
than only into a resolve reason.** The lock is the extension's (`settings.lock`, around a WRITE of
this same file); the server has never had a client for it and does not need one, because
`File.Move(overwrite: false)` makes this create-if-absent and atomic — stronger than an advisory lock
file that can be stale, broken or ignored. The same paragraph answers the other recurring question,
why a READ does a write at all: because the alternative puts `--providers` on defaults for a machine
whose configuration exists, and because every failure here is caught and returned as a sentence, so a
read stays a read on a filesystem that will not take the write.

Rejected with reasons, and recorded here because a reader will wonder: moving the adoption into
`DataDirFrom` (it is the LOG ROOT's resolver — resolving a log path would write a settings file);
`Directory.CreateDirectory` on the wrong path (it is already the parent); path traversal through a
side name (`IsSafeSide` admits no separator and refuses `.` and `..`); an identical-path check that
already exists; and *adoption runs on every property access* (it runs once per `Layer`, of which
there are four in the product, and after the first adoption it costs one `File.Exists`).

## Definition of Done

- [x] Every test above written RED first, with its failure message recorded.
- [x] Both suites green; the extension's suite green against the same vectors.
- [x] `research/module_server.md` records the rule, the migration protocol and where historical logs
      remain — and `research/module_tests.md` names the scenario and the seam's fifth leg.
- [ ] A server release, since this changes where a running server reads its settings.
- [x] Through `review_plan` (seven of seven accepted) and `review_code`.
