# PLAN — two green suites must not be able to disagree about one wire

> Status: **plan only, nothing implemented yet.** Scope: the client/server seam of the Team server
> — `src_vs_code/src/teamServerApi.ts`, `src_server/src/{Auth,SessionEndpoints}.cs`, and the test
> projects on both sides.
>
> Related docs: [module_team_server.md](../research/module_team_server.md),
> [PLAN_team_server.md](../research/PLAN_team_server.md).

## The symptom this comes from

Extension 0.31.1 shipped a sign-in that could not work for anybody. `createSession` posted the
Microsoft token as `{ token }` in the request BODY; `POST /api/session` authorises from the
`Authorization` header alone. Every sign-in was answered `401` before any JWT scheme ran.

**Both halves had passing tests, and they expected opposite things.**
`src_server/tests/SessionTests.cs:19` posts a header and `content: null`.
`src_vs_code/src/test/teamServerApi.test.ts` asserted the token was in the body. Neither suite can
observe the other, so nothing was red and the defect reached the Marketplace. Fixed on the client
in `fix(panel): the Team-server sign-in sends its token where the server reads it` (0.31.2) — this
plan is about the class, not that instance.

It also took a server journal to diagnose, which is the second half of the problem: the failure was
unreadable from both ends. `401` with an empty body, no `WWW-Authenticate`, and — because nothing
authenticated — no `JwtBearerHandler` line either. The panel could only say *the server answered
401*.

## Accepted in the gate and deferred to here

From the plan round of that fix (codex, gemini and the local reviewer each raised a form of it):

1. **A cross-seam contract test.** A unit assertion at the client's `fetch` boundary proves what
   the client SENDS; it cannot prove the server ACCEPTS it. Something must exercise one against the
   other.
2. **The single-auth-path rule is a convention, not an enforcement.** `Auth.Bearer` reading only
   `Request.Headers.Authorization` is documented now
   ([module_team_server.md](../research/module_team_server.md)) and true by inspection; nothing
   fails when a future route reads a token from somewhere else.
3. **The server's 401 says nothing.** A refusal that names no reason costs whoever meets it a
   server-log session. The reason must reach the caller without becoming an oracle for guessing
   tokens.

## Open questions the plan must answer before it is built

- **At which layer.** Three candidates, and they are not equivalent:
  a hand-written `.http`/smoke suite run against a started server (the shape `ClaudeRag` already
  uses, `tools/http-smoke`); a shared fixture describing each route's wire format that BOTH suites
  assert against; or a test that boots the real server in-process and drives the extension's own
  `ask` at it. The third is the only one that can catch this exact defect without anyone writing
  the expectation down twice — and it is the most expensive.
- **What the 401 may safely say.** "No credential was presented" is safe. "Your token's audience is
  wrong" is a hint to an attacker holding a token. The split has to be decided rather than assumed.
- **Whether `Attempt.body` should stay.** After this fix no call in `teamServerApi.ts` sends a body,
  though `POST /api/reviews` on the server takes one. Kept deliberately; revisit if it is still
  unused when the review routes reach the panel.

## Definition of Done

- [ ] A test exists that fails when the client and the server disagree about `POST /api/session`'s
      wire format — demonstrated by reverting the 0.31.2 fix and watching it go red.
- [ ] The "credential comes from the Authorization header and nowhere else" rule is enforced by
      something that fails, not only written down.
- [ ] A caller refused by this server learns why, to the extent that saying so is safe.
- [ ] `research/module_team_server.md` records what shipped.
