# `http/` — the contract suite for `coai-server`

Every route this server has, as a request a client actually sends. The tier nothing else covers:
routing, the path grammar, the verb, model binding, the auth scheme, the status code and the
serialized shape — none of which a test inside the process can see.

The rule is [`.agents/conventions/common/http-contracts.md`](../.agents/conventions/common/http-contracts.md).

## Running the whole suite

```bash
node http/run-contracts.mjs
```

Exit code is the verdict: **0** pass · **1** a contract regression · **3** environment (the build or
the server would not start) · **4** configuration.

It does the whole lifecycle itself, so a fresh checkout needs nothing prepared:

1. checks `coai-server` is built, and refuses with exit 3 naming the build command if not — it does
   NOT build it, because resolving `dotnet` through PATH is the one thing this script is otherwise
   careful to avoid, and both callers have just built anyway;
2. makes a **throwaway data directory** and starts the server on a free loopback port with
   `Auth__Local__SigningKey` set and `boss@example.com` in `Coai__Admins`;
3. polls `/api/health` until it answers, and gives up with exit 3 rather than running requests
   against a server that never came up;
4. mints the three personas below and writes them to a git-ignored `http/.env`, through a temporary
   file and a rename so two runs at once cannot hand each other the wrong tokens;
5. runs [httpyac](https://httpyac.github.io) over every `.http` file;
6. stops the server and deletes the data directory, whatever happened.

Individual files also open in VS Code and send one request at a time — which is the point of writing
them — as long as `http/.env` exists from a previous run.

The whole run takes about six seconds.

### Three things about the runner that are not obvious

**The server writes to a FILE, never to a pipe the runner holds.** `spawnSync` blocks Node's event
loop for the whole run, so nothing drains a piped stdout — the server logs a line per request, fills
the pipe, and then BLOCKS writing to it, stops answering, and the run wedges with every assertion
already passed. It cost an afternoon to find because it only appeared once the suite configured a
vendor and the server had more to say.

**httpyac is a pinned devDependency here, spawned as plain Node with no shell.** `npx` on Windows
puts a `cmd.exe` in between, and pinning it also means the run does not depend on a download.

**A PATH with no vendor CLIs on it.** `/api/catalog` probes each configured vendor by launching its
CLI. With the developer's real PATH the suite starts `codex`, which on a signed-in machine waits. The
suite asserts that `cliFound` is a boolean, not that it is true, so a probe that fails immediately is
exactly as good a test and is the same on every machine.

## The three personas

The `Local` HMAC scheme exists for exactly this: an offline issuer for tests and air-gapped
deployments, never configured in production. The suite mints:

| Variable | Who | Why the suite needs them |
|---|---|---|
| `{{token}}` | `dev@example.com` | an ordinary member of the company |
| `{{adminToken}}` | `boss@example.com` | in `Coai__Admins`; the only one who may ask `scope=company` |
| `{{outsiderToken}}` | `someone@other-company.test` | a valid signature from outside the allowed domain — the 403 that keeps another company off these subscriptions |

## Why no review ever runs

`http/reviews/` submits, polls and cancels REAL jobs — 202, 200, 204, and the 403 for somebody
else's — and **nothing is ever sent to a vendor**. The cold stack has one vendor in the allowlist and
no account signed in for it: the allowlist accepts the request, the job is queued, and the runner
refuses to start it because there is nobody to run it as. Every one of those statuses is a real answer
from real state, at zero subscription cost.

What that cannot reach is a review that actually *ran*, and it is declared rather than faked. Mocking
the vendor would prove the mock works.

## What is deliberately not covered

Each declaration lives beside the requests it belongs to, as `# @uncovered`, so
`http-coverage.mjs` counts the route as *declared* rather than missing. Summarised here:

- **`429` on the per-caller queue cap** — needs twenty-one queued reviews and a vendor that will not
  drain. Machinery, not a request.
- **`500` on a failed session delete** — needs a filesystem that refuses a delete.
- **`Done` / `failed` review outcomes** — need a signed-in paid account and real vendor spend.
- **The rate limiter's own `429`** — 120 requests in 10 seconds, which is a load generator rather
  than a contract.
