# PLAN — settings of their own for each side of the machine

> Status: **IMPLEMENTED, 2026-09-06.** Each side of the machine can keep its own settings —
> `coai.perSideSettings`, off by default, with a **This side** section in the panel that names the
> side it is editing. Merged as #56 and released in extension 0.31.0.
>
> Deviations, all deliberate. The switch is ONE shared flag, seeded per side when it is turned on —
> the operator chose that over a per-side flag, because a side whose flag is off follows a shared
> value another side can no longer see. The overlay stores a WHOLE value per key rather than merging
> field by field, which is why two sides' vendor arrays cannot dilute each other (a review-gate
> finding on the vendor-stage change worried about exactly that merge; there is none). And `sideKey`
> was EXTRACTED from `installedKey` rather than written anew: that identity — remote kind, distro or
> hostname, storage path — had already been argued through review once, for the two-sides install
> record, and two WSL distros mounting the same `/home/<user>/.vscode-server/…` is the collision both
> features have to survive.

## The symptom

One person, three working environments on one machine: a Windows window for one company, a WSL
distro for another, and a second WSL distro for a third. They need **different proxy servers,
different CLI binaries and different logins** in each. Today they get one set of settings for all
three, and this is not a bug in the extension — it is how VS Code resolves settings:

- All eighteen `coai.*` settings are declared with the default scope (`window`). A Remote-WSL window
  reads `window`- and `application`-scoped user settings from the **client's** `settings.json`
  (`%APPDATA%\Code\User\settings.json`) and hands them to the remote extension host.
- Only `machine` / `machine-overridable` settings can hold a different value on the remote side, in
  `~/.vscode-server/data/Machine/settings.json`.
- So: one settings file, several extension hosts. `coai.vendors[].executablePath` — a Windows
  `C:\…\codex.cmd` — is handed verbatim to a WSL host where it means nothing.

What already IS separate per side: `globalStorage` and `globalState` records keyed by side, the
installed binary, the sessions, the worktrees and the rounds database.

## What must be true when this is done

1. A **switch** — one, shared, default **off**. Off is exactly today's behaviour: one set of settings
   for every window. There is no migration and nothing to undo.
2. On, each side keeps its **own** values, seeded from the shared ones at the moment it is switched
   on, so nothing changes until something is edited.
3. "Side" separates a Windows window from a WSL one **and one WSL distro from another** — a third
   company on a third distro must be separable.
4. The panel says which side it thinks it is, in words, so the person can see the discriminator
   rather than infer it.
5. Everything a person configures follows the switch: reviewers, rounds, thresholds, vendors (model,
   `baseUrl`, `executablePath`, prices), the creds key, the server settings.
6. `settings.json` stays the source of truth when the switch is off, and stays untouched by the
   overlay when it is on — the overlay never writes into a file the user shares with three windows.

## The shape

**Reuse the identity that already exists.** `installedKey(side)` in `coaiInstall.ts:310` already
folds `vscode.env.remoteName`, `WSL_DISTRO_NAME` (or the hostname, for remotes with no distro) and
`globalStorageUri.fsPath` into one injectively-escaped key — and it was hardened, in review, against
exactly the two-WSL-distro collision this feature needs to survive (`/home/<user>/.vscode-server/…`
is the same path in every distro). Extract `sideKey(side, purpose)` from it; do not invent a second
identity.

**Storage: `globalState`, per side.** The client's `globalState` is one database shared by every
window of the profile — which is what made it wrong for the installed version until it was keyed by
side, and what makes it right here for the same reason. Nothing else is available: there is no API
target that writes a remote host's machine settings.

**The read path is one function.** Everything already reads through `settings()`; that becomes
`settings()` → shared values, then `overlayFor(side)` merged over them when the switch is on. A
write from the panel goes to the overlay instead of `config.update` when the switch is on.

## Build order

1. `sideKey(side, purpose)` extracted from `installedKey`, with its tests moved onto it (RED first:
   two distros with the same storage path must produce two keys — that test exists for the install
   record and must hold for the settings key).
2. The overlay: `readOverlay(state, side)` / `writeOverlay(state, side, patch)`, pure over a
   `Memento`, with the seed-on-enable behaviour.
3. `settings()` merges the overlay when `coai.perSideSettings` is on. One place, so nothing can read
   around it.
4. The panel's write path routes to the overlay; the panel gains a **THIS SIDE** section with the
   switch and a line naming the side (`wsl · Ubuntu-24.04 · /home/x/.vscode-server/…`).
5. `research/module_extension.md` gains the settings-resolution paragraph, and the manifest's
   description of `coai.perSideSettings` says plainly what it does and does not touch.

## Test plan

- Two sides with the same storage path and different distros do not share an overlay.
- With the switch off, a panel write goes to `config.update` and the overlay is not consulted.
- With the switch on, a panel write lands in the overlay and `settings.json` is unchanged.
- Switching on seeds the overlay from the shared values: the first read after enabling equals the
  last read before it.
- A setting absent from the overlay falls back to the shared value (so a new setting added by an
  update is not invisible on a forked side).
- The vendor array merges by vendor id, not by index — a side that reordered its vendors must not
  read another side's model into its own vendor.

## Definition of Done

- [ ] The switch defaults off and off is byte-for-byte today's behaviour.
- [ ] Windows, and each WSL distro, can hold different `baseUrl`, `executablePath` and creds key.
- [ ] The panel names the side it identifies as.
- [ ] `settings.json` is never written while the switch is on.
- [ ] One identity function, shared with the install record.
- [ ] Tests above pass; module docs updated.

## Open question for the person

The switch is **one shared flag** here: turn it on and every side forks, seeded from what it had.
The alternative is a per-side flag (on for WSL, off for Windows), which is more precise and harder
to reason about — a side whose flag is off follows a shared value that another side can no longer
see. Say which you want; this plan builds the first.
