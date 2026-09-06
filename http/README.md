# `http/` — the contract suite for `coai-server`

Every route this server has, as a request a client actually sends. The tier nothing else covers:
routing, the path grammar, the verb, model binding, the auth scheme, the status code and the
serialized shape — none of which a test inside the process can see.

The rule is [`.claude/rules/shared/common/http-contracts.md`](../.claude/rules/shared/common/http-contracts.md).

## Running the whole suite

```bash
node http/run-contracts.mjs
```

Exit code is the verdict: **0** pass · **1** a contract regression · **3** environment (the build or
the server would not start) · **4** configuration.

It does the whole lifecycle itself, so a fresh checkout needs nothing prepared:

1. builds `coai-server`;
2. makes a **throwaway data directory** and starts the server on a free loopback port with
   `Auth__Local__SigningKey` set and `boss@example.com` in `Coai__Admins`;
3. polls `/api/health` until it answers, and gives up with exit 3 rather than running requests
   against a server that never came up;
4. mints the three personas below and writes them to a git-ignored `http/.env`;
5. runs [httpyac](https://httpyac.github.io) over every `.http` file;
6. stops the server and deletes the data directory, whatever happened.

Individual files also open in VS Code and send one request at a time — which is the point of writing
them — as long as `http/.env` exists from a previous run.

## The three personas

The `Local` HMAC scheme exists for exactly this: an offline issuer for tests and air-gapped
deployments, never configured in production. The suite mints:

| Variable | Who | Why the suite needs them |
|---|---|---|
| `{{token}}` | `dev@example.com` | an ordinary member of the company |
| `{{adminToken}}` | `boss@example.com` | in `Coai__Admins`; the only one who may ask `scope=company` |
| `{{outsiderToken}}` | `someone@other-company.test` | a valid signature from outside the allowed domain — the 403 that keeps another company off these subscriptions |

## Why no review ever runs

`http/reviews/` submits, polls and cancels real jobs, and **nothing is ever sent to a vendor**. The
cold stack has a fresh data directory, so no account has been signed in; the runner correctly refuses
to start a review on an account nobody signed in, and every job stays `queued`. That exercises the
whole contract of submit / poll / cancel — status, shape, ownership, the id grammar — at zero cost.

What it cannot reach is a review that actually *ran*, and that is declared rather than faked. Mocking
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
