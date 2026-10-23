# PLAN — E2 stories: the Vibe series on `ubuntu-claude`, and the C1 admission that a full pass switches on

> Status: **stories S0–S4 built and merged on `docs/native-linux-e2` (2026-10-22): S0 the probe amendment, S1 measured (every cell passed, RESULTS_vibe §32), S2 the C1 flip in the server, S3 the extension's half, S4 the limitation row and the docs; the Opus judge round is done (F1–F4 accepted and fixed, F5 is the owner's §9 question — record at the end); the code gate (coai `review_code`) is next; S5 (the live round on a local binary, the release, the rollback line) is open.** The E2 plan round passed earlier (coai 99e086b8, `proceed`, four findings, all accepted and folded the same day; the C1 risk consult 8e2480a0 answered and folded the same day; both records are at the end). **DN1 (2026-10-23, the owner's decision, built the same day): "unmeasured" is a note, never a refusal — Kimi is admitted on a native Linux host without a measurement, with a note; see the section DN1 at the end.**
> Scope: the paid Vibe series on the owner's native Ubuntu host
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
- **`Built` has exactly THREE direct callers** (verified by the C1 risk consult 8e2480a0 and re-read at `3fc30be3b`; an earlier draft here
  said four): `VibeRuntime.Build` (`VibeRuntime.cs:143`, the review launch; the runtime holds its `VibeHost`, `:41`),
  `VibeRuntime.ProbeVersionAsync` (`VibeRuntime.cs:177`, the `providers` probe's `--version` — it BYPASSES `TakeAsync` and goes straight to
  the launcher, so its host argument is load-bearing on its own), and `VibeConsultant.Build` (`VibeConsultant.cs:72`, the consult and the
  question row, through its `inner` runtime). `VibeLaunch.TakeAsync` (`VibeLaunch.cs:143-154`) is the VALIDATION site, not a builder: it
  calls `EnvironmentRefusal` (`:147`) on a request one of the three made, and its own `--version` (`VersionAsync`, `:278-293`) is that same
  request `with { Arguments = ["--version"] }` — the second version-probe path, which inherits whatever the builder wrote. The retry/repair
  road (`VibeRuntime.ForLaunch`, `VibeRuntime.cs:250`) rebuilds arguments on the invocation's existing request and calls no builder, so it
  carries what `Build` made. Each of the three callers passes its host's kind; `TakeAsync` passes `host.Kind` to the control. One builder,
  one control, as D17 demands — and `VibePrintsUtf8OnEveryPathTests.EveryVibePath_RunsInUtf8Mode`
  (`src_mcp/tests/VibePrintsUtf8OnEveryPathTests.cs:41-67`) gains a Linux leg that walks the same five request paths (the reviewer, the
  retry, the consult, the question row, the providers probe), while S2's DELIVERY tests (S-N23) prove the value reaches a real child on
  every road, both version probes included.

**Never inherited from the server's environment.** `Built` sets `InheritsEnvironment = false` and `Passthrough = ProcessEnvironment.Minimal`
(`:99-100`); the Unix minimal list (`ProcessEnvironment.cs:90-91`) is `PATH HOME USER LOGNAME SHELL LANG LC_ALL LC_CTYPE TERM TMPDIR
TMP TEMP DOTNET_ROOT` and does not name `PYTHON_KEYRING_BACKEND`, so a value in coai-mcp's own environment never reaches Vibe; the value
Vibe sees is the one `Built` wrote. S-N12 pins all three legs (§4, S2): the fifth name on Linux, its absence (and refusal) on WSL2 and
Windows, and coai's value winning over a server-environment value.

**Never on WSL2 or Windows.** WSL2 launches are admitted as they were measured (RESULTS_vibe §31) WITHOUT the variable; closing the road
there would be a change to a measured launch, and E2 does not make it. The pins that keep WSL2 and Windows byte-identical are in §2.6.

**Kimi's launch never carries it** — Kimi has no keyring road (its sign-in is a file coai links); `KimiEnvironment` is not touched.

**The claim is qualified by the MEASURED versions, and an unmeasured Vibe says so** (C1 risk consult 8e2480a0, point 2, verified by the
lead). What the closure rests on is specific: `keyring` **25.7.0**'s core short-circuits `load_env()` → `load_config()` → discovery, so the
forced backend beats a `keyringrc.cfg` AND any backend a plugin entry point would offer; and Vibe **2.26.1**'s Linux credential reads go
through `keyring.get_password` — no direct D-Bus or Secret Service path was found in it. A later Vibe could call `keyring.set_keyring`,
or Secret Service directly, and the variable would close nothing. The product today RUNS a Vibe version with no manifest entry, with a
warning (`VibeLaunch.Verdict`, `VibeLaunch.cs:322-326` → `Unmeasured`, `:335-339`, through `VersionWarnings.Unmeasured`,
`src_mcp/runners/Reviewers/VersionWarnings.cs:23-24`; the owner's decision of 2026-10-13: Vibe warns, never refuses) — and E2 keeps that
decision. What E2 adds instead, in three places: (a) the admission remarks (`HostAdmission.cs:22`, `VibeLaunch.cs:72-78`) and §32 say
*the keyring road was measured closed on Vibe 2.26.1 (keyring 25.7.0)*; (b) on `HostKind.Linux` the unmeasured-version warning gains ONE
clause (§2.5 pins the words; S2's S-N22 tests it), so a person running another Vibe on a native host is told the closure is unmeasured for
it; (c) §9 carries a NON-blocking question for the owner — whether an unmeasured Vibe should be refused on native Linux rather than warned.
To carry the clause, `Verdict`, `VersionWarning` and `Unmeasured` take the host (`VibeHost`, or its `Kind` beside the `Platform` string
they take today), and `VibeRuntime.VersionWarning` (`VibeRuntime.cs:280-283`) passes it — the same sentence on the launch and on the card.

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

**The banner is classification, not attestation — and every misclassification lands on the safe side** (C1 risk consult 8e2480a0,
point 3, the reason the lead's doubt is closed). Three cases: (i) a WSL2 kernel misread as native Linux (a banner that names neither
`microsoft` nor `WSL`, which no shipped WSL kernel produces — `shared/wsl-banner-vectors.json` holds the rule) would get the forced backend
on a launch that WSL2 was measured without — the variable only CLOSES a road, so the launch still signs in through the linked `.env` and
nothing opens; (ii) a container whose kernel banner names Microsoft (a WSL-kernel container) reads as `HostKind.Wsl` (`HostKind.cs:59`)
and follows the UNCHANGED WSL branch, judged WSL1/WSL2 by `WslGenerations.Of` (`WslGeneration.cs:29-30`) exactly as today; (iii) a blank
or missing banner is `Unreadable` and refuses a member (§2.1). The admission never reads the banner as proof of what the host IS — only
as which measured branch applies — and the one answer that would be unsafe (an unknown host admitted) is the one it cannot give.

### 2.5 The sentences — pinned words

Every text that today says native Linux is refused changes in the same direction and names the same two facts: the release, and the
closed keyring. The words below are what S-N8 and S-N11 search for; a coder may add around them, never rephrase them.

**Server (not shown to a person; the remarks):** `HostAdmission.cs:7-11` and `:22`, `VibeLaunch.cs:72-78` say *Vibe is measured on a
native Linux host (research/RESULTS_vibe_confinement.md §32, Vibe 2.26.1, 2026-10-xx) and its launch there closes the keyring road
(`PYTHON_KEYRING_BACKEND=keyring.backends.fail.Keyring`, DN10) — the keyring road was measured closed on Vibe 2.26.1 (keyring 25.7.0);
Kimi is not yet measured there (E3)*.

**The unmeasured-version warning on a native Linux host** (S-N22 pins it): today's sentence (`VersionWarnings.cs:23-24`, filled by
`VibeLaunch.Unmeasured`, `:335-339`) — *Vibe X is not a version coai has measured (it measured 2.26.1); it is run anyway, but its
confinement (research/RESULTS_vibe_confinement.md) and its home's plugin hashes belong to 2.26.1; this version's plugin files are admitted
by name only — re-run …* — gains, on `HostKind.Linux` ONLY, this clause after `admitted by name only`:

> `, and the keyring closure (PYTHON_KEYRING_BACKEND) is measured only on Vibe 2.26.1 with keyring 25.7.0 — on this native Linux host another Vibe could reach a keyring by a road this build does not close`

The pinned words: `keyring closure` and `measured only on Vibe 2.26.1 with keyring 25.7.0`. On WSL2, Windows and every other host the
sentence is byte-identical to today's (no clause). The same sentence is the launch's warning and the card's (`VibeRuntime.VersionWarning`).

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
  "text": "Reads only a copy coai makes of the live tree, without its .git, .vibe/, skills and AGENTS.md: read_file is its one tool, a read outside that copy was refused, and every write and shell call was denied - measured on vibe 2.26.1 on a native Linux host. On a native Linux host coai turns Vibe's keyring off, so it signs in only through ~/.vibe/.env, linked into coai's own home and never copied - the keyring road was measured closed on vibe 2.26.1 (keyring 25.7.0).",
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
carried the forced backend — §32 says so in its own column. **So the pass criteria of this series include, beside a3c's marker written
and a3's absent: a SIGNED-IN launch and a RESUMED launch both succeed under `fail.Keyring`** (C1 risk consult 8e2480a0, point 4) — N-V2
(M-V6, M-V1: signed-in homes that answer) and N-V3 (M-V5: a resumed second turn) cover exactly that once S0 has landed, because S0 is what
puts the variable into those launches; without S0 they would prove nothing about the closed road.

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
- **the road is not provably closed — checked BEFORE a paid phase counts (S0's stop rule, the Opus judge round of 2026-10-22).** The probes decide
  "native" by reading `/proc/version` themselves (`runsOnNativeLinux`, S0); a misread silently runs the series on the OPEN road, which measures
  a launch E2 does not ship. So, per phase (`n-v1` … `n-v4`, and every re-run): its header line (`[probe] host: …`, stderr) must say
  `native Linux; keyringRoad: closed`; every row of its `results.json`/table must say `keyringRoad: closed` — except arm **a3c, which must say
  `open`** (its teeth); the `results.json` header (`nativeLinux: true, keyringRoad: closed`) and the manifest's `steps.json`
  (`keyringRoad: closed`) likewise. Any other reading → **stop the series, do not count the phase, record why** (the banner text, the header line,
  the row that said `open`). A phase run on the open road is a measurement of today's product on a host the product will not run it on;
- **N-H1 reports `banner.read = false`** → stop the series before any paid run, and measure only a READ native banner. An unread banner runs the
  probe OPEN (S0: `isNativeLinuxHost` is false for it), while the product classifies the same host as `HostKind.Linux` with
  `WslGeneration.Unreadable` and would add the fifth name on it (§2.3) — so the series would measure a launch the product makes differently;
  the admission refuses Vibe there, which is why that case is harmless to ship, and why it is not a case to measure;
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
   outcomes. **Every paid or probe phase's table — N-V1, N-V2, N-V3 and N-V4 (the manifest's `steps.json`), not only N-V1 — carries the
   `keyringRoad` column** (`closed` in every row, `open` in a3c's alone) beside the header line it was copied from (`native Linux; keyringRoad:
   closed`) and N-H1's `banner.read` (§3.4's two stop rules are the check; this column is the evidence that they were run).
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

**Goal.** On a native Linux host — the banner read and naming no Microsoft/WSL (the product's HostKind rule for a read banner; an unread one runs the probe open — S0's judge round and §3.4) — every Vibe launch the probes make
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
  **Why a3c's deletion holds even when the PROBE's own process has the name set** (plan round 99e086b8, finding 3): off Windows the base of
  the child environment is `minimalEnv(platform, source)` (`probe-vibe-confinement.mjs:501`), an ALLOW-LIST over `MINIMAL_UNIX`
  (`scripts/lib/vibe.mjs:30`, `:40-49` — only the thirteen named variables are copied, absent names stay absent), never a spread of
  `process.env`; the `replaceEnv` path (`:497`) takes the arm's own object, which `builtEnv` builds over the same allow-list (`vibe.mjs:62-63`),
  and the manifest script uses that `builtEnv` (`vibe-home-manifest.mjs:126`). So `PYTHON_KEYRING_BACKEND` can enter a child only through
  `NATIVE_LINUX_BUILT` (S0) or an arm's own `env` — and a3c's deletion removes both. On `win32` the base IS the whole `source` (`:501`, as
  the Windows cells were measured), but a3/a3c do not apply there (`keyringArm`, `vibe.mjs:287`) and the native-Linux spread never does.
  S-N20 pins the inherited case below, so the allow-list stays the reason rather than an assumption.
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
  **The inherited case** (finding 3): *given* a `source` environment that itself holds `PYTHON_KEYRING_BACKEND=keyring.backends.null.Keyring`
  (the probe run from a shell that set it), `nativeLinux: true`, *then* a3c's child environment LACKS the name, and a3's (and M-V6's)
  carries `keyring.backends.fail.Keyring` — the forced value, never the inherited one; *given* `nativeLinux: false` (WSL2) with the same
  `source`, no child carries the name at all. RED against a base built by spreading `source` instead of `minimalEnv`: `"a3c inherited
  PYTHON_KEYRING_BACKEND from the probe's own shell — the child base is not the allow-list"` and `"a3 carried the shell's
  keyring.backends.null.Keyring, not the forced keyring.backends.fail.Keyring"`.
- `isNativeLinuxHost`: the three banners of `host-facts.test.mjs:206-208` (`7.0.0-30-generic` → true; `microsoft-standard-WSL2` → false;
  unread → false), `win32` → false; `builtEnv(…, { nativeLinux: true })` holds the name, `builtEnv(…)` does not.
- The S-N17 cases (`:262-293`) stay green unchanged.

**Break-its.** (B1) drop the `delete` for `dropsKeyring` → the a3c case red; (B2) make `isNativeLinuxHost` ignore `namesWsl` → the WSL2
banner case red; (B3) spread `NATIVE_LINUX_BUILT` unconditionally in `builtEnv` → the WSL2 case of S-N20 red; (B3b) build the Linux base
as `{ ...source }` instead of `minimalEnv(platform, source)` → the inherited case red with its two messages.

**Done when.** S-N20 and the `isNativeLinuxHost` cases observed red with the messages above, then green, on Windows and the Linux CI
job; B1–B3 both ways; Opus review has no open finding; merged BEFORE S1's archive is taken.

**Roles.** Opus brief; Sonnet codes; Opus reviews (reading `childEnvFor` for the one deleted name and the cut-out test's `new Function`
seam, which only sees what `minimalEnv` and the sliced source give it — `isNativeLinuxHost` must be passed in, not imported inside the
slice).

### S0 — status: built 2026-10-22 on `docs/native-linux-e2`, merged there; the Linux CI job is green (run 39769143254, on `c7857f21d`); the Opus judge round is done (the record at the end)

Built as the spec above, test-first, with the deviations below. The tests start no CLI and nothing paid ran in them (one accidental manual run is recorded below): the whole story is `node --test`
over cut-out functions and injected file systems. `node --test scripts/lib/*.test.mjs scripts/native/*.test.mjs` on Windows: **401 tests, 344 pass,
0 fail, 57 skipped** (after the judge round below) (the bash/`/proc`/`setsid` cases skip by name, saying they run on the Linux CI job); before the story it was 344 / 287 / 0 / 57,
so the story adds 57 tests (12 in `vibe.test.mjs`, 4 in `host-facts.test.mjs`, 39 in the new `cli-args.test.mjs` of the incident below), all green on Windows. The Linux CI job (*Probe script tests*) ran green (run 39769143254,
`success`, on `c7857f21d`): that was the story's remaining half of "Done when".

**What was built, where.**

- `scripts/lib/vibe.mjs` — `NATIVE_LINUX_BUILT` (one object literal), `KEYRING_FAIL_BACKEND` now derived from it (one spelling; `keyringArm`'s `forced` is a copy of it),
  pure `isNativeLinuxHost({ platform, banner })`, `builtEnv(…, { nativeLinux })`, `keyringRoadOf(env)` (`closed | open`), and `dropsKeyring: true` on arm a3c alone
  (`false` on every other arm).
- `scripts/lib/host-facts.mjs` — `runsOnNativeLinux({ platform, fsx })`: `readBanner` + `isNativeLinuxHost`, the ONE reading the Vibe probe and the manifest script share;
  `deps.vibePython` (the interpreter `--vibe-python` names) for N-V0; N-V0's forced reading goes through `builtEnv(…, { nativeLinux: true })` instead of re-spreading the variable.
- `scripts/probe-vibe-confinement.mjs` — `NATIVE_LINUX` read once; `childEnvFor(spec, { cli, platform, env, nativeLinux })` adds `NATIVE_LINUX_BUILT` after the base and before
  `spec.env` and deletes the name when `spec.dropsKeyring`; the local `builtEnv(home)` (so `versionRun` and M-V16's prime) takes the option; M-V7's setup hands `dropsKeyring`
  to the spec; every launch's `meta` and every row carry `keyringRoad`; the header line and `results.json` say `keyringRoad: closed | open`.
- `scripts/vibe-home-manifest.mjs` — `builtEnv` takes the option from `runsOnNativeLinux()`; `steps.json` records `keyringRoad`.
- `scripts/probe-native-linux-host.mjs` — `--vibe-python <path>` (in `FLAGS`, so `SAFE` checks it), handed to `collectHostFacts`.

**Deviations from the spec (all small, none changes a measured launch on WSL2 or Windows).**

1. **The host reading lives in `host-facts.mjs`, not `vibe.mjs`.** The spec puts the pure `isNativeLinuxHost` in `vibe.mjs` (done) but both scripts also need to READ `/proc/version`;
   `host-facts.mjs` already imports `vibe.mjs`, so the reader (`runsOnNativeLinux`) sits beside `readBanner` and `vibe.mjs` imports nothing back (no cycle, one copy of the "names Microsoft or WSL" rule).
2. **The road is added on the `replaceEnv` path too**, not only on the base path. Every `replaceEnv` child (the built environment of M-V16, the `versionRun`, and M-V12's question-row
   environment) is a Vibe launch the probe starts, and the plan's own sentence is "every Vibe launch the probes make"; `dropsKeyring` is honoured there as well. The `--cli inherit` behaviour of that path is unchanged.
3. **On WSL2 arms a, a2 and a3 still carry their own forced value** — E1 measured them that way (`keyringArm`, unchanged). "WSL2 carries nothing" in S-N20's inherited case therefore holds for every child
   that is not one of those three arms (M-V6-shaped and a3c are asserted absent; a3 is asserted to carry the arm's own forced value, never the shell's).
4. **B3b's two spec messages cannot both fire from `{ ...source }` alone**, because the built code is immune on two further counts: the a3c child has the name deleted, and an a3/M-V6 child has the forced value
   written AFTER the base. The mutation is still caught (its WSL2 legs and the `SECRET` leg go red); the `a3c inherited …` message fires when B3b is combined with B1 (see the break-its), and the `a3 carried the shell's …` message is the guard for a base that would let an arm's own absent value fall through to the shell's — with the arm's forced value written last it is not reachable by one mutation.
5. **Two source guards were added** (`vibe.test.mjs`): the probe's M-V7 setup and the manifest script run on import and cannot be imported, so a text assertion pins that the setup hands `keyringSetup`'s
   `dropsKeyring` to the spec and that the manifest builds its environment with the option and records `keyringRoad`. They are guards of wiring, not behaviour; the behaviour is in the cut-out cases.
6. **`trustWithVibe`'s environment was left alone** (§4, *E1's open tail*): it serves M-V4, which no native cell runs.

**REDs observed (before the real definitions; `isNativeLinuxHost`/`keyringRoadOf`/`runsOnNativeLinux` were stubs that compiled and answered `false`/`"open"`, `childEnvFor` ignored the option).**

| Case | Failure message |
|---|---|
| S-N20 M-V6 on a native host | `M-V6 on a native Linux host carries no PYTHON_KEYRING_BACKEND — the signed-in cells would measure a launch E2 does not ship (DN10)` |
| S-N20 replaced environment | `a replaced environment on a native Linux host carries no PYTHON_KEYRING_BACKEND` |
| S-N20 inherited case | `M-V6 carried the shell's keyring.backends.null.Keyring, not the forced keyring.backends.fail.Keyring` |
| S-N20 only a3c drops | `Expected values to be strictly deep-equal` — actual `[]`, expected `['a3c']` |
| S-N20 wiring guard | `M-V7's setup no longer hands keyringSetup's dropsKeyring to the spec — a3c would carry the variable on a native host` |
| `isNativeLinuxHost` | `7.0.0-30-generic is a native host` |
| `builtEnv` option | `builtEnv(..., { nativeLinux: true }) carries no PYTHON_KEYRING_BACKEND` |
| `keyringRoadOf` | actual `'open'`, expected `'closed'` |
| N-V0 `--vibe-python` | `the facts name the interpreter they asked` |
| `runsOnNativeLinux` | `Expected values to be strictly equal` (false for a native banner) |
| the probe accepts `--vibe-python` | exit 65 where 69 was expected |

The a3c message `a3c on a native Linux host still carried PYTHON_KEYRING_BACKEND — its marker could never be written, so a3 would prove nothing` is the "stub that only adds" RED; it was observed as B1 below (the drop removed from the finished code
is the same state).

**Break-its — each applied to the finished code, the named test seen red with the real symptom, the file restored byte-identical (compared), the suite green again.**

| | Mutation | Red |
|---|---|---|
| B1 | drop the `delete` for `dropsKeyring` (base path) | `a3c on a native Linux host still carried PYTHON_KEYRING_BACKEND — its marker could never be written, so a3 would prove nothing`; and the inherited case: `a3c inherited PYTHON_KEYRING_BACKEND from the probe's own shell — the child base is not the allow-list` |
| B1b | drop the `delete` on the `replaceEnv` path | the replaced-environment case |
| B2 | `isNativeLinuxHost` ignores `namesWsl` | `microsoft-standard-WSL2 is not a native host` |
| B3 | `builtEnv` spreads `NATIVE_LINUX_BUILT` unconditionally | the Linux built-environment case and the `builtEnv` option case |
| B3 (child) | `childEnvFor` adds the road unconditionally | the WSL2 cases: `WSL2 carried PYTHON_KEYRING_BACKEND — a measured launch was changed`, `WSL2: a child inherited PYTHON_KEYRING_BACKEND …`, and the three pre-existing environment cases |
| B3b | base is `{ ...source }` instead of `minimalEnv(platform, source)` | `SECRET reached the child`, `the shell still does not reach the child`, `WSL2: a child inherited PYTHON_KEYRING_BACKEND from the probe's own shell`; combined with B1 (and the road moved after `spec.env`) the spec's `a3c inherited PYTHON_KEYRING_BACKEND from the probe's own shell — the child base is not the allow-list` fires as well |
| B4 | N-V0 ignores `deps.vibePython` | `the facts name the interpreter they asked` |
| B5 | M-V7's setup stops handing `dropsKeyring` over | the wiring guard |
| B6 | `--vibe-python` removed from `FLAGS` | the probe-accepts-the-flag case |
| B7 | the manifest drops the option | `the manifest's built environment no longer takes the native Linux option — N-V4 would measure a launch E2 does not ship` |

**Incident and fix (2026-10-22): a value-taking flag with no value started Vibe.** While smoke-testing S0 the builder ran `node scripts/vibe-home-manifest.mjs --unstage` (no platform) to see that the
script still loaded. The script read `flag('--unstage', '')` as `''`, took that as "the flag was not given", skipped the unstage branch and fell through to `measure()`: it made three `vibe --version`
launches and one tiny model turn (about 7 000 input tokens on the Mistral subscription; Windows, the Credential Manager sign-in), all on the script's own scratch homes under the temp
folder, which the builder then removed. `~/.vibe` and the repository were untouched (no `--write`, `git status` showed only the builder's own edits) and no Vibe process of the builder's was left running.
The defect was the script's, not the invocation's, and it differed per script. `vibe-home-manifest.mjs`: a value-taking flag with no value (`--unstage`, `--from`, `--vibe`, `--scratch`), or followed by another `--flag`, was read as an absent flag, and an
unknown flag such as `--help` was ignored — every one of those fell through to `measure()`, a paid run. `probe-vibe-confinement.mjs`: the same lookup, and its worst case is `--cells` with no value, which fell back to EVERY cell (`ALL_CELLS`) — a full paid run; the other value flags fell back to their defaults and unknown flags were ignored.
`probe-native-linux-host.mjs` was already stricter: its even-argument-count check and allow-list refused a trailing value-less flag, `--help` and any unknown flag, and its only hole was taking another `--flag` as a value (`--series --kimi`, since `--kimi` matches its safe pattern). The fix: one pure parser,
`scripts/lib/cli-args.mjs` (`parseFlags`, `FLAG_SPECS` with the arity audit, `manifestPlan`), which the three scripts now call first — a value-taking flag needs exactly one non-empty value that is not a `--flag`
(and, for the host probe, one that matches its safe pattern), and an unknown flag, a stray word or a repeated flag is refused too; a refusal names the flag and exits 65 before anything is read or launched.
`cli-args.test.mjs` (39 tests) walks every value-taking flag of every script (manifest `--vibe --scratch --from --unstage`; Vibe probe `--vibe --scratch --repeats --cells --cli --arms --reparse`; host probe
`--series --scratch --kimi --coai-mcp --vibe-python`), once with no value and once with a `--flag` or an empty string as its value, plus `manifestPlan(['--unstage'])`. RED (stub keeping the old fall-through,
37 of 38 failing): `--unstage with no value planned "measure" - that is the fall-through that started Vibe`, `--unstage with no value was accepted`, `--unstage took "--write" as its value`. Break-its, restored byte-identical:
C1 (the missing-value check removed from the parser) — the same cases red; C2 (the manifest script back on a private `argv.indexOf` lookup) — `vibe-home-manifest.mjs still has a private argv.indexOf flag lookup - the fall-through that started Vibe`.
The tests exercise the pure parser and source text only; none of the three scripts is started by them (except the pre-existing host-probe cases of `host-facts.test.mjs`, which stop at the usage or platform refusal off Linux).

**S0 — judge round (Opus, 2026-10-22).** The judge read S0 at `417548c17`: the code is ready for the paid series, with one condition (J1) and five low items (J2–J6), all accepted and fixed in this round.
**J1 (condition).** (a) The Vibe probe's own wiring had no test: with `nativeLinux: NATIVE_LINUX` deleted from `childEnv` (`probe-vibe-confinement.mjs`, the `childEnvFor` call) the whole suite stayed green (399 tests, 342 pass, 0 fail, 57 skipped — observed before the fix), i.e. the series
would have run on the OPEN road without a test noticing. `vibe.test.mjs` now guards the probe's three wiring lines over its CODE (`lib/code-text.mjs` `codeOf` drops comment lines and trailing `//` comments, so a comment quoting the line cannot keep a guard green; the manifest and
`dropsKeyring` guards use it too): `const NATIVE_LINUX = runsOnNativeLinux();`, `childEnv` passing `nativeLinux: NATIVE_LINUX`, and the local `builtEnv` taking the option. RED→break-its, each restored byte-identical: `nativeLinux: NATIVE_LINUX` removed from `childEnv` →
`the Vibe probe's childEnv no longer passes nativeLinux: NATIVE_LINUX to childEnvFor — no child would carry the closed road on a native host (N-V2/N-V3/N-V4 would measure a launch E2 does not ship)`; `const NATIVE_LINUX = false;` → `the Vibe probe no longer reads the host once through
runsOnNativeLinux() — every child would run the open road`; the option dropped from the local `builtEnv` → `the Vibe probe's local builtEnv no longer takes the native Linux option — the version run and M-V16's prime would run the open road`. (b) §3.4 gains two STOP RULES and §3.5 the evidence column:
before any paid phase counts, its header line must say `native Linux; keyringRoad: closed`, every row `closed` except a3c's `open`, and `results.json`/`steps.json` `closed` — otherwise stop and record why; and the series STOPS when N-H1 reports `banner.read = false` (an unread banner runs the probe open while
the product would add the name — measure only a READ native banner). §3.5 item 4 requires the `keyringRoad` column in the N-V2, N-V3 and N-V4 tables too, not only N-V1's.
**J2.** The incident write-up above is reworded per script: the manifest script and the Vibe probe read a missing value as an absent flag (the probe's worst case: `--cells` with no value fell back to EVERY cell — a full paid run), while the host probe's even-argument check and allow-list already
refused a trailing value-less flag, `--help` and unknown flags, its only hole being another `--flag` as a value (`--series --kimi`); `cli-args.mjs`'s header and `module_runners.md` say the same. **J3.** `vibe.mjs`, the probe's header and `module_runners.md` now state the unread-banner rule exactly: the product classifies it
`HostKind.Linux` with `WslGeneration.Unreadable`, the admission refuses Vibe there (§2.3's `Built` would still add the fifth name); the probe runs that case open — harmless because no shipped launch differs, and not a case to measure (§3.4). The S0 goal sentence is corrected to match. **J4.** "every probe child" /
"cannot reach any child" became "every Vibe launch" (`trustWithVibe` passes the whole environment, M-V4 only, not a native cell; win32's base is the whole shell environment). **J5.** The header line says `S0 adds it to no launch` on WSL2/Windows (arms a/a2/a3 carry the name there); no test asserted that text.
**J6.** The audit test is no longer circular: it compares each script's `FLAG_SPECS` entry with the flags that script's code actually reads (quoted `--flag` literals, plus `--arms` for `armFilterOf(argv)`), so a script reading a flag its spec lacks, or a spec flag nothing reads, is red — break-its: `flag('--bogus', '')` added to the Vibe probe,
and the manifest's `--stage` read misspelt, both red with `… reads flags its FLAG_SPECS.<script> does not declare (or declares ones it never reads)`. Windows: `node --test scripts/lib/*.test.mjs scripts/native/*.test.mjs` → **401 tests, 344 pass, 0 fail, 57 skipped**.

**Done when, as it stands.** S-N20 and the `isNativeLinuxHost` cases red then green on Windows: yes. Green on the Linux CI job: yes — run 39769143254 (`success`). B1–B3 both ways: yes. Opus review: done (the judge round at the end).
Merged before S1's archive: open (the branch is `docs/native-linux-e2`; no PR was made by this story).

### E2.S1 — The series on `ubuntu-claude`, and RESULTS_vibe §32 (no code; the lead)

**Goal.** §3, end to end: predictions first, the facts, the six Vibe cells, the two readings of N-V6, the record, the removal.

**Files.** `research/RESULTS_vibe_confinement.md` (§32, appended after line 1426); nothing else in the repository. The series folder on
the host, for the duration of the series only.

**Scenarios.** The cells of §3.2 against §3.3's predictions, with §3.4's stop rules. The record is the scenario: every cell's table in §32,
the `~/.vibe` verdict per phase, the spend against the cap, the removal verdict. The pass criteria, named: a3c's marker written and a3's
absent (N-V1); a signed-in launch (N-V2) and a resumed launch (N-V3) succeeding under `fail.Keyring` — covered once S0 lands (§3.2); the
pinned hashes equal (N-V4); the two readings agreeing (N-V6).

**Break-its.** None — a measurement has no mutant. What replaces them: the teeth rule (a3c), the free pre-checks (N-V0's forced class
reading), and the two readings of N-V6 (one implementation read twice could only agree with itself).

**Done when.** §32 holds 1–7 of §3.5; the owner has been told the outcome (pass, or which cell failed and with what numbers); the series
folder is gone (`remove-series.sh` exit 0, the `.env` `stat` identical); the parent's `### E2` and this document's status line say
"measured, <pass | failed at N-Vx>"; `plan-lifecycle.mjs` clean. **Nothing in `src_mcp`, `src_vs_code` or `shared/` changed by S1.**

**Roles.** The lead, from Windows; the owner answers Q1/Q2 if a stop reaches them (`ask_user`). No coder.

### S1 — status: measured 2026-10-22, every cell PASSED

The series `/tmp/testsToDelete-native-vibe-20261022-374500` ran on `ubuntu-claude` from the archive of `c7857f21d`: `facts`, N-V1–N-V6, all
PASS; 22 paid turns of 30; no stop rule fired (banner read; the forced-backend class `fail.Keyring`; `keyringRoad: closed` in every paid row,
`open` in a3c's alone, a3c's marker 3 of 3, a3's 0 of 3; `~/.vibe` 74 → 74 in every phase); the folder removed by `remove-series.sh` (exit
0, the sign-in facts identical). The record is RESULTS_vibe §32 with four deviations: the S-N17 WSL check and the AOT build in WSL were not
possible (this session has no WSL access — the same teeth ran on the host; N-V6 used a self-contained JIT build of the same commit), `vibe`
resolved at `~/.local/bin/vibe`, and the host HAS a session bus (`/run/user/0/bus`, the VS Code server's environment names it) with no
keyring daemon — the closed road is therefore not academic on this host. Nothing in `src_mcp`, `src_vs_code` or `shared/` changed. **S2–S5
may proceed** (the full-pass branch of §5).

### E2.S2 — The C1 flip in the server: Vibe enters the set, the launch closes the road (only on a full pass)

**Goal.** `HostAdmission.MeasuredOnNativeLinux` = {Vibe}; a native Linux Vibe launch carries the forced backend and the control admits
exactly that there and only there; every WSL2 and Windows answer and launch byte-identical; the remarks name §32.

**Files.**

- `src_mcp/runners/Platform/HostAdmission.cs:22-25` (the entry and its remark), `:7-11` (the class remark).
- `src_mcp/runners/Vibe/VibeLaunch.cs:58-61` (two constants), `:72-78` (the remark), `:87-90` (`EnvironmentRefusal(request, HostKind host)`),
  `:97-108` (`Built(request, home, model, HostKind host)`), `:111-119` (`CarriesTheFour`/`CarriesTheFive` — rename `CarriesTheThree`, which
  has been four since §26), `:146-147` (`TakeAsync` passes `host.Kind` — the validation site), `:322-339` (`Verdict`, `VersionWarning`,
  `Unmeasured` take the host for the native-Linux clause of §2.5).
- **The host threaded into the three `Built` callers and the validation site**, no more and no fewer: `VibeRuntime.cs:143` (the review
  launch), `VibeRuntime.cs:177` (the providers `--version` probe — it bypasses `TakeAsync`, so this argument alone decides what the probe
  carries), `VibeConsultant.cs:72` (consult and question row); and `VibeLaunch.cs:147` (`EnvironmentRefusal(request, host.Kind)`). A
  `Built`-forwarding method on `VibeRuntime` so no caller spells the kind twice is the Opus brief's call. `VibeRuntime.VersionWarning`
  (`:280-283`) passes the host for the clause.
- Tests: NEW `src_mcp/tests/VibeNativeLinuxLaunchTests.cs` (S-N12, S-N21, S-N22) and NEW `src_mcp/tests/VibeNativeLinuxDeliveryTests.cs`
  (S-N23, a real child on the Linux CI job); `VibeRuntimeTests.cs:363-368`, `VibeLaunchControlTests.cs:192-202`
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
- **S-N22 (the unmeasured-version clause).** *Given* a manifest with an entry for 2.26.1 only and a `--version` of `2.27.0`, *then* on
  `HostKind.Linux` `VibeLaunch.VersionWarning` and the launch's `LaunchTurn.Warning` contain `keyring closure` and `measured only on Vibe
  2.26.1 with keyring 25.7.0`; on `HostKind.Wsl` (WSL2) and `HostKind.Windows` the sentence is byte-identical to today's (a literal in the
  test, no clause); for 2.26.1 itself the warning is empty on every host; `VibeRuntime.VersionWarning` on a native host gives the same
  sentence as the launch. RED (the clause not written): `"an unmeasured Vibe 2.27.0 on a native Linux host was not told the keyring closure
  is measured only on 2.26.1 — a later Vibe could reach a keyring by a road this build does not close"`.
- **S-N23 (DELIVERY — the value reaches a real child on every road), `VibeNativeLinuxDeliveryTests`, `[Linux only]` by a skip reason
  elsewhere.** Not a request-dictionary assertion: a REAL child through the real `ProcessLauncher` (as `ACliRowsEffortTests.cs:25` and
  `AChildThatExitedBeforeItsStartTimeWasReadTests.cs:35` already launch one). The child is the fake CLI (`src_mcp/tests_fakecli`,
  `FakeCli` beside the test binary — `AFakeCliSteeringFileIsBesideItsBinaryTests.cs:24` names it), whose `RecordEnvironment`
  (`src_mcp/tests_fakecli/Program.cs:617-627`) already writes every `VIBE_*` and `PYTHON*` name WITH its value to a record file — so
  `PYTHON_KEYRING_BACKEND` lands in it with no change to the fake. The test runs under `VibeHost.Using` a `HostKind.Linux` host with a
  read banner and a fake sign-in in its own scratch (the `VibeFlowHost` shape, `src_mcp/tests/VibeFlowHost.cs`), and with the TEST PROCESS's
  own environment set to `PYTHON_KEYRING_BACKEND=keyring.backends.null.Keyring` for the duration (restored in `finally`) — a CONFLICTING
  value. Six roads, each a case: the review launch (`VibeRuntime.Build` → `ReviewerExecutor`), the retry/repair (`ForLaunch`), the consult
  (`VibeConsultant.Build`), the question row (`ConsultantLaunches.ForQuestion` over the consultant's request), the providers probe
  (`ProbeVersionAsync`), and `TakeAsync`'s own `--version` (the record file of the `--version` child). *Then* each record file holds
  `PYTHON_KEYRING_BACKEND=keyring.backends.fail.Keyring` — never the null one, never absent. And the refusal half: a request whose fifth
  entry is MISSING (`built with { Environment = Without(name) }`) or WRONG (`With(name, "keyring.backends.null.Keyring")`) handed to
  `TakeAsync` on the Linux host is refused BEFORE spawning — the outcome is `NotStarted`/`Refused` with the D17 sentence and the fake's
  record file was NEVER written (the strongest observation: not "refused", but "no child existed"). REDs, against the threading left out
  on one road at a time: `"the providers --version probe on a native Linux host reached the child WITHOUT keyring.backends.fail.Keyring
  (it carried keyring.backends.null.Keyring from the server's own environment) — the one road that bypasses TakeAsync"` (the probe road
  is the one to show first, since it bypasses validation), `"the consult child on a native Linux host carried no PYTHON_KEYRING_BACKEND"`,
  and for the refusal half, against a control that validates after spawning: `"a Vibe launch missing PYTHON_KEYRING_BACKEND on a native
  Linux host SPAWNED before it was refused — the fake wrote its record file"`. On WSL2 (the `VibeFlowHost.Wsl2` arm) the same six record
  files hold NO `PYTHON_KEYRING_BACKEND` line even with the conflicting server value set — the pin that the closed road never leaks.

**Break-its.** (B4) remove `"Vibe"` from the set → S-N5 red with the refusal sentence; (B5) add the fifth name on every host → S-N12's
WSL2 leg red and S-N23's WSL2 leg red; (B6) make `EnvironmentRefusal` on Linux accept four names → S-N12's "a four-name launch on Linux is
refused" leg red and S-N23's refusal half red; (B7) read the variable from the process environment → the inherited-value leg of S-N12 red
and S-N23's six roads red (they carry the null backend); (B8) change `NATIVE_LINUX_BUILT`'s value in `vibe.mjs` → S-N21 red (and S-N20
still green, which is why S-N21 exists); (B8b) drop the clause on Linux → S-N22 red; add it on every host → S-N22's WSL2 literal red; (B8c)
pass `HostKind.Windows` from `ProbeVersionAsync` only → S-N23's providers-probe road red while the other five stay green — the proof that
the delivery test sees each road on its own.

**Done when.** S-N12 (with the no-op red shown first), S-N21, S-N22, S-N5, S-N6 observed red then green; S-N23 observed red then green ON
THE LINUX CI JOB (its messages copied from that job's log into the summary; it skips by name on Windows and macOS); B4–B8c both ways; the
§2.6 pins green WITHOUT an edit; the whole `CoaiMcp.Tests` executable green; `coai-mcp --host-facts` on the `ubuntu-latest` job shows
`admission.Vibe` empty; Opus review has no open finding.

**Roles.** Opus brief (it fixes the two constants' names, the `host` parameter's position, and the order "no-op first, red, then the
arm"); Sonnet codes; Opus reviews, reading `EnvironmentRefusal`'s two counts side by side with `Built`.

### S2 — status: built 2026-10-22 on `feat/native-e2-s2`, merged into `docs/native-linux-e2`; the Opus judge round is done (F2 and F4 touched it: the banner pin and the allow-list arm, see the record at the end)

Built as the spec above, RED first, with the deviations below. The product is `d9a3d07e8` (the arm) over `65036a3b9` (the RED commit: the host threaded
through `Built`, `EnvironmentRefusal`, `Verdict` and `VersionWarning` as a NO-OP, the two new test classes, the flipped pins). Three break-it commits were then
pushed ONLY so the Linux CI job could show S-N23's teeth — `3e0eee720` (B6 + B8c), `09a13ef33` (B4 + B5), `7e8c710ba` (B7) — and `de4670b9e` writes the product
back: `git diff d9a3d07e8 de4670b9e -- src_mcp scripts` is empty. Whole `CoaiMcp.Tests` executable on Windows, once, at low priority on a shared machine
(the product of `de4670b9e`): **13 532 cases, 13 392 passed, 135 skipped, 5 failed — none of them S2's**, each green when run alone right after (5 of 5):
`AConversationPlaceTests.CopiesThatOutrunTheRowsBudget_RefuseThePlace_…` (*… "0 of 2 copied …" to contain "1 of 2"* — a one-second copy budget),
`TheServerAnswersInitializeAtOnceTests.Initialize_IsAnswered_WhileTheVaultReadAndTheProbeAreStillRunning` (*System.TimeoutException : The operation has timed out*
in `StdioServer.RoundTrip`), `ConsultAntigravityDenialScenarioTests.AHungFollowUp_IsKilledByTheLauncher_InsideTheRemainingBudget_…` (*Expected clock.Elapsed to be
less than 11s … but found 28s*), and both rows of `AChildThatExitedBeforeItsStartTimeWasReadTests.AChildThatIsStillEndingWhenTheReadFails_IsGivenAMomentToEnd_…`
(*Did not expect any exception … but found System.InvalidOperationException: Cannot process request because the process has exited* / *Win32Exception (0x80004005)*);
the same suite was green on every CI job of run 39781674311. The S2 classes on Windows:
296 cases, 279 passed, 17 skipped by name (the Linux-only ones), 0 failed.

**What was built, where.** `HostAdmission.MeasuredOnNativeLinux` = `{ "Vibe" }`; its remark and the class remark name §32 (Vibe 2.26.1, `ubuntu-claude`,
2026-10-22), the closed road, why Kimi is not in it, and the rule that an absent fact refuses. `VibeLaunch`: three constants beside `Utf8Variable`
(`KeyringBackendVariable`, `KeyringBackendClosed`, `KeyringClosureMeasuredOn`); `Built(request, home, model, HostKind host)` adds the fifth name on
`HostKind.Linux` only (ONE predicate, `ClosesTheKeyringRoad`); `EnvironmentRefusal(request, HostKind host)` counts five on Linux and four elsewhere,
refuses the fifth elsewhere and pins its value on Linux (`CarriesTheThree` is now `CarriesTheBuiltNames`); the refusal sentence names five/four; `TakeAsync`
passes `host.Kind`; `Verdict`/`VersionWarning`/`Unmeasured` take the host and add §2.5's clause on Linux only. The three `Built` callers go through ONE
internal forwarder, `VibeRuntime.Built(request, home, model)`, which passes the adapter's own `host.Kind` (`VibeRuntime.Build`, `ProbeVersionAsync`, and
`VibeConsultant.Build` through its `inner`); `VibeRuntime.VersionWarning` passes the host. Tests: `VibeNativeLinuxLaunchTests` (S-N12, S-N21, S-N22, the S-N5
twin), `VibeNativeLinuxDeliveryTests` (S-N23, Linux only), the Linux leg of `VibePrintsUtf8OnEveryPathTests`, the flipped pins; the fake CLI records a bare
`--version` child's environment too (`FAKECLI_VERSION_RECORD_ENV`); `VibeFlowHost` gained a `NativeLinux` mode and a `Host` property. The case-by-case
table is `research/module_tests.md` (*E2.S2*); the facts for S4's text are in `research/module_runners.md` (the DL1/E2 section, *Native Linux, E2.S2*).

**The REDs, observed.**

- **S-N12, S-N21, S-N22, S-N23 — CI run 39777405670 on `65036a3b9` (the no-op).** Windows, macOS and Linux jobs all red on the request-level cases:
  *a native Linux Vibe launch carries no PYTHON_KEYRING_BACKEND — the host's keyring could sign a fresh owned home in (DN4)*; *a four-name Vibe launch on a native
  Linux host was admitted — the keyring road is open*; *the launch carried the server's PYTHON_KEYRING_BACKEND, not coai's*; the refusal sentence without the fifth
  name; S-N21 *the probes forced keyring.backends.fail.Keyring and the product forces  — the cells measured another launch*; S-N22 *an unmeasured Vibe 2.27.0 on a
  native Linux host was not told the keyring closure is measured only on 2.26.1 …* (and the launch/card case); the Linux leg of `VibePrintsUtf8OnEveryPathTests`
  and `VibeLaunchControlTests.ANativeLinuxHost_StartsTheRow_…`. S-N23 on the **Linux job only**: the six native roads, each record holding `PYTHONUTF8=1`, the three
  `VIBE_*` and the minimal names and NO `PYTHON_KEYRING_BACKEND` line (*… to contain "PYTHON_KEYRING_BACKEND=keyring.backends.fail.Keyring" because the providers
  --version probe on a native Linux host reached the child WITHOUT keyring.backends.fail.Keyring … — the one road that bypasses TakeAsync*, and *the consult --version
  child on a native Linux host carried no PYTHON_KEYRING_BACKEND*); the refusal half: `missing` *Expected turn.Refusal "" to contain "D17"* (admitted), `wrong`
  refused with the four-name sentence. The six WSL2 roads were green, as they must be. GREEN: run 39781674311 on `d9a3d07e8`, every job.
- **S-N5, S-N6 — NOT red in `65036a3b9`**, which already held `"Vibe"` in the set (the order "no-op first" was kept for the keyring only). Their teeth were shown
  after the fact, by B4 (the entry removed), on Windows and on the Linux job (run 39805049620): *a native Linux host should start a Vibe row now that it is measured
  (RESULTS_vibe §32), but it was refused, but found "Vibe runs on WSL2 only for now — it is measured on WSL2 and not yet on a native Linux host (DL1 of
  research/PLAN_kimi_and_vibe_on_linux.md)"*, the S-N6 matrix row `Vibe/Linux/None`, the unread-banner sentence cases, and — Linux job only — the real
  `--host-facts` line (`HostFactsModeTests.OnANativeLinuxHost_TheRealLineSaysLinux_BannerRead_VibeAdmitted_AndKimiRefused`: *… to be empty because E2 admits Vibe on
  a native Linux host with a read banner, but found "Vibe runs on WSL2 only for now …"*). That test, green in runs 39781674311 and later, is the record that
  `coai-mcp --host-facts` on `ubuntu-latest` reports `admission.Vibe` empty and `admission.Kimi` refused (the job prints no line of its own).

**Break-its, each red then restored by writing the file back and rebuilding** (Windows = the S2 classes by the executable; Linux = the CI job):

| | Mutation | Windows | Linux job |
|---|---|---|---|
| B4 | `"Vibe"` removed from the set | 16 red: S-N5 (the twin, `ANativeLinuxHost_IsAdmitted_OnAReadBanner`, the launch-control row), the S-N6 matrix, four native banner vectors, the unread-banner sentences, the launch/card case — each with the WSL2-only sentence | run 39805049620 (with B5): also the real `--host-facts` line; the native roads through `TakeAsync` and both refusal cases refused with the WSL2-only sentence; the providers probe green (it bypasses the admission) |
| B5 | the road closed on every host | builder only: 53 red (every Windows/WSL2 launch refused by the control, *a WSL2 Vibe launch carried PYTHON_KEYRING_BACKEND — a measured launch was changed*, the §2.6 pins); builder and control (`ClosesTheKeyringRoad => true`): 14 red in the S2 classes | run 39805049620: the six WSL2 roads red, each record carrying `PYTHON_KEYRING_BACKEND=keyring.backends.fail.Keyring` |
| B6 | the control accepts a four-name launch on Linux | 1 red: the `four names` row (*… was admitted — the keyring road is open*) | run 39803154228 (with B8c): the `missing` refusal case alone (*Expected turn.Refusal "" to contain "D17"*); `wrong` green |
| B6b | the fifth name's value not pinned | 2 red: `another backend`, `an empty value` | — |
| B7 | `Built` takes the server's own value | 1 red: *the launch carried the server's PYTHON_KEYRING_BACKEND, not coai's* (differs at index 17) | run 39807218471: all six native roads — the providers probe's child carried `keyring.backends.null.Keyring`; the five roads through `TakeAsync` were refused by the value pin, so no child existed (*… the road never reached a child*) |
| B8 | `NATIVE_LINUX_BUILT`'s value → `null.Keyring` in `vibe.mjs` | S-N21 red (*the probes forced keyring.backends.null.Keyring and the product forces keyring.backends.fail.Keyring — the cells measured another launch*) | — |
| B8b | the clause dropped on Linux | 2 red: the Linux sentence, the launch/card case | — |
| B8b′ | the clause on every host | 4 red: the WSL2 literal, Windows, macOS, `VersionWarningsTests.VibesSentence_IsByteForByteWhatItWas_…` | — |
| B8c | `ProbeVersionAsync` builds with `HostKind.Windows` | 1 red: the providers-probe path of the Linux leg of `VibePrintsUtf8OnEveryPathTests` | run 39803154228: the providers-probe road alone; the other five native roads green — the delivery test sees each road on its own |

**Deviations from the spec.**

1. **The RED commit already held `"Vibe"` in the set**, so S-N5 and S-N6 were not red in it; B4 is their red (above). The keyring half kept the order.
2. **§2.6's "pass WITHOUT an edit" could not hold, in two ways.** (a) Mechanical: `Built` and `EnvironmentRefusal` take the host, so every call site in the
   pins gained an argument (`VibeLaunchRulesTests` built-environment checks, `VibePrintsUtf8OnEveryPathTests`' Windows request, `ConfinementPlannerTests`,
   `VibeConsultantTests`, `TheVibeProbeLeavesThePersonsHomeAloneTests`, `AVibeLaunchOnWslLinksTheSignInTests`' manifest case, `VersionWarningsTests`) — no
   assertion changed in them. (b) Pins that carried a NATIVE-LINUX row and so had to move: `VibeLaunchRulesTests.OnlyAWsl2Banner_StartsVibe` (a banner naming no
   WSL is now a native host that starts — expectation "WSL2, or a native host"), `AVibeLaunchOnWslLinksTheSignInTests.OnlyAWsl2Host_StartsVibe` → `OnlyAMeasuredHost_StartsVibe`
   (the native row flips, an unreadable-banner row added), `AVibeReviewerRunsThroughAFakeVibeTests.APlanRoundOnANativeLinuxHost_IsRefused_NamingWsl2Only_…` →
   `…WithAnUnreadableBanner_IsRefused_NamingTheBanner_…`, and `HostFactsModeTests`' two report cases (read banner: Vibe admitted; unreadable: the banner sentence)
   beside the Linux real line §2.6 named. `KimiLaunchChecksTests` and `HostAdmissionOnNativeLinuxTests.TheSet_IsNotConsultedOutsideNativeLinux` are untouched.
3. **The pins moved on purpose** (§2.6's list): `VibeLaunchRulesTests.ANativeLinuxHost_IsAdmitted_OnAReadBanner`, `VibeLaunchControlTests.ANativeLinuxHost_StartsTheRow_OnAReadBanner_InTheClosedKeyringEnvironment`
   (+ its unreadable-banner sibling), `HostAdmissionOnNativeLinuxTests.TheProductsSet_AdmitsVibe_AndNotKimi_OnAReadNativeLinuxHost` (the one line "E1 admits
   nothing" is the assertion that changed), the S-N6 matrix, `ARuntimeOutsideTheSet_GetsTodaysWsl2OnlySentence_OnAnUnreadableBanner` (S-N7 kept with an injected
   empty set), `HostFactsModeTests` (the Linux line: Vibe empty, Kimi non-empty).
4. **S-N23's RED did not show "the null value from the server's environment"** on the probe road, as the spec's message predicted: under the no-op the child carried
   NO backend at all (the minimal passthrough already kept the server's value out). The test's reason text is the spec's; the record shows the truth. The
   server's value reaching a child was shown by B7 instead (the probe road delivered `null.Keyring`).
5. **B7 turns five of the six native roads red by refusal, not by delivery**: the control pins the value, so a launch built with the server's value never spawns;
   only the probe road (no control) delivers it — which is why the probe road exists in S-N23.
6. **B8 also turns four of S-N20's `node --test` cases red** (they assert the `fail` literal) — the plan expected S-N20 to stay green. S-N21 is still the only
   case that ties the script to the PRODUCT's value.
7. **A WSL2 distro on a custom kernel whose banner names no WSL** (`wsl2-custom-kernel-without-any-marker` in `shared/wsl-banner-vectors.json`) now starts Vibe as a
   native host, in the keyring-closed launch (before: the WSL2-only refusal). Stated, not solved — the vector's `note` was updated to say so.
8. **The break-its rode the feature branch**: four extra commits (three deliberately wrong, one restore) instead of a scratch branch, because the concurrency group
   of `ci.yml` is per ref and the dispatches were sequential.

**Done when, as it stands.** S-N12, S-N21, S-N22 red then green: yes (CI 39777405670 → 39781674311). S-N5, S-N6 red then green: yes, by B4 after the fact. S-N23
red then green on the Linux job: yes (same runs). B4–B8c both ways: yes. The §2.6 pins green: yes, with the edits of deviation 2. Whole `CoaiMcp.Tests` green:
on CI (run 39781674311, every job; the docs-only head after it is dispatched on the branch), on Windows locally 5 load-timing failures outside S2,
each green alone (above). `admission.Vibe` empty on `ubuntu-latest`: yes (the real-line test). Opus review: open.

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

**The `HostKind` audit — every consumer outside the two exhaustive note tables, decided** (plan round 99e086b8, finding 4). Read at
`3fc30be3b` by a grep of `HostKind` and `hostKind` over `src_vs_code/src` (non-test) and of every `=== 'linux'`-shaped comparison; the
coder re-runs the grep and extends the table if the tree moved:

| Site | What it does with the kind | On `linux-unread` | The test that pins it |
|---|---|---|---|
| `hostSide.ts:149-155` `hostKindOfPlatform` | a platform alone → `windows` / `darwin` / `linux`; never asks a banner | never answers it — a platform alone is `linux`, which from E2 RUNS Vibe, so this fallback is a promise wherever a caller skips the banner; the only product caller that could skip it does not (next rows) | the existing case `vibeOnLinux.test.ts:257` ("a caller that names no host gets its platform's own") kept, its sentence updated to say the platform's kind is `linux` and the product never calls it without the banner |
| `panelProvider.ts:473` `hostKindHere = hostKind()`; `:1415` into `PanelState.hostKind`; `:1954` `commandFor(vendor, hostPlatform(), await this.hostKindHere)` | asked once per window; carried, never compared | passes through unchanged — **as itself** | `panelView.ts:202` types the field as `HostKind`, so a `'linux-unread'` fixture is a compile-time member; a new card case renders a `PanelState` with `hostKind: 'linux-unread'` |
| `panelView.ts:1161` `host = state.hostKind` → `runtimeSkewNote(serverVersion, [v], host)` | the card's host note | **as refused** (the `'linux-unread'` arm of `HOST_NOTES`) | S-N10's `runtimeSkewNote` case; the card case above asserts the refusal note and no greyed boxes (`runtimeHeld` is the server's knowledge of the runtime, not the host) |
| `panelView.ts:1188` `kimiAllowNetworkSignIn: host === 'wsl2' \|\| host === 'linux' ? state.settings.kimiAllowNetworkSignIn : undefined` | shows the Linux-side Kimi network sign-in box | **as linux** — it is a Linux side, and the box is a setting, not a promise to run; the comparison gains `\|\| host === 'linux-unread'` | new case: a state with `hostKind: 'linux-unread'` yields the setting, `windows` and `darwin` yield `undefined` |
| `vendorTerminal.ts:419` `vibeNoteFor(host ?? hostKindOfPlatform(platform))`, `:578` `kind = host ?? hostKindOfPlatform(platform)` | the install note per host | **as refused** when the host is passed (`VIBE_NOTES['linux-unread']`, `KIMI_NOTES['linux-unread']`); the `??` fallback stays the platform's kind (`linux`) — only a caller that asked no banner reaches it, and the product's one caller (`panelProvider.ts:1954`) passes the kind | new: `vendorInstall(VIBE, 'linux', 'linux-unread').note` is the refusal note and names the banner; the Kimi twin |
| Serialisation | `hostKind` travels only inside the `PanelState` object from `panelProvider.ts:1415` into the panel's render (`panelView.ts:1161`); no write into settings, the ledger, a log line, a round, the `.mcp.json` block or the server — the grep finds none | nothing persists it — **as unknown to every store**, because no store receives it | a structural list test: the non-test sources that mention `hostKind` are exactly `hostSide.ts`, `panelProvider.ts`, `panelView.ts`, `vendorTerminal.ts`, `runtimeSince.ts` (`wslNetwork.ts:155` is a comment, updated) — a new consumer fails it until classified here |
| `chatOrphans.ts:302`, `hostSide.ts:101`, `:124`, `:182`, `coaiInstall.ts:47`, `localEngines.ts:64` | compare `Platform` (`'linux'`), not `HostKind` | unaffected | — |

**Break-its.** (B9) make `runtimeOnNativeLinux` fall back to `sinceMet` (no entry = met) → the Kimi-on-`linux` case red (Kimi's refusal
gone); (B10) make `hostKind` return `'linux'` on a failed read → S-N10 red; (B11) drop the `'linux-unread'` arm of `HOST_NOTES` → the
`linux-unread` note case red ("promised"); (B12) leave the ru sentence old → S-N11's ru case red; (B12b) leave `panelView.ts:1188` at
`'wsl2' || 'linux'` → the audit's `kimiAllowNetworkSignIn` case red (`"a linux-unread side lost its Kimi network sign-in box"`); (B12c)
add a `hostKind` mention to a settings writer → the structural list test red.

**Done when.** S-N9, S-N10, S-N11 observed red then green; B9–B12 both ways; `npm test` green; `tsc` lists no site left over the widened
union; the lead has read the ru and uk sentences; Opus review has no open finding.

**Roles.** Opus brief (it carries §2.5's sentences verbatim and the union decision of §2.4); Sonnet codes; Opus reviews (every `HostKind`
site, and that no `'linux'` arm silently serves `'linux-unread'`).

### S3 — status: built 2026-10-22 on `feat/native-e2-s3` (from `docs/native-linux-e2` `755e19142`), merged into `docs/native-linux-e2` (`6ef0bd4a3`); the Opus judge round is done; the lead's read of ru/uk is open

**First run on the merged tree:** CI run 39825037272 (dispatched on `9b259700d`, `docs/native-linux-e2` after the S3 and S4 merge) — all jobs green (`completed`, `success`: extension typecheck and test, workflows actionlint, build · test · family checks, macOS · mcp · server · bench, Windows · mcp, the real-editor job).

Built as the spec above, test-first, `src_vs_code` and its docs only (`src_mcp` is S2's, `shared/consultant-limitations.json` S4's — neither touched).
The extension's `npm test` (compile + every file under `out/test` and the source `.test.mjs` files): see *Result* below.

**What was built, where.**

- `runtimeSince.ts` — `VIBE_ON_NATIVE_LINUX_SINCE = '0.64.0'`, `ON_NATIVE_LINUX_SINCE = { vibe }` (no `kimi`), `runtimeOnNativeLinux` (**false when the runtime has
  no entry**, true for an unknown server), `HOST_NOTES.linux` per runtime (`nativeLinuxNote`: Vibe nothing at/above 0.64.0, the update note below it, Kimi's unchanged
  refusal at any version), the NEW `HOST_NOTES['linux-unread']` arm, `hostRuntimes` counting a native entry too (`hasLinuxEntry`), and `VIBE_NATIVE_LINUX`.
- `hostSide.ts` — `HostKind` gains `'linux-unread'`; `hostKindOf('linux', blank or whitespace)` answers it (aligned with the server: today's `linux` for a blank banner is gone);
  `hostKind` answers it on a failed read, still one warning, whose text now says *treated as a Linux host whose banner could not be read, so a Vibe row is shown as refused here*.
- `vendorTerminal.ts` — `VIBE_NOTE_LINUX` is `VIBE_SCRIPT_NOTE + VIBE_ENV_FILE + ' ' + VIBE_NATIVE_LINUX`, NEW `VIBE_NOTE_LINUX_UNREAD`, `VIBE_NOTES` gained the key (six different notes),
  `KIMI_NOTES['linux-unread'] = KIMI_NOTE_LINUX`.
- `vendors.ts` (the hint), `helpContent.ts`, `helpDe/Es/Ru/Uk.ts` — §2.5's sentences, the old refusal removed; the question-consultant parenthesis in five languages.
- `panelView.ts` — `onLinuxSide(host)` (`wsl2 | linux | linux-unread`) decides the Kimi network sign-in box; comments in `panelProvider.ts`, `panelView.ts`, `wslNetwork.ts`, `kimiLinuxLine.ts` brought level.

**RED, observed** (against a compile scaffold — the union member and the two table entries mirroring the old `linux` text, nothing else — because the widened union is
what the compiler forces first; the failures are the plan's own messages): S-N9 `server "0.64.0": a native Linux host with coai-mcp 0.64.0 was still told Vibe is refused there — the
card does not know ON_NATIVE_LINUX_SINCE` (actual `coai-mcp refuses a vibe row on a native Linux host for now — Vibe is measured on WSL2 only.`, expected `''`) and `native Linux at 0.58.0`;
S-N10 `an unreadable banner promised a Vibe row: it is neither native Linux nor WSL, it is a refusal` (`'linux' !== 'linux-unread'`), `server "0.59.0": an unreadable banner promised a Vibe
row`, and `hostKindOf('linux', '')` `'linux' !== 'linux-unread'`; S-N11 `the en article never says On a native Linux host coai-mcp runs Vibe from coai-mcp 0.64.0 and turns Vibe's keyring off
there, … about Vibe on a native Linux host`, the hint (`the hint does not say what the server does on a native Linux host`), the install note, and `the en question-consultant help does not
say what is refused for a Vibe row: …`; the audit `a linux-unread side lost its Kimi network sign-in box` (`0 !== 1`); the structural test red on the `wslNetwork.ts` comment. The full
suite then added one the plan's file list did not name: `wslBannerVectors.test.ts` — `hostKindOf for empty-banner` (`'linux-unread'` vs `'linux'`).

**Break-its, each red then restored (tree clean afterwards).** B9 (`runtimeOnNativeLinux` → `sinceMet`): `Kimi has no native entry: absence is a refusal, not "met"`, `server "0.57.0": Kimi's
native refusal moved`, and `kimiOnLinux`'s `native Linux: refused at ANY version, never "update"`. B10 (`hostKind` returns a native banner on a failed read): `an unreadable banner promised a Vibe
row: it is neither native Linux nor WSL, it is a refusal`. B11 (no `linux-unread` arm — here the key retargeted to `darwin`, so it still compiles): `server "0.59.0": an unreadable banner
promised a Vibe row`, `the card says why an unread host refuses a kimi row`. B12 (ru sentence old): `the ru article never says На нативном Linux-хосте coai-mcp запускает Vibe …` and `the ru help
still says: На нативном Linux-хосте coai-mcp пока отклоняет Vibe — он измерен только на WSL2`. B12b (`onLinuxSide` without `linux-unread`): `a linux-unread side lost its Kimi network sign-in box`.
B12c (a `hostKind` comment added to `settingsShape.ts`): `a new consumer of the host kind (settingsShape.ts) was not classified in the audit table of E2.S3` and `settingsShape.ts mentions the host
kind: a persisted linux-unread would outlive the window that could not read its banner`. The guard (`VIBE_ON_NATIVE_LINUX_SINCE` set to `0.63.0` once): `vibe: ON_NATIVE_LINUX_SINCE 0.63.0 is not ahead
of the released 0.63.0 and its section does not name vibe on a native Linux host`.

**The `HostKind` audit — outcome.** Each row of the table above was implemented as decided and pinned: `hostKindOfPlatform` never answers `linux-unread` (the `vibeOnLinux` case kept, its sentence
updated); `panelProvider.ts:473/:1415/:1954` carry it as itself (comments only; a `PanelState` with `hostKind: 'linux-unread'` is a compile-time member and a card case renders it); `panelView.ts:1161`
draws the refusal (`runtimeNote` matches the banner sentence, `runtimeHeld` is false, the page shows it on the card); `panelView.ts:1188` keeps the box **as linux** (`onLinuxSide`;
`windows`/`wsl1`/`darwin` give `undefined`); `vendorTerminal.ts:419/:578` use `VIBE_NOTES`/`KIMI_NOTES` when the host is passed and the platform's `linux` when not;
serialisation: the grep re-run found the same five modules, and `hostKindConsumers.test.ts` pins the list (it scans raw text, comments included, so the `wslNetwork.ts:155` comment was
reworded rather than listed). Nothing outside the audit table consumes the kind.

**Deviations from the spec.**

1. **`VIBE_NATIVE_LINUX` lives in `runtimeSince.ts`, re-exported by `vendorTerminal.ts`.** The spec puts it in `vendorTerminal.ts` for `vendors.ts` to import, but `vendorTerminal.ts` imports `Vendor`
   from `vendors.ts`: that is a new import cycle (`importCycles.test.mjs` freezes the count, and the graph counts type imports). `runtimeSince.ts` is imported by both and already holds the release
   the sentence interpolates. The tests import it from `vendorTerminal`, as the spec says.
2. **`wslBannerVectors.test.ts` changed** (not in the spec's file list): the shared vector `empty-banner` has generation `none` (no WSL), which the test mapped to `linux`; a blank banner is now
   `linux-unread`. `shared/wsl-banner-vectors.json` is untouched (it speaks of WSL generations, and the server's tests read it too).
3. **The RED was taken against a scaffold, not against bare E1 code** — a widened union does not compile without its table entries, so the two entries (mirroring the old `linux` text) and the
   new constants existed before the first red run; every behaviour the tests pin was still the old one.
4. **`onLinuxSide` is a new helper** in `panelView.ts` (the ternary would have grown to three comparisons inside an already long function); `hasLinuxEntry` likewise in `runtimeSince.ts`
   (`ON_NATIVE_LINUX_SINCE` is a subset of `ON_LINUX_SINCE`, so counting it changes no result today — it follows the spec's sentence and stays right if a native-only runtime ever exists).
5. **Kimi's tables hold five different notes for six kinds** — `KIMI_NOTES['linux-unread']` is `KIMI_NOTE_LINUX`, as the spec says; `kimiOnLinux.test.ts` asserts the equality.

**Result.** `npm test` (final run): **6501 compiled tests, 6478 pass, 0 fail, 23 skipped**, plus the 14 source `.test.mjs` files, 0 fail, exit 0. The first full run had two failures — `wslBannerVectors`
(`hostKindOf for empty-banner`, fixed, deviation 2) and `notificationsFile.test.js` *a write that never settles does not hold the window open, and says so* (`the rest lands, and a complete drain says
true`: `false !== true`, 30 s, under the suite's parallel load; it passes alone, 20/20, and passed in the final full run — nothing in it touches the host kind). Targeted files green: `vibeOnLinux` 40/40, `hostKindLogsAnUnreadableBanner` 5/5, `kimiOnLinux` 11/11, `kimiNetworkSignInSetting` 13/13, `hostKindConsumers` 2/2, `runtimeSinceIsAhead`
8/8, `wslBannerVectors` 11/11. `npm run typecheck` clean; `eslint` clean on every touched file. **Open:** the lead reads the ru and uk sentences (§2.5's table, as pinned, plus the parenthesis
the plan left to "the same words": de `mit abgeschaltetem Schlüsselbund von Vibe`, es `con el llavero de Vibe desactivado`, ru `с отключённой связкой ключей Vibe`, uk `з вимкненою в’язкою ключів Vibe`);
the Opus review; the release number if another `src_mcp` minor ships first (DL14).

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

### S4 — status: built 2026-10-22 on `feat/native-e2-s4` (from `docs/native-linux-e2` 755e19142), merged into `docs/native-linux-e2` (`33a9f1c7a`); the Opus judge round (the docs against the code) is done

**First run on the merged tree:** CI run 39825037272 (dispatched on `9b259700d`) — all jobs green (`completed`, `success`: extension typecheck and test, workflows actionlint, build · test · family checks, macOS · mcp · server · bench, Windows · mcp, the real-editor job).

Built as the spec above, test-first. **What changed.**

- `shared/consultant-limitations.json` — the `vibe`/`linux` row is §2.5's, word for word: `confined`, `measured` with `date` **2026-10-22** (§32's date; the spec's `2026-10-xx` placeholder filled), `cliVersion` `vibe 2.26.1`, the `cells` and `document` of §2.5, no `source`. Nothing else in the file moved (`git diff` is 8 lines added, 3 removed, inside that one row).
- `src_mcp/tests/ConsultantLimitationsTests.cs` — S-N8's C# half (two tests: the row; the `wsl` row's text as a literal plus the `windows`/`macos`/Kimi-`linux` standings). `src_vs_code/src/test/vibeOnLinux.test.ts` — the `linux` row test flipped to `confined` / `native Linux host` / `keyring off` / `~/.vibe/.env` / the version-bound clause / `Section 32`.
- Docs: `research/module_runners.md` (the Vibe reviewer intro, the launch-control line, the Kimi diagram's set check, DL1's refused list, and a new bullet *Native Linux, E2.S4* under DL1), `research/module_extension.md` (a separate short subsection *Vibe on a native Linux host — the limitation row*, and the old `linux`-row sentence qualified), `research/module_tests.md` (a new *E2.S4* section), `research/PLAN_kimi_and_vibe_on_linux.md:22` (the open-tail line).

**RED, GREEN, break-its.** RED against the E1 row — C#: *Expected row.Standing to be LimitationStanding.Confined … because the vibe/linux row still says unmeasured - the admission says one thing and the row another, but found LimitationStanding.Unmeasured*; extension: *the admission starts Vibe there; the row must not still say unmeasured — actual 'unmeasured', expected 'confined'* (both the real symptom, not a setup error; the `wsl` literal test was green throughout). GREEN after the row: `CoaiMcp.Tests.exe --filter-class "*ConsultantLimitationsTests"` 17/17; `vibeOnLinux.test.js` + `kimiOnLinux.test.js` 42/42. **B13** (`standing` back to `unmeasured`): both sides red with the messages above. **B14** (`keyring off` → `keyring disabled`): the C# test red, *Expected row.Text … to contain "keyring off" because the admission never reads as native Linux without the keyring sentence (S-N8)*, the extension test red too. The row was restored byte-identical after each (compared against a copy) and the tests re-run green.

**Deviations from the spec.**

1. **There is no generated file derived from `consultant-limitations.json`.** The spec asked to find and regenerate them; there are none: the server EMBEDS the file (`CoaiMcp.Runners.csproj:34`, logical name `CoaiMcp.Runners.consultant-limitations.json`), the existing `TheFile_ParsesThroughTheSourceGeneratedContext_AndTheServerCarriesThatFile` holds the embedded copy equal to the repository's (it passes after a rebuild), and the extension reads the JSON directly (`vibeOnLinux.test.ts`, `kimiOnLinux.test.ts`; `consultantHealth.ts` only types what `--consultants` returns). `src_vs_code/src/securityLane.generated.ts` is rewritten by `npm run compile` and was NOT staged.
2. **The docs describe S2's and S3's behaviour (the set holding `Vibe`, the fifth environment name, `ON_NATIVE_LINUX_SINCE`) from this plan, not from merged code**: S4 was built on `docs/native-linux-e2`, which carries neither story. The new bullet and subsection say so and defer to the code on any disagreement; the lead reconciles the `file:line` citations when S2/S3 merge, and S2/S3 still owe their own module-doc rows (`module_tests.md` rows for S-N5, S-N9–S-N12, S-N21, S-N22; the extension's notes section). Where the spec's line ranges overlap theirs, S4's text is a separate short subsection.
3. **The `cells` text names N-V1 to N-V6 and details N-V1 to N-V4**, as §2.5 pins it; §32's N-V5 (48 `.env` vectors) and N-V6 (`--host-facts` read twice) passed too and are not spelled in the row.

### E2.S5 — The P7 rollout: a live round on a LOCAL binary, the release, the post-release round, the rollback line

**Goal.** Before any tag, the admission branch is proven live on `ubuntu-claude`; the release carries the rollback line; after the release
the same two checks pass on the released build.

**Files.** None in the repository but the release notes (the rollback line) and, on a red, the rollback itself. On the host: a SECOND series
folder for the binary, `/tmp/testsToDelete-native-vibe-<yyyymmdd>-<6 hex>`, made with `make-series.sh` and removed with `remove-series.sh`
exactly as §3.1 — never a path outside the shape.

**Steps (the lead; the owner presses *Update*).**

1. `dotnet publish src_mcp/src/CoaiMcp.csproj -c Release -r linux-x64 -p:Version=0.64.0-local` of the admission branch (S2 merged, S3/S4
   with it) on the lead's WSL2; sha256 recorded in the §32 tail ("32.10 The live round on the local binary").
2. The binary to `<series2>/bin/coai-mcp`, mode `700`, by `scp`; its sha256 recorded. **It runs BESIDE the installed server, never in its
   place** (plan round 99e086b8, findings 1 and 2, both accepted): replacing the file the extension installed under its global storage
   (`entryPathIn`, `src_vs_code/src/coaiInstall.ts:61-63`) was the first draft here and is withdrawn — a running server makes the write fail
   with `ETXTBSY`, a background update can overwrite the file mid-round, and an interrupted run leaves the host on an unofficial binary.
   The parent's "server-path setting" (DN13, §6) does not exist in `src_vs_code/package.json` at `3fc30be3b`, so the route is this: a
   scratch project folder INSIDE the series, `<series2>/live-project/` — a tiny git repository holding one committed plan file — with a
   project-scoped `<series2>/live-project/.mcp.json` whose `mcpServers.coai` names `<series2>/bin/coai-mcp` and carries an `env` of
   `COAI_DATA_DIR=<series2>/live-data` and `COAI_VENDORS=[<the Vibe row>]`. The block is produced by the extension's own
   `mcpServerBlock(binaryPath, env)` (`src_vs_code/src/mcpBlock.ts:60-64`; the `Claude Code (one project)` shape of `:27`) on Windows and
   copied by `scp`, so it is the shipped shape, not a hand-written one. Claude Code reads a project's `.mcp.json` only when opened in
   that folder (`src_vs_code/src/mcpClientsRead.ts:29` names it as the per-project client file), so the person's normal sessions never
   read it; the installed extension binary is never touched, so there is nothing to restore; and when `remove-series.sh` removes the
   folder, the registration, the data folder and the binary go with it.
3. The two checks, on the local build: **(a) the providers answer** — `<series2>/bin/coai-mcp --providers` (`src_mcp/src/Program.cs:386`,
   a one-shot mode) run by its path under the same two variables shows the Vibe row enabled with NO refusal, and `--host-facts` by path
   says `admission.Vibe` empty with the banner read; **(b) one live plan round** — a Claude Code session opened over Remote-SSH in
   `<series2>/live-project` (`open`, then `review_plan` with the committed plan) gets the Vibe row's answer (paid: one turn, counted
   against the cap's remainder). **The scripted alternative**, when no session should be opened on the host: the repository already drives
   a coai-mcp over its stdio in `scripts/live-consult-all.mjs` — it spawns the binary by path with `COAI_DATA_DIR` and `COAI_VENDORS` in
   the child's environment and speaks line-framed JSON-RPC over the pipes (`scripts/live-consult-all.mjs:26-45`, `tools/call` of
   `consult` at `:94`); a Vibe consult goes through the same `VibeLaunch.Built`, so its answer is the closed road's too. It is copied into
   `<series2>/run/` and started by the argument-array launcher of §3.1 (`node` by `node.txt`'s absolute path), never by an inline string;
   `src_mcp/tests/StdioServer.cs` is the same shape for the C# suites and is not run on the host. A red here is a code change, not a
   release: back to S2/S3 with the symptom; nothing on the host to restore.
4. Green → the release: `src_mcp` 0.64.0 (release-please; the second-merger rule re-checks `VIBE_ON_NATIVE_LINUX_SINCE` against the tag)
   with an extension minor; the release notes carry the line: *Rollback: if a native-Linux Vibe row misbehaves, revert the `"Vibe"` entry of
   `HostAdmission.MeasuredOnNativeLinux` in a `src_mcp` patch release (the forced keyring backend stays — it only closes a road) and bump
   `VIBE_ON_NATIVE_LINUX_SINCE` past the bad minor in an extension patch, so no card promises what the server no longer does; the owner
   presses Update once more. Nothing on any host needs restoring by hand: the pre-release round ran a separate binary beside the
   installed one, never in its place.*
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
| S-N22 (new, E2) | S2 | `CoaiMcp.Tests` — `VibeNativeLinuxLaunchTests` (the unmeasured-version clause) |
| S-N23 (new, E2) | S2 | `CoaiMcp.Tests` — `VibeNativeLinuxDeliveryTests` (a real child on the Linux CI job; skips by name elsewhere) |
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
  folder, each removed by its own `SERIES_PATH`; neither is ever installed — P7's is registered only for a scratch project inside its
  series folder, so an interrupted round leaves the host on the binary the extension installed (plan round findings 1 and 2).

## 9. Questions for the lead

Assumed where possible, and said:

- **Q1 — answered by the plan round (99e086b8, findings 1 and 2), no longer open.** The question was how the extension is pointed at the
  locally built binary, since the parent's "server-path setting" does not exist. The route chosen (S5 step 2): the local build runs as a
  SEPARATE stdio MCP server, registered through a project-scoped `.mcp.json` in a scratch project inside the series folder (the shipped
  `mcpServerBlock` shape), or driven by `scripts/live-consult-all.mjs` over its stdio — never by replacing the installed binary, which
  risked `ETXTBSY`, an update overwriting it mid-round, and a host left on an unofficial binary. The release check then uses the released
  binary through the normal extension.
- **Assumed: `'linux-unread'` as a member of the extension's `HostKind` union** (§2.4) rather than a flag beside `hostKind` — because the
  exhaustive tables then fail to compile until they say what such a host is told. The lead may prefer the flag; S3's brief would change,
  nothing else.
- **Assumed: `VIBE_ON_NATIVE_LINUX_SINCE = '0.64.0'`** (§2.7), from the manifest's released 0.63.0; the second merger re-checks.
- **Assumed: the four translations of §2.5 are this document's**; the lead reads the ru and uk ones before S3 merges (they are the
  owner's languages).
- **Assumed: `trustWithVibe`'s environment stays out of E2** (it serves a cell E2 does not run); the Opus brief of S0 may take the
  one-line fix if it wants it.
- **For the owner, NON-blocking (C1 risk consult 8e2480a0, point 2):** should a Vibe version with no manifest entry be REFUSED on a native
  Linux host, rather than run with the warning and its new clause (§2.5)? The keyring closure is measured on Vibe 2.26.1 with keyring
  25.7.0 only; a later Vibe could reach a keyring by a road the variable does not close. E2 keeps the owner's 2026-10-13 decision
  (warn, never refuse) and ships the clause; a "refuse" answer is one arm in `VibeLaunch.Verdict` for `HostKind.Linux`, a follow-up.
  **Asked of the question consultant 2026-10-22 (consult 86973bb1, gpt-6-astra; the other two rows were rate-limited): keep
  warn-and-run.** Its reasons, checked against the code: the fifth name is added on every native launch whatever the version
  (`VibeLaunch.BuiltEnvironment`), so an unmeasured version loses only the PROOF, not the closure; refusing would stop every new Vibe
  version on this host until a ~20-turn re-measurement, and the vendor CLI still runs for its own `--version` before `Verdict`, so a
  refusal is not a full boundary either; refusing only when a keyring daemon is SEEN (`KeyringSessionFacts`) is not a sound boundary
  (a snapshot of process names). The owner can still choose "refuse"; until then E2 ships warn-and-run with the clause.

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

## The E2 plan round (99e086b8, 2026-10-22, proceed) — 4 findings, 4 accepted

Verdict `proceed`. Every finding, its decision, and where it was folded; nothing was rejected.

| # | Finding | Decision | Folded into |
|---|---|---|---|
| 1 + 2 (Major, codex + gemini) | S5 step 2 / Q1 replaced the extension's installed binary in its global storage for the pre-release round: `ETXTBSY` on a running server, a background update overwriting it mid-round, a host left on an unofficial binary after an interrupted run | **accepted** — the local build runs BESIDE the installed one as a separate stdio MCP server: registered only for a scratch project inside the series folder through a project-scoped `.mcp.json` written by the extension's own `mcpServerBlock` (`src_vs_code/src/mcpBlock.ts:60-64`), or driven over its stdio by the repository's existing `scripts/live-consult-all.mjs` (`:26-45`, `:94`); the providers check is the binary's own `--providers` by path; nothing is ever restored; the release check uses the released binary through the normal extension | S5 steps 2–4 (the rollback line now says nothing on a host needs restoring); §8; §9 (Q1 closed, the route and the reason stated) |
| 3 (Major, gemini) | S0's "delete `PYTHON_KEYRING_BACKEND` for a3c" must hold when the PROBE's own process has the name set; the document did not say how the child environment is built | **accepted** — stated with lines: off Windows the base is the `minimalEnv` allow-list (`probe-vibe-confinement.mjs:501`, `vibe.mjs:30`, `:40-49`), the `replaceEnv` path and the manifest script build over the same allow-list (`:497`, `vibe.mjs:62-63`, `vibe-home-manifest.mjs:126`); S-N20 gains the inherited case (the probe run with the name set → a3c lacks it, a3 and M-V6 carry the forced value, WSL2 carries nothing) with its two RED messages and break-it B3b | S0 (Files, S-N20, break-its) |
| 4 (Minor, gemini) | The new `'linux-unread'` member of `HostKind` needs an audit of every consumer outside the exhaustive note tables | **accepted** — the audit table in S3: `hostKindOfPlatform` (never answers it; the fallback is the platform's `linux`, reached by no product caller), `panelProvider.ts:473`/`:1415`/`:1954` (carried as itself), `panelView.ts:1161` (as refused), `panelView.ts:1188` (as linux — the Kimi network sign-in box, the comparison widened), `vendorTerminal.ts:419`/`:578` (as refused when the host is passed), serialisation (nothing persists it — a structural list test pins the set of consumers), and the `Platform` comparisons that are not `HostKind` at all; break-its B12b, B12c | S3 (the audit table, scenarios, break-its) |

## The C1 risk consult (8e2480a0, 2026-10-22, gpt-6-astra) — four points, each verified by the lead and acted on

| # | What the consult found | Verified | Acted on — where |
|---|---|---|---|
| 1 | `VibeLaunch.Built` has THREE direct callers — `VibeConsultant.cs:72`, `VibeRuntime.cs:143` (the launch), `VibeRuntime.cs:177` (the `--version` probe); `TakeAsync` is the validation site, not a builder; the providers probe bypasses `TakeAsync` but uses `Built`, so its host argument matters | yes — re-read at `3fc30be3b`; an earlier draft here said "four callers" | §2.3 rewritten (the three callers, the validation site, `TakeAsync`'s own `--version` as the second probe path, `ForLaunch` carrying what `Build` made); S2's host threading names exactly those three plus `VibeLaunch.cs:147` |
| 2 | The closure is a claim about MEASURED versions: `keyring` 25.7.0's core short-circuits env → config → discovery (so the forced backend beats `keyringrc.cfg` and plugin entry points) and Vibe 2.26.1's Linux credential reads go through `keyring.get_password` (no direct D-Bus path found); but `VibeLaunch.cs:322` RUNS an unmeasured Vibe with a warning (the owner's 2026-10-13 decision), and a later Vibe could call `set_keyring` or Secret Service directly | yes | The owner's warn-and-run kept. §2.3 qualifies the claim by version; the admission remarks and §32 say *measured closed on Vibe 2.26.1 (keyring 25.7.0)* (§2.5, the limitation row too); on `HostKind.Linux` the unmeasured-version warning gains one pinned clause (§2.5), `Verdict`/`VersionWarning`/`Unmeasured` take the host, tested by S-N22 (S2) with break-it B8b; §9 carries the NON-blocking owner question "refuse unmeasured Vibe versions on native Linux?" |
| 3 | Host misclassification: a WSL kernel misread as native Linux still gets the forced backend; a WSL-kernel container follows the unchanged WSL branch; a blank or missing banner refuses | yes — `HostKind.cs:59`, `WslGeneration.cs:29-30`, `:40-45` | Recorded in §2.4 as the reason the doubt is closed: the banner is classification, not attestation; each misread lands on the safe side (a closed road on a WSL launch opens nothing; the WSL branch is unchanged; an unread banner refuses) |
| 4 | S2 needs DELIVERY tests, not only request-dictionary assertions: a real child observing the value on every road, with a conflicting server value; a missing or wrong fifth entry refusing BEFORE spawning. And S1's pass criteria should name a signed-in and a resumed launch succeeding under `fail.Keyring` | yes — the fake CLI already records `PYTHON*` values (`tests_fakecli/Program.cs:617-627`); real-child launches exist (`ACliRowsEffortTests.cs:25`) | S-N23 in S2 (`VibeNativeLinuxDeliveryTests`, Linux CI job, six roads including both version probes, the conflicting server value, the refusal half proven by the record file never written, the WSL2 no-leak leg) with its REDs and break-its B5–B7, B8c; §3.2 and S1 name the signed-in (N-V2) and resumed (N-V3) launches under `fail.Keyring` as pass criteria, covered once S0 lands |

## The E2 Opus judge round (2026-10-22) — F1–F4 accepted, F5 is the owner's §9 question

The Opus judge read the merged tree of S0–S4 (`docs/native-linux-e2` `9b259700d`). Five findings; four accepted and fixed, one handed to the owner. F2, F3 and F4 are fixed in the commit that carries this record; F1 is a process finding the lead closed by running CI.

| # | Finding | Verdict | What changed |
|---|---|---|---|
| F1 | the MERGED tree had never run CI: `ci` had run only on `feat/native-e2-s2`; S3 and S4 had run only on Windows, each on its own branch, and the merge joined two separate edits to `vibeOnLinux.test.ts`; the Linux-only proofs (S-N23, the real `--host-facts` line) had not run on the merged tree | accepted | the lead dispatched `ci` on `9b259700d`: run 39825037272, all six jobs green; this commit's head runs again (39828922944) |
| F2 | `VibeLaunchRulesTests.OnlyAWsl2Banner_StartsVibe` read the generation with the pure text rule `WslGenerations.Of(vector.Banner)`, which maps the shared `empty-banner` vector ("") to `None`, host kind Linux, "starts". The test therefore asserted that Vibe is ADMITTED on an empty banner, while the product reads the banner through `ProcVersion.ReadFrom` (unread, so `Unreadable`, so refused, DN6) and the extension maps the same vector to `linux-unread`. A test that asserts something false. | accepted | the test reads the vector the product's way — `WslGenerations.Of(onLinux: true, ProcVersion.ReadFrom(() => vector.Banner))` — expects the generation `Unreadable` for an unread banner, and `starts = Wsl2 \|\| (kind == Linux && the banner was read)`; renamed `AWsl2OrAReadNativeBanner_StartsVibe_AnUnreadOneDoesNot`. |
| F3 | stale status lines: this plan's line 3, S0's "Linux CI not yet read", the parent plan's status line and its `### E2`, the `todo/README.md` row, no `research/module_tests.md` pointer for S3's S-N9–S-N11, no CI run id on S3 and S4 | accepted | all of them, in this commit (S3 and S4 carry CI run 39825037272 as their first run on the merged tree) |
| F4 | `HostAdmission.NativeLinux` ended in `_ => string.Empty`: a DEFAULT-ADMIT. `HostKind.Linux` with `Wsl1` or `Wsl2` is impossible in production (a WSL banner makes the host `HostKind.Wsl`) but would have been admitted, against §2.1's rule that the absence of a fact refuses. | accepted | an allow-list: `(true, WslGeneration.None)` is the ONE admitting arm; `Unreadable` keeps the banner sentence; every other generation gets a new sentence (*the host reads as native Linux but its kernel banner says Wsl2; the two facts disagree, so coai cannot tell what it is running on (DN6)*). The "could not be read" sentence would be false here (the banner WAS read) and the "WSL2 only" one would blame the wrong fact. A generation added to the enum later is refused until someone admits it. |
| F5 | whether a Vibe version with no manifest entry should be REFUSED on a native Linux host | the owner's — §9, the last bullet (non-blocking) | nothing: E2 keeps the 2026-10-13 decision (warn, never refuse) |

**RED, GREEN, break-it.**

- F2, RED: the OLD test body run against the product's real path for `empty-banner` (only its generation line swapped for the product's, and its first assertion removed so that the `starts` expectation is the one under test) failed: *Expected (VibeLaunch.HostRefusal(kind, "vibe", generation).Length == 0) to be True, but found False.* — `OnlyAWsl2Banner_StartsVibe(id: "empty-banner")`. The old expectation ("native Linux starts") contradicts the product, which refuses. GREEN: the rewritten test passes for every vector.
- F4, RED: `HostAdmissionOnNativeLinuxTests.ANativeLinuxHost_WhoseBannerNamesWsl_IsRefused_ByTheAllowList` (Wsl1 and Wsl2) failed: *Expected HostAdmission.Refusal("Vibe", HostKind.Linux, "vibe", generation) "" to contain "disagree".* (the combination was admitted). GREEN after the allow-list. Break-it: the default arm restored (`_ => string.Empty`) turned both red again; the fix put back, green.
- Run: `*HostAdmission*` 41/41, `*VibeRuntimeTests` 31/31, `*VibeLaunch*` 72 (70 pass, 2 skip by name on Windows), `*KimiLaunch*` 88/88, `*HostFactsModeTests` 11 (10 pass, 1 skip), `*ConsultantLimitationsTests` 17/17.

## The E2 code gate (coai 99e086b8, 2026-10-22, proceed; 8 of 12 — Gemini's quota out): 6 findings, 5 accepted, 1 rejected

Verdict `proceed`; eight of the twelve reviewers answered (Gemini's quota was out). Every finding, its decision, and where it was folded. The fixes are in the commit that carries this record.

| # | Finding | Decision | What changed |
|---|---|---|---|
| 0 + 2 | the server's `HostAdmission.MeasuredOnNativeLinux` and the extension's `ON_NATIVE_LINUX_SINCE` are two tables of the same admission with nothing holding them equal: a one-sided change (Kimi in E3) would merge green | accepted | `NativeLinuxAdmissionTablesAgreeTests` (C#): the two name the same runtimes, and each named runtime has a `confined` `linux` row with a `measured` field in `shared/consultant-limitations.json`. The two tables stay two (a version cannot be derived from a set); only their NAMES are pinned. A C# test, because the server side is read as the real set and rows, and only the small TypeScript object literal is parsed as text — **superseded by PR #922's CI round, last line of this record: that parse was refused by `NothingReadsAnotherProgramsSourceTests`; the admission is now ONE shared file, `shared/native-linux-admission.json`** |
| 1 | the probes' native-Linux detection (`isNativeLinuxHost` / `runsOnNativeLinux`) is a third implementation of a rule the product holds twice | accepted | `scripts/lib/probe-host-vectors.test.mjs` holds it to `shared/wsl-banner-vectors.json`, vector by vector |
| 3 | the five-language help predates E2 | **rejected** — the language policy is out of scope of E2: the help's five languages were fixed by an earlier decision and this plan neither added nor changed a language | nothing |
| 4 | the probe `delete`s `PYTHON_KEYRING_BACKEND` (and `VIBE_CLI`) from an environment it had already built: a mutation, against the convention | accepted | `without(env, name)` in `scripts/lib/vibe.mjs` builds a NEW object; `childEnvFor` uses it on both branches. `vibe-home-manifest.mjs` did not delete and needed nothing |
| 5 (security) | on Linux an UNREADABLE `/proc/version` made `isNativeLinuxHost` false, so a probe run directly launched Vibe with the keyring road OPEN, which arm a3c proves reaches an available keyring | accepted | the pure `launchHost` decision (+ `launchHostOf`): both Vibe scripts refuse before any launch, exit 65, with a sentence naming `/proc/version`, as the product refuses (`WslGeneration.Unreadable`) and as §3.4's stop rule says; `--reparse` and `--from` launch nothing and are not refused |

**RED, GREEN, break-it.**

- G4, RED (stubs that never refuse / never copy): *a Linux host whose banner could not be read was allowed to launch with the keyring road open*; *probe-vibe-confinement.mjs no longer reads the host through launchHostOf() — an unread banner would launch Vibe with the keyring road open*. G3, RED: *Expected values to be strictly deep-equal* (`without` returned its input), *the probe still mutates an object with `delete`*. Green after the fix.
- G2: the `wsl` alternative dropped from the banner pattern (it lives in `readBanner`, `host-facts.mjs`; `isNativeLinuxHost` reads its `namesWsl`) → red naming `wsl2-custom-kernel-that-keeps-the-tag`; restored, green.
- G1: `"Kimi"` added to the server set only → red, *HostAdmission.MeasuredOnNativeLinux admits these runtimes … has no entry … {"kimi"}* and *kimi … must hold a linux row … confined (found: Unmeasured)*; `kimi` added to the extension table only → red, *the extension's ON_NATIVE_LINUX_SINCE … promises these runtimes … does not admit them … {"kimi"}*; both restored, green.
- Run: `node --test scripts/lib/*.test.mjs scripts/native/*.test.mjs` 419 tests (363 pass, 56 skipped by name on Windows); `CoaiMcp.Tests.exe --filter-class "*NativeLinuxAdmissionTablesAgreeTests"` 3/3.
- **PR #922's CI round (run 39931052357, all three test jobs red; 2026-10-23).** `NothingReadsAnotherProgramsSourceTests` refused G1's guard: `NativeLinuxAdmissionTablesAgreeTests.RuntimeSince()` parsed `src_vs_code/src/runtimeSince.ts` from C#, the shape that goes quiet rather than red. Fixed the repo's way: ONE shared file, `shared/native-linux-admission.json` (`vibe` → `0.64.0`, RESULTS §32), embedded in the runners assembly and LOADED by the new `NativeLinuxAdmission`, whose names `HostAdmission.MeasuredOnNativeLinux` now is (public shape and case-insensitive names kept; Vibe admitted only on a read native banner, Kimi refused, as before); the extension keeps `ON_NATIVE_LINUX_SINCE` and `src/test/nativeLinuxAdmissionFile.test.ts` holds it to the file (names and releases); `NativeLinuxAdmissionFileTests` replaces the old class (confined `linux` row per runtime kept; the loader refuses a lost or blanked document). Break-it: `kimi` in the shared file only → red on both sides naming it; `kimi` in the TypeScript table only → red naming it; restored, green.

## DN1 — owner decision 2026-10-23: "unmeasured" is a note, never a refusal

> The owner's words, verbatim (Russian): *"Unmeasured - убери вобще защиту. просто оставь пометку. я не могу работать норм. аналогично для кими."*
> In English: *Unmeasured — remove the protection altogether, just leave a note. I cannot work normally. Same for Kimi.*
> Recorded by the lead in the session of 2026-10-23; built the same day on `docs/native-linux-e2` (PR #922). It extends the owner's earlier decision of 2026-10-13
> (an unmeasured CLI *version* is warned about and run, never refused) from versions to hosts.

**The rule now.** Nothing is refused only because it is "not measured". It runs, and the person is told in one visible note: *not measured on this host/version; admitted by the owner's decision of 2026-10-23.*
Every SAFETY refusal stays: anything that blocks because the thing is unsafe, impossible, or because coai cannot tell which host it is on.

### What changed

| Part | Before | After |
|---|---|---|
| `shared/native-linux-admission.json` | per runtime: `runtime`, `since`, `source`. Kimi had no row ("Kimi has no entry until E3 measures it"). | every row also says WHY it is admitted: `basis` = `measured` or `owner-decision`; an `owner-decision` row carries `note`, the one sentence the person is shown. Kimi: `basis` `owner-decision`, `since` `0.64.0`, `source` this decision. Vibe: `basis` `measured` (§32). |
| `NativeLinuxAdmission` (server) | read `runtime`/`since`/`source`. | reads `basis` and `note` too (`NativeAdmission`, `AdmissionBasis`); a row without a legal `basis`, or an `owner-decision` row without `note`, throws by name. `Entries` is new; `Since` is derived from it. |
| `HostAdmission` | the native Linux arm admitted only a runtime in the set `MeasuredOnNativeLinux`. | the set is renamed `AdmittedOnNativeLinux` (it is no longer only "measured"). New `HostAdmission.Note(runtime, host, wsl)`: the row's `note` for an `owner-decision` runtime on a native Linux host with a READ banner, else empty. |
| `KimiLaunch` / `KimiRuntime` | refused a native Linux host with the WSL2-only sentence. | admits it. `KimiLaunch.HostNote(host)` joins the launch's warning (`LaunchTurn.Warning`, after the optional network line, before the version warning); `KimiLaunch.CardWarning` / `KimiRuntime.VersionWarning` put the same sentence on the row's card (`providers` → `warning`). This is the SAME channel Vibe's unmeasured-version warning uses; no new channel. |
| `shared/consultant-limitations.json` | `kimi`/`linux`: *Not run here ... coai refuses it on a native Linux host.* | *Not measured here: coai runs Kimi on a native Linux host by the owner's decision of 2026-10-23, not by a measurement ...* (standing stays `unmeasured`; nothing reads a standing to refuse). |
| Parity guard `NativeLinuxAdmissionFileTests` | "every admitted runtime has a confined, measured `linux` row". | split by `basis`: a `measured` runtime still needs the confined, measured row; an `owner-decision` runtime needs an `unmeasured`, `source`-evidenced row whose text names the decision, and a `note` in the file that says "not measured" and names the decision. A test also pins that Vibe stays `measured` (its launch carries no host note; only Kimi's does). |
| Extension | `ON_NATIVE_LINUX_SINCE` had `vibe` only; `KIMI_NATIVE_LINUX` said "refuses Kimi for now"; the card said "refuses a kimi row ... measured on WSL2 only". | `KIMI_ON_NATIVE_LINUX_SINCE = '0.64.0'` and the table row; the card tells an OLD server (below 0.64.0) to update, and says nothing to a current one (the server's note rides the row's warning, so it is not said twice). `KIMI_NATIVE_LINUX`, the hint, the install note and the help in five languages say Kimi RUNS from 0.64.0, unmeasured, by the owner's decision. A host whose banner could not be read gets its own Kimi note (`KIMI_LINUX_UNREAD`) instead of a promise. `nativeLinuxAdmissionFile.test.ts` also checks `basis` and `note`. |

### The inventory — every refusal that says "unmeasured", "not measured" or "not admitted"

| Where (file:line, as built) | Sentence (short) | Verdict |
|---|---|---|
| `HostAdmission.cs:66` native arm, runtime not in the set | *runs on WSL2 only for now — measured on WSL2 and not yet on a native Linux host* | **Measurement-only → note** for Kimi (now in the set, `owner-decision`). The arm stays for a runtime with NO row: an absent fact refuses (no runtime is in that state today). |
| `shared/native-linux-admission.json` (why, last line) | *Kimi has no entry until E3 measures it.* | **Measurement-only → note.** Replaced by Kimi's row. |
| `shared/consultant-limitations.json` `kimi`/`linux` | *coai refuses it on a native Linux host* | **Measurement-only → note.** Rewritten (above). |
| `runtimeSince.ts` `nativeLinuxNote` / `ON_NATIVE_LINUX_SINCE` | *refuses a kimi row on a native Linux host for now — Kimi is measured on WSL2 only* | **Measurement-only → note.** Kimi has an entry; the no-entry arm stays (generic words). |
| `kimiLinuxLine.ts` `KIMI_NATIVE_LINUX`, `vendors.ts` Kimi hint, `vendorTerminal.ts` note, help ×5 | *refuses Kimi for now — measured on WSL2 only* | **Measurement-only → note.** Rewritten in five languages. |
| `KimiLaunch.cs:50` `WarningFor`; `VibeLaunch.cs:376` `Unmeasured` | an unmeasured CLI VERSION | Already warn-and-run (owner 2026-10-13). **Kept as is.** |
| `HostAdmission.cs:74` `BannerUnreadable`; `:77` `BannerDisagrees` | the kernel banner could not be read, or disagrees with the host kind | **Ambiguity refusal — kept.** coai cannot tell native Linux from WSL1 (DN6); a refusal here is not "unmeasured", it is "unknown host". |
| `HostAdmission.cs:54` WSL1 | was *measured on WSL2, and this is WSL1*; now *Kimi is refused on this WSL1 host — WSL1 translates Linux system calls for Windows and has no pidfd, so coai cannot end Kimi's process tree by identity* | **Safety — kept, reworded to the real reason** (DN1b item 5). The extension's WSL1 notes and the card say the same. |
| `HostAdmission.cs:86` `WslBridge` | was *refused until it is measured there*; now *which a Windows process cannot supervise — it cannot take a Linux lock or end a Linux process tree (D18)* | **Impossible — kept, reworded** (DN1b item 1's twin). |
| `HostAdmission.cs:80` `NoLaunchPath` (macOS and other) | was *refused on a macOS host until it is measured there*; now *{runtime} has no launch path on a macos host — {the missing thing}* | **Kept, reworded** (DN1b item 1): Kimi lacks the exclusive inheritable lock; Vibe's sign-in link is judged through a Linux descriptor only (`RegularFiles.LinuxOnly`). No macOS launch path exists for either. |
| `LinuxPidfd.cs:182` `PidfdLayer.Refusal` | no pidfd on this kernel | **Impossible — kept** (cannot end Kimi's tree by identity). |
| `Junctions.cs:200` `HostAdmitted` | was *Kimi is refused on a macOS host until it is measured there*; now *Kimi has no launch path on a macos host — its sign-in hold needs an exclusive lock ...* | **Kept, reworded** — same as the macOS row. |
| `KimiPersonsConfig.cs:74` | sign-in stored as keyring, not files — was *refused as unmeasured*; now *... there is no credentials folder to link (D19)* | **Impossible — kept, reworded** (DN1b item 4). |
| `KimiPersonsConfig.cs:76` | provider type is not `kimi` — was *refused as unmeasured*; now *coai writes type = "kimi" into the owned config, so it would run Kimi's protocol against a service you set up for another kind of provider (D19)* | **Kept, reworded** (DN1b item 4): another provider type is another service and another billed key, the cost reason of `KimiModels.cs:34`. |
| `RegularFiles.cs:63` `NoStatx` | *statx is not available in this libc — Vibe on this distribution is unmeasured* | **Impossible — kept.** Without `statx` coai cannot prove a file is a regular file. |
| `VendorDiagnosis.cs:126` | the installed Vibe started its Rust CLI, which takes no `--time-limit` | **Impossible — kept.** The CLI itself refuses the flag (exit 2). |
| `VibeLaunch.cs` plugin-hash check, known version | bytes differ from the pinned hashes | **Safety — kept** (tampered bytes of a known version). An UNKNOWN version already warns. |
| `KimiModels.cs:34` (D10) | a `moonshot-ai/*` model — *not on the Kimi Code plan* | **Safety (billing) — kept.** It would bill the person's own API key. |
| `KimiModels.cs:35` (D10) | a `kimi-code/*` id that is not curated — was *not one coai has seen Kimi answer* | **Measurement-only → note** (DN1b item 3). `KimiModels.RefusalFor` no longer refuses it; the launch carries `KimiModels.NoteFor`; with no block anywhere the REAL reason is `NoBlockRefusal`. |
| `CapabilityMatrix.cs:91` / `:105`, `capabilityAdmission.ts` | was *'vibe' has not been measured for 'web' ... a measured cell is what admits it* | **Measurement-only → note** (DN1b item 2): `AdmissionFlag.Unmeasured` / `'unmeasured'`. |
| `SnapshotBudget.cs` | a volume that cannot be measured is refused | **Safety — kept** (free space unknown). |
| `scripts/` (`vibe.mjs`, Kimi probes) | the Vibe probe refuses an unread banner | **Kept** — same ambiguity rule as the product. The probes are measuring tools, not admissions. |

### What stayed, and why (the three the owner named)

- **Vibe's forced `PYTHON_KEYRING_BACKEND` on a native Linux host stays.** It blocks nothing: it only closes the keyring road, and the sign-in is `~/.vibe/.env`.
- **Kimi's Linux tree supervisor, the survivor gate and the credentials lock stay.** They are safety: they decide whether two Kimi processes can refresh one rotating sign-in at once.
- **The unread-banner refusal stays**, for both runtimes, with its own sentence (`UnreadableKimi` in the tests). It is not a measurement question.

### Tests (RED first, then green; break-it for each new guard)

- Changed because they encoded the old decision (Kimi refused on native Linux): `HostAdmissionOnNativeLinuxTests` (the product's set, the matrix, the sentences), `KimiLaunchChecksTests` (`ANativeLinuxHost_IsAdmitted_WhenItsBannerWasRead_AndRefusedWhenItWasNot`; the banner-vector theory now mirrors Vibe's), `KimiLaunchControlTests` (the refusal row is now "a native Linux host whose banner could not be read"), `AKimiRowRunsThroughTheServiceOnWsl2Tests` (the native plan round now RUNS and the reviewer state carries the note; `KimiFlowHost`'s Linux arm skips when the owner's real Kimi runs), `HostFactsModeTests`, `ConsultantLimitationsTests` (the Kimi `linux` row), extension `kimiOnLinux.test.ts`, `vibeOnLinux.test.ts`, `kimiWindowsInstall.test.ts`.
- New: `HostAdmissionOnNativeLinuxTests.KimiOnANativeLinuxHost_CarriesTheNotMeasuredNote_AndNothingElseDoes`; `KimiLaunchControlTests` (`KimiOnANativeLinuxHost_WithAReadBanner_IsAdmitted_AndTheLaunchCarriesTheNotMeasuredNote`, `TheNativeLinuxNote_IsJoinedWithTheVersionWarning_NotReplacedByIt`, `OnWsl2_TheLaunchCarriesNoHostNote`); `KimiRuntimeTests.OnANativeLinuxHost_TheCardCarriesTheNotMeasuredNote_JoinedWithTheVersionWarning`; `NativeLinuxAdmissionFileTests` (the `basis`/`note` loader cases, `EveryOwnerDecisionRuntimeTheFileNames_HasAnUnmeasuredLinuxRowThatSaysSo_AndAFileNote`, `VibeIsAdmittedOnItsMeasurement_BecauseItsLaunchCarriesNoHostNote`); extension: the `basis` test in `nativeLinuxAdmissionFile.test.ts`, the Kimi native-Linux cases and the five-language `SAID_NATIVE` in `kimiOnLinux.test.ts`.
- `NothingReadsAnotherProgramsSourceTests` stays green: no new test parses another program's source; both sides read `shared/native-linux-admission.json`.

### What this does to the plan

- **E3's measurement is optional evidence, not a gate.** Kimi is admitted on native Linux now. If E3 is run and every cell passes, the Kimi row's `basis` becomes `measured` (and its `linux` limitation row becomes a measured one); what a FAILED cell changes is the owner's call, not written here.
- The first release that carries it is the same `0.64.0` as Vibe's (the next minor after the newest released `src_mcp`, 0.63.0): `KIMI_ON_NATIVE_LINUX_SINCE` and the shared file say so, and `runtimeSinceIsAhead.test.ts` holds it.
- S5's rollout (a live round on the LOCAL binary, then the release) now covers Kimi on a native Linux host too; the round must show the note on the Kimi row.

### DN1b — the lead's decisions on the five open questions (2026-10-23)

The owner's rule, applied by the lead: a refusal whose ONLY reason is "not measured" becomes a note; a refusal for safety, cost to the person, or impossibility stays, with its REAL reason stated. Built on the same branch, RED first.

1. **Vibe on macOS — there is no launch path, so the stop stays, and its sentence now says so.** Checked in the code: `VibeSignIn.For` makes a link on every host but Windows, and `RegularFiles.ReadOwned` judges the file through a descriptor on Linux only (`RegularFiles.LinuxOnly`), so on macOS every launch with a `~/.vibe/.env` would be refused at that step and one without would run signed out. The sentence is `Vibe has no launch path on a macos host — coai links its sign-in only after reading it through a file descriptor, which it can do on Linux only`; Kimi's is the missing exclusive inheritable lock. `NothingMeasured` is renamed `NoLaunchPath`. The extension's install notes and the help in five languages say the same.
2. **The capability matrix — unmeasured pairs now RUN, with a note.** `vibe`×`web` and `api`×`web`: `AdmissionFlag.Unmeasured` (spelled `unmeasured`), caveat *not measured on this runtime for this capability: the row runs anyway, and the answer may come without it*. `ConfinementPlanner` gives such a row NO web tool (Vibe: `read_file` only, in an empty folder; api: a completion with no tools), so it cannot reach the web — the risk is a weaker answer, never egress; `ApiConsultant` now builds a `web` row as a `none` completion ONLY when the plan carries `AdmissionFlag.Unmeasured` — an unflagged web plan is refused by name (a programming-path guard from the code round: the caveat "web was not measured, the answer may come without it" cannot be lost); a `disk` row is still refused by name: a hosted completion has no disk. A web row is still given the sanitised question and nothing of the checkout (A2). Both halves: `CapabilityMatrix` and `capabilityAdmission.ts` over the same vectors (`shared/capability-matrix-vectors.json` now says `admitted: true`, `flag: "unmeasured"`); the settings tab shows the note and NO "Can read this machine" tick (nothing here reads the machine). The refusal that stays is the `unsupported` one (`antigravity`×`web`, `local`/`api` without the tool).
3. **An uncurated `kimi-code/*` id — a note, not a refusal.** `KimiModels.RefusalFor` refuses only an id off the plan (a `moonshot-ai/*` id bills the person's own key — kept as it was). The launch reads the person's own `[models."id"]` block (`KimiModelBlocks.For`, whose routing check still refuses a block routed through another provider) and carries *the model 'X' is not in coai's measured list (…); it runs on your own config.toml block, not measured here — admitted by the owner's decision of 2026-10-23*. With no block anywhere (none pinned, none of the person's) it cannot run — there is no context size or capability list to write — and the sentence is now that real reason: *the model 'X' has no model block … add that block, or choose one of …*.
4. **A Kimi provider of another type — kept, and the sentence now says why.** coai writes `type = "kimi"` into the owned config; another type is another service and another billed key (the same cost reason as `KimiModels.cs:34`), so it is not "unmeasured". The keyring-storage sentence (`:74`) is an impossibility (no files to link) and is reworded the same way.
5. **WSL1 — reworded to the real reason**: WSL1 has no pidfd, so coai cannot end the process tree by identity. Server (`HostAdmission.Wsl1`), the extension's WSL1 notes (`KIMI_ON_WSL1`, `VIBE_NOTE_WSL1`, the card's `wsl1` note). The runtime-with-no-row sentence (`NoRow`) lost its "measured on WSL2" wording too.

**Found while doing this, and kept with their real reason:**
- `VibeLaunch.ModelRefusal` (*not one coai has seen Vibe accept*): Vibe answers an unknown alias on its OWN default model instead of refusing it, so the person would pay for a model they did not pick. Cost to the person — kept; the sentence already says so.
- `featureAvailability.ts` `effortRefusal` for `vibe`, `kimi`, `codex` (*which efforts X accepts has not been measured yet*): Vibe has no effort flag and Kimi's level was never shown taking effect, so no level can be sent — a setting that does nothing, not a measured-versus-not question; codex's legal values cannot be named without a paid turn. Kept; if the owner wants the effort KEPT with a note ("kept, not sent", as the server does), it is a UI change in `catalogWriteRules.ts`.

**Tests.** C#: `KimiLaunchChecksTests` (no-launch-path sentence, WSL1, the `wsl.exe` bridge, the uncurated id and its missing block), `VibeLaunchRulesTests`, `KimiLaunchControlTests` (uncurated id with and without the person's block), `KimiPersonsConfigTests`, `JunctionsTests`, `HostAdmissionOnNativeLinuxTests`, `CapabilityMatrixTests`, `ConfinementPlannerTests`, `ApiConsultantTests`, `QuestionAdmissionTests`. Extension: `capabilityAdmission.test.ts`, `qconsultSection.test.ts` (the page RUN: a vibe web row switches on, shows the note, no tick), `kimiOnLinux.test.ts`, `vibeOnLinux.test.ts` (the five-language help). Each new guard was broken once and restored by writing the file back.
