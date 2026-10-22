import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';

/**
 * Who may know the host kind — the structural half of the `HostKind` audit (E2.S3 of
 * research/PLAN_kimi_and_vibe_on_native_linux_E2.md, plan round 99e086b8, finding 4).
 *
 * <p>`'linux-unread'` is a member of the union `HostKind`, and a member of a union is only safe where every consumer has been decided:
 * the two exhaustive note tables fail to COMPILE until they say what an unread host is told, but a consumer that merely COMPARES the
 * kind (`host === 'linux'`) or WRITES it somewhere compiles whatever the union holds. The audit table in the plan classified each
 * consumer; this test makes the list itself a fact: the non-test sources that mention the host kind are exactly the five below. A sixth
 * — above all a settings writer, the ledger, a log line, a round, the `.mcp.json` block or anything that reaches the server — fails
 * here until it is classified in the plan's audit table and named in this list, because a persisted `linux-unread` would outlive the
 * window that could not read its banner and keep refusing a host that reads fine on the next start.</p>
 *
 * <p>The scan is of the raw text, comments included, on purpose: a comment that names `hostKind` is a place a reader will look for
 * the consumer, so it is held to the same list.</p>
 */

const SOURCE = join(__dirname, '..', '..', 'src');

/** The five modules that know the kind, and the part each plays — `hostSide` makes it, `panelProvider` asks and carries it, `panelView` reads it, the other two choose a text by it. */
const CONSUMERS: readonly string[] = ['hostSide.ts', 'panelProvider.ts', 'panelView.ts', 'runtimeSince.ts', 'vendorTerminal.ts'];

const MENTIONS_THE_KIND = /hostKind|HostKind/u;

function sourcesMentioningTheKind(): string[] {
  return readdirSync(SOURCE)
    .filter((name) => name.endsWith('.ts') && !name.endsWith('.d.ts'))
    .filter((name) => MENTIONS_THE_KIND.test(readFileSync(join(SOURCE, name), 'utf8')))
    .sort();
}

test('the sources that mention the host kind are exactly the five the audit classified — nothing persists it, nothing else compares it', () => {
  const found = sourcesMentioningTheKind();
  const unclassified = found.filter((name) => !CONSUMERS.includes(name));
  const vanished = CONSUMERS.filter((name) => !found.includes(name));

  assert.deepEqual(unclassified, [], `a new consumer of the host kind (${unclassified.join(', ')}) was not classified in the audit table of E2.S3 — decide what it does on 'linux-unread', then name it here`);
  assert.deepEqual(vanished, [], `${vanished.join(', ')} no longer mentions the host kind: the audit table and this list are stale`);
});

test('no settings writer, ledger, server block or log line receives the host kind: the writers are not on the list', () => {
  const writers = ['settingsShape.ts', 'serverSettingsFile.ts', 'mcpBlock.ts', 'configTransfer.ts', 'sideSettings.ts'];
  const found = sourcesMentioningTheKind();

  for (const writer of writers) {
    assert.ok(!found.includes(writer), `${writer} mentions the host kind: a persisted linux-unread would outlive the window that could not read its banner`);
  }
});
