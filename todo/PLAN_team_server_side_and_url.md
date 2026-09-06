# PLAN — the Team server belongs to a SIDE, and the Server section says which one

> Status: **plan only, nothing implemented yet, 2026-09-06.** Scope: the extension's panel
> (`src_vs_code/src/panelView.ts`, `teamServerView.ts`, `panelProvider.ts`) and the sign-in records
> (`teamServerAuth.ts`).
>
> Related docs: [PLAN_team_server.md](../research/PLAN_team_server.md),
> [PLAN_server_version_per_side.md](../research/PLAN_server_version_per_side.md),
> [module_extension.md](../research/module_extension.md).

## The symptom

Two, reported by the operator on the panel that shipped in 0.31.1.

**1. The Server section says nothing about the Team server.** It reads `coai-mcp 0.18.7 is
installed` and stops. The address the reviews actually travel to, and the version of the server
answering them, are in a different section — so the one place a person looks to ask "what am I
talking to" answers only half of it. The address must be VISIBLE there and NOT editable: changing
where a live session points is a sign-out, not a text edit.

**2. A sign-in is recorded as though this machine had one side.** `signedInKey`
(`src_vs_code/src/teamServerAuth.ts:39`) keys the record by server id alone, and it is read from
`globalState` (`panelProvider.ts:1147`) — the CLIENT's database, which VS Code shares with every
window of the profile, local and remote alike. The TOKEN is not shared: `writeToken` puts it under
`coaiDataDir()` on the extension host's own filesystem, which for a WSL window is inside the distro.

So today, after signing in from a Windows window and opening a WSL one, the panel says *Signed in as
you@company* and the shim in that distro has no token file at all. The panel is describing a machine;
everything underneath it is per side. That is the same defect
[PLAN_server_version_per_side.md](../research/PLAN_server_version_per_side.md) fixed for the coai-mcp
version, one layer over.

## What the operator asked for, in their words

> в раздел сервер добавь инпут, который показывает текущий урл сервера без возможности менять. для
> смены нужно логаут сделать. но оно должно работать в тандеме с выбранной стороной. если галочки не
> стоит, то и виндовс и всл юзают автоматически один и тот же логин. если стоит — я могу в каждом
> всл и виндовс войти в разные аккаунты.

Three requirements:

1. The Server section shows the current server URL, read-only; changing it means signing out first.
2. **Switch off** (*Separate settings for each side* — `panelView.ts:451`): Windows and WSL use the
   same login, **automatically** — nobody signs in twice.
3. **Switch on**: each side can be signed in to a DIFFERENT account.

The add-a-server flow (name, then URL, then *Sign in*) is explicitly out of scope: *"то что при
логине спрашивает урл это ок. не трогай."*

## The design

### The panel says what THIS side can actually do

The first draft of this plan kept ONE record per server and let the panel render it. Every reviewer
in the plan round took that apart from a different direction, and they were right: a shared record is
an INTENTION, and a review runs on a TOKEN FILE. Whenever the two disagree — the mint failed, the
machine was offline, the Microsoft session had gone, the switch was toggled while another side held
another account's token — the panel would have shown a session that does not exist, which is the
symptom this change exists to remove, restated one layer up.

So the two are separated and BOTH are stored:

| Record | Scope | What it is |
|---|---|---|
| `teamServer:<id>` | shared | **The intent**, when the per-side switch is OFF: the account this machine should be signed in as. |
| `teamServer:<id>@<sideKey>` | per side | **The intent**, when the switch is ON. `sideKey` is `coaiInstall.ts:323` — already injective over remote kind, distro and storage path. |
| `teamServer:<id>#<sideKey>` | per side, **always** | **The fact**: whose token file this side actually holds, when it was minted, and when it expires. |
| `teamServer:<id>:trusted` | shared, always | Which Microsoft application the person approved. A fact about the SERVER, not the side — and it is what lets an unattended mint be acceptable at all. |
| `<intent key>:revoked` | follows the intent | When somebody last signed this server out, in ms. |

**The panel renders the FACT.** A side with no token file reads *Not signed in on this side* even
when another side is signed in — and when the intent names an account, it says so and offers the
button: *your work account is signed in on another side; press Sign in to use it here.* There is no
state in which the panel claims a session the shim cannot use.

### One reconciliation, run on every refresh

Pure, and therefore tested rather than trusted — `sessionAction(intent, fact, revokedAtMs, nowMs)`:

```
no intent   + a fact minted BEFORE the revocation  → sign out here
no intent   + anything else                        → do nothing
an intent   + no fact                              → mint
an intent   + a fact for a DIFFERENT account       → mint
an intent   + a fact nearly expired                → renew (the same mint)
otherwise                                          → do nothing
```

The file is the truth: before this runs, a fact whose token file is gone is discarded, so deleting a
token by hand is a state the panel recovers from rather than one it lies about.

That single table produces both of the operator's behaviours:

- **Switch off** — one shared intent. A WSL window opened after a Windows sign-in finds an intent and
  no fact, and mints its own token without asking. Requirement 2, and **no token ever crosses between
  the two filesystems** to achieve it. (A reviewer proposed copying the token file across sides
  instead; that is rejected below.)
- **Switch on** — the intent is per side. A side that has not signed in has no intent, mints nothing,
  and shows *Not signed in on this side*. Its *Sign in* runs the interactive flow, which already
  passes `clearSessionPreference: true` (`panelProvider.ts:1179`), so a different Microsoft account
  can be chosen. Requirement 3.
- **Sign-out reaches the other sides.** A sign-out clears the intent and stamps `:revoked`. Every
  other side under that same intent sees a fact older than the revocation on its next refresh and
  signs ITSELF out — deleting its token file and ending its own session on the server. Without this,
  signing out on Windows left a live, usable session inside the WSL distro.

### The mint is guarded three ways

1. **The application must already be approved** (`trustedKey`), which `signIn(…, interactive: false)`
   enforces today.
2. **The identity provider must still have a live session** — `createIfNone: false` never shows a
   prompt. When it returns nothing, the mint FAILS, and the failure is shown on the row rather than
   swallowed: *signed in as X on another side, but Microsoft did not answer here — press Sign in.*
3. **The account must be the one intended.** `createSession` answers with the email it signed in as;
   when a silent mint is run for an intent, that email is compared to the intent's BEFORE the token
   is written, and a mismatch ends the new session again and says so. Otherwise a machine with a
   personal and a work Microsoft account signed in could quietly get a token for the wrong one.

A failed mint is **not retried on every refresh**: the refresh window is 60 s
(`panelProvider.ts:118`), so an unreachable identity provider would otherwise mean a request a
minute, for ever. The failure is remembered per server-and-side and the next attempt waits ten
minutes; pressing *Sign in* is always immediate.

### Toggling the switch

Neither direction may sign anybody out by surprise:

- **OFF → ON**: this side's intent is seeded from the shared one, exactly the way `seedIfEmpty`
  (`sideSettings.ts:52`) already seeds this side's settings — so nothing changes until something is
  edited. Seeded ONCE, at the moment of the toggle, so a later deliberate sign-out on this side is
  not undone by the next refresh.
- **ON → OFF**: this side's intent is promoted to the shared record when there is none, so the side
  that turned sharing on is the one whose account becomes shared. Other sides then hold a token for a
  DIFFERENT account than the shared intent — which the reconciliation table already covers: fact
  disagrees with intent, so they re-mint as the shared account rather than quietly reviewing as
  somebody else.

### The Server section

A new pure function in `teamServerView.ts`, rendered by `serverBody` (`panelView.ts:561`) under the
coai-mcp lines, and empty when no Team server is configured — a person who has none sees the section
exactly as it is today. **One block per configured server**, not one for "the" server: the panel has
always allowed several, and picking one arbitrarily would be a display that is right by luck.

```
Team server — RemSoft Dev
[ https://coai.remsoft.dev                    ]   ← readonly
coai-server 0.5.2 — signed in as oleksandr@remsoft.dev
Every side of this machine uses this sign-in.
To point this side somewhere else, sign out under Team servers first.
```

The version line reuses the states the *Team servers* section already has rather than inventing new
ones: *Connecting…* while a token exists and no catalog has answered yet, and a version marked as
last known when the server has since stopped answering. A version that is silently stale is a number
that makes a broken connection look healthy.

## Build order

1. `teamServerAuth.ts` — `signedInKey(id, side)`, `tokenFactKey`, `revokedKey`, `AuthHost.side`, the
   pure `sessionAction`, `reconcile` replacing `renewIfDue`, and the expected-account check in
   `signIn`. RED tests first.
2. `panelProvider.ts` — `sideScope()`, used by `authHost()`, `teamServerStates()` and the signed-in
   filter at `panelProvider.ts:1541`; the mint backoff; seeding and promotion on the toggle.
3. `teamServerView.ts` — `teamServerHere(states, perSide)` and the *signed in elsewhere* sentence.
4. `panelView.ts` — `serverBody` renders it.
5. CHANGELOG: fold into the unreleased 0.31.1 entry rather than open 0.31.2 — 0.31.1 has not been
   published, and two entries for one feature nobody has seen is a changelog nobody can read.

## Test plan

Every one of these is a unit test in `src_vs_code/src/test`, run by `npm test`:

- `teamServerAuth.test.ts` — a side-scoped key differs from the shared one and from another side's;
  `sessionAction` over its whole table, including the revocation comparison; a mint whose account
  differs from the intent ends the session it created and writes no token; a mint for an intent
  refuses when the identity provider answers nothing, and says which account is signed in elsewhere.
- `teamServerView.test.ts` — the URL appears and is read-only; the version appears only once a
  catalog has answered; *Connecting…* while it has not; the two per-side sentences; the *signed in on
  another side* sentence; one block per server; empty output when there are none.
- `panelView.test.ts` — the Server section carries the address; a panel with no Team server is
  unchanged.
- `sideSettings.test.ts` / provider tests — the OFF→ON seed and the ON→OFF promotion, and that a
  sign-out after a seed is not undone by the next refresh.

## Definition of Done

- [ ] The Server section shows the address, read-only, and the coai-server version once connected.
- [ ] With the switch off, a second side signs itself in with the same account and no prompt.
- [ ] With the switch on, a second side starts signed out and can choose a different account.
- [ ] The panel never reports a session this side has no token for.
- [ ] A sign-out on one side takes the other sides with it.
- [ ] `trustedKey` stayed shared, and it is written down why.
- [ ] `npm test` green; the gate's plan and code rounds both reached `proceed` or `good_enough` with
      every finding resolved.
- [ ] This plan is promoted to `research/` with what shipped differently.

## What the plan round changed, and what it did not

Accepted: the phantom signed-in state (the panel now renders the token file, not the intent); the
missing migrations on both directions of the toggle; sign-out that did not reach other sides; the
unbounded retry of a failing mint; the wrong-Microsoft-account mint; the single-server assumption in
the Server section; and the stale version number.

Rejected, with reasons:

- **"Copy the token file from the primary side to the secondary side"** (local, on the identity-state
  finding). The diagnosis is taken — a silent mint can fail — and is handled by showing the real
  state and the reason. The remedy is not: a bearer token would have to travel through the client's
  unencrypted `globalState` to reach the other side's filesystem, which is a worse credential
  exposure than the problem, and the extension host on one side cannot write the other's disk anyway.
- **"Offer a per-side sign-out while the switch is off"** (local). With sharing on, one login for
  every side IS the setting; a sign-out that only affected one side would make the switch mean
  nothing. Somebody who wants a different account on one side turns the switch on — that is the
  control, and it is one click away in the same panel.
- **"Define how two per-side `trustedKey` entries merge"** (local, minor). There are never two:
  `trustedKey` is shared in every mode, deliberately, and this plan does not introduce a per-side
  variant of it. There is nothing to merge.
