import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import { BEFORE_THE_CATALOG } from '../serverRoles';

/**
 * The two halves agree about which roles every deployed Team server already runs.
 *
 * <p>There are two copies of that list, in two languages, and they answer two different questions
 * about the same fact. `RemoteRoles.BeforeTheCatalog` in `coai-mcp` decides whether a round is SENT;
 * `BEFORE_THE_CATALOG` here decides what a person is TOLD before they start one. Neither can be
 * deleted — they run in different processes, and the extension has no way to ask the gate — so the
 * only thing left is to make them fail together.</p>
 *
 * <p><b>Written because they had already drifted.</b> Both started as the same proxy: "is this one of
 * the roles this product ships". Plan 4 added two document roles to that seed, so the proxy quietly
 * began to mean seven, and a round would have been sent to a server that had never heard of them.
 * `coai-mcp` was fixed in PR #235 with the literal list below; this half kept the proxy, and went on
 * promising on the roles page a round the gate would refuse to send. One server, two clients, two
 * different answers — the failure `RemoteRoles`' own remarks describe, happening to the code that
 * describes it.</p>
 *
 * <p>It reads the C# SOURCE rather than a generated artefact on purpose: the list is a fact about
 * what shipped before `/api/catalog` named roles, it is frozen by definition, and a generator would
 * be a third place for it to be wrong.</p>
 */

const csharp = (file: string): string =>
  fs.readFileSync(path.resolve(__dirname, '../../..', 'src_mcp', file), 'utf8');

/** `public const string ArchitectureRole = "Architecture";` → `ArchitectureRole` ➜ `architecture`. */
function shippedIds(): ReadonlyMap<string, string> {
  const source = csharp('core/Rounds/RoleCatalog.cs');
  const found = new Map<string, string>();
  for (const [, name, value] of source.matchAll(/public const string (\w+Role) = "([^"]+)";/g)) {
    found.set(name, value.toLowerCase());
  }

  return found;
}

/** The `RoleCatalog.XxxRole` entries inside the FrozenSet literal, resolved to their values. */
function beforeTheCatalogInCSharp(): readonly string[] {
  const source = csharp('runners/Reviewers/RemoteRoles.cs');
  const at = source.indexOf('BeforeTheCatalog');
  assert.notEqual(at, -1, 'RemoteRoles no longer declares BeforeTheCatalog — the halves cannot be compared');
  const block = source.slice(source.indexOf('[', at), source.indexOf('];', at));
  const ids = shippedIds();

  return [...block.matchAll(/RoleCatalog\.(\w+Role)\b/g)].map(([, name]) => {
    const value = ids.get(name);
    assert.ok(value, `RoleCatalog has no constant ${name} — one of the two files moved without the other`);

    return value;
  });
}

test('both halves name the same roles as predating the catalog field', () => {
  const theirs = [...beforeTheCatalogInCSharp()].sort();
  const ours = [...BEFORE_THE_CATALOG].sort();

  assert.deepEqual(
    ours,
    theirs,
    'the extension and coai-mcp disagree about which roles an un-answered Team server runs. '
      + 'A role in only one of them is a round the panel promises and the gate refuses, or the '
      + 'reverse. This list is frozen: it is what shipped BEFORE /api/catalog named roles, so the '
      + 'fix is almost never to add to it.',
  );
});

test('and it is FIVE — a role added later cannot join it', () => {
  // Stated as a number because that is the assertion a future reader has to argue with. The list is
  // a fact about the past; growing it would be claiming that a server deployed before a role existed
  // somehow runs it.
  assert.equal(BEFORE_THE_CATALOG.size, 5);
  assert.equal(BEFORE_THE_CATALOG.has('documentreview'), false, 'plan 4 added this one, long after');
  assert.equal(BEFORE_THE_CATALOG.has('documentsummary'), false);
});
