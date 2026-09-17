# PLAN — the settings file and the logs ignore the side that partitions everything else

> Status: **plan only, nothing implemented yet.** Scope: `src_mcp/src/Server/SettingsFile.cs`,
> `src_mcp/src/Program.cs`, `src_mcp/tests`, and the shared fixture
> [shared/data-side-vectors.json](../shared/data-side-vectors.json).
>
> Related docs: [module_server.md](../research/module_server.md),
> [PLAN_the_data_directory_moves_and_each_side_keeps_its_own.md](../research/PLAN_the_data_directory_moves_and_each_side_keeps_its_own.md).
>
> Found while building
> [PLAN_the_install_asks_where_the_data_lives.md](../research/PLAN_the_install_asks_where_the_data_lives.md)
> and deliberately left out of it: this is the SERVER's half, it needs a server release, and it is
> wrong today whether or not anything from that plan ships.

## The symptom

`COAI_DATA_SIDE` partitions a chosen data directory so two installations sharing one NAS keep their
own database, sessions and sign-ins. It partitions *almost* everything.

`SettingsFile.DataDirFrom` ([SettingsFile.cs:74-75](../src_mcp/src/Server/SettingsFile.cs#L74-L75))
is a bare `COAI_DATA_DIR` read:

```csharp
public static string DataDirFrom(Func<string, string?> environment) =>
    environment("COAI_DATA_DIR") is { Length: > 0 } dir ? dir : PanelSettings.DefaultDataDir;
```

No side, and no trim — where `PanelSettings.ResolveDataDir`
([PanelSettings.cs:513](../src_mcp/src/Server/PanelSettings.cs#L513)) applies both. That function
locates three things:

| What | Where it is composed | Where it lands today |
|---|---|---|
| `settings.json` | [Program.cs:722](../src_mcp/src/Program.cs), `PanelServiceHost` | `<root>/settings.json` |
| `logs/<day>/` | [Program.cs:715-716](../src_mcp/src/Program.cs) | `<root>/logs/` |
| the `--providers` settings layer | [Program.cs:231](../src_mcp/src/Program.cs) | `<root>/settings.json` |

while `coai.db`, `sessions/` and everything else live in `<root>/<side>/`.

**So on a side-partitioned installation, two sides share one settings file and one log directory**
— which is the opposite of what the person asked for, and it is silent. Two sides configured
differently overwrite each other's settings, and the extension's own `settings.lock` is contended
between machines that were meant to be independent. `' '` as a value is a configured directory to
this function and an unset one to the other, so they disagree about a whitespace variable too.

## What must be true when this is done

1. One rule. `SettingsFile.DataDirFrom` answers exactly what `PanelSettings.ResolveDataDir` answers,
   including the side, the trim, and the refusal of an unusable side name.
2. The extension agrees: `coaiDataDir()` already resolves the side, and the settings file it writes
   must land where the server reads it. Today they diverge and the divergence is invisible until two
   sides are configured differently.
3. **A migration that loses nothing.** An installation that has been running partitioned already has
   a `settings.json` in the root that both sides have been reading. Moving to `<root>/<side>` must
   not silently start either side on defaults — the existing file is adopted by the side that finds
   no file of its own, or the server says plainly that it is about to.
4. The shared vectors cover it, so the two halves are asserted against each other rather than each
   against itself.

## Open question for the round

Whether `logs/` should be partitioned at all. A shared log directory is arguably *better* — one
place to read when two sides are misbehaving together — and the file name already carries the pid.
The argument against is that a log is written by a server whose whole state is a side's, and reading
two sides' runs interleaved is how the panel's own rounds list became confusing enough to need the
storage section in the first place. This plan assumes partitioned, and the round should decide it.

## Test plan (RED first)

| # | Test | RED symptom expected |
|---|---|---|
| 1 | `src_mcp/tests`: two sides given the same `COAI_DATA_DIR` resolve DIFFERENT settings paths | both resolve to `<root>/settings.json` |
| 2 | a whitespace `COAI_DATA_DIR` means "unset" here exactly as it does in `PanelSettings` | it is treated as a configured directory |
| 3 | an unusable `COAI_DATA_SIDE` refuses here too, rather than silently sharing the root | it resolves and the server starts on a name the rest of the product refuses |
| 4 | a side with no settings file of its own adopts the root's, once, and says so | it starts on defaults and nobody is told |
| 5 | the shared vectors carry a `settingsPath` per case, asserted by both suites | each half is self-consistent and blind to the other |

## The boundary with the notifications plan

> Reciprocal of the *Who builds what* table in
> [PLAN_every_message_is_written_down.md](PLAN_every_message_is_written_down.md), which is
> MANDATORY on both sides — a boundary named once is not a boundary.

**This plan goes FIRST, and the notifications plan's S8 depends on it.** That step writes
`server-notices.jsonl` into the server's data directory and must use `PanelSettings.ResolveDataDir`
rather than `SettingsFile.DataDirFrom` — the bare `COAI_DATA_DIR` read this plan is here to fix. On
a side-partitioned install the two halves would otherwise disagree about where the file is, and
neither would say so.

Until this lands, S8 must not be built.

## Definition of Done

- [ ] Every test above written RED first, with its failure message recorded.
- [ ] Both suites green; the extension's suite green against the same vectors.
- [ ] `research/module_server.md` records the rule and the migration.
- [ ] A server release, since this changes where a running server reads its settings.
- [ ] The diff through the `coai` plan and code rounds, every finding resolved.
