# PLAN — two green suites must not be able to disagree about one wire

> Status: **IMPLEMENTED, 2026-09-07.** Scope: `src_vs_code/src/contract/`,
> `src_vs_code/scripts/run-contract.mjs`, the `test:contract` script, and three steps in
> `.github/workflows/ci.yml`.
>
> Related docs: [module_team_server.md](module_team_server.md),
> [PLAN_team_server.md](PLAN_team_server.md).
>
> **Open tail:** the two DoD items this did not build — enforcing the one-credential-path rule, and
> a refusal that says why — are [../todo/PLAN_refusals_that_explain_themselves.md](../todo/PLAN_refusals_that_explain_themselves.md).

## The symptom this comes from

Extension 0.31.1 shipped a sign-in that could not work for anybody. `createSession` posted the
Microsoft token as `{ token }` in the request BODY; `POST /api/session` authorises from the
`Authorization` header alone. Every sign-in was answered `401` before any JWT scheme ran.

**Both halves had passing tests, and they expected opposite things.**
`src_server/tests/SessionTests.cs:19` posts a header and `content: null`.
`src_vs_code/src/test/teamServerApi.test.ts` asserted the token was in the body. Neither suite can
observe the other, so nothing was red and the defect reached the Marketplace. Fixed on the client
in 0.31.2 — this plan is about the class, not that instance.

## What was built

**One contract suite that drives the REAL client against a REAL server.**

- `src_vs_code/src/contract/teamServerSession.contract.ts` calls the extension's own
  `createSession` and `ask` — not a stub — and asserts a session is minted (`201`), that the
  session token then works on a later call, and that an account outside the company domain is
  refused **403 rather than 401**.
- `src_vs_code/scripts/run-contract.mjs` gets a server in front of it. With `COAI_CONTRACT_URL`
  set it uses the one you already have; without it, it starts the server this repository just
  built on a free loopback port with a throwaway data directory and a generated key, and stops it
  afterwards.
- `npm run test:contract`, deliberately **not** part of `npm test` — that suite stays hermetic and
  needs nothing running.
- CI runs it in `build · test · family checks`, which already has the .NET SDK and has just
  compiled the server.

**No Microsoft is involved.** The server carries a symmetric-key `Local` scheme built for exactly
this (`src_server/src/Auth.cs`: issuer `coai-local`, no audience check, HMAC-SHA256 over ≥32
bytes). The test mints one in fifteen lines of `node:crypto` with no dependency, which is what
makes this cheap enough to run on every pull request.

## The layer question, decided

The plan's open question was which of three shapes to build. The measured answer:

| shape | verdict |
|---|---|
| a shared fixture both suites assert against | rejected — the expectation is written down twice, so it can be wrong twice |
| the real client against a real server | **chosen** |
| the same, but through `src_server/Dockerfile` in CI | rejected **for CI only** |

The container was rejected on cost, not on principle: that Dockerfile publishes Native AOT (clang,
a full link) and would add minutes to every pull request, and no image is published to pull instead
— there is no `server-v*` tag. Meanwhile the CI job that already builds this server has the SDK and
node in it. **The container remains the better local story**, which is why the address is an input
(`COAI_CONTRACT_URL`) rather than something the script owns: `docker compose up` then
`npm run test:contract` works unchanged.

## Proof that it has teeth

Not assumed — measured, by reverting the 0.31.2 fix and running it:

```
✖ the real client mints a session against the real server
  AssertionError: the server refused the sign-in: status 401: the server answered 401
✖ the session the server issued is accepted on a later call
✖ an account outside the company domain is refused 403, not 401
  tests 3 · pass 0 · fail 3
```

The first failure is the operator's own sentence, reproduced from the defect. Restoring the fix:
`tests 3 · pass 3 · fail 0`.

The third going red is its own small finding: with the token in the body the server cannot even
tell that the domain is wrong, because nothing was authenticated — which is the 401-versus-403
distinction that made the original defect readable once anyone looked at the server.

## What shipped differently

**The assembly is `coai-server.dll`, not `CoaiServer.dll`.** The project sets `AssemblyName`; the
first run of the script reported the server as "not built" while it sat there under another name.
The error message names both paths it looked in, which is why that took one run rather than a
debugging session.

**Logs follow the data directory** rather than needing their own switch — `CoaiLogPath.RootFor`
puts them under `Coai__DataDir`, so the throwaway directory takes them too and nothing is written
beside the extension's sources.

**Three env vars, all real:** `Auth__Local__SigningKey` (≥32 bytes or startup refuses, on purpose),
`Coai__AllowedDomains`, and `Coai__RequireForwardedHttps=false` — without the last one the
forwarded-proto gate refuses every loopback request, correctly, since there is no proxy in front.

## Definition of Done

- [x] A test exists that fails when the client and the server disagree about `POST /api/session`'s
      wire format — demonstrated by reverting the 0.31.2 fix and watching it go red.
- [x] It runs on every pull request, and locally against a container just as well.
- [x] `research/module_team_server.md` records what shipped.
- [ ] The "credential comes from the Authorization header and nowhere else" rule is enforced by
      something that fails, not only written down. → the open tail.
- [ ] A caller refused by this server learns why, to the extent that saying so is safe. → the open
      tail.
