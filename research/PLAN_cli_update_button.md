# PLAN — an update button per reviewer CLI, and colour on the sections that open

> Status: **IMPLEMENTED, 2026-09-01.** Scope: `src_vs_code/src/cliVersions.ts` (new),
> `vendorTerminal.ts`, `panelView.ts`, `panelProvider.ts` and their tests. No server change.
>
> Related docs: [module_extension.md](module_extension.md).

## The goal

Two things asked for from the panel:

1. **A third button in the vendor row: update this CLI.** It must say, by its colour, whether there
   is anything to update — green when a newer version is published, grey when the installed one is
   current. Pressing it opens a terminal with the vendor's own update command typed, exactly as ▶
   and ⤓ already do.
2. **The collapsible section headers carry the panel's colours** — every header that opens
   (`REVIEWERS`, `PROMPTS PER ROUND`, `THE GATE`, …), in the same tone palette the role boxes use.

## Why the first one is not obvious

A button that always looks the same teaches nothing: the question a person actually has is *"is
there anything to do here"*, and today they answer it by leaving the panel. So the button has to
know two versions.

**Installed**: run the vendor's own binary with `--version`. The panel already spawns a CLI for
`readCodexModels` (`panelProvider.ts:467`), and `providers` proves the output is parseable —
`codex-cli 0.152.0`, `agy` answers `1.1.23`.

**Latest**: measured 2026-09-01, every vendor this build knows has an OFFICIAL source, which is the
operator rule that already governs the install button (`vendorTerminal.ts:150`, `OFFICIAL_SOURCES`):

| runtime | source | verified |
|---|---|---|
| codex | `registry.npmjs.org/@openai/codex/latest` | package name already in `PACKAGE` |
| gemini | `registry.npmjs.org/@google/gemini-cli/latest` | same |
| claude | `registry.npmjs.org/@anthropic-ai/claude-code/latest` | same |
| antigravity | `…run.app/manifests/<platform>.json` → `.version` | **the endpoint Google's own `install.sh` reads** (line 99 of the script); answered `1.1.23` for `linux_amd64`, `darwin_arm64` and `windows_amd64` |

The antigravity one matters: the previous belief was that this vendor has no machine-readable
version at all, and that belief is what produced the false "no Linux CLI" sentence
([RESULTS_predelivery_campaign.md](RESULTS_predelivery_campaign.md) §4b). It was checked
this time rather than assumed.

## Constraints

- **No new dependency.** `fetch` is in the extension host already (`installer.ts:108` uses it).
- **Official sources only.** The update command may only be what the vendor itself publishes; the
  existing `OFFICIAL_SOURCES` test must cover the update path too.
- A version check must never block a repaint or throw: an offline machine is not an error worth
  showing, which is how `latestServerVersion` already behaves (`installer.ts:99`).
- Cached like the server check, so opening the panel does not spawn four processes and four fetches
  every few seconds.

## Build order

1. **`cliVersions.ts`** — pure first: `parseCliVersion(output)` (a semver out of whatever the CLI
   prints), `updateAvailable(installed, latest)` (numeric compare, not string), `versionSourceFor
   (runtime, platform)` (npm package or manifest URL). Then one `latestCliVersion(source)` that
   fetches and returns `undefined` on any failure.
2. **`vendorTerminal.ts`** — widen `vendorInstall(vendor, platform, mode: 'install' | 'update')`
   rather than adding a second table: npm gains `@latest`, antigravity's script installs the newest
   either way. One implementation, per reuse-first.
3. **`panelView.ts`** — the ⟳ button after ⤓, green with an update and grey without; a `cliStatus`
   field on `PanelState`; `--tone-*` on every `.section > summary`.
4. **`panelProvider.ts`** — `readCliStatus()` with a cache, and the `updateVendorCli` command (the
   `never` guard in `run()` forces the case to exist).

## Test plan

RED first for each:

- `parseCliVersion` on the strings the real CLIs print, including one with no version at all.
- `updateAvailable('0.9.0', '0.10.0')` — the case a string compare gets WRONG, which is why it is a
  test and not a `<`.
- `versionSourceFor` names an official host for every runtime this build ships, and `undefined` for
  one it does not know — no guessed registry.
- The panel renders one update button per vendor, green exactly when its status says an update is
  available.
- Every section header carries a tone class, and the tone list has no gap for a section that exists.

## Definition of Done

- [ ] Every rule above holds with a test that was watched fail.
- [ ] `npm` and the antigravity manifest are the only version sources, and the OFFICIAL_SOURCES test
      covers `mode: 'update'`.
- [ ] A machine with no network renders the panel unchanged, with the button grey.
- [ ] `research/module_extension.md` records where a CLI version comes from.

## What shipped differently

**Step 2 of the build order was deleted rather than built, and the vendors are why.** The plan
widened `vendorInstall` with a `mode: 'install' | 'update'` so npm could gain `@latest`. Checking
each vendor's own site first — which is the rule that produced this section — showed there is no
second command to build: OpenAI's quickstart prints the identical `curl … install.sh | sh` under
*Install Codex* AND under *Update Codex*; Anthropic's native install is the same `install.sh` /
`install.ps1`; `agy` has no `update` subcommand at all, checked against the binary's own `--help`.
So the button reuses `vendorInstall` verbatim and the whole feature is the two version numbers.

**`executableFor` was extracted** from `vendorTerminal()` instead of the version reader deciding for
itself which binary a vendor is. Two answers to that question would have drifted, and the one that
drifts is the one nobody looks at. The quoting stayed behind: a command LINE needs a path with a
space quoted and `spawn` must never see the quotes.

**Two of my own defects were caught by tests already in this repository**, both while adding this:
a second `.section > summary` rule (the duplicate-CSS check, which exists because `.usage` was once
defined twice and dimmed the spending section), and a `PANEL_COMMANDS` entry the `never` guard
demanded before the button could compile — the guard added after *Update coai-mcp* shipped dead.

**One assertion was written too strong and corrected**: "every tone is a `--vscode-charts-*` token"
is false, because `--tone-code` is a border. The rule that IS true — a `--vscode-*` token with a hex
fallback — replaced it. Inventing a rule the code never had is how a test starts lying.
