# PLAN — the client reads the contract the server has been sending all along

> Status: **IMPLEMENTED, 2026-09-09.** Scope: `src_vs_code/src/teamServerApi.ts`,
> `teamServerView.ts`, the Team-server state the panel keeps, and their tests.
>
> Related docs: [module_team_server.md](module_team_server.md),
> [module_extension.md](module_extension.md).

## The symptom

The Team-server contract mechanism is **half-wired**, and the half that is missing is the one its own
design document names as the right place for the decision. `ContractVersion` (`src_server/src/ContractVersion.cs:26`):

> Above `Current` it SERVES: a newer client knows what it is doing better than an older server does,
> **and its own check against the response header is the right place to decide.**

The server holds up its end. It sends `X-Coai-Contract` on **every** response
(`src_server/src/Program.cs:204`) precisely so a client can read it.

The client does not. `CONTRACT_HEADER` appears exactly once in the extension
(`src_vs_code/src/teamServerApi.ts:119`) — building the REQUEST header. The response's copy is
dropped: `request()` returns `{ ok, status, value }` and never looks at `response.headers`.

So today the panel can be told what a server speaks, on every call it already makes, and throws it
away. The one direction that works is the server refusing a client that is too old (426). A panel
talking to a server too old for it has no idea.

Nothing is broken *yet*: `Current` is 1 on both sides and no shape has moved. That is exactly the
argument the server's own comment makes for having built its half early —

> The version has to exist BEFORE the first breaking change or it never usefully exists at all.

— and it applies unchanged to this half. The day a response shape moves, the panels that cannot read
the header are already in the field.

## What must be true when it is done

1. Every Team-server call records the contract the server answered with; a response without the
   header is recorded as **legacy** rather than as an error or as "current".
2. The panel names the oldest server contract it can work with, as a constant with the same rule the
   server's `MinimumSupported` has: **raised only when an older server would be MISREAD**, never
   because a newer one exists.
3. A server below that constant is said out loud in the *Team servers* section, naming the server, what
   it speaks, and what this panel needs — the shape `conventionsSkew` and `roleSwitchSkew` already use.
4. A server at or above it is silent. A mechanism that talks when nothing is wrong is one people learn
   to scroll past.
5. Nothing is REFUSED on the client side. The server refuses; the panel reports. A panel that stopped
   talking to a server it merely suspects would turn a warning into an outage, which is the failure the
   server's own comment refuses for the mirror case.
6. The contract survives the same way the rest of the row does — through a stale answer, so the
   sentence does not flicker away the moment a call fails.

## The change

- **`teamServerApi.ts`** — `request()` reads `response.headers.get(CONTRACT_HEADER)` and puts it on
  `ServerResult`, on BOTH arms, exactly as `status` already is: a 4xx from a too-old server is the
  case that most wants the number. Absent or unparseable is `0`, meaning legacy.
- **`teamServerApi.ts`** — `SERVER_CONTRACT_REQUIRED = 1`, with the raising rule written beside it.
- **`teamServerView.ts`** — the row gains a sentence when `contract < SERVER_CONTRACT_REQUIRED`,
  beside `publishedNote` and worded like it: what it is, what is needed, and where to fix it.
- **The state** — `TeamServerState` carries `contract`, filled by whatever last answered.

## Constraints

- **No new endpoint and no new call.** The number rides on responses the panel already makes; adding
  a probe for it would be the second thing to keep in step that the server's comment rejects.
- **The panel never refuses.** Reporting only.
- `teamServerView.ts` stays pure and `vscode`-free.
- The server half is not touched. It is correct and this plan has no opinion about it.

## Test plan

- **RED first:** a test that a response carrying `X-Coai-Contract: 0` is recorded as legacy fails
  today, because nothing reads the header at all.
- A response WITHOUT the header records legacy, not a crash and not "current".
- The header rides on a failure arm too — a 426 must still say what the server speaks.
- `teamServerView`: a row below the required contract carries the sentence; a row at or above it
  carries nothing; a row that has never answered carries nothing rather than "legacy".
- The whole extension suite.

## Definition of Done

- [ ] The response header is read on both arms and absent means legacy.
- [ ] The panel names what it requires, with the raising rule beside the constant.
- [ ] The sentence appears only when it is true, and refuses nothing.
- [ ] Documentation: `module_team_server.md`, this plan promoted, `research/README.md`.
- [ ] The extension suite green.

## What shipped differently

The plan round returned eleven findings and eight were taken. One rewrote the central decision:

**`undefined` and `0` are different answers.** The plan had "absent or unparseable is `0`, meaning
legacy", and four reviewers converged on what that costs — a dropped connection, a DNS failure or a
client-side timeout would have overwritten a known-good contract with `0` and flashed the skew
warning on every network blip. So `0` is now only what a server that ANSWERED and named nothing
means, `undefined` is "not known", and `panelProvider` keeps the last known value through an
`undefined`. A header that is not an integer is `undefined` too, for the same reason: `v2` from a
proxy is not evidence that a server is ancient.

Three were rejected with reasons: an out-of-order guard (a server's contract is a constant of its
binary, so two answers seconds apart can only differ across a redeploy, which the next repaint
settles); CORS exposure (checked rather than assumed — this extension has no `browser` entry, runs
only in the desktop host, and Node's fetch has no CORS layer to strip a header); and persisting the
contract to disk (the catalog, the usage and the staleness flag beside it are all in-memory and
re-learned on the first call after a restart; one field outliving its own row would be a second
lifetime to reason about).

## The open tail

- The number is recorded from whichever call answered last. That is right today, because every route
  carries the same header from the same server — but if a Team server ever fronts more than one
  version behind a load balancer, the row would report whichever answered most recently rather than
  the range.
- The response fixtures in `teamServerAuth.test.ts` and `teamServerSide.test.ts` still build a
  `Response` by hand and cast it. They now carry `headers`, which is what their absence broke, but
  `typescript/doctrine.md` asks for a typed factory or a note on the interface, and `Response` is a
  DOM lib type nobody here can annotate. A single documented factory per file is the fix; attempting
  it inside this branch cut through the files own constants and broke six tests, so it is its own
  change.
- Nothing yet READS `SERVER_CONTRACT_REQUIRED` except the note. The day a panel feature genuinely
  needs a newer server, raising the constant is the whole change — which is the property this was
  built for, and the reason it ships while it is still quiet.
