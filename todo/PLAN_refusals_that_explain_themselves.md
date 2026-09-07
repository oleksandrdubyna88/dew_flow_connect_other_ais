# PLAN — the Team server's refusals say why, and its one credential path is enforced

> Status: **plan only, nothing implemented yet.** Scope: `src_server/src/Auth.cs`,
> `CallerFilter.cs`, and the server's test project.
>
> Related docs: [PLAN_contract_across_the_seam.md](../research/PLAN_contract_across_the_seam.md)
> (this is its open tail), [module_team_server.md](../research/module_team_server.md).

## The symptom

On 2026-09-07 a sign-in failed for every user and the only thing anyone could see was
**`the server answered 401`**. That sentence is the client's fallback for an EMPTY response body
(`said()` in `teamServerApi.ts`), and the body was empty because the 401 is a bare challenge: no
body, no `WWW-Authenticate`. Diagnosing it took an SSH session and `journalctl` on the deployment.

Two things would each have shortened that to a minute.

### 1. The refusal says nothing

`Auth.RequireCaller` answers 401 when it cannot identify the caller and 403 when the caller's
domain is not allowed — a genuinely useful distinction that never reaches anybody, because both are
written as a status code with no body.

What it may safely say is the whole question, and it must be decided rather than assumed:

- **Safe:** "no credential was presented", "this route needs an identity-provider token, not a
  session token", "your account is outside the company domain" (the caller already knows their own
  domain).
- **Not safe:** "the audience is wrong", "the signature did not verify", "that token expired
  eleven minutes ago" — each narrows the search for somebody holding a token they should not.

The likely shape is a short, fixed vocabulary of reasons chosen by the SERVER, never the exception
text from `Microsoft.IdentityModel` — which is where the `IDX…` codes live and where they should
stay.

### 2. The one-credential-path rule is a paragraph

`research/module_team_server.md` now says every route that has a caller reads its credential from
`Authorization` and nowhere else. A paragraph does not fail. A future route that reads a token out
of a body, a query string or a cookie would contradict it silently, and the contract suite would
not notice — it exercises the routes that exist today.

## Open questions to answer before building

- **How do you test "no handler reads a body for auth"?** A Roslyn analyser is heavy for one rule.
  A test that asserts every endpoint registered without `RequireCaller` is one of the two known
  anonymous routes is cheap and catches the realistic mistake (a new route with no gate) but not
  the exotic one (a gated route that ALSO trusts a body).
- **Does the reason belong in the body or in `WWW-Authenticate`?** The header is the standard place
  and the client would have to learn to read it; the body is what `said()` already prefers. The
  measured fact from 2026-09-07: this server currently sends neither.
- **What does the panel do with it?** A reason worth sending is a reason worth showing, and the
  Team-servers section has one line to show it in.

## Test plan

- A server test per reason: each refusal carries its sentence, and the two sensitive cases
  (audience, signature) carry the generic one instead.
- A test that the reason for a wrong-domain caller is 403 and names the domain rule, while an
  unidentified caller is 401 and does not.
- The endpoint-registration test above, proved to have teeth by adding a gateless route to a
  fixture and watching it go red.

## Definition of Done

- [ ] A caller refused by this server learns why, from a vocabulary the server owns.
- [ ] Nothing in a refusal narrows the search for a valid token.
- [ ] The single-credential-path rule fails a test rather than only reading well.
- [ ] The panel shows the reason where it used to show `the server answered 401`.
- [ ] `research/module_team_server.md` records what shipped.
