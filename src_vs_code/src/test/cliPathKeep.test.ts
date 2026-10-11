import assert from 'node:assert/strict';
import { test } from 'node:test';

import { CLI_PATH_KEPT_PREFIX, droppedNoticeText, keptKey, type DroppedValue, type KeptStore } from '../cliPathKeep';
import { CliPathKeeper } from '../cliPathKeeper';
import { pathForThisSide, type ExecutableBySide, type PathFamily } from '../pathFamily';
import { DEFAULT_VENDORS, vendorsFrom, type Vendor } from '../vendors';

/**
 * A per-side CLI path an older extension's save dropped is SAID, and offered back — never restored on its own
 * (todo/PLAN_paths_per_side.md, E3 as built: the plan gate's Major finding, the risk consultant's findings a and b).
 *
 * <p>Measured: extension v0.67.0 and v0.65.0 rebuild every row from the fields they know when they save `coai.vendors`,
 * so any vendor save they make removes `executablePathBySide` from every row. Every row here goes through the real reader
 * and a JSON round trip, as `coai.vendors` does between a save and a read; the store is a plain map, as `globalState`
 * is.</p>
 */

const W = 'C:\\tools\\codex.cmd';
const P = '/opt/codex/bin/codex';
const P2 = '/usr/local/bin/codex';

/** A `globalState` stand-in: keys, get, update with `undefined` deleting. */
function store(): KeptStore & { readonly map: Map<string, unknown> } {
  const map = new Map<string, unknown>();

  return {
    map,
    keys: () => [...map.keys()],
    get: (key) => map.get(key),
    update: (key, value) => {
      if (value === undefined) {
        map.delete(key);
      } else {
        map.set(key, JSON.parse(JSON.stringify(value)) as unknown);
      }
      return Promise.resolve();
    },
  };
}

/** A stored catalog row read back the way the extension reads `coai.vendors`. */
function row(executablePath: string, executablePathBySide?: ExecutableBySide, identity: Partial<Pick<Vendor, 'id' | 'runtime' | 'model'>> = {}): Vendor {
  const stored = { ...DEFAULT_VENDORS[0]!, id: 'codex', runtime: 'codex', model: 'gpt-6', enabled: true, executablePath, ...identity, ...(executablePathBySide === undefined ? {} : { executablePathBySide }) };

  return vendorsFrom(JSON.parse(JSON.stringify([stored])) as unknown)[0]!;
}

/** The settings, the keeper over them, and everything it said and wrote — `layer` null: this side keeps its own settings. */
function world(first: readonly Vendor[], layer: string | null = 'user') {
  const kept = store();
  const said: DroppedValue[] = [];
  const resolved: string[] = [];
  const refused: string[] = [];
  const writes: Vendor[][] = [];
  let rows: readonly Vendor[] = first;
  const keeper = new CliPathKeeper<Vendor>({
    store: kept,
    layer: () => layer ?? undefined,
    rows: () => vendorsFrom(JSON.parse(JSON.stringify(rows)) as unknown),
    write: (next) => { writes.push([...next]); rows = next; return Promise.resolve(); },
    say: (one) => { said.push(one); },
    resolved: (subject) => { resolved.push(subject); },
    refused: (_one, reason) => { refused.push(reason); },
    failed: (error) => { throw error; },
  });

  return {
    keeper, kept, said, resolved, refused, writes,
    rows: () => rows,
    /** A save by anyone: the settings now hold these rows. */
    set: (next: readonly Vendor[]) => { rows = next; },
  };
}

/** What a side of this family runs for the row — the disk saying "absent" to every other-OS spelling. */
function resolved(stored: Vendor, family: PathFamily): string {
  return pathForThisSide(stored, { family, systemDrive: 'C:', fileAt: () => 'absent' }).path;
}

/** Both sides saved by a newer extension: Windows W (legacy W), WSL P. */
const BOTH = (): Vendor => row(W, { windows: W, posix: P, mirrored: W });

/** An older WSL extension's save of P2: the legacy value it knows, the per-side member dropped. */
const OLDER_WSL_SAVE = (): Vendor => row(P2);

test('a member an older save dropped is said ONCE per run, naming the kept value — and said again only after it came back and went again', async () => {
  const w = world([BOTH()]);
  await w.keeper.check();
  assert.equal(w.said.length, 0, 'a row carrying its member is only copied');

  w.set([OLDER_WSL_SAVE()]);
  await w.keeper.check();
  await w.keeper.check();
  assert.equal(w.said.length, 1, 'the drop is said once, not on every look');
  assert.deepEqual(w.said[0]!.kept, { windows: W, posix: P, mirrored: W });
  assert.equal(w.said[0]!.legacy, P2);

  w.set([BOTH()]);
  await w.keeper.check();
  assert.deepEqual(w.resolved, [w.said[0]!.key], 'the member came back: the condition ended');
  w.set([OLDER_WSL_SAVE()]);
  await w.keeper.check();
  assert.equal(w.said.length, 2, 'dropped again after it came back is news');
});

test('the notice speaks of a previously saved value — never of who removed it — and names both kept values', () => {
  const text = droppedNoticeText({ key: 'k', id: 'codex', runtime: 'codex', model: 'gpt-6', kept: { windows: W, posix: '', mirrored: W }, legacy: P2 });
  const all = `${text.title} ${text.cure}`;

  assert.match(all, /a previously saved value/);
  assert.doesNotMatch(all, /older extension|deleted|removed it/i, 'a missing member looks the same whatever removed it');
  assert.ok(text.title.includes(W), 'the Windows value is named');
  assert.match(text.title, /WSL \/ Linux: empty/, 'an explicitly empty value is named as such');
});

test('a removal is never undone on its own: no settings write until Restore is clicked, and the copy is kept', async () => {
  const w = world([BOTH()]);
  await w.keeper.check();
  w.set([OLDER_WSL_SAVE()]);
  await w.keeper.check();
  await w.keeper.check();

  assert.equal(w.writes.length, 0, 'the keeper wrote the settings without a click');
  assert.equal(w.rows()[0]!.executablePathBySide, undefined);
  assert.ok(w.kept.get(keptKey('user', w.rows()[0]!)) !== undefined, 'the copy is still there to restore');
});

test('Restore puts the kept value back and leaves the legacy value the older save wrote — Windows runs W, WSL runs the older save', async () => {
  const w = world([BOTH()]);
  await w.keeper.check();
  w.set([OLDER_WSL_SAVE()]);
  await w.keeper.check();

  await w.keeper.restore(w.said[0]!);

  assert.equal(w.writes.length, 1);
  const after = vendorsFrom(JSON.parse(JSON.stringify(w.writes[0])) as unknown)[0]!;
  assert.equal(after.executablePath, P2, 'the legacy value is the older save\'s — a restore must not mirror the Windows value into it');
  assert.deepEqual(after.executablePathBySide, { windows: W, posix: P, mirrored: W });
  assert.equal(resolved(after, 'windows'), W, 'the Windows side has its own value back');
  assert.equal(resolved(after, 'posix'), P2, 'the WSL side keeps what its older save meant');
});

test('an explicitly empty kept value is restored as empty — the PATH lookup — never as absent', async () => {
  const w = world([row('//server/share/codex', { windows: '', posix: P, mirrored: '//server/share/codex' })]);
  await w.keeper.check();
  w.set([row('//server/share/codex')]);
  await w.keeper.check();
  assert.equal(w.said.length, 1, 'the drop of an empty value is said too');
  await w.keeper.restore(w.said[0]!);

  assert.equal(w.writes.length, 1, 'Restore wrote nothing');
  const after = vendorsFrom(JSON.parse(JSON.stringify(w.writes[0])) as unknown)[0]!;
  assert.equal(after.executablePathBySide?.windows, '');
  assert.equal(resolved(after, 'windows'), '', 'empty: PATH, never the share');
});

test('a stale Restore is refused and writes nothing: a later save, a later edit, a removed row', async () => {
  for (const [later, reason] of [
    [[BOTH()], /saved for it since/],
    [[row('/opt/other/codex')], /changed since/],
    [[row(W, undefined, { id: 'claude', runtime: 'claude', model: 'opus' })], /no longer in the list/],
  ] as const) {
    const w = world([BOTH()]);
    await w.keeper.check();
    w.set([OLDER_WSL_SAVE()]);
    await w.keeper.check();
    w.set(later);

    await w.keeper.restore(w.said[0]!);

    assert.equal(w.writes.length, 0, `a stale restore wrote over ${JSON.stringify(later)}`);
    assert.match(w.refused[0] ?? '', reason);
    assert.equal(w.rows().length, 1, 'a removed row is never recreated');
  }
});

test('a reused id with another runtime or model never inherits the copy — and the copy goes with its row', async () => {
  for (const reused of [{ runtime: 'claude' as const }, { model: 'gpt-7' }]) {
    const w = world([BOTH()]);
    await w.keeper.check();
    // The row was removed and freeVendorId handed its id to the next one added: no per-side member yet.
    w.set([row(P2, undefined, reused)]);
    await w.keeper.check();

    assert.equal(w.said.length, 0, `${JSON.stringify(reused)}: another CLI was offered the old one's path`);
    assert.deepEqual(w.kept.keys(), [], 'a copy no row carries is deleted');
  }
});

test('a side that keeps its own settings is left alone: nothing copied, nothing said', async () => {
  const w = world([BOTH()], null);
  await w.keeper.check();
  w.set([OLDER_WSL_SAVE()]);
  await w.keeper.check();

  assert.deepEqual([w.kept.keys(), w.said.length], [[], 0]);
});

test('a copy is per layer: a row of one layer is never offered another layer\'s copy', async () => {
  const w = world([OLDER_WSL_SAVE()], 'side:wsl');
  await w.kept.update(keptKey('user', OLDER_WSL_SAVE()), { bySide: { windows: W, posix: P, mirrored: W } });
  await w.keeper.check();

  assert.equal(w.said.length, 0);
  assert.ok(w.kept.keys().every((key) => key.startsWith(`${CLI_PATH_KEPT_PREFIX}user@`)), 'and another layer\'s copy is not pruned from here');
});
