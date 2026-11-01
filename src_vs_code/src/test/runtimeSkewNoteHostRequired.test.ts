import assert from 'node:assert/strict';
import { test } from 'node:test';

import { runtimeSkewNote } from '../runtimeSince';
import type { Vendor } from '../vendors';

/**
 * The host is a REQUIRED argument of `runtimeSkewNote` (E3 of research/PLAN_kimi_and_vibe_on_linux.md, code gate).
 *
 * <p>It used to default to `'windows'`, so a caller that forgot it gave a WSL2 or native-Linux card no host note at all —
 * silently the Windows answer. A missing argument must be a compile error. That is a property of the TYPE, which a runtime
 * assertion cannot see, so it is held with `@ts-expect-error`: the directive is itself an error (TS2578) the day the
 * parameter becomes optional again, and `npm run typecheck` is what goes red.</p>
 */

const VIBE: Vendor = {
  id: 'vibe', runtime: 'vibe', model: 'mistral-medium-3.5', enabled: true, plan: true, code: true,
  baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
};

test('a caller that names no host does not compile, so a Linux card can never get the Windows answer by omission', () => {
  // @ts-expect-error the host kind is required: leaving it out is the bug this guards
  const omitted = runtimeSkewNote('0.57.0', [VIBE]);

  // At run time the omission still answers (JavaScript has no required parameters); the guarantee is the compile error above.
  assert.equal(typeof omitted, 'string');
  assert.equal(runtimeSkewNote('0.57.0', [VIBE], 'wsl2').includes('Update coai-mcp to run Vibe here'), true);
});
