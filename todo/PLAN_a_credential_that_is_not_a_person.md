# PLAN — a credential that is not a person

> Status: **plan only, nothing implemented yet.** Scope: `src_server` (a second kind of credential
> and its endpoints), `deploy/` (the canary stops holding a person's session), and the extension's
> panel if an operator is to manage them without a shell.
>
> Related docs: [module_team_server.md](../research/module_team_server.md),
> [architecture.md](../research/architecture.md), [deploy/README.md](../deploy/README.md).

## The symptom

The deploy's canary authenticates as a **person**. `/etc/coai-canary.token` holds an ordinary
session token — the same kind a developer's panel holds — and `Sessions.Issue` stamps an absolute
`ExpiresUtc` that nothing extends:

```csharp
// src_server/src/Sessions.cs:90
if (record.ExpiresUtc <= nowUtc)
{
    Delete(name);

    return null;
}
```

`Validate` refreshes `LastUsedUtc` and writes it back, but the deadline is untouched — so the token
dies `Coai:SessionTtlDays` after it was minted whether it was used every hour or never. The
operator therefore rewrites it by hand, for ever, on a clock unrelated to anything they are doing.

On 2026-09-14 the clock ran out during a deploy of `server-v0.6.0`. Three vendors' canaries were
refused in 350 ms and the release was rolled back, against a build whose entire auth path was
byte-identical to the one serving. The immediate costs are paid — the wrapper now asks
`/api/whoami` before it touches anything, and the canary reports the status — but both are
mitigations of a credential that is the wrong SHAPE for what is holding it.

**And the obvious shortcut is deliberately closed.** Giving the box a local signing key so the
deploy could mint its own JWT, as the test suites do, is refused at startup:

```csharp
// src_server/src/Startup.cs:79
if (localEnabled && (!string.IsNullOrWhiteSpace(msTenant) || googleEnabled))
{
    throw new InvalidOperationException(
        "Auth:Local:SigningKey is set alongside a real identity provider. The local scheme "
        + "signs identities with a shared secret, so anyone holding that key could sign in "
        + "as anyone in an allowed domain without going near Microsoft or Google. …
```

That guard is right and this plan must not weaken it. Whatever is built here is **not** a key that
can mint arbitrary identities.

## What a person does with it

An admin issues a credential for the canary once, from the panel or one command. It is named, it is
revocable, it appears in a list with what it was last used for, and it does not expire on a weekly
clock. The deploy stops holding anybody's session. Revoking it breaks the canary and nothing else.

## The decisions to settle

None of these are settled — that is what makes this a plan and not a patch.

### 1. What it IS

A credential that authenticates a **job**, not a person: an identity that cannot sign in to the
panel, cannot read another caller's reviews, and cannot be used to submit work billed to somebody
else. The narrowest form that serves the canary is a token that may do exactly two things — `GET
/api/whoami` and submit a review under its own name — which is already close to a capability rather
than an identity.

### 2. Whether it expires at all

A credential with no expiry is one nobody notices has leaked. A credential with a 7-day expiry is
what this plan exists to remove. The likely answer is a LONG expiry that is stated when it is issued,
plus a listing that shows the deadline, plus revocation — so it fails visibly and on purpose rather
than silently and by the calendar. `POST /api/session`'s shape is the precedent to widen or copy.

### 3. Who may issue one

`Caller.IsAdmin` exists (`Coai:Admins`). Issuing a non-expiring credential is an admin act, and the
one that most deserves an audit line.

### 4. Where the secret lives

`/etc/coai-canary.token` at `0600` is already the pattern, and `deploy/README.md` already argues why
a path beats a value. Nothing here changes that; what changes is what the file holds.

### 5. Whether the panel manages them

An operator who cannot list and revoke these without a shell will not revoke them. That argues for a
small admin section; it is also the largest part of the work, and it can follow.

## Also on the list: sliding expiry for PEOPLE

A separate change, recorded here so it is not lost, and deliberately **not** a substitute for the
above.

`Sessions.Validate` already writes `LastUsedUtc` on use (subject to `LastUsedResolution`). Extending
`ExpiresUtc` on the same write would keep an actively used session alive and let an abandoned one
die on schedule — which is what a person expects from a panel they use daily and is nearly free,
since the write already happens.

It does **not** help the canary: deploys can be weeks apart, so a sliding window sized for a
developer's day would lapse between them anyway. Two different problems that happen to touch one
field.

The security question to answer before building it: a sliding window means a stolen token renews
itself for as long as the thief keeps using it. The usual answer is an absolute ceiling alongside the
sliding one — re-authenticate after N days however active you have been — and that ceiling is the
decision, not the sliding part.

## Build order

1. `src_server` — the credential, its issue/list/revoke endpoints, and the guard that keeps it from
   becoming a second way to be a person.
2. `deploy/` — the canary uses it; `preflight_token` and `refusal_hint` say the new thing when it is
   wrong. The 401 sentences written on 2026-09-14 name a session token and will need rewording.
3. `src_vs_code` — the admin list, if it is in scope by then.
4. Docs — `module_team_server.md`, `deploy/README.md`, and the POST_DEPLOY row that checks it.

## Test plan

- A job credential authenticates the canary's two calls and is refused everywhere a person's session
  is accepted but a job's must not be.
- Revocation takes effect on the next request, and the refusal says it was revoked rather than that
  it expired — the two are different events and the deploy should not confuse them.
- The startup guard still refuses `Auth:Local:SigningKey` alongside a real provider. **A regression
  test on that specific line**, because this plan is exactly the pressure that would erode it.
- An expired or revoked job credential stops the deploy in `preflight_token`, before anything is
  fetched — the guarantee that change bought must survive the credential changing underneath it.

## Definition of Done

- [ ] The canary holds no person's session token.
- [ ] Its credential is listable and revocable by an admin, and shows when it was last used.
- [ ] The shared-secret guard in `Startup.Guard` is unchanged and covered by a test that names it.
- [ ] `deploy/README.md` no longer tells an operator to rewrite a token every seven days.
- [ ] The sliding-expiry question above is either built with its absolute ceiling decided, or
      extracted into its own plan with that ceiling named as the open question.

## Out of scope

- **Raising `Coai:SessionTtlDays`.** Considered and declined by the operator on 2026-09-14: it
  applies to every session on the server, so the whole team would carry a longer-lived credential to
  spare one script a re-authentication.
- **A shared signing key on the box.** Refused at startup, for the reason quoted above.
