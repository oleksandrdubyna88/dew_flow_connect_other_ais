import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { existsSync, statSync } from 'node:fs';
import { basename, dirname } from 'node:path';
import { test } from 'node:test';
import { ownedTemp } from './ownedTemp';

/** The part of the owned wrap (`scripts/owned.mjs`) this contract reads: the name's form and the run folder. */
interface OwnedWrap {
  classifyName(name: string): { form: string; owner?: string; token?: string };
  tempRoot(): string;
}
// eslint-disable-next-line @typescript-eslint/no-require-imports -- the wrap is an .mjs the compiled tests reach as roundCountsRead.test.ts does
const { owned } = require('../../scripts/owned.mjs') as { owned: OwnedWrap };

/**
 * The contract of `ownedTemp` (E5 S7): the adapter is not a `*.test.*` file, so the leftovers guard cannot see a raw temp call put back into it. This test is the control:
 * a folder that is not made by the wrap's `ownedDir` has no owner form and no claim beside it, and fails here.
 */
test('ownedTemp makes two different folders of the wrap, each named in the owner form with a claim beside it, inside the run folder', () => {
  const first = ownedTemp('coai-contract');
  const second = ownedTemp('coai-contract');
  assert.notEqual(first, second, 'two calls give two folders');
  for (const dir of [first, second]) {
    assert.ok(statSync(dir).isDirectory(), `${dir} exists`);
    const name = owned.classifyName(basename(dir));
    assert.equal(name.form, 'owner', `${basename(dir)} is named in the owner form`);
    assert.equal(name.token, 'coai', `${basename(dir)} holds the repository token`);
    assert.ok(existsSync(`${dir}.owner`), `${dir}.owner, the claim, lies beside the folder`);
    assert.equal(dirname(dir), owned.tempRoot(), 'the folder lies in the run folder');
  }
});
