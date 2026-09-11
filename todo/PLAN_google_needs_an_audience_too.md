# PLAN — Google needs an audience too

> Status: **plan only, nothing implemented yet, 2026-09-10.** Scope: `src_server/src/Startup.cs`,
> `src_server/src/Auth.cs`, `src_server/src/Program.cs`. Finding 9 of
> [the product audit of 2026-09-09](../research/REVIEW_product_audit_2026-09-09.md).
>
> Related docs: [module_team_server.md](../research/module_team_server.md) — *Configuration* and story 2.1.

## The symptom

`Startup.Guard` refuses a Microsoft tenant configured without audiences (`Startup.cs:44-50`), and the
comment above it says why in six lines: without an `aud` check, a token minted for ANY application in
the tenant buys a seven-day session on the company's paid subscriptions. The Google scheme has the
same field and no such guard. `Auth.cs:71` sets `ValidateAudience = googleAudiences.Count > 0`, so
`Auth:Google:Enabled=true` with an empty `Auth:Google:Audiences` starts cleanly and validates no
audience — the audit confirmed it by building the options and reading `ValidateAudience: False`.

A Google ID token is handed to every application a person signs into with Google. Issuer, signature,
lifetime, the allowed domain and `email_verified` are still checked, so this is not anonymous access —
it is any third-party application a colleague ever signed into being able to present that colleague to
this server. Same defect, same fix, one identity provider over.

Deployments that use only Microsoft are unaffected, and `coai.remsoft.dev` is one of them: **the
operator stated on 2026-09-11 that Google is disabled there and Microsoft is the only scheme
configured.** That is a person's answer about a file this repository cannot read
(`/etc/coai-server.env`, `0600` on the box), not something measured here — `deploy/README.md:257-259`
lists Microsoft keys only, which is consistent with it and proves nothing on its own.

Two things follow and they point in opposite directions, which is why the answer is worth writing down
rather than acted on twice. **The deploy risk is nil**: a server with Google disabled cannot reach the
new refusal, so this change cannot stop that box from starting. And **the live exposure is nil too**:
nobody can present a Google token to it at all today. So this closes a footgun for the NEXT deployment
rather than an open door on this one — worth doing, and not worth hurrying.

## The change

1. `Startup.Guard` takes `googleAudiences` and refuses `googleEnabled && googleAudiences.Count == 0`
   with the same sentence shape as the Microsoft refusal, naming the cure: the OAuth client id(s) in
   `Auth:Google:Audiences`.
2. `Auth.cs`: `ValidateAudience = true` for the Google scheme. The guard guarantees the list, so the
   conditional is no longer a decision — it was the decision that let this through.
3. `Program.cs:72` passes the list.
4. `module_team_server.md` *Configuration*: the refusal joins the list of start-up refusals.

**A deploy note, because this is the one change here that CAN stop a server from starting** — not
this one, on today's answer, but the next box or the day somebody adds Google to this one. A live
`/etc/coai-server.env` with Google enabled and no audiences will refuse to start after this release.
Read that file before deploying; POST_DEPLOY item 10 (`/api/health` reports the new version) is what
catches a server that did not come up. The note stays in the release notes even though
`coai.remsoft.dev` is out of its reach: a refusal nobody was warned about is the same outage whether
or not this deployment was the one at risk.

No growth surface.

### What the gate's plan round changed (2026-09-10, accepted)

**Inspecting the options is not observing the refusal** (codex, Major), and the rule this repository
already carries says the same thing in more words: *asserting that you SENT a protective option is not
testing that it protects.* Test 2 as drafted reads `ValidateAudience` out of a `JwtBearerOptions` — it
would pass whether or not the handler is wired to those options at all.

So the round adds a test that presents an actual token with the wrong `aud` and asserts **401**. The
Google scheme resolves its keys from `accounts.google.com`, which a test may not reach, so the test
points `MetadataAddress` at a loopback OIDC document served by the test itself, signs with a key it
generated, and sends two requests: the configured audience → the request is authorised, a different one
→ 401. That is the whole mechanism, exercised by the real handler.

**If that stub turns out not to be reachable from `WebApplicationFactory` without changing production
code, the test is not weakened to fit** — the options assertion stays as what it is, the gap is named in
this plan's deviations, and a `POST_DEPLOY.md` item covers it against the real identity provider. Which
of the two happened is recorded when the work ships; nothing here is reported as covered until it is.

## Test plan

| # | Test | Holds |
|---|---|---|
| 1 | `StartupGuardTests.GoogleEnabledWithNoAudience_RefusesToStart` — with audiences it starts (`localKey: null`, for the reason the Microsoft test gives) | RED today: `NotThrow` |
| 2 | `AuthSchemeTests.TheGoogleSchemeValidatesTheAudience` — `Auth.AddSchemes` into a `ServiceCollection`, read `JwtBearerOptions` for `"Google"`: `ValidateAudience` true, `ValidAudiences` the configured list | the audit's own probe, kept as a test |
| 3 | `AuthenticationTests.AGoogleTokenMintedForAnotherApplicationIsRefused` — a loopback OIDC document, a key the test made, the same claims and a different `aud` | the refusal itself, through the real handler |
| 4 | The existing `StartupGuardTests` and `AuthenticationTests` | nothing else moved |

## Definition of Done

- [ ] Tests 1–2 written, watched fail, passing; test 3 unedited and green.
- [ ] The deploy note is in the release notes of the server version that carries this.
