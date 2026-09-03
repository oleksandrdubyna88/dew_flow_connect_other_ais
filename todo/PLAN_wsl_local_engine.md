# PLAN — a local reviewer that works from WSL, or says exactly why it cannot

> Status: **plan only, nothing implemented yet.** Scope: `src_vs_code/src/localEngines.ts`,
> `panelProvider.ts`, `panelView.ts`, a new `src_vs_code/src/wslNetwork.ts`, and
> `src_mcp/runners/Reviewers/VendorDiagnosis.cs`.
>
> Sibling of [PLAN_local_trust_and_vllm.md](PLAN_local_trust_and_vllm.md), which covers the other
> half of the local reviewer — trust, keys, streaming, truncation. Nothing here overlaps it: this
> plan is only about an engine the machine HAS and this side cannot reach.
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [module_runners.md](../research/module_runners.md), [PLAN_local_models.md](../research/PLAN_local_models.md).

## The symptom, measured 2026-09-03

One person, one machine, the same coai 12.1 installed on both sides. From Windows the `local`
reviewer answers. From a VS Code attached to WSL every local round dies in zero seconds, ten times
in a row, and the ledger (`~/.local/share/coai-mcp/usage.jsonl`) says the same sentence each time:

```
"provider":"local","seconds":0,"outcome":"exit 69: [coai-mcp] the local engine at
 http://127.0.0.1:11434/v1 could not be reached: Connection refused (127.0.0.1:11434)"
```

The two settings files are byte-identical, `"baseUrl":""` on the local row in both — and an empty
base falls back to [`LocalRuntime.DefaultEndpoint`](../src_mcp/runners/Reviewers/LocalRuntime.cs#L38),
`http://127.0.0.1:11434/v1`. On Windows that is the right address. In WSL it is the distro's own
loopback, where nothing listens.

**Two independent barriers, and fixing one is not enough:**

1. Windows Ollama binds loopback only — `netstat` shows `TCP 127.0.0.1:11434 LISTENING`, never
   `0.0.0.0`. So even the Windows host's address refuses.
2. WSL2 in its default NAT mode has its own network namespace, so `127.0.0.1` there is not the
   Windows host's loopback at all.

The product already knows this: [`engineNote`](../src_vs_code/src/localEngines.ts#L199) prints
exactly those two barriers on Linux. Three things are still wrong with how it lands.

- **The advice only exists in the panel.** A failed ROUND prints
  [`Because`](../src_mcp/runners/Reviewers/BoundedScheduler.cs#L242)'s reason, and
  [`VendorDiagnosis.Known`](../src_mcp/runners/Reviewers/VendorDiagnosis.cs#L16) has no marker for an
  unreachable local engine — so the round says what happened and not what to do, ten times.
- **The advice names the worse of the two cures.** It sends a person to `OLLAMA_HOST=0.0.0.0` plus
  the gateway address: that opens the engine to the network, needs a firewall rule, and pins an IP
  that WSL re-allocates on the next boot. `networkingMode=mirrored` in `%USERPROFILE%\.wslconfig`
  needs none of that — verified on this machine, `127.0.0.1:11434` from Ubuntu answered
  `{"version":"0.33.2"}` and the real `coai-mcp --ask-local` shim returned `exit 0` with 249/142
  tokens and a valid findings object in 31 s.
- **The panel cannot tell the two cases apart.** "No local engine answered" is printed identically
  to a machine with no engine at all and to this one — which has fifteen models, one hop away.

## What the gate changed about this plan (round 1, 2026-09-03)

Three reviewers, twelve findings, eleven accepted. The largest change is a **removal**, and it is
worth stating before the design rather than after it.

**The NAT-gateway probe is gone.** The first draft had the panel probe the WSL default gateway for
an engine. Three reviewers took it apart from three directions and all three were right:

- *codex (Blocking) and gemini (Blocking):* an endpoint discovered by the PANEL never reaches the
  reviewer. The round is run by `coai-mcp`, which reads `baseUrl` from the settings file — an empty
  one still resolves to `127.0.0.1` and still fails. Discovery that changes nothing about the thing
  that failed is decoration.
- *gemini (Major, Security):* "is the default gateway inside `172.16.0.0/12`" is not a test for "is
  this the Windows host". On a corporate network in that range it names the **office router**, and
  the plan's own constraint forbade exactly that.
- *local (Major):* a distro with a VPN or a second bridge has more than one route, so the parse is a
  guess even when the range is right.

Removing the leg answers all four findings at once, and costs nothing: mirrored networking makes
`127.0.0.1` correct, and anyone who prefers to bind the engine to `0.0.0.0` types the address into
the endpoint field that already exists. What replaces it is a question that touches **no network
device at all** — asking the Windows side of this same machine, through WSL interop, whether an
engine is there.

Also accepted, each shaping the design below: a bounded deadline and a defined result for the
interop probe (codex); an atomic write with a verified result for `.wslconfig` (codex, local); a
test that nothing is written before the button is pressed (codex); **WSL, not Linux**, as the test
for offering WSL advice (gemini); a UTF-16 file and disabled interop handled rather than corrupted
(gemini); and a **way back** — mirrored is a global switch and the button that sets it must also
unset it (local).

Rejected: a retry-and-cache around the interop probe (local). `probeLocalEngine` deliberately does
NOT cache a probe that found nothing, so that starting an engine after opening the panel shows up on
the next repaint; a cache over the failed answer would restore the staleness that decision removed.
Its legitimate half — a bounded deadline — is accepted separately.

## What must be true when this is done

1. In WSL, when loopback answers nothing, the panel says whether an engine answers **on the Windows
   side**, and names it — instead of printing the same "no engine answered" a machine with no engine
   at all gets.
2. A button writes `networkingMode=mirrored` into the Windows-side `.wslconfig`, merging into an
   existing file rather than replacing it, shows what it will write before writing it, and tells the
   person the one command it cannot run itself.
3. The same button takes it back: when mirrored is already set, it offers to restore the previous
   mode, because a global networking switch with no way back is not a cure.
4. A round that fails on an unreachable local engine carries a cure as well as the address — and the
   WSL cure is offered only when the server is actually running under WSL, never on native Linux.
5. Nothing is WRITTEN before the person presses the button, and installation touches no networking
   configuration. The only process started on its own is the bounded, read-only question to this
   machine's own Windows side, and only after every candidate has already refused.

## Constraints

- **The extension cannot apply `.wslconfig` itself.** It is read at cold start, so applying it means
  `wsl --shutdown` — which terminates the distro the extension host is running in, mid-call. The
  button writes the file and prints the command; it never runs it.
- **`.wslconfig` is global.** It switches networking for every distro, `docker-desktop` included, and
  mirrored mode is known to conflict with some VPN clients. Hence the preview before the write, the
  way back, and no install-time action of any kind.
- **The write is atomic or it did not happen.** A temporary file beside the target, then a rename;
  the original is left intact on any failure, and the result is confirmed by reading the file back
  before the person is told to restart WSL.
- **A file this product cannot merge safely is not merged.** A UTF-16 `.wslconfig` (PowerShell's
  redirection still writes those) is detected by its BOM and left alone, with the two lines to paste
  shown instead. Corrupting a global networking file is worse than not helping.
- **Interop may be off.** `/mnt/c` unmounted or `interop.enabled=false` in `/etc/wsl.conf` is an
  ordinary configuration, not an error: the probe answers "not asked" and the panel says so, rather
  than reporting "no engine".
- **Bounded, always.** The interop probe gets its own deadline, the child is killed when it expires,
  and every failure mode — timeout, non-zero exit, missing binary — resolves to the same empty
  answer rather than to a hang.
- Purity, per the module's own note: everything except the interop call and the file write is a pure
  function over text, so the tests are shapes rather than a machine with Ollama on it.
- No new dependency. `/proc/version`, `cmd.exe` and `curl.exe` through interop are all that is used.

## Build order

1. **`wslNetwork.ts`, pure half first** — `isWsl(procVersion)`, `wslconfigWith(existing, mode)`
   returning the merged text, whether it changed anything, and a refusal when the file cannot be
   merged safely; `networkingModeOf(existing)` so the button knows which way it points. Tests before
   any caller exists.
2. **`VendorDiagnosis`** — a cure for an unreachable local engine that keeps the address and adds
   what to do, keyed on this product's own sentence and gated on WSL rather than on Linux. Smallest
   change, and it is what makes the ten silent failures speak.
3. **The probe** — when every candidate refuses and this is WSL, one bounded interop question turns
   the empty list into a diagnosis. Injected, so the tests drive it without a machine.
4. **The button** — a `PanelCommand`, its `case`, the preview, the atomic write, the verified result,
   and the way back. It appears only when step 3 saw an engine this side cannot reach.

## Test plan

Extension (`npm test`, node's runner, `src_vs_code/src/test/`):

- `isWsl` on a real WSL `/proc/version` and on a plain Linux one.
- `wslconfigWith`: empty input creates the section; an existing `[wsl2]` with other keys keeps them
  and gains one line; a different `networkingMode` is replaced in place; a file already in the asked
  mode reports no change and comes back byte-identical; a UTF-16 BOM is refused rather than merged;
  a `[wsl2]` section that is not the last one gains the key inside ITSELF, not at the end of the file.
- `networkingModeOf` on all of the above, which is what decides the button's direction.
- `engineNote` in three states — reachable, unreachable with an engine seen on the Windows side,
  unreachable with none — and that the mirrored cure is named only in the second.
- `discoverEngine` asks the Windows side only when every candidate refused, and never on a platform
  that is not WSL (driven by an injected probe, so the assertion is on the calls).
- **Nothing writes before the button:** the writer is reachable only from the command path — asserted
  over the sources, which is the only check that catches a future caller added during activation.

Server (`./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe`):

- The exact ledger sentence from the symptom above yields a cure that still names the endpoint and
  adds the mirrored route, when WSL is the platform.
- The same sentence on native Linux yields advice with no `.wslconfig` in it.
- A vendor CLI's unrelated stderr still yields no cure — the marker must not widen into a catch-all.

## Definition of Done

- [ ] The five "must be true" statements hold, each with a test that was watched fail first.
- [ ] `npm test` and the MTP executable both pass, with their output in the summary.
- [ ] `research/module_extension.md` and `research/module_runners.md` record the new behaviour.
- [ ] The change went through this product's own gate — the plan round above, then a code round with
      a scope, per `.claude/rules/common/review-gate.md`.
