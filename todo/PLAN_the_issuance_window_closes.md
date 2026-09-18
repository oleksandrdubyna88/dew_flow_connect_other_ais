# PLAN — the issuance window closes, and the audit gets a reader

> Status: **plan only, nothing implemented yet, 2026-09-17.** Scope: `src_bugs` (an idempotency
> record on `POST /admin/keys`), and `src_vs_code` (readers for `/admin/audit` and `/admin/active`).
>
> Related docs: [PLAN_who_holds_a_key.md](../research/PLAN_who_holds_a_key.md) (the plan this is the
> tail of), [module_server.md](../research/module_server.md),
> [module_extension.md](../research/module_extension.md).

## Boundary with `PLAN_who_holds_a_key.md`

| Item | Which plan builds it | The other one's part |
|---|---|---|
| The admin API, the Users tab, the delivery, the send | `PLAN_who_holds_a_key.md` — **shipped 2026-09-17** | this plan assumes all of it and changes none of it |
| A server-side record that makes issuance replay-safe | **this plan** | the shipped one narrowed the window with a local attempt marker and said, in code and in its own status line, that closing it needs a server change |
| A reader for `/admin/audit` and `/admin/active` | **this plan** | the shipped one built both endpoints and recorded, deliberately, that neither has a reader |

**Order:** the issuance record first. It is the one with a correctness argument behind it; the audit
readers are a surface over endpoints that already work, and they inherit the cursor rule the Users
tab established.

**What is disjoint:** nothing in the shipped plan is reopened. No schema step it froze is edited, and
the marker format `# coai-bugs-admin-keys v1` is fixed.

## Symptom 1 — a key can exist that nobody holds

`POST /admin/keys` commits the key before it answers, so a host death between the commit and the
extension's write leaves a live key with no local record. Story 3 narrowed it as far as one side can:
an ATTEMPT is written before the request leaves, and the next open says a key may exist and points at
the newest row (`src_vs_code/src/bugsAdminKey.ts`, `beginIssuance`). Three reviewers said the
client-only version is not enough, and they were right — two commits cannot be made atomic from one
side.

**What would close it:** the caller supplies an idempotency token with the request; the server stores
it beside the key and returns the SAME key for the same token. A retry after a lost answer is then
the original answer rather than a second key. The token is a `POST` body field, not a header, because
this API's bodies are already read field by field and a header would be a second parsing rule.

## Symptom 2 — two endpoints nobody can read

`/admin/audit` and `/admin/active` shipped with story 2 and have no reader in the extension. They
were recorded as a known gap rather than discovered as one, and the Users tab's paging rule applies
to both: `nextBefore` is an opaque forward-only token, and a client never composes one.

## Build order

1. The idempotency column and its test, including the one that matters: the same token twice yields
   one key and one row.
2. `AdminApi` reads the token, and refuses a request that reuses one with a DIFFERENT note — that is
   a client bug, not a retry.
3. The extension sends a token it generates before the request, which is the value `beginIssuance`
   already writes down.
4. The audit and active readers, after.

## Test plan

- The same token twice: one key, one row, the same key returned.
- A token reused with a different note: refused, and the refusal says why.
- No token at all: still works, because an older client must not be broken by a newer server.
- A kill between the server's commit and the client's write, then a retry with the same token: the
  client ends up holding the key the server actually issued.

## Definition of Done

- [ ] `POST /admin/keys` is replay-safe for a caller that supplies a token, and unchanged for one
      that does not.
- [ ] The extension supplies one, and the attempt marker it already writes is what carries it.
- [ ] `research/PLAN_who_holds_a_key.md`'s status line stops saying the window is open.
- [ ] `/admin/audit` and `/admin/active` have readers, or this plan records why they still do not.
