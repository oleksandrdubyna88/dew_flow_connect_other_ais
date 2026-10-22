# PLAN — E2 stories: the Vibe series on `ubuntu-claude`, and the C1 admission that a full pass switches on

> Status: **plan only, nothing implemented yet, 2026-10-22.** Scope: the paid Vibe series on the owner's native Ubuntu host
> `ubuntu-claude` (N-H1, N-V0, N-V1–N-V6) and its record, `research/RESULTS_vibe_confinement.md` §32; one amendment to E1's probe
> plumbing BEFORE the series (every native cell carries the closed keyring road the product will ship); then — only on a full pass —
> the C1 admission in `src_mcp` (Vibe enters `HostAdmission.MeasuredOnNativeLinux`, `VibeLaunch.Built` forces Python `keyring`'s
> failing backend on `HostKind.Linux` only), the extension's half (`ON_NATIVE_LINUX_SINCE.vibe`, per-runtime `HOST_NOTES.linux`, the
> unreadable-banner refusal, the hint, the install note, the help in five languages), the `vibe`/`linux` limitation row, the module
> docs, and the P7 rollout (a live round on a LOCALLY BUILT binary, then the release, then the post-release round, with a rollback
> line). **No Kimi anywhere in this epic.** Epic E2 of
> [PLAN_kimi_and_vibe_on_native_linux.md](PLAN_kimi_and_vibe_on_native_linux.md) (its `## Epics`, `### E2`); cornerstone **C1**,
> whose spec is §2 of this document.
>
> Related docs: the parent plan above (§1–§14, the plan gate 942810c1, the E1 plan round b54f22f5, the cadence consult e9a9b20e);
> [PLAN_kimi_and_vibe_on_native_linux_E1.md](PLAN_kimi_and_vibe_on_native_linux_E1.md) (what E1 built, merged as #914 at
> `3fc30be3b`); [RESULTS_vibe_confinement.md](../research/RESULTS_vibe_confinement.md) §30–§31 (the WSL2 record this one extends);
> [module_runners.md](../research/module_runners.md), [module_extension.md](../research/module_extension.md),
> [module_tests.md](../research/module_tests.md); the promoted parent
> [PLAN_kimi_and_vibe_on_linux.md](../research/PLAN_kimi_and_vibe_on_linux.md) (DL1's open tail, line 22).

This document is self-contained: a reviewer reads only this file. Every `file:line` below was read at `3fc30be3b` on 2026-10-22.

## 1. The goal — what is true when E2 is done

Vibe 2.26.1 is measured on `ubuntu-claude` with the keyring road closed exactly as the product will close it, and the record says
what was predicted, what was observed and what was spent. Then one of two things is true:

- **Every cell passed.** Vibe is admitted on a native Linux host — one entry in the measured set — and its launch there carries
  `PYTHON_KEYRING_BACKEND=keyring.backends.fail.Keyring`, so the only sign-in a coai launch can reach is the linked `~/.vibe/.env`.
  The card, the hint, the install note, the help in five languages and the limitation row say exactly that. A live plan round on a
  locally built binary answered BEFORE the release was tagged; the release carries a rollback line; the post-release round answered.
  WSL2's Vibe launch is byte-for-byte what it was. The series folder is gone from the host, removed by its own recorded path.
- **A cell failed.** §32 records it with its numbers, nothing is admitted, every refusal sentence stays as it is today, and the
  stories that admit (S2–S5) do not run (§5).

Either way: `~/.vibe` on the host is listing-identical and its `.env` `stat`-identical before and after every phase; the probes never
opened, hashed, copied or printed a sign-in; no `kimi` was started by anything in this epic; `plan-lifecycle.mjs` is clean.

From the parent's *Done when* for E2, verbatim in substance: §32 holds N-H1, N-V0, N-V1–N-V6 with predictions, commit, the binaries'
sha256, spend, `~/.vibe` `stat`-identical, and arms a3/a3c recorded as the proof of the closed road; either every cell passed and S-N5,
S-N6, S-N8–S-N12 are green (S-N12's teeth shown red first) and the live round on the local binary answered, then the release and the
post-release round — or a cell failed and the record alone shipped with nothing admitted; WSL2's Vibe launch unchanged (its pins
untouched); the series folder removed by `remove-series.sh`; `plan-lifecycle.mjs` clean.

## 2. Cornerstone C1 — the admission and the launch it permits (the spec)

C1 is the boundary between measured evidence and permission to launch with the owner's credentials. E1 plumbed it with nothing
admitted; this section says what the plumbing MEANS, what E2 changes in it, and what every text must say. Where E1's code is cited, it
is what the stories start from.

### 2.1 The admission's inputs — and the rule that an absent fact refuses

`HostAdmission.Refusal(runtime, host, executable, wsl)` (`src_mcp/runners/Platform/HostAdmission.cs:31`) answers empty ("may start") or
a sentence. For `HostKind.Linux` it asks exactly two facts, in this order (`HostAdmission.cs:46-51`):

1. **Membership** — is the runtime's name in `HostAdmission.MeasuredOnNativeLinux` (`:25`, a case-insensitive set, EMPTY at
   `3fc30be3b`)? Asked FIRST. Not a member → today's sentence, whatever the banner says:
   *`{runtime} runs on WSL2 only for now — it is measured on WSL2 and not yet on a native Linux host (DL1 of research/PLAN_kimi_and_vibe_on_linux.md)`*
   (`:48`, `:61-62`).
2. **The read banner** — the `WslGeneration` the host was judged by. A member whose generation is `WslGeneration.Unreadable`
   (`WslGeneration.cs:16`, produced ONLY by `WslGenerations.Of(onLinux, BannerReading)` when `/proc/version` is absent, throws or is
   blank — `:40-45`, `ProcVersion.ReadFrom` `:81-93`) → the DN6 sentence:
   *`{runtime} is refused on this Linux host — the kernel banner /proc/version could not be read, so coai cannot tell a native Linux host from WSL1 (DN6 of todo/PLAN_kimi_and_vibe_on_native_linux.md)`*
   (`:55-56`). A member with a read, non-WSL banner (`WslGeneration.None`) → admitted (`:50`).

**The rule C1 fixes: the ABSENCE of a fact is a refusal, never an admission.** No entry in the set → refused. No banner → refused. There
is no third input: the keyring-session facts (`KeyringSessionFacts.cs:17`, `:29-34`) are RECORDED by `--host-facts`
(`HostFactsReport.cs:19-26`) and decide nothing — the admission must never read them (DN10, the cadence consult). A future reader who
adds a fact to this arm adds it as a REFUSAL condition on a member, never as a road into the set. The set has no default member, no
settings switch, no environment switch: a runtime enters it by an edit to `HostAdmission.cs` that names the RESULTS section its cells
are in. The same rule holds in the extension (§2.5): a runtime with no `ON_NATIVE_LINUX_SINCE` entry is refused on `linux`; a host whose
banner could not be read is refused on every runtime.

`HostAdmission.Refusal` has exactly two callers — `VibeLaunch.HostRefusal` (`src_mcp/runners/Vibe/VibeLaunch.cs:79`) and
`KimiLaunch.HostRefusal` (`src_mcp/runners/Kimi/KimiLaunch.cs:29`) — and `HostFactsReport.Of` reads both (`HostFactsReport.cs:24`). No
other runtime can receive a native-Linux sentence.

### 2.2 Where Vibe enters the set, and how

**One line.** `HostAdmission.MeasuredOnNativeLinux` becomes `new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Vibe" }`
(`HostAdmission.cs:25`), conditioned in prose, not in code, on the recorded series: the remark at `:22` names
*research/RESULTS_vibe_confinement.md §32 (Vibe 2.26.1, ubuntu-claude, 2026-10-xx)* as the evidence the entry rests on, and the class
remark (`:7-11`) stops saying the set is empty and says which runtime is in it and why Kimi is not (E3's). `"Kimi"` is NOT added by E2
under any outcome. There is no second admission and no second copy of any sentence: `Wsl2Only`, `BannerUnreadable`,
`NothingMeasured`, `WslBridge` (`:55-65`) are untouched.

What the entry changes, and nothing else: on `HostKind.Linux` with a read non-WSL banner, `VibeLaunch.HostRefusal` answers empty, so
`VibeLaunch.TakeAsync` (`VibeLaunch.cs:143-154`) goes on to the environment check, the budget, the model, the home, the sign-in link
(`VibeSignIn.For` already links on every host but Windows, `VibeSignIn.cs:29-32`), `--version`, the shape and the manifest — the path WSL2
already takes. The `linux` manifest entry (`shared/vibe-home-manifest.json`, 2.26.1 / `linux` / 12 pinned hashes, measured
2026-10-16) serves both hosts: `VibeHost.Platform` is `linux` for WSL and native alike (`VibeHost.cs:30-36`), which is why N-V4 must
prove the pinned bytes are equal on the native host before the entry is made (§3, the stop rule).

### 2.3 DN10 — the closed keyring road in the launch the admission permits

**The variable and its value.** `PYTHON_KEYRING_BACKEND` = `keyring.backends.fail.Keyring`. Python `keyring` 25.7.0 (Vibe 2.26.1's
tool environment) takes the backend from this variable BEFORE its config file and before `max(available)`; `fail.Keyring` raises
`NoKeyringError` on every call, and Vibe catches `KeyringError` and reads "no key" (the parent, DN10, read from the source on the lead's
machine; the host's own versions are recorded by N-V0). So with the variable set, no backend on the host — a desktop Secret Service,
KWallet, KeePassXC, a `keyringrc.cfg` under the real `HOME` — is reachable from a coai launch, and the sign-in can come only through
the `.env` link coai makes. **It is proven, not assumed, by N-V1's arms a3 and a3c on the host** (§3); until that runs, the claim is
marked "to be proven" wherever it is written.

**Where the product adds it — `VibeLaunch.Built`, on `HostKind.Linux` ONLY.** Today `Built(request, home, model)`
(`VibeLaunch.cs:97-108`) makes exactly four names on top of `ProcessEnvironment.Minimal` — `VIBE_HOME`, `VIBE_CLI=python`,
`VIBE_ACTIVE_MODEL`, `PYTHONUTF8=1` — and `EnvironmentRefusal` (`:87-90`) admits exactly that: `Confined` (`:111-112`, nothing
inherited but the minimal names) and `CarriesTheThree` (`:115-119`, `Environment.Count == 4`, only those keys, the two pinned values, a
home named). E2 makes the host an input of both:

- `VibeLaunch.Built(request, home, model, HostKind host)`: on `HostKind.Linux` the dictionary gains the fifth entry,
  `[KeyringBackendVariable] = KeyringBackendClosed` (two new `public const string`s beside `Utf8Variable`, `:58-61`); on every other
  host the four names, byte-identical to today.
- `VibeLaunch.EnvironmentRefusal(request, HostKind host)`: on `HostKind.Linux` it demands exactly FIVE names with the fifth's value
  `keyring.backends.fail.Keyring` (a fifth name with another value, or a four-name launch, is refused); on `HostKind.Windows`,
  `HostKind.Wsl` and every other host it demands exactly the FOUR and REFUSES a launch carrying the fifth. The refusal sentence (`:90`)
  names the five on Linux and the four elsewhere.
- Every caller passes its host's kind: `VibeRuntime.Build` (`VibeRuntime.cs:141-156`; the runtime holds its `VibeHost`, `:41`),
  `VibeRuntime.ProbeVersionAsync` (`:170-179`, the `providers` probe's `--version`), `VibeConsultant.Build` (`VibeConsultant.cs:72-75`,
  through its `inner` runtime), and `VibeLaunch.TakeAsync` (`:146-147`, which already holds `host`). So the review launch, the consultant
  launch, the question row and the `--version` probe all carry the closed road on a native host — one builder, one control, as D17
  demands — and `VibePrintsUtf8OnEveryPathTests.EveryVibePath_RunsInUtf8Mode` (`src_mcp/tests/VibePrintsUtf8OnEveryPathTests.cs:41-67`)
  gains a Linux leg that walks the same five paths.

**Never inherited from the server's environment.** `Built` sets `InheritsEnvironment = false` and `Passthrough = ProcessEnvironment.Minimal`
(`:99-100`); the Unix minimal list (`ProcessEnvironment.cs:90-91`) is `PATH HOME USER LOGNAME SHELL LANG LC_ALL LC_CTYPE TERM TMPDIR
TMP TEMP DOTNET_ROOT` and does not name `PYTHON_KEYRING_BACKEND`, so a value in coai-mcp's own environment never reaches Vibe; the value
Vibe sees is the one `Built` wrote. S-N12 pins all three legs (§4, S2): the fifth name on Linux, its absence (and refusal) on WSL2 and
Windows, and coai's value winning over a server-environment value.

**Never on WSL2 or Windows.** WSL2 launches are admitted as they were measured (RESULTS_vibe §31) WITHOUT the variable; closing the road
there would be a change to a measured launch, and E2 does not make it. The pins that keep WSL2 and Windows byte-identical are in §2.6.

**Kimi's launch never carries it** — Kimi has no keyring road (its sign-in is a file coai links); `KimiEnvironment` is not touched.

**The probes measure this launch, not today's.** E1 put the forced backend into M-V7's arms a, a2 and a3 only (`scripts/lib/vibe.mjs:284-298`).
But on a native host the product closes the road in EVERY launch — the signed-in review launches that N-V2 (M-V6, M-V1) and N-V3 (M-V5)
measure, and the manifest's launches that N-V4 measures. A signed-in launch under `fail.Keyring` is the one thing no arm has yet run: if
Vibe 2.26.1 touches `keyring` on a signed-in start (a `set_password` it does not catch, say), the product's native launch would fail where
the probe's passed. So **E2 amends E1's plumbing before the series (story S0): on a native Linux host the probe's child environment and
the manifest script's built environment carry the fifth name on every launch; arm a3c, whose purpose is to reach a keyring, explicitly
drops it.** On WSL2 the probes are unchanged (the host decides, by the banner, the way the product decides). This is the one amendment
E2 makes to E1's instruments, and the reason is the parent's own rule (DN5 item 5): the arms measure the launch E2 ships.

### 2.4 The read banner — the server half (E1) and the extension half (E2)

Server: done in E1 — `WslGenerations.ForHost(kind)` (`WslGeneration.cs:54-57`) reads this kernel for WSL AND native Linux; `VibeHost.ThisProcess`
(`VibeHost.cs:19-24`) and `KimiHost.Of` (`KimiHost.cs:17-18`) take it; an unread or blank `/proc/version` is `Unreadable`, and the
admission refuses a member on it (§2.1). E2 changes nothing here but the remark.

Extension (DN6's second half, E2's): `hostSide.ts` narrows a Linux host by the banner (`hostKindOf`, `hostSide.ts:161-168`) and, when the
read FAILS, `bannerOf` (`:186-197`) warns once and returns `''`, which `hostKindOf` reads as `linux` — "a refusal, never a promise" while
`linux` refused Vibe. Once `linux` RUNS Vibe that fallback would promise a row the server refuses. So the union `HostKind`
(`hostSide.ts:143`, `'windows' | 'wsl2' | 'wsl1' | 'linux' | 'darwin'`) gains **`'linux-unread'`**: a Linux host whose kernel banner could
not be read, or read blank (the server's rule, `WslGeneration.cs:87`: whitespace is not a read banner — the two sides must agree, else the
card promises what the launch refuses). `hostKind` returns it on a failed read, still warning once; `hostKindOf('linux', '')` returns it
too. It is a member of the union rather than a flag beside it so that every exhaustive table over the union — `VIBE_NOTES`
(`vendorTerminal.ts:525-531`), `KIMI_NOTES` (`:635`) — fails to COMPILE until it says what a `linux-unread` host is told, and the one
partial table, `HOST_NOTES` (`runtimeSince.ts:150-157`), gets its arm written in S3 and pinned by S-N10. What it says is in §2.5. On
`linux-unread` every runtime with a Linux entry is refused: Vibe with the banner sentence, Kimi with its native sentence (Kimi is refused
on native Linux in E2 anyway).

### 2.5 The sentences — pinned words

Every text that today says native Linux is refused changes in the same direction and names the same two facts: the release, and the
closed keyring. The words below are what S-N8 and S-N11 search for; a coder may add around them, never rephrase them.

**Server (not shown to a person; the remarks):** `HostAdmission.cs:7-11` and `:22`, `VibeLaunch.cs:72-78` say *Vibe is measured on a
native Linux host (research/RESULTS_vibe_confinement.md §32, Vibe 2.26.1, 2026-10-xx) and its launch there closes the keyring road
(`PYTHON_KEYRING_BACKEND=keyring.backends.fail.Keyring`, DN10); Kimi is not yet measured there (E3)*.

**The extension's one English sentence** (a constant `VIBE_NATIVE_LINUX` in `vendorTerminal.ts`, imported by `vendors.ts`'s hint and
by the help where English is read; translated by hand into the four other files):

> `On a native Linux host coai-mcp runs Vibe from coai-mcp 0.64.0 and turns Vibe's keyring off there, so it signs in only through ~/.vibe/.env, which coai links and never copies.`

with `0.64.0` interpolated from `VIBE_ON_NATIVE_LINUX_SINCE` (§2.7). The four translations S-N11 pins, each carrying `WSL2`, `0.64.0`
(interpolated), `~/.vibe/.env` and the keyring word of that language:

| Language | File | Sentence |
|---|---|---|
| de | `helpDe.ts:87` (the *Add a model* paragraph), `:63` (the question consultant) | `Auf einem nativen Linux-Host führt coai-mcp Vibe ab coai-mcp 0.64.0 aus und schaltet dort Vibes Schlüsselbund ab, sodass es sich nur über ~/.vibe/.env anmeldet, das coai verlinkt und nie kopiert.` |
| es | `helpEs.ts:87`, `:63` | `En un host Linux nativo coai-mcp ejecuta Vibe desde coai-mcp 0.64.0 y desactiva allí el llavero de Vibe, así que solo inicia sesión mediante ~/.vibe/.env, que coai enlaza y nunca copia.` |
| ru | `helpRu.ts:87`, `:63` | `На нативном Linux-хосте coai-mcp запускает Vibe начиная с coai-mcp 0.64.0 и отключает там связку ключей Vibe, так что он входит только через ~/.vibe/.env, который coai связывает и никогда не копирует.` |
| uk | `helpUk.ts:31`, `:127` | `На нативному Linux-хості coai-mcp запускає Vibe починаючи з coai-mcp 0.64.0 і вимикає там в’язку ключів Vibe, тож він входить лише через ~/.vibe/.env, який coai зв’язує і ніколи не копіює.` |

The question-consultant article's parenthesis — today `(on a native Linux host or macOS coai-mcp refuses it for now)` (`helpContent.ts:335`
and the four files, the `QUESTION_ROW` table of `vibeOnLinux.test.ts:299-305`) — becomes `(on a native Linux host from coai-mcp 0.64.0,
with Vibe's keyring off; on macOS coai-mcp refuses it for now)` in each language, by the same words as the table.

**The sentences that must be GONE from every text** (the test's `GONE` table, `vibeOnLinux.test.ts:286-297`, gains them): `On a native
Linux host coai-mcp refuses Vibe for now — it is measured on WSL2 only` (en, `vendors.ts:302`, `vendorTerminal.ts:511-513`,
`helpContent.ts:115`), `Natives Linux ist vorerst nur unter WSL2 gemessen`-shaped sentences and their siblings in the de/es files, `На
нативном Linux-хосте coai-mcp пока отклоняет Vibe — он измерен только на WSL2` (ru), `На нативному Linux-хості coai-mcp поки відхиляє
Vibe — він виміряний лише на WSL2` (uk). `vibeOnLinux.test.ts:35`'s `NATIVE_REFUSED` constant is deleted with them.

**The card (`HOST_NOTES`, `runtimeSince.ts:150-157`), per runtime now:**

- `linux`, Vibe, server ≥ `0.64.0` → `''` (no note; the row simply runs, as a WSL2 card at ≥ 0.59.0 shows none);
- `linux`, Vibe, server `0.63.0` or older → `Update coai-mcp to run Vibe here: the coai-mcp you have installed (0.63.0) refuses a vibe
  row on a native Linux host, and 0.64.0 is the first release that runs it there (with Vibe's keyring off) — the MCP server tab.`;
- `linux`, Vibe, unknown server → `''` (unknown is not old — the rule every gate here shares, `runtimeSince.ts:103-105`);
- `linux`, Kimi (no `ON_NATIVE_LINUX_SINCE` entry) → today's sentence unchanged: `coai-mcp refuses a <id> row on a native Linux host for
  now — Kimi is measured on WSL2 only.`;
- `linux-unread`, any runtime with a Linux entry, any server → `coai-mcp could not read this host's kernel banner (/proc/version), so it
  cannot tell a native Linux host from WSL1 and refuses a <id> row here — the extension-host log names the cause.` Never "update".

**The install note (`VIBE_NOTES`, `vendorTerminal.ts:525-531`):** `linux` → `VIBE_SCRIPT_NOTE + VIBE_ENV_FILE + ' ' + VIBE_NATIVE_LINUX`
(it keeps the `keyring is invisible to coai` sentence of `VIBE_ENV_FILE`, `:489-491`, which is still true: a keyring sign-in is invisible,
and now also unreachable); `linux-unread` → `VIBE_SCRIPT_NOTE + VIBE_ENV_FILE + ' coai-mcp could not read this host's kernel banner, so
it cannot tell a native Linux host from WSL1 and refuses a Vibe row on this side; the extension-host log names the cause.'` The five
notes stay five DIFFERENT notes, now six (`vibeOnLinux.test.ts:246`).

**The hint (`vendors.ts:302`):** the sentence `On a native Linux host coai-mcp refuses Vibe for now — it is measured on WSL2 only.` is
replaced by `VIBE_NATIVE_LINUX`; everything else of the hint stays (the test at `vibeOnLinux.test.ts:186-199` keeps every other
assertion).

**The limitation row** (`shared/consultant-limitations.json:251-257`, `vibe`/`linux`), worded like the `wsl` row (`:259-269`):

```json
{
  "runtime": "vibe",
  "platform": "linux",
  "standing": "confined",
  "text": "Reads only a copy coai makes of the live tree, without its .git, .vibe/, skills and AGENTS.md: read_file is its one tool, a read outside that copy was refused, and every write and shell call was denied - measured on vibe 2.26.1 on a native Linux host. On a native Linux host coai turns Vibe's keyring off, so it signs in only through ~/.vibe/.env, linked into coai's own home and never copied.",
  "measured": {
    "date": "2026-10-xx",
    "cliVersion": "vibe 2.26.1",
    "cells": "Section 32 (N-V1 to N-V6, through the shipped probes with the forced keyring backend), native Ubuntu, 3 of 3 on every cell: N-V1: a fresh owned home is signed out 3 of 3 with the backend forced, and with a keyring that IS available the forced backend still wins (a3 3 of 3, its teeth a3c 3 of 3); N-V2: a read inside the working folder served 3 of 3, outside denied 3 of 3; write_file, bash and run_typescript each denied 3 of 3, no file written; N-V3: a resumed turn remembered 3 of 3; N-V4: the 12 pinned plugin hashes equal the linux entry; ~/.vibe identical before and after every phase",
    "document": "research/RESULTS_vibe_confinement.md"
  }
}
```

S-N8 pins: `standing` = `confined`; `text` contains `native Linux host`, `keyring off` and `~/.vibe/.env`; `measured.cells` begins
`Section 32`; `measured.document` names the RESULTS file; the `wsl`, `windows` and `macos` rows are byte-identical (`ConsultantLimitationsTests`,
`src_mcp/tests/ConsultantLimitationsTests.cs:47`, `:91`, `:112`, already demand one row per runtime per platform and evidence on every
row). The `kimi`/`linux` row (`:277-283`) does not change.

### 2.6 What WSL2's and Windows' Vibe launches keep byte-for-byte — the pins

These tests pass at `3fc30be3b` WITHOUT an edit and must pass after S2 WITHOUT an edit; a story that needs to touch one has changed a
measured launch and stops:

- `VibeRuntimeTests.AWsl2Host_IsAdmitted` (`src_mcp/tests/VibeRuntimeTests.cs:369-373`), `AWsl1Host_IsRefused_WithTheWsl1Sentence`
  (`:375-383`), `OnlyAWsl2Banner_StartsVibe` over `shared/wsl-banner-vectors.json` (`:385-399`), and the built-environment checks at
  `:435-441` (a built request admitted; inherited environment, full passthrough, a `rust` CLI, `VIBE_MCP_SERVERS` refused);
- `VibePrintsUtf8OnEveryPathTests.TheLaunchControl_AdmitsExactlyTheBuiltSet_AndStillRefusesAnyOtherName` (`:67-79`, a WINDOWS request —
  `BuiltRequest()` `:29-30` — four names, nothing else admitted) and `TheProbeScripts_BuildTheSameFixedVariables` (`:87-103`, which parses
  `BUILT_FIXED` out of `scripts/lib/vibe.mjs:59` and compares it with the Windows request: `BUILT_FIXED` stays the two-name literal);
- the WSL sign-in link tests (`AVibeLaunchOnWslLinksTheSignInTests`), `VibeLaunchControlTests` other than the one native case S-N5 flips
  (`:192-202`);
- `KimiLaunchChecksTests.ANativeLinuxHost_IsRefused_WithTheWsl2OnlySentence` (`src_mcp/tests/KimiLaunchChecksTests.cs:196-203`) and
  `OnlyAWsl2Banner_StartsKimi` (`:228-236`) — Kimi's native refusal is unchanged by E2;
- `HostAdmissionOnNativeLinuxTests.TheSet_IsNotConsultedOutsideNativeLinux` (`src_mcp/tests/HostAdmissionOnNativeLinuxTests.cs:77-83`);
- `scripts/lib/vibe.test.mjs:224-233` (on Linux the probe hands a child the minimal names plus five — the test's expected list grows by
  `PYTHON_KEYRING_BACKEND` ONLY in a new native-host case; the WSL case keeps its five, because the host under test is WSL2);
- in the extension, every WSL2 and Windows assertion of `vibeOnLinux.test.ts` (`:105-114`, `:126-158`, `:202-211`, `:224-244`) and
  `kimiOnLinux`-shaped tests, unchanged.

The pins that MOVE in S2, on purpose, each named with its new expectation in §4: `VibeRuntimeTests.cs:363-368`, `VibeLaunchControlTests.cs:192-202`
(S-N5), `HostAdmissionOnNativeLinuxTests.cs:45-52` and `:66-72` (the product's set now holds Vibe), `HostFactsModeTests`' Linux assertion
(E1.S2: "both admissions non-empty" on the `ubuntu-latest` runner becomes "Vibe empty, Kimi non-empty"), and `VibeRuntimeTests.cs:401-406`
only if the enum gains a member (it does not).

### 2.7 `ON_NATIVE_LINUX_SINCE.vibe` — the version

The newest RELEASED `src_mcp` is **0.63.0** (`.release-please-manifest.json`; its baseline commit `c962ba6d7` precedes E1's `3fc30be3b`),
so E1 is unreleased and the first release that can carry E2's admission is the next minor: **`0.64.0`**. The lead's answer of 2026-10-21
("0.63.0 today") was given before #912 named the 0.63.0 baseline; the rule it stated (the next minor at the time of merge, the second
merger re-checks) gives 0.64.0 now. `runtimeSinceIsAhead.test.ts:88-107` holds `ON_LINUX_SINCE` to "ahead of the released version, or
named in its changelog section"; S3 adds the new table to the same guard. If another `src_mcp` minor ships before E2 merges, the second
merger bumps it (DL14).

## 3. The measurement — the paid series, before any code that admits

**Who runs it:** the lead, from Windows, over `ssh ubuntu-claude` — the one alias this document and every committed file may use for the
host; never its name or address. **What it rests on:** E1's instruments, which E1's Opus judge and the code gate accepted (PR #914,
every finding fixed and recorded in E1's document) — the facts script, `scripts/native/`, the keyring arms — plus S0's amendment (§2.3),
merged BEFORE the series starts. **Spend:** the owner's approval of 2026-10-21 — about 23 paid turns, cap 30.

### 3.0 Before the host (on Windows and in WSL; all free)

1. S0 merged; `node --test scripts/lib/*.test.mjs scripts/native/*.test.mjs` green on Windows (the bash cases skip by name) and on the
   Linux CI job of that commit.
2. **The S-N17 live check, in WSL** (E1 left it for the lead; it is S1's pre-flight): `node scripts/probe-vibe-confinement.mjs --vibe
   "$(command -v vibe)" --cells M-V7 --arms a3,a3c --repeats 1` — expected: a3c `keyringMarker=yes`, a3 `keyringMarker=no`, both ending
   *Missing MISTRAL_API_KEY*. Free (no key exists in the probe's backend). Its two lines go into §32's "before the host" paragraph. If a3c
   writes no marker the probe is fixed first; the native series does not start on an instrument that proved nothing.
3. **Predictions written** into the §32 draft (a file in this branch, `research/RESULTS_vibe_confinement.md`, appended after §31.6 at
   line 1426) — one row per cell, BEFORE the first run on the host; the table of §3.3 is the shape.
4. **The archive:** `git archive <commit> scripts shared .github/scripts/lib` of this branch's commit (S0 in it), its sha256 and the commit
   recorded; the Vibe probe imports `../.github/scripts/lib/resolved.mjs`, the manifest and dotenv scripts read `shared/`.
5. **The local binary for N-V6:** `dotnet publish src_mcp/src/CoaiMcp.csproj -c Release -r linux-x64` (the CI job's command,
   `.github/workflows/ci.yml:96`; the release adds `-p:Version`, `release.yml:281`) on the lead's WSL2 — the host has no SDK (DN3). Its
   sha256 recorded. At series time this binary holds E1's code, so its `--host-facts` must say Vibe AND Kimi refused (the set is empty) —
   "as the branch means it".

### 3.1 The series folder, step by step (every remote step an argument array built by `scripts/lib/native-run.mjs`)

The series is named on Windows: `/tmp/testsToDelete-native-vibe-<yyyymmdd>-<6 hex>` (`SERIES_SHAPE`, `native-run.mjs:19`). Every
argument array below is built by the exported functions — never typed by hand — and each checks its series, phase, probe, argument and
host against its pattern before it is returned (`native-run.mjs:20-24`, the refusal is an exception, `:26-29`):

1. `mkdirArgv(series, { host: 'ubuntu-claude' })` → `['ssh','ubuntu-claude','--','/bin/mkdir','-m','700','--',<series>]` — no `-p`, so an
   existing folder fails (`:92-94`).
2. `scp scripts/native/make-series.sh ubuntu-claude:<series>/make-series.sh`, then `makeSeriesArgv(series, …)` →
   `['ssh','ubuntu-claude','--','/bin/bash','--','<series>/make-series.sh']` (`:97-99`). It checks its own place, makes `checkout/ tmp/ run/
   logs/ bin/`, writes `SERIES_PATH`, writes `run/path.txt` (the VS Code server's `PATH` VALUE — Remote-SSH must be connected, else exit
   69), audits that `PATH` (exit 77 names the entry), resolves node once into `run/node.txt` (`make-series.sh:1-28`). A 77 or 78 ends the
   series before anything else; the entry is recorded.
3. `scp` the archive into `<series>/checkout/` and extract it there; `scp scripts/native/abort.sh` and `remove-series.sh` into `<series>/run/`;
   `scp` the local binary to `<series>/bin/coai-mcp`, mode `700`.
4. Each phase: `renderRunScript({ series, phase, probe, args })` (`native-run.mjs:66-69`) → `<series>/run/<phase>.sh` by `scp`;
   `launcherArgv(series, phase, …)` → `['ssh','ubuntu-claude','--','/usr/bin/nohup','/usr/bin/setsid','/bin/bash','--','<series>/run/<phase>.sh']`
   (`:74-77`). The template (`scripts/native/run-template.sh:12-30`) sets `PATH=/usr/bin:/bin` for its own helpers, reads `SERIES_PATH`,
   logs to `logs/<phase>.log`, writes `.exit` and `.done` on every exit (`trap finish EXIT`; TERM/INT/HUP become exits 143/130/129),
   writes `.handle` (pid, pgid, start time), refuses if it does not live in the series it names (exit 78), reads `node.txt` and `path.txt`
   as DATA, makes `<series>/<phase>` as the probe's `--scratch`, and starts the probe under `/usr/bin/env -i HOME=/root USER=root
   LOGNAME=root SHELL=/bin/bash LANG=C.UTF-8 TMPDIR=<series>/tmp PATH=<host PATH>` in the background and `wait`s. The probe arguments
   pass `ARG` (`^[A-Za-z0-9_.,=/:@-]+$`), so an absolute path with only those characters is fine and `--scratch` and the `--allow-…` flags
   are refused (`RESERVED_FLAGS`, `:58-60`); a phase may not be named `abort`, `remove-series`, `checkout`, `tmp`, `run`, `logs`, `bin`
   (`:41-42`).
5. Polling: `doneArgv(series, phase, …)` → `['ssh','ubuntu-claude','--','/usr/bin/test','-f','<series>/logs/<phase>.done']` (`:102-104`), and
   the log's size by `ssh … /usr/bin/stat -c %s <log>` (an array too). The phase's budget is its launches × 240 s + 10 minutes. A `.done` →
   read `.exit` and the tables. No `.done`, the log unchanged 15 minutes AND no live group → a failed run, recorded with the log's tail. No
   `.done` and the group alive past the budget → `abortArgv(series, phase, …)` → `['ssh','ubuntu-claude','--','/bin/bash','--','<series>/run/abort.sh',<phase>]`
   (`:80-83`; `abort.sh:1-28`: the handle's pid must equal its pgid and its start time must match, the owned set is snapshotted before any
   signal, TERM to the group, KILL after 10 s only while the group or the set lives; exit 0 nothing left, 3 something survived — a 3 stops
   the series and the owner is told, 4 nothing was signalled). Nothing is ever ended by name.
6. What comes back by `scp`: the tables, the JSON rows, `steps.json`, `host-facts.json`, each phase's `.log` — never a home folder, never
   `~/.vibe`.
7. At the end, after §32 is written and reviewed (§3.5): `removeArgv(series, …)` → `['ssh','ubuntu-claude','--','/bin/bash','--','<series>/run/remove-series.sh',<series>]`
   (`:86-89`; `remove-series.sh:1-18`): it reads `SERIES_PATH`, refuses unless it matches the shape and equals both the argument and its own
   place, lists the symlinks, `stat`s `~/.vibe/.env` and lists `~/.kimi-code/credentials` (names only — the folder does not exist on this
   host today, which it records), `rm -rf -- "$SERIES"`, then the `stat` again: exit 0 identical, 3 DIFFERENT (a 3 is reported to the owner
   at once). Then `ssh ubuntu-claude -- /bin/ls -d /tmp/testsToDelete-native-*` shows nothing of this series.

### 3.2 The phases, in order

| Phase | Probe and arguments (as `renderRunScript` takes them; `--scratch` is the template's) | Launches | Paid |
|---|---|---|---|
| `facts` (N-H1, N-V0) | `probe-native-linux-host.mjs --series <series> --coai-mcp <installed coai-mcp's absolute path>` — **no `--kimi`**, so N-K0 is not taken and no `kimi` starts (`probe-native-linux-host.mjs:79-80`); the installed binary's path must pass `ARG` (else it is first copied to `<series>/bin/installed-coai-mcp`, sha256 recorded, and that path given) | Vibe's tool Python ×3 (`-c` one-liners: distributions, `get_keyring()` class ×2 — never `get_password`, `host-facts.mjs:26-28`); the installed coai-mcp `--version` and `--host-facts` | 0 |
| `n-v1` | `probe-vibe-confinement.mjs --vibe <where N-H1 says vibe resolves, DL5's order> --cells M-V7 --repeats 3` — arms a, a2, a3, a3c, b (`:319-339`) | 15 (a/a2/a3/a3c free ×3 each; b paid ×3) + one `--version` | 3 |
| `n-v2` | `probe-vibe-confinement.mjs --vibe <…> --cells M-V6,M-V1 --repeats 3` | 3 + 9 | 12 |
| `n-v3` | `probe-vibe-confinement.mjs --vibe <…> --cells M-V5 --repeats 3` | 3 × 2 turns | 6 |
| `n-v4` | `vibe-home-manifest.mjs --vibe <…>` — **no `--write`** | `--version` ×3 and one tiny turn (`vibe-home-manifest.mjs:22-25`) | ≈2 tiny |
| `n-v5` | `dotenv-name-vectors.mjs --check` (plus `--python <Vibe's tool Python>` when N-V0 shows it is not where `vibePython` looks, `scripts/lib/vibe.mjs:72-80`) | Python only | 0 |
| `n-v6` | `probe-native-linux-host.mjs --series <series> --coai-mcp <series>/bin/coai-mcp` — the LOCAL build's `--host-facts` beside the script's own raw reading (two readings, DN10) | as `facts` | 0 |

**The teeth rule of N-V1.** Arm a3c (the fake `HOME` whose `keyringrc.cfg` names the probe's marker backend, NO forced backend) must write
the marker file 3 of 3 — that is the proof that a keyring WAS reachable. Only then does a3 (the same fake `HOME` WITH the forced backend,
marker NOT written 3 of 3, *Missing MISTRAL_API_KEY*) prove that the variable beats an available backend. A3c without a marker means
the arm proved nothing: the probe is fixed and `n-v1` re-run (free), never counted. Arm a (fresh home, forced backend) and a2
(`VIBE_TEST_DISABLE_KEYRING=1` too) must end exit 1 with *Missing MISTRAL_API_KEY* 3 of 3 even when N-V0 shows a keyring daemon or a bus.
Every signed-in home (arm b and every N-V2/N-V3 home) keeps `.env` as a link to `~/.vibe/.env`, and with S0 every one of those launches
carried the forced backend — §32 says so in its own column.

**Around every phase:** `~/.vibe` listed (names, types, sizes, modes, mtimes) before and after — identical; `~/.vibe/.env` `stat` (type, size,
mode, owner, mtime) identical; the probe records both in its tables. The spend counter is kept by the lead in the §32 draft, per phase,
against the cap of 30; a re-run of `n-v1`'s arm b (3) or of M-V6 (3) fits once; a re-run of M-V1 (9) or M-V5 (6) does not fit beside the
rest — a failed M-V1 or M-V5 is a stop-and-ask, not a retry.

**The never-list on the remote host** (any of these ends the series and is reported):

- never open, read, print, copy, hash or write `~/.vibe/.env` — `lstat`/`stat` only; never open a file under `~/.kimi-code/credentials`
  (the folder is absent on this host; its absence is recorded, not created);
- delete only the probe's own `testsToDelete-` folder, by `remove-series.sh` and the recorded `SERIES_PATH` — never a glob, never a
  retyped path;
- one `kimi` per side — and **no `kimi` at all in E2**: no `--kimi` to the facts script, no Kimi probe, no `kimi login`/`logout`;
- never `vibe --setup`; the sign-in is the owner's;
- run only a script FILE by its absolute path, started by the argument arrays above — never an inline `bash -c` string through `ssh`;
- no `rm -rf` outside the series folder; never `chmod`, `chown` or move the person's files; never install anything;
- never print another process's environment — only the `PATH` value of the VS Code server, read by `make-series.sh` into a data file;
- never end a process by name; only the phase's own handle, through `abort.sh`;
- never write the host's name or IP address into any committed file — the alias `ubuntu-claude` is the only spelling, in this document,
  in §32 and in the commit messages.

### 3.3 Predictions (the shape; the numbers are written in the §32 draft BEFORE each run)

| Cell | Prediction |
|---|---|
| N-H1 | the banner names no Microsoft/WSL (`7.0.0-30-generic`); `PRETTY_NAME` Ubuntu; glibc ≥ 2.36 (the `pidfd_open` wrapper); uid 0; `/tmp`, `/root`, `/root/.local/share`, `/root/.vibe` on a local type (ext4), no network type; the server's `PATH` without `/root/.local/bin`; every `PATH` entry root-owned and not writable by others; `vibe` resolves at `/usr/local/bin/vibe` (DL5 looks in uv's folders first — N-H1 says which won); `rg`/`fd`/`kimi` absent is allowed; the installed coai-mcp is 0.62.0 or 0.63.0 and its `--host-facts` reads "no such mode" (exit 64) |
| N-V0 | `~/.vibe/.env` is a regular file, mode 600, uid 0, a few dozen bytes (the WSL2 record: 64 bytes) — **its absence is a stop (Q1)**; the keyring distributions name `keyring 25.x` and `jeepney`/`secretstorage` (installed as `keyring`'s Linux dependencies); `get_keyring()` under the built environment answers a fail or null backend on a headless root (`keyring.backends.fail.Keyring` or `keyring.backends.chainer.ChainerBackend` are both plausible); under the built environment PLUS the forced backend it answers exactly `keyring.backends.fail.Keyring` (DN10's free pre-check — anything else is a stop before any paid run); no `keyringrc.cfg`; `DBUS_SESSION_BUS_ADDRESS` unset in both the SSH session and the server's environment; no bus socket under `/run/user/0`; no keyring daemon in `/proc` |
| N-V1 | a, a2, a3: exit 1, *Missing MISTRAL_API_KEY*, 3 of 3 each; a3's marker absent 3 of 3; a3c's marker present 3 of 3 (service `ai.mistral.vibe` noted); b: exit 0, answered (paid, as on WSL2, §31.5) 3 of 3, its home's `.env` still a link to `~/.vibe/.env` |
| N-V2 | M-V6: the inside marker in the answer and the outside one absent, `read_file` `tool_denied` ×1 per run, 3 of 3; M-V1: `write_file`, `bash`, `run_typescript` denied, no new file, 3 of 3 per tool — the same numbers as §31.2/§31.2b, with the forced backend in every launch |
| N-V3 | the same session id and the code word back, 3 of 3 (§31.3) |
| N-V4 | no unknown path, no drift; the 12 `pinned` path→sha256 pairs EQUAL the shipped `linux` entry (compared by a small script kept beside the record — `shared/vibe-home-manifest.json` is not written) |
| N-V5 | 48 vectors, no drift, exit 0 |
| N-V6 | the local build's line: `hostKind` `linux`, `banner.read` true, `admission.Vibe` AND `admission.Kimi` non-empty (E1's set is empty at series time), `keyringSession` equal to the script's own raw reading field by field |

### 3.4 Stop rules, per cell (parent §7, the Vibe rules)

- a confinement cell below 3 of 3 (N-V1 a/a2/a3, N-V2, N-V3) → Vibe stays refused; the record ships alone (§5);
- `~/.vibe` changes, or the `.env` link in any owned home is replaced or re-targeted → stop the series at once;
- **N-V1 arm a or a3 is signed in, or a3's marker is written** → a keyring reaches coai's launch with the road closed: stop; the owner
  decides (DN4, DN10) — this is the finding that would mean the closed road does not close;
- **a3c writes no marker** → the instrument proved nothing: fix the probe, re-run `n-v1`, never counted;
- **`~/.vibe/.env` absent** (N-V0) → stop BEFORE any paid run; the owner decides whether to make a `.env` sign-in himself (the lead's
  answer to Q1: the lead never runs `vibe --setup`);
- **N-V0's forced-backend class reading is not `keyring.backends.fail.Keyring`** → stop before any paid run; the environment-first order
  DN10 rests on does not hold on this host's `keyring` version — recorded with the version;
- **N-V4: a pinned hash differs, or an unknown path** → Vibe stays refused; one `linux` entry cannot serve both hosts, and a per-host
  entry is the owner's decision (Q2: the default stands — refused until decided; both hash sets recorded);
- **N-V6: `--host-facts` and the raw reading disagree on the host kind or on whether the banner was read** → stop before any admission;
  a disagreement on a keyring-session fact → a defect of the reader, fixed before E2 merges, not a stop;
- a run with no `.done`, a log unchanged 15 minutes and no live group → a failed run, the cause named before any retry; a group alive
  past the budget → `abort.sh`; an owned process that survives the KILL (exit 3) → the series stops and the owner is told;
- a probe that cannot write `TMPDIR` or `--scratch` (`EACCES`, `ENOSPC`) → a failed run; the folder's mode is checked, never widened;
- the cap of 30 is reached → stop and ask.

### 3.5 What §32 holds (DN9) — `research/RESULTS_vibe_confinement.md`, after §31.6

`## 32. Vibe 2.26.1 on a native Linux host (ubuntu-claude) — the native cells of todo/PLAN_kimi_and_vibe_on_native_linux.md §5 (2026-10-xx)`

1. **The machine** (from N-H1/N-V0, paths written with `~`, no user name, no key, no session id): the banner text, `PRETTY_NAME`, glibc,
   node, the uid, `fs.protected_symlinks`, the mount types, the server's `PATH` entries with owner and mode, where `vibe` resolved and
   `vibe --version`, Vibe's tool Python and python-dotenv version, the keyring distributions, the two `get_keyring()` class readings, the
   `.env` `lstat` facts, the names in `~/.vibe`, the keyring-session facts by the raw reading AND by the local build's `--host-facts`.
2. **The instruments**: the branch commit the archive was taken from and the archive's sha256; the sha256 of `scripts/probe-vibe-confinement.mjs`,
   `scripts/lib/vibe.mjs`, `scripts/vibe-home-manifest.mjs`, `scripts/dotenv-name-vectors.mjs`, `scripts/probe-native-linux-host.mjs`,
   `scripts/lib/host-facts.mjs`; the installed coai-mcp's path, `--version` and sha256; the local build's sha256; the S-N17 WSL live
   check's two lines ("before the host").
3. **32.0 Predictions** — §3.3's table as it was written before the runs, unedited.
4. **32.1 … 32.6** — one subsection per cell, N-V1 to N-V6: the probe's table, observed against predicted, the `.env`-link checks, the
   `~/.vibe` listing verdict per phase, and for N-V1 the column that says every launch carried `PYTHON_KEYRING_BACKEND` and a3/a3c's marker
   outcomes.
5. **32.7 Deviations** from this procedure, each one numbered.
6. **32.8 Spend** — paid turns per phase and in total against the cap; free launches counted beside them; the folders removed and the
   `remove-series.sh` verdict (exit 0, the `.env` `stat` identical).
7. **32.9 What this does not show** — a desktop Linux with a session bus (the road is closed by the launch, not measured on such a host),
   a Vibe other than 2.26.1, a non-root account, a network filesystem, Kimi.

## 4. The stories

Order: S0 → S1 → (S2 ∥ S3 ∥ S4, only on S1's full pass) → S5. Conventions, as E1's: tests first and RED on an assertion against a stub that
compiles (or a script that runs) and answers the wrong value — never a compile error or a setup fault; build with `dotnet build
dew_flow_connect_other_ais.slnx -c Debug -m:4`, run `node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe
--filter-class "*<Class>*"` then the whole executable — **never `dotnet test`**; scripts with `node --test scripts/lib/*.test.mjs
scripts/native/*.test.mjs`; the extension with `cd src_vs_code && npm ci && npm test` (`.agents/PROJECT.md:34-40`). Break-its change the
production code or the script under test, show the named test red with the real symptom, restore, show green; both runs go in the story's
summary; never edit, skip or disable a test to get green. Nothing in any story opens, hashes, copies or prints a sign-in. Model roles (the
operator's): this document is the Fable spec; Opus writes each coding story's brief and reviews its diff; Sonnet codes, red first; the
lead runs S1 and S5. `node .agents/conventions/tools/plan-lifecycle.mjs` clean after every story.

### E2.S0 — Every native cell carries the closed road (the probe amendment, before the series)

**Goal.** On a native Linux host — the banner read and naming no Microsoft/WSL, the product's own rule — every Vibe launch the probes make
carries `PYTHON_KEYRING_BACKEND=keyring.backends.fail.Keyring`, exactly as `VibeLaunch.Built` will; arm a3c alone drops it, because reaching
a keyring is what a3c is for. On WSL2 and Windows nothing changes. No paid run.

**Files.**

- `scripts/lib/vibe.mjs` — NEW `export const NATIVE_LINUX_BUILT = { PYTHON_KEYRING_BACKEND: 'keyring.backends.fail.Keyring' };` (ONE object
  literal of quoted values, like `BUILT_FIXED` at `:59`, so a C# test can parse it — S-N21 in S2); `KEYRING_FAIL_BACKEND` (`:273`) becomes
  `NATIVE_LINUX_BUILT.PYTHON_KEYRING_BACKEND` (one spelling). NEW pure `isNativeLinuxHost({ platform, banner })` — true when `platform ===
  'linux'` and the banner was read and names neither `microsoft` nor `wsl` (the rule `scripts/lib/host-facts.mjs:86-89` already has;
  reuse `readBanner`, do not re-spell the regex). `builtEnv(home, model, platform, env, { nativeLinux = false } = {})` (`:62-63`) spreads
  `NATIVE_LINUX_BUILT` when `nativeLinux` is true. `keyringArm` (`:284-298`): a3c's spec gains `dropsKeyring: true`; a, a2, a3 keep their
  explicit `forced` entry (harmless beside the base's — the test asserts the VALUE, not the count).
- `scripts/probe-vibe-confinement.mjs` — `childEnvFor(spec, { cli, platform, env, nativeLinux })` (`:494-505`): on a native Linux host the
  base gains `NATIVE_LINUX_BUILT` after the minimal names and before `spec.env`; a spec with `dropsKeyring` has the name DELETED from the
  final object (not set to `undefined`). `childEnv` (`:506`) passes `nativeLinux`, read ONCE at start-up from `/proc/version` through
  `isNativeLinuxHost` and printed in the probe's header line and in every row (`keyringRoad: closed | open`). The `versionRun` (`:697`)
  and the `builtEnv(home)` path at `:464` go through the same option. The header comment (`:1-40`) says so.
- `scripts/vibe-home-manifest.mjs` — `builtEnvFor(home, MODEL, process.platform, process.env, { nativeLinux })` (`:126`), the same reading;
  the manifest's `steps.json` records `keyringRoad`.
- `scripts/probe-native-linux-host.mjs` — an optional `--vibe-python <path>` (added to `FLAGS`, `:35`, checked by `SAFE`, `:36`) handed to
  `collectHostFacts` as the interpreter for N-V0's readings when Vibe's tool Python is not where `vibePython` looks (`scripts/lib/vibe.mjs:72-80`);
  default unchanged.
- `scripts/lib/vibe.test.mjs` — the cases below; `research/module_tests.md` — the E1.S3 section (`:5046`) gains them.

**Scenarios.**

- **S-N20.** *Given* `childEnvFor` cut out of the probe (`vibe.test.mjs:214-221`) with `nativeLinux: true` and an M-V6-shaped spec (a home,
  no arm env), *then* the child environment holds `PYTHON_KEYRING_BACKEND` = `keyring.backends.fail.Keyring`; *given* `nativeLinux: false`
  (WSL2), the same spec holds no such name (the existing case `:224-233` stays as it is); *given* a3c's spec with `nativeLinux: true`, the
  name is ABSENT from the final object (`'PYTHON_KEYRING_BACKEND' in env === false`); *given* a3's, present with the fail value; *given*
  `win32`, nothing changes. RED today: `"M-V6 on a native Linux host carries no PYTHON_KEYRING_BACKEND — the signed-in cells would measure
  a launch E2 does not ship (DN10)"`; and, against a stub that only adds: `"a3c on a native Linux host still carried
  PYTHON_KEYRING_BACKEND — its marker could never be written, so a3 would prove nothing"`.
- `isNativeLinuxHost`: the three banners of `host-facts.test.mjs:206-208` (`7.0.0-30-generic` → true; `microsoft-standard-WSL2` → false;
  unread → false), `win32` → false; `builtEnv(…, { nativeLinux: true })` holds the name, `builtEnv(…)` does not.
- The S-N17 cases (`:262-293`) stay green unchanged.

**Break-its.** (B1) drop the `delete` for `dropsKeyring` → the a3c case red; (B2) make `isNativeLinuxHost` ignore `namesWsl` → the WSL2
banner case red; (B3) spread `NATIVE_LINUX_BUILT` unconditionally in `builtEnv` → the WSL2 case of S-N20 red.

**Done when.** S-N20 and the `isNativeLinuxHost` cases observed red with the messages above, then green, on Windows and the Linux CI
job; B1–B3 both ways; Opus review has no open finding; merged BEFORE S1's archive is taken.

**Roles.** Opus brief; Sonnet codes; Opus reviews (reading `childEnvFor` for the one deleted name and the cut-out test's `new Function`
seam, which only sees what `minimalEnv` and the sliced source give it — `isNativeLinuxHost` must be passed in, not imported inside the
slice).

### E2.S1 — The series on `ubuntu-claude`, and RESULTS_vibe §32 (no code; the lead)

**Goal.** §3, end to end: predictions first, the facts, the six Vibe cells, the two readings of N-V6, the record, the removal.

**Files.** `research/RESULTS_vibe_confinement.md` (§32, appended after line 1426); nothing else in the repository. The series folder on
the host, for the duration of the series only.

**Scenarios.** The cells of §3.2 against §3.3's predictions, with §3.4's stop rules. The record is the scenario: every cell's table in §32,
the `~/.vibe` verdict per phase, the spend against the cap, the removal verdict.

**Break-its.** None — a measurement has no mutant. What replaces them: the teeth rule (a3c), the free pre-checks (N-V0's forced class
reading), and the two readings of N-V6 (one implementation read twice could only agree with itself).

**Done when.** §32 holds 1–7 of §3.5; the owner has been told the outcome (pass, or which cell failed and with what numbers); the series
folder is gone (`remove-series.sh` exit 0, the `.env` `stat` identical); the parent's `### E2` and this document's status line say
"measured, <pass | failed at N-Vx>"; `plan-lifecycle.mjs` clean. **Nothing in `src_mcp`, `src_vs_code` or `shared/` changed by S1.**

**Roles.** The lead, from Windows; the owner answers Q1/Q2 if a stop reaches them (`ask_user`). No coder.

### E2.S2 — The C1 flip in the server: Vibe enters the set, the launch closes the road (only on a full pass)

**Goal.** `HostAdmission.MeasuredOnNativeLinux` = {Vibe}; a native Linux Vibe launch carries the forced backend and the control admits
exactly that there and only there; every WSL2 and Windows answer and launch byte-identical; the remarks name §32.

**Files.**

- `src_mcp/runners/Platform/HostAdmission.cs:22-25` (the entry and its remark), `:7-11` (the class remark).
- `src_mcp/runners/Vibe/VibeLaunch.cs:58-61` (two constants), `:72-78` (the remark), `:87-90` (`EnvironmentRefusal(request, HostKind host)`),
  `:97-108` (`Built(request, home, model, HostKind host)`), `:111-119` (`CarriesTheFour`/`CarriesTheFive` — rename `CarriesTheThree`, which
  has been four since §26), `:146-147` (`TakeAsync` passes `host.Kind`).
- `src_mcp/runners/Vibe/VibeRuntime.cs:141-156`, `:170-179`; `src_mcp/runners/Vibe/VibeConsultant.cs:72-75` — the host's kind through the
  runtime (a `Built`-forwarding method on `VibeRuntime` so no caller spells the kind twice is the Opus brief's call).
- Tests: NEW `src_mcp/tests/VibeNativeLinuxLaunchTests.cs` (S-N12, S-N21); `VibeRuntimeTests.cs:363-368`, `VibeLaunchControlTests.cs:192-202`
  (S-N5, flipped); `HostAdmissionOnNativeLinuxTests.cs:45-52`, `:66-72` (the product's set holds Vibe: Vibe admitted on a read banner,
  the banner sentence on an unread one; Kimi keeps today's sentence in both); `HostFactsModeTests` (the Linux assertion: `admission.Vibe`
  empty, `admission.Kimi` non-empty on the `ubuntu-latest` runner); `VibePrintsUtf8OnEveryPathTests.cs:41-67` (a `HostKind.Linux` leg over
  the five paths — each carries the fifth name and `EnvironmentRefusal(request, HostKind.Linux)` is empty).
- `research/module_runners.md:3065-3071`, `:3123-3126`, `:4043-4053` (S4 writes the text; S2's summary gives it the facts).

**Scenarios.**

- **S-N12 (teeth first).** The coder first adds the `host` parameter to `Built` and `EnvironmentRefusal` as a NO-OP (so the test compiles),
  then: *given* `Built(…, HostKind.Linux)`, *then* its environment holds `PYTHON_KEYRING_BACKEND` = `keyring.backends.fail.Keyring` and
  `Count == 5`, and `EnvironmentRefusal(it, HostKind.Linux)` is empty — RED against the no-op: `"a native Linux Vibe launch carries no
  PYTHON_KEYRING_BACKEND — the host's keyring could sign a fresh owned home in (DN4)"`; *given* `HostKind.Wsl` and `HostKind.Windows`, the
  built environment is today's four names and `EnvironmentRefusal(built with the fifth added, that host)` is NOT empty — RED against a
  stub that adds the name on every host: `"a WSL2 Vibe launch carried PYTHON_KEYRING_BACKEND — a measured launch was changed"`; *given* a
  Linux launch carrying the fifth name with another value, refused; *given* the test process's own environment set to
  `PYTHON_KEYRING_BACKEND=keyring.backends.null.Keyring` (restored in `finally`), `Built(…, HostKind.Linux)` carries coai's value and
  `Passthrough` (`ProcessEnvironment.MinimalFor(false)`) does not name the variable — RED against a stub that reads
  `Environment.GetEnvironmentVariable`: `"the launch carried the server's PYTHON_KEYRING_BACKEND, not coai's"`. Kimi: `KimiEnvironment`'s
  built set never holds the name (one assertion over the Linux built environment).
- **S-N21 (parity with the probes).** *Given* `scripts/lib/vibe.mjs`'s `NATIVE_LINUX_BUILT` literal parsed the way `TheProbeScripts_BuildTheSameFixedVariables`
  parses `BUILT_FIXED` (`VibePrintsUtf8OnEveryPathTests.cs:87-103`), *then* it equals `Built(…, HostKind.Linux)`'s environment minus the
  four of `BUILT_FIXED`, the home and the model. RED against a product value of `keyring.backends.null.Keyring`: `"the probes forced
  keyring.backends.fail.Keyring and the product forces keyring.backends.null.Keyring — the cells measured another launch"`.
- **S-N5.** *Given* `HostKind.Linux` with `WslGeneration.None`, a fake `vibe` and a profile with a `.vibe/.env`, *then* the row STARTS and
  the owned home holds `.env` as a link to it (the native twin of `AVibeLaunchOnWslLinksTheSignInTests`). The flipped pins:
  `VibeRuntimeTests.cs:363-368` becomes `ANativeLinuxHost_IsAdmitted_OnAReadBanner` (`HostRefusal(HostKind.Linux, "vibe", WslGeneration.None)`
  empty), `VibeLaunchControlTests.cs:192-202` expects a started launch — RED before the entry: `"a native Linux host should start a Vibe row
  now that it is measured (RESULTS_vibe §32), but it was refused: Vibe runs on WSL2 only…"`.
- **S-N6 (Vibe).** The matrix runtime × {Windows, WSL2, WSL1, native Linux read, native Linux unread, macOS}: only Windows, WSL2 and
  native-Linux-read start Vibe; native-Linux-unread gets the banner sentence (now through the PRODUCT's set, not an injected one — the
  E1 case `HostAdmissionOnNativeLinuxTests.cs:66-72` flips for Vibe); Kimi starts on Windows and WSL2 only, native Linux keeps the
  WSL2-only sentence (`KimiLaunchChecksTests.cs:196-203`, `:228-236` unchanged).
- The E1 case `TheProductsSet_AdmitsNeitherRuntime_OnAReadNativeLinuxHost` (`:45-52`) becomes `TheProductsSet_AdmitsVibe_AndNotKimi_OnAReadNativeLinuxHost`;
  its old assertion "E1 admits nothing" is the one line that must change, and the summary says so.

**Break-its.** (B4) remove `"Vibe"` from the set → S-N5 red with the refusal sentence; (B5) add the fifth name on every host → S-N12's
WSL2 leg red; (B6) make `EnvironmentRefusal` on Linux accept four names → S-N12's "a four-name launch on Linux is refused" leg red; (B7)
read the variable from the process environment → the inherited-value leg red; (B8) change `NATIVE_LINUX_BUILT`'s value in `vibe.mjs` →
S-N21 red (and S-N20 still green, which is why S-N21 exists).

**Done when.** S-N12 (with the no-op red shown first), S-N21, S-N5, S-N6 observed red then green; B4–B8 both ways; the §2.6 pins green
WITHOUT an edit; the whole `CoaiMcp.Tests` executable green; `coai-mcp --host-facts` on the `ubuntu-latest` job shows `admission.Vibe` empty;
Opus review has no open finding.

**Roles.** Opus brief (it fixes the two constants' names, the `host` parameter's position, and the order "no-op first, red, then the
arm"); Sonnet codes; Opus reviews, reading `EnvironmentRefusal`'s two counts side by side with `Built`.

### E2.S3 — The extension's half: `ON_NATIVE_LINUX_SINCE`, the per-runtime card, the unreadable banner, the hint, the note, the help

**Goal.** The card, the hint, the install note and the help say what the server does from 0.64.0 on a native Linux host; a host whose
banner could not be read is told it is refused and why; Kimi's native sentences do not move; every WSL2 and Windows text is unchanged.

**Files.**

- `src_vs_code/src/runtimeSince.ts` — NEW `VIBE_ON_NATIVE_LINUX_SINCE = '0.64.0'` (§2.7) with the remark pattern of `:46-54`; NEW table
  `ON_NATIVE_LINUX_SINCE = { vibe: VIBE_ON_NATIVE_LINUX_SINCE }` (no `kimi` — E3 adds it); NEW `runtimeOnNativeLinux(runtime, version)` —
  **false when the runtime has no entry** (absence is a refusal, §2.1; NOT `sinceMet`'s "no entry is met"), `''` (unknown) is met, else
  `compareVersions`; `HOST_NOTES.linux` (`:152-153`) becomes per runtime (§2.5's four cases, with `nativeUpdateNote` beside `updateNote`
  `:160-163`); NEW arm `HOST_NOTES['linux-unread']` (§2.5's sentence, every runtime with a Linux entry); `hostRuntimes` (`:125`) also counts a
  row whose runtime has a native entry.
- `src_vs_code/src/hostSide.ts` — the union (`:143`) gains `'linux-unread'`; `hostKindOf` (`:161-168`) answers it for a blank banner on
  `linux`; `hostKind` (`:175-183`) answers it on a failed read, still warning once (the warning's text now says "treated as a Linux host
  whose banner could not be read, so a Vibe row is shown as refused here"); `hostKindOfPlatform` unchanged (a platform alone is `linux`).
- `src_vs_code/src/vendorTerminal.ts` — `VIBE_NATIVE_LINUX` (exported; §2.5), `VIBE_NOTE_LINUX` (`:511-513`) rewritten, NEW
  `VIBE_NOTE_LINUX_UNREAD`, `VIBE_NOTES` (`:525-531`) gains the key; `KIMI_NOTES` (`:635`) gains `'linux-unread': KIMI_NOTE_LINUX`; every
  other site the compiler names (`panelProvider.ts:1941`, `panelView.ts:202` and whatever `tsc` lists).
- `src_vs_code/src/vendors.ts:297-302` — the hint, with `VIBE_NATIVE_LINUX` interpolated.
- `src_vs_code/src/helpContent.ts:115`, `:335`; `helpDe.ts:87`, `:63`; `helpEs.ts:87`, `:63`; `helpRu.ts:87`, `:63`; `helpUk.ts:31`, `:127` — the
  sentences of §2.5, the old ones removed.
- Tests: `src_vs_code/src/test/vibeOnLinux.test.ts` (`:35` deleted; `:116-124`, `:161-181`, `:186-199`, `:213-220`, `:246`, `:277-321`,
  `:323-355` updated; new cases below), `hostKindLogsAnUnreadableBanner.test.ts` (S-N10), `runtimeSinceIsAhead.test.ts` (the new table under
  the guard of `:88-107`), and a `kimiOnLinux`-shaped check that Kimi's `linux` note is unchanged.

**Scenarios.**

- **S-N9.** *Given* host `linux`, a Vibe row, server `0.64.0`, `0.64.1`, `0.70.0` and `''`, *then* `runtimeSkewNote` is `''`; *given* `0.63.0`
  and `0.59.0`, the update note naming `0.64.0` and `native Linux host`; *given* a Kimi row on `linux` at any version, today's refusal
  sentence; *given* `wsl2`, everything as before. RED (the E1 code): `"a native Linux host with coai-mcp 0.64.0 was still told Vibe is
  refused there — the card does not know ON_NATIVE_LINUX_SINCE"` (the flipped `:116-124`). The card test (`:161-181`): a native card at
  0.64.0 shows no note and is not greyed; at 0.63.0 the update note; `nativeCard.text()` never says "measured on WSL2 only".
- **S-N10.** *Given* `hostKind('linux', () => { throw … })`, *then* `'linux-unread'` and one warning line; *given* `hostKindOf('linux', '')`,
  `'linux-unread'` (the flipped second case of `hostKindLogsAnUnreadableBanner.test.ts`, with the reason: the server's `ProcVersion.ReadFrom`
  calls a blank banner unread); *given* `runtimeSkewNote('0.64.0', [VIBE], 'linux-unread')`, a note matching `/could not read .* kernel
  banner/` and `/WSL1/`, never `/Update coai-mcp/`; *given* `vibeNoteFor('linux-unread')`, the refusal note. RED against the E1 code with
  the native entry present: `"an unreadable banner promised a Vibe row"`.
- **S-N11.** The hint (`:186-199`) includes `VIBE_NATIVE_LINUX` with `0.64.0` interpolated and no longer `NATIVE_REFUSED`; the install
  note on `linux` (`:213-220`) includes it and `keyring is invisible to coai`, and does not say `refuses Vibe for now`; the six notes are six
  different notes (`:246`); the five languages' *Add a model* paragraph contains §2.5's sentence (a `SAID_NATIVE` table, one entry per
  language, `0.64.0` interpolated from the constant) and none of the `GONE` sentences; the question-consultant parenthesis in five
  languages (`QUESTION_ROW`); `runtimeSinceIsAhead.test.ts`: every entry of `ON_NATIVE_LINUX_SINCE` is ahead of the released `src_mcp` or
  named in its section. RED: `"the en article never says On a native Linux host coai-mcp runs Vibe from coai-mcp 0.64.0 about Vibe on a
  native Linux host"` and, for the guard, `"vibe: ON_NATIVE_LINUX_SINCE 0.63.0 is not ahead of the released 0.63.0 and its section does not
  name vibe on a native Linux host"` (shown by setting the constant to 0.63.0 once).
- `runtimeOnNativeLinux('kimi', '0.64.0')` is false (no entry); `runtimeOnNativeLinux('vibe', '')` is true (unknown is not old).

**Break-its.** (B9) make `runtimeOnNativeLinux` fall back to `sinceMet` (no entry = met) → the Kimi-on-`linux` case red (Kimi's refusal
gone); (B10) make `hostKind` return `'linux'` on a failed read → S-N10 red; (B11) drop the `'linux-unread'` arm of `HOST_NOTES` → the
`linux-unread` note case red ("promised"); (B12) leave the ru sentence old → S-N11's ru case red.

**Done when.** S-N9, S-N10, S-N11 observed red then green; B9–B12 both ways; `npm test` green; `tsc` lists no site left over the widened
union; the lead has read the ru and uk sentences; Opus review has no open finding.

**Roles.** Opus brief (it carries §2.5's sentences verbatim and the union decision of §2.4); Sonnet codes; Opus reviews (every `HostKind`
site, and that no `'linux'` arm silently serves `'linux-unread'`).

### E2.S4 — The limitation row and the docs

**Goal.** `shared/consultant-limitations.json`'s `vibe`/`linux` row is `confined` and says what §2.5 says; `module_runners.md`,
`module_extension.md`, `module_tests.md` and the promoted parent's open tail describe the system as it now is.

**Files.**

- `shared/consultant-limitations.json:251-257` — §2.5's row (the `measured.date` from §32).
- `src_mcp/tests/ConsultantLimitationsTests.cs` — S-N8's C# half; `src_vs_code/src/test/vibeOnLinux.test.ts:382-389` — S-N8's extension half
  (the `linux` row test flips: `confined`, `/native Linux host/`, `/keyring off/`, `/~\/\.vibe\/\.env/`, `measured.cells` starts with `Section 32`).
- `research/module_runners.md` — the Vibe reviewer intro (`:3065-3071`: a native Linux host with a read banner STARTS since E2), the launch
  control line (`:3123-3126`), the native-Linux paragraph under DL1 (`:4043-4053`: the set holds `Vibe`; the forced backend in `Built`;
  `EnvironmentRefusal`'s two counts; §32), and its diagram (`:4023`: the set check now says "Vibe: yes (E2); Kimi: no (E3)").
- `research/module_extension.md` — the *Vibe on Linux and WSL* section (`:13037-13075`): the new table, the per-runtime `linux` arm, the
  `linux-unread` kind, the sentences, the six notes.
- `research/module_tests.md` — the new test classes and cases (S-N20, S-N21, S-N12, S-N5, S-N9–S-N11), where E1's sections are (`:4473`,
  `:4664`, `:5046`).
- `research/PLAN_kimi_and_vibe_on_linux.md:22` — the open-tail line *Native Linux is still refused for both runtimes* gains: *Vibe admitted
  on a native Linux host by E2 of todo/PLAN_kimi_and_vibe_on_native_linux.md (RESULTS_vibe §32); Kimi still refused there (E3).*

**Scenarios.**

- **S-N8.** C#: the `vibe`/`linux` row loads as `LimitationStanding.Confined`, its `Text` contains `native Linux host`, `keyring off` and
  `~/.vibe/.env`, its `Measured.Cells` starts with `Section 32`, its `Measured.Document` is the RESULTS file; the `wsl` row's text is
  byte-identical to today's (a literal in the test). RED against the E1 row: `"the vibe/linux row still says unmeasured — the admission
  says one thing and the row another"`. Extension: the flipped `:382-389`.
- The existing structural tests (`ConsultantLimitationsTests.cs:47`, `:67`, `:82`, `:91`, `:112`) stay green: the row still carries
  `measured` and no `source`.

**Break-its.** (B13) keep `standing: unmeasured` → S-N8 red both sides; (B14) drop `keyring off` from the text → the C# words case red.

**Done when.** S-N8 red then green on both sides; B13–B14; the four docs updated and the diagrams render; Opus review has no open finding.

**Roles.** Opus brief; Sonnet codes the row and the tests, and drafts the docs from S1's §32 and S2/S3's summaries; Opus reviews the docs
against the code (the self-audit of the Knowledge Base Sync rule).

### E2.S5 — The P7 rollout: a live round on a LOCAL binary, the release, the post-release round, the rollback line

**Goal.** Before any tag, the admission branch is proven live on `ubuntu-claude`; the release carries the rollback line; after the release
the same two checks pass on the released build.

**Files.** None in the repository but the release notes (the rollback line) and, on a red, the rollback itself. On the host: a SECOND series
folder for the binary, `/tmp/testsToDelete-native-vibe-<yyyymmdd>-<6 hex>`, made with `make-series.sh` and removed with `remove-series.sh`
exactly as §3.1 — never a path outside the shape.

**Steps (the lead; the owner presses *Update*).**

1. `dotnet publish src_mcp/src/CoaiMcp.csproj -c Release -r linux-x64 -p:Version=0.64.0-local` of the admission branch (S2 merged, S3/S4
   with it) on the lead's WSL2; sha256 recorded in the §32 tail ("32.10 The live round on the local binary").
2. The binary to `<series2>/bin/coai-mcp`, mode `700`, by `scp`. **How the extension is pointed at it:** see Q1 (§9) — the parent names
   "the server-path setting", and no such setting exists in `src_vs_code/package.json` at `3fc30be3b`; the fallback this document assumes
   until the lead answers is to replace, on the host, the binary the extension installed (the file `entryPathIn(rid, version)` names,
   `src_vs_code/src/coaiInstall.ts:61-63`, under the extension's global storage) with the local build, keeping a copy of the installed one
   beside it (`<series2>/bin/installed-coai-mcp`), and restoring it after the round. Both sha256s recorded.
3. Over Remote-SSH on `ubuntu-claude`: the `providers` answer shows the Vibe row enabled with NO refusal (the card: no host note, the row
   not greyed; the server's `--host-facts`, run by its path from `<series2>/bin`, says `admission.Vibe` empty, banner read); then ONE live plan
   round with the Vibe row (paid: one turn, counted against the cap's remainder) answers. A red here is a code change, not a release:
   back to S2/S3 with the symptom; the binary restored.
4. Green → the release: `src_mcp` 0.64.0 (release-please; the second-merger rule re-checks `VIBE_ON_NATIVE_LINUX_SINCE` against the tag)
   with an extension minor; the release notes carry the line: *Rollback: if a native-Linux Vibe row misbehaves, revert the `"Vibe"` entry of
   `HostAdmission.MeasuredOnNativeLinux` in a `src_mcp` patch release (the forced keyring backend stays — it only closes a road) and bump
   `VIBE_ON_NATIVE_LINUX_SINCE` past the bad minor in an extension patch, so no card promises what the server no longer does.*
5. After the release the owner presses *Update* on `ubuntu-claude`; the installed binary is the released one (sha256 against the asset);
   the same two checks (the providers answer, one live plan round) pass. A red here is the rollback line of step 4, executed.
6. `remove-series.sh` on `<series2>`; `/promote-plan` for this document (and the parent only when E3 is done too, per the parent's E3
   *Done when*).

**Scenarios.** The two checks, twice; the record in §32.10/32.11 (the local round: the binary's sha256, the providers line, the round id and
its answer's first line; the released round the same).

**Done when.** Both rounds answered and recorded; the release notes carry the rollback line; the second series folder removed; this
document's status line says `IMPLEMENTED <date>` with the deviations; `plan-lifecycle.mjs` clean.

**Roles.** The lead; the owner for *Update*. No coder.

### The scenario map — every S-N the parent gives E2, and where it lives

| Parent row | Story | Test home |
|---|---|---|
| S-N5 | S2 | `CoaiMcp.Tests` — `VibeRuntimeTests`, `VibeLaunchControlTests` |
| S-N6 (Vibe) | S2 | `CoaiMcp.Tests` — `HostAdmissionOnNativeLinuxTests`, `KimiLaunchChecksTests` unchanged |
| S-N8 | S4 | `CoaiMcp.Tests` — `ConsultantLimitationsTests`; `npm test` — `vibeOnLinux.test.ts:382-389` |
| S-N9 | S3 | `npm test` — `vibeOnLinux.test.ts` |
| S-N10 | S3 | `npm test` — `hostKindLogsAnUnreadableBanner.test.ts`, `vibeOnLinux.test.ts` |
| S-N11 | S3 | `npm test` — `vibeOnLinux.test.ts`, `runtimeSinceIsAhead.test.ts` |
| S-N12 | S2 | `CoaiMcp.Tests` — NEW `VibeNativeLinuxLaunchTests` |
| S-N20 (new, E2) | S0 | `node --test scripts/lib/vibe.test.mjs` |
| S-N21 (new, E2) | S2 | `CoaiMcp.Tests` — `VibeNativeLinuxLaunchTests` (parses `vibe.mjs`) |
| §5 N-H1, N-V0, N-V1–N-V6; §7 Vibe rules; §14 Q1, Q2 | S1 | the record, §32 |
| §10 P7 (Vibe), DN13 | S5 | the two live rounds, §32.10–32.11 |

Nothing the parent assigns to E2 (its *Owns* line: §3 Vibe; DN4, DN6's extension half, DN7, DN8, DN9 Vibe, DN10's forced backend, DN13; §5
Vibe cells; §7 Vibe rules; §8 Vibe; §9 S-N5, S-N6 Vibe, S-N8–S-N12; §10 P2, P3, P7 Vibe; §14 Q1, Q2) is unassigned: DN4 and DN10 are S0 + S1 +
S2; DN6's extension half is S3; DN7 is S1's thresholds; DN8 is S3; DN9 is S1; DN13 is S5; §8 is S2–S4.

### E1's open tail — what E2 must fix before its series, and what stays

E1's document names three items it left for later; read against the code:

| Item | Where | Fires in a Vibe-only series? | E2's decision |
|---|---|---|---|
| `versionEnv` builds the Kimi probe's `kimi --version` environment without an OS-home redirect (`profile` = `os/` is not passed) | `scripts/lib/kimi-home.mjs:326-329` (`builtEnv({ home })` only) | **No** — nothing in E2 starts `kimi`; the facts script is run without `--kimi` and `probe-kimi-confinement.mjs` is not run | Stays for E3 (it is the first Kimi cell's instrument) |
| M-K12 is in the Linux default cell list, and the probe refuses a run that includes M-K12 without `--allow-agents-canary` | `scripts/lib/kimi-d19.mjs:251` (`OWNED_CELLS` holds `M-K12`), `:257-263` (`cellsPlan` takes the owned cells by default off Windows); `scripts/probe-kimi-confinement.mjs:124-126` (the refusal before any launch) | **No** — no Kimi probe runs; note for E3: a `--cells`-less native run would refuse at start-up, and `--allow-agents-canary` is a `RESERVED_FLAG` on the host (`native-run.mjs:58`), so E3 must always name `--cells` | Stays for E3 (E3 passes explicit cells; removing M-K12 from the default list is E3's one-line fix) |
| `trustWithVibe` hands Vibe's `TrustedFoldersManager` the probe's WHOLE environment plus `VIBE_HOME` | `scripts/probe-vibe-confinement.mjs:164-172`; used only by M-V4's arms (`:271-301`) | **No** — M-V4 is not a native cell (parent §3: L-V5/L-V-home do not apply; M-V4 is a Windows/WSL2 trust cell) | Stays out of E2; a one-line `minimalEnv` fix may ride S0 at the Opus brief's discretion since it changes no cell E2 runs — recorded here so it is not forgotten |

So E2 fixes none of the three before its series, and says why: none can fire in a series that starts no `kimi` and runs no M-V4. What E2
DOES fix before the series is the one gap E1's tail did not name — the signed-in cells measured without the closed road (§2.3, S0).

## 5. The failure branch

If any cell of §3.2 fails its pass column (or a stop rule of §3.4 ends the series):

- §32 is written anyway — the predictions, the observed numbers, the failing cell named, the spend, the removal verdict — and ships as the
  only change of E2 besides S0; `HostAdmission.MeasuredOnNativeLinux` stays empty; every sentence of §2.5 stays as it is today; the
  `vibe`/`linux` row stays `unmeasured` (its `source` may gain one clause naming §32 and the cell that failed — that is a doc change, not
  an admission); no `ON_NATIVE_LINUX_SINCE` table is added.
- **S2, S3, S4 and S5 do not run.** S4's doc half shrinks to one paragraph in `module_runners.md` (the series ran, which cell failed, Vibe
  still refused) and the open-tail line of the promoted parent.
- The parent's `### E2` and this document's status say `measured 2026-10-xx — failed at N-Vx; nothing admitted`, and this document is
  promoted with that status and its deviations; the owner decides what, if anything, follows (a re-measurement on another Vibe, a per-host
  manifest entry, a keyring finding).
- E3 is not affected (it depends on E1, not on E2's outcome).

## 6. Build order, and what runs in parallel

1. **S0** (Sonnet, Opus brief and review) — a small PR on its own; merged first. Nothing else can start before it: S1's archive must hold it.
2. **S1** (the lead) — the WSL pre-flight, the predictions, the series, §32. Serial by nature: one phase at a time on one host, the spend
   counter between phases.
3. **S1's verdict.** Full pass → 4. Any failure → §5 and stop.
4. **S2, S3, S4 in parallel** (three Sonnet coders, three Opus briefs; the files do not overlap: `src_mcp` / `src_vs_code` / `shared` + docs)
   — one pull request for the three, or three stacked on one branch; the code gate (`review_code`) on the whole; S4's docs are written last
   from S2's and S3's summaries.
5. **S5** (the lead and the owner) — the local-binary round BEFORE the release is tagged; the release; the post-release round; the second
   series folder removed; promotion.

## 7. Test plan

| What | How | Where it runs |
|---|---|---|
| S-N20 and the `isNativeLinuxHost` cases (S0) | `node --test scripts/lib/*.test.mjs` | Windows and the Linux CI job (`.github/workflows/ci.yml`, the *Probe script tests* step) |
| The E1 suites stay green after S0 | `node --test scripts/lib/*.test.mjs scripts/native/*.test.mjs` | the same (the bash cases on the Linux job) |
| S-N12, S-N21, S-N5, S-N6 (S2) | `dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4`, then `node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*VibeNativeLinuxLaunch*"`, `"*HostAdmission*"`, `"*VibeRuntime*"`, `"*VibeLaunchControl*"`, `"*VibePrintsUtf8*"`, then the whole executable — **never `dotnet test`** | Windows; the Linux and macOS CI jobs (the Linux job's `--host-facts` line read in S2's summary) |
| S-N8 (S4) | the same runner, `--filter-class "*ConsultantLimitations*"`; `npm test` | Windows; CI |
| S-N9, S-N10, S-N11 (S3) | `cd src_vs_code && npm ci && npm test` | Windows; CI |
| The measurement (S1) | the cells' pass columns, recorded in §32 — the measurement IS the test of the claim | `ubuntu-claude` |
| The live rounds (S5) | the providers answer and one plan round, twice | `ubuntu-claude`, over Remote-SSH |
| The plan's own hygiene | `node .agents/conventions/tools/plan-lifecycle.mjs` | after every story and at promotion |

## 8. Risks

- **A signed-in launch under `fail.Keyring` fails where today's passes** (Vibe 2.26.1 touching `keyring` on a signed-in start). This is
  the risk S0 exists for: N-V2/N-V3 measure it on the host with the forced backend in every launch, before any product code carries it. If
  it fires, the cells fail and the road stays closed in the probe only; the owner decides whether `null.Keyring` (reads nothing, raises
  nothing) is measured instead — a new decision, not a quiet substitution.
- **N-V1 arm a or a3 signed in, a3's marker written** — a keyring reaches a coai launch with the road closed: the stop rule; the finding
  would mean the environment-first order does not hold in the host's `keyring` version (N-V0 records it; the free pre-check catches a
  version whose `get_keyring()` ignores the variable before a paid run).
- **`.env` absent on the host** (Q1: stop and ask). **Plugin hashes differ** (Q2: stop; both sets recorded).
- **`--host-facts` disagreeing with the raw reading** on host kind or banner (stop); on a keyring fact (a reader defect, fixed before merge).
- **A fifth built name leaking onto WSL2 or Windows, or inherited from the server** — S-N12's three legs; the §2.6 pins.
- **The admission read as "native Linux" anywhere in the texts without the keyring sentence** — S-N8 and S-N11 pin the words of §2.5.
- **A release before the local-binary round** — P7's order is the gate (S5 step 3 precedes step 4); the rollback line is in the notes.
- **The extension's `linux-unread` kind reaching a site that still treats it as `linux`** — the union makes the exhaustive tables fail
  to compile; the partial one (`HOST_NOTES`) is pinned by S-N10; the Opus review reads every `HostKind` site.
- **A blank-but-read banner judged differently by the two sides** — both now call it unread (the server at `WslGeneration.cs:87`, the
  extension in S3); the shared banner vectors are untouched because `WslGenerations.Of(string)` is.
- **The spend**: 23 planned of 30; one 3-turn re-run fits, a 9-turn one does not — said in §3.2, so a failed M-V1 is a stop-and-ask.
- **The host's `PATH` and root**: E1's `make-series.sh` audit (exit 77) and the data-file rule; a Debian-style group-writable
  `/usr/local/bin` would refuse the series (E1's J6) — the owner's host has it `755 root:root`.
- **The installed coai-mcp's path failing the argument pattern** (a space or a character outside `ARG`) — copied into `bin/` first, sha256
  recorded; never a relaxed pattern.
- **Two local builds, two series folders** (N-V6's from E1's code during S1; P7's from the admission branch in S5) — each in its own shaped
  folder, each removed by its own `SERIES_PATH`.

## 9. Questions for the lead

Assumed where possible, and said:

- **Q1 (real).** How is the extension on `ubuntu-claude` pointed at the locally built binary for the P7 round? The parent (DN13, §6) says
  "the server-path setting", but `src_vs_code/package.json` at `3fc30be3b` has no such setting, and `coaiInstall.ts` installs the binary
  under the extension's global storage (`entryPathIn`, `:61-63`). This document assumes the fallback of S5 step 2 (replace the installed
  file, keep a copy, restore after). If a supported route exists (an MCP registration of the local path, say), S5 uses it instead — a
  one-line change here.
- **Assumed: `'linux-unread'` as a member of the extension's `HostKind` union** (§2.4) rather than a flag beside `hostKind` — because the
  exhaustive tables then fail to compile until they say what such a host is told. The lead may prefer the flag; S3's brief would change,
  nothing else.
- **Assumed: `VIBE_ON_NATIVE_LINUX_SINCE = '0.64.0'`** (§2.7), from the manifest's released 0.63.0; the second merger re-checks.
- **Assumed: the four translations of §2.5 are this document's**; the lead reads the ru and uk ones before S3 merges (they are the
  owner's languages).
- **Assumed: `trustWithVibe`'s environment stays out of E2** (it serves a cell E2 does not run); the Opus brief of S0 may take the
  one-line fix if it wants it.

## 10. Definition of Done — epic E2

- [ ] S0: S-N20 and the `isNativeLinuxHost` cases red then green; B1–B3 both ways; merged before the archive; on WSL2 and Windows the
      probes' environments unchanged.
- [ ] S1: the WSL S-N17 live check recorded; predictions written before every run; N-H1, N-V0, N-V1–N-V6 in §32 with the commit, the
      archive's and the binaries' sha256, the spend (≤ 30), the `~/.vibe` and `.env` verdicts, the deviations, "what this does not show";
      arms a3/a3c recorded as the proof of the closed road; the series folder removed by `remove-series.sh` (exit 0); no `kimi` started;
      no sign-in opened.
- [ ] Either: every cell passed, and —
  - [ ] S2: S-N12 (no-op red first), S-N21, S-N5, S-N6 red then green; B4–B8; the §2.6 pins green without an edit; the whole
        `CoaiMcp.Tests` executable green; Vibe in the set, Kimi not;
  - [ ] S3: S-N9, S-N10, S-N11 red then green; B9–B12; `npm test` green; the `linux-unread` kind handled at every site;
  - [ ] S4: S-N8 red then green on both sides; B13–B14; `module_runners.md`, `module_extension.md`, `module_tests.md` and the promoted
        parent's open tail updated; the diagrams render;
  - [ ] S5: the live round on the LOCAL binary answered before the tag; the release with the rollback line; the post-release round
        answered; the second series folder removed;
  - [ ] the code gate reached `proceed` or `good_enough`; every Opus review has no open finding;
- [ ] Or: a cell failed, §32 records it, nothing admitted, S2–S5 not run, the status lines say so (§5).
- [ ] WSL2's and Windows' Vibe launches byte-identical (§2.6).
- [ ] No committed file names the host other than as `ubuntu-claude`; no IP address anywhere.
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` clean; this document promoted with `/promote-plan` (its status `IMPLEMENTED
      <date>` with deviations; the parent promoted when E3 is done too).
