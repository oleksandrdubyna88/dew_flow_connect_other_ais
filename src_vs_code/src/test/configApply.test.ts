import assert from 'node:assert/strict';
import { test } from 'node:test';
import { applyImport, failureSentence, type ApplyIo } from '../configApply';
import type { Imported } from '../configTransfer';

/** Applying an imported config all or nothing — issue #467. */

interface Fake {
  readonly settings: Map<string, unknown>;
  readonly prompts: Map<string, string>;
  readonly io: ApplyIo;
}

function fake(settings: Record<string, unknown>, prompts: Record<string, string>, failOn: (what: string) => boolean = () => false): Fake {
  const s = new Map(Object.entries(settings));
  const p = new Map(Object.entries(prompts));
  const io: ApplyIo = {
    baseValue: (key) => s.get(key),
    writeSetting: async (key, value) => {
      if (failOn(`setting ${key}=${JSON.stringify(value)}`)) {
        throw new Error('disk full');
      }
      if (value === undefined) {
        s.delete(key);
      } else {
        s.set(key, value);
      }
    },
    readPrompt: async (id) => p.get(id),
    writePrompt: async (id, text) => {
      if (failOn(`prompt ${id}=${text}`)) {
        throw new Error('locked');
      }
      p.set(id, text);
    },
    removePrompt: async (id) => {
      if (failOn(`remove ${id}`)) {
        throw new Error('locked');
      }
      p.delete(id);
    },
  };

  return { settings: s, prompts: p, io };
}

const imported = (settings: Record<string, unknown>, prompts: Record<string, string>): Extract<Imported, { ok: true }> =>
  ({ ok: true, settings, prompts, refused: [] });

test('every setting and prompt in the file is applied, and nothing it does not name is touched', async () => {
  const f = fake({ reviewTimeoutSeconds: 600, untouched: 'x' }, { 'kept-one': 'old' });

  const out = await applyImport(imported({ reviewTimeoutSeconds: 900 }, { 'new-one': 'text' }), f.io);

  assert.deepEqual(out, { ok: true, settings: 1, prompts: 1 });
  assert.equal(f.settings.get('reviewTimeoutSeconds'), 900);
  assert.equal(f.settings.get('untouched'), 'x');
  assert.equal(f.prompts.get('kept-one'), 'old', 'an import deletes nothing');
  assert.equal(f.prompts.get('new-one'), 'text');
});

test('a vendor imported keeps the CLI path this machine already has for it', async () => {
  const f = fake({ vendors: [{ id: 'codex', executablePath: 'C:/tools/codex.cmd' }] }, {});

  await applyImport(imported({ vendors: [{ id: 'codex', model: 'new' }] }, {}), f.io);

  assert.deepEqual(f.settings.get('vendors'), [{ id: 'codex', model: 'new', executablePath: 'C:/tools/codex.cmd' }]);
});

test('a write that fails puts back every setting and prompt the import had already changed', async () => {
  const f = fake({ a: 1, b: 2 }, { 'had-text': 'mine' }, (what) => what === 'prompt last-one=boom');

  const out = await applyImport(imported({ a: 10, c: 3 }, { 'had-text': 'theirs', 'fresh': 'new', 'last-one': 'boom' }), f.io);

  assert.equal(out.ok, false);
  assert.deepEqual(Object.fromEntries(f.settings), { a: 1, b: 2 }, 'a setting was left changed, or one it added stayed');
  assert.deepEqual(Object.fromEntries(f.prompts), { 'had-text': 'mine' }, 'a prompt was left changed, or one it added stayed');
  assert.match(out.ok ? '' : failureSentence(out), /stopped at prompt last-one \(locked\)\. Everything it had changed was put back\./);
});

test('a restore that fails as well is named, not swallowed', async () => {
  const f = fake({}, {}, (what) => what === 'prompt second=x' || what === 'remove first');

  const out = await applyImport(imported({}, { first: 'x', second: 'x' }), f.io);

  assert.equal(out.ok, false);
  assert.deepEqual(out.ok ? [] : out.notRestored, ['prompt first']);
  assert.match(out.ok ? '' : failureSentence(out), /could not be put back and need a look: prompt first/);
});
