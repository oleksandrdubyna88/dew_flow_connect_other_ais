# PLAN — one `Response` fixture, with the note the doctrine asks for

> Status: **IMPLEMENTED, 2026-09-09.** Scope: a new
> `src_vs_code/src/test/responseFixture.ts` and the three test files that build a `Response` by hand.
>
> Related docs: [module_tests.md](module_tests.md),
> [PLAN_the_client_reads_the_contract_back.md](PLAN_the_client_reads_the_contract_back.md).

## The symptom

Three test files build a `Response` by hand and cast it:

```ts
return { ok: true, status: 200, headers: new Headers(), text: async () => body } as Response;
```

`typescript/doctrine.md` names this exactly:

> No `as` cast in a test fixture standing in for a real type — and if one remains, the interface it
> casts to carries a note saying so.

There are **eight** of them across `teamServerApi.test.ts`, `teamServerAuth.test.ts` and
`teamServerSide.test.ts`, and no note anywhere. `Response` is a DOM lib interface — two dozen members
and a body stream — so a fixture that implemented it honestly would be testing the fixture. The cast
is the right call; having it eight times, undocumented, is not.

**It has already cost something.** Every one of those eight omitted `headers`, because nothing read
one. The day the client started reading a response header
([PLAN_the_client_reads_the_contract_back.md](PLAN_the_client_reads_the_contract_back.md))
all eight threw `Cannot read properties of undefined`, in tests that had nothing to do with the
change. They were fixed one at a time, by hand, and one of them was missed twice. A fixture that can
silently omit a field the production code reads is the defect; the cast is only where it hides.

Raised on that change's code round and deferred to here, because an attempt to do it inside that
branch cut through the files' own constants and broke six tests.

## What must be true when it is done

1. Exactly **one** `as Response` remains in the extension's tests, in one file, with the note the
   doctrine asks for beside it.
2. The factory takes what the production code actually reads — `ok`, `status`, `headers`, `text` —
   and nothing else, so a caller cannot forget a field it does not know about.
3. `headers` is filled by default, so a fixture that says nothing about them still behaves like a
   real response rather than throwing.
4. Every existing fixture goes through it, and no test's meaning changes: the suite is green before
   and after, with the same count.
5. The factory lives beside the tests, not in `src/`. It is a test fixture and must never be
   reachable from shipped code.

## The change

- **`src_vs_code/src/test/responseFixture.ts`** — new. One exported `response(parts)`, one cast, one
  note explaining why the cast stays and what it has already cost. A sibling of
  `teamServerSession.contract.ts`, which is the precedent for a non-`.test.ts` module in this folder.
- **`teamServerApi.test.ts`** — its `stub()` builds the response through the factory.
- **`teamServerAuth.test.ts`**, **`teamServerSide.test.ts`** — the same, at every site.
- Placement matters and is why this is its own change: the helper is IMPORTED, so nothing is inserted
  between a file's own constants and the code that reads them.

## Constraints

- **No production code changes.** This touches tests only; if a production line has to move to make a
  fixture work, that is a finding about the production line and belongs in its own change.
- **No test's assertions change.** The count before and after must match, and the same tests must
  pass — this is a refactor of how a fixture is built, not of what it proves.
- The factory must not be exported from anything `src/` can import.

## Test plan

- **RED first:** a test asserting the extension's test sources contain at most one `as Response`
  fails today, at eight.
- The factory defaults `headers` — a fixture that names none still answers `get()` with `null` rather
  than throwing, which is the failure this exists to prevent.
- The whole extension suite, before and after, with the same count and no new failures.

## Definition of Done

- [ ] One cast, one note, one factory.
- [ ] Every fixture goes through it.
- [ ] The suite green with the same test count.
- [ ] Documentation: `module_tests.md` names the fixture, this plan promoted, `research/README.md`.

## What shipped differently — and better

The plan aimed for **one** cast with the note the doctrine asks for. What shipped is **none**, because
a reviewer asked the better question: why is a fixture pretending, when Node has shipped the real
`Response` since 18? `new Response(body, { status, headers })` gives `ok` derived from the status,
real case-insensitive `Headers`, a real `text()`, and `json()`, `clone()` and `bodyUsed` already
correct — every member nobody has needed yet. The factory is eight lines and asserts nothing.

That single change also answered five other findings at once: what the factory's return type is, that
the default headers must be a real `Headers`, that four fields would break the day something calls
`json()`, that two calls must not share a headers object, and that a 204 legitimately carries no body
— the constructor refuses one, which a hand-built object would have carried happily.

**And the guard test lied on its first run.** It read `__dirname`, which under this harness is
`out/test` — the compiled JavaScript, where TypeScript has already erased every `as` cast. It passed
against eight offenders and proved nothing. It reads `src/test` now, with comments stripped, because
the note in the fixture names the very shape it forbids. That is the same defect as the one this plan
is about, in the test written to catch it: a thing that stands in for the real one and quietly lacks
what makes it real.

One finding was rejected: that writing a failing test before the fix is a hazard. It is the order
`testing.md` mandates, the window is one commit long, and what the finding asked for — a hook
asserting the suite is green before the red test runs — would forbid the practice the rule requires.

## The open tail

- `as typeof fetch` remains at every stub site. It is a different cast with a different risk — a
  function shape, not a fixture standing in for a value with members to forget — and nothing has gone
  wrong with it. Worth a look if `fetch`'s signature ever grows an overload these stubs would miss.
