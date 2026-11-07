import { tmpdir } from 'node:os';

// The one read of the temp root for two `.ts` tests that only compare against it (E5 S6): a read, not a folder made here. Not a `*.test.*` file, so the guard does not read it.
export const tempRoot = (): string => tmpdir();
