# PLAN — ⟳ updates Codex with `codex update`, and the panel stops saying no CLI can update itself

> Status: **plan only, nothing implemented yet (2026-10-03).** Plan gate proceed (1 of 1, gemini; 3 accepted, 1 rejected). Scope: `src_vs_code/src/vendorTerminal.ts` (`vendorUpdate`),
> `panelProvider.ts` (`updateVendorCli`), `helpContent.ts` + `helpRu/Uk/De/Es.ts` (one help sentence in five languages),
> `src/test/vendorUpdate.test.ts`,
> `research/module_extension.md`, `research/module_tests.md`.
>
> Related docs: [module_extension.md](../research/module_extension.md).

## 1. Symptom

The panel's ⟳ for a Codex reviewer re-runs the installer (`npm install -g @openai/codex@latest`) because
`vendorTerminal.ts:52` says Codex has "no update subcommand in the full `codex --help`". It has one:

```
$ codex --version            # this machine, 2026-10-02
codex-cli 0.156.1
$ codex --help | grep update
  update            Update Codex to the latest version
```

On the Team server host, `codex update` took 0.153.4 to 0.160.0 on 2026-10-02. It was added on 2026-04-28
(openai/codex #19933, `Add codex update command`), and the first release that carries it is **rust-v0.126.0**
(`compare rust-v0.125.0...b985768` is *diverged*, `rust-v0.126.0...b985768` is *behind*, i.e. contained).

The help page says something wrong in a second way. `helpContent.ts:116` tells the person that "for every CLI here
re-running the installer IS the update… and `agy` has no update subcommand at all". Since `SELF_UPDATE`
(`vendorTerminal.ts:83`) holds `claude update` and `agy update`, two of four were already false. The module doc's
"There is no update COMMAND" paragraph (`research/module_extension.md:6296`) says the same.

## 2. Goal

- ⟳ for Codex types `codex update` (the vendor's CLI path when one is set, quoted when needed), as `claude` and `agy`
  already do — **when the installed Codex is 0.126.0 or newer**.
- A Codex older than 0.126.0, or one whose version could not be read, still gets the installer. That command works
  on every version, and `codex update` on an old one would fail with "unrecognized subcommand".
- The help sentence and the module doc say what each CLI's update actually is.

## 3. Design

- `vendorTerminal.ts`: `SELF_UPDATE` gains `codex: 'update'`. A new `SELF_UPDATE_SINCE: Record<string, string>` holds
  `codex: '0.126.0'`.
- `vendorUpdate(vendor, platform, installed = '')` self-updates when the runtime has a `SELF_UPDATE` entry and either
  no `SELF_UPDATE_SINCE` entry, or a known `installed` that is not older than it. "Older" is
  `updateAvailable(installed, since)` from `cliVersions.ts:102`: the numeric comparison the ⟳ colour already uses,
  reused rather than written twice. An empty `installed` is unknown, and unknown gets the installer.
- A version counts only when it is exactly `X.Y.Z` (`^\d+\.\d+\.\d+$`). Banner text, an error or anything else is
  unknown, and unknown gets the installer (plan round, gemini: negating `updateAvailable` over garbage would have
  read it as new enough).
- `vendorTerminal.ts` exports `updateFor(status: Readonly<Record<string, CliStatus>>)`, which returns the
  `(vendor, platform) => vendorUpdate(vendor, platform, status[vendor.id]?.installed ?? '')` that `openCliTerminal`
  takes. `panelProvider.ts` `updateVendorCli` passes `updateFor(this.cliStatus)`. That status is what the ⟳ button's
  colour was drawn from, so the command matches what the person was told. Extracted so a test RUNS the version
  choice instead of reading the call site (plan round, gemini).
- `helpContent.ts:116` and its four translations (`helpRu.ts:72`, `helpUk.ts:24`, `helpDe.ts:72`, `helpEs.ts:72`,
  all carrying the same wrong sentence) name each CLI's update: `claude update`, `agy update`, `codex update` from
  0.126.0, and npm's `@latest` install for Gemini and for an older Codex ("the Gemini CLI" — a `gemini` row; Antigravity rows get `agy update`). All five change in one commit: there is
  no guard yet against a stale translation ([PLAN_a_stale_translation_is_invisible.md](../todo/PLAN_a_stale_translation_is_invisible.md)),
  so the test checks every language for the commands.
- Comments: `vendorTerminal.ts:52` and `panelProvider.ts:1543` say the same thing. The correction is recorded the
  way the `agy` one was: the claim was inferred from a list, and running it would have refuted it.

## 4. Build order

1. RED: `vendorUpdate.test.ts`, run against the current code:
   - Codex 0.156.1 → `codex update`.
   - A pinned path → `"<path>" update`.
   - Codex 0.125.0 → the npm install.
   - Unknown version → the npm install.
   - 0.126.0 exactly → `codex update`.
   - The help sentence names `claude update`, `agy update`, `codex update` and 0.126.0 in every language.
2. GREEN: `vendorTerminal.ts`, `panelProvider.ts`, `helpContent.ts`.
3. Teeth: drop the version gate → the 0.125.0 test goes red. Drop the `SELF_UPDATE` entry → the 0.156.1 test goes red.
4. Docs, full `npm test`, lint (complexity 4).

## 5. Test plan

| Test | Proves |
|---|---|
| `vendorUpdate.test.ts`: codex at or above 0.126.0 | ⟳ types `codex update`, through the vendor's own path |
| `vendorUpdate.test.ts`: codex below 0.126.0, or version unknown | ⟳ keeps the installer, which works on every version |
| `vendorUpdate.test.ts`: help text | the help page no longer says no CLI can update itself |

| `vendorUpdate.test.ts`: `updateFor` | the status the ⟳ colour came from picks the command; a vendor missing from it gets the installer |
| `vendorUpdate.test.ts`: a version that is not `X.Y.Z` | banner text or garbage gets the installer, never `codex update` |

What stays untested is the single `updateFor(this.cliStatus)` argument in `PanelProvider`, which no unit test can
construct. There is no source-text assertion for it.

## 6. Definition of Done

- [ ] Plan gate and code gate passed.
- [ ] Every test RED first, then GREEN, teeth recorded.
- [ ] `npm test` green; lint clean; suppression file did not grow.
- [ ] Help text, comments, `research/module_extension.md`, `research/module_tests.md` updated; plan promoted.
- [ ] Extension released (patch), CHANGELOG section on the release PR, Marketplace verified.
