import { after } from 'node:test';

/** The part of the owned wrap (`scripts/owned.mjs`) this file uses — one shape, named instead of an `any`. */
interface OwnedScope { after(step: () => void): void; close(): void }
interface OwnedWrap {
  cleanupScope(): OwnedScope;
  ownedDir(scope: OwnedScope, what: string, options?: { parent?: string }): string;
  tempRoot(): string;
}
// eslint-disable-next-line @typescript-eslint/no-require-imports -- the wrap is an .mjs the compiled tests reach as roundCountsRead.test.ts does
const { owned } = require('../../scripts/owned.mjs') as { owned: OwnedWrap };

/** The file's ONE scope: every folder ownedTemp makes is claimed first and removed, with its claim, when this test file ends (each file runs in its own process: scripts/run-tests.mjs). */
const scope = owned.cleanupScope();
after(() => scope.close());

/** A fresh owned folder of the wrap inside the run folder — never a raw temp folder (E5 S7). Call it inside a test or a hook, never at module load. */
export function ownedTemp(what: string): string {
  return owned.ownedDir(scope, what); // the default parent: owned.tempRoot(), unresolved, as tmpdir() was
}
