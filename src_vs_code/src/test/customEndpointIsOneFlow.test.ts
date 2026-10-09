import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';

/**
 * ONE flow asks for a custom endpoint's name and base URL.
 *
 * <p>The boxes are `vscode.window.showInputBox`, so they live in `panelProvider.ts` and no test here
 * can drive them. What CAN be held is that there is exactly one of them. The id a name mints keys
 * the vault entry: two copies of the flow drifting on validation would be two spellings of one name,
 * which is two keys and a credential that is only there half the time. Story C6 extracted the boxes
 * out of `addVendor` for precisely that reason, and this is what stops them being pasted back. (The
 * consultant's own flow that shared them went with the old Settings page, research/PLAN_one_model_catalog.md
 * E5.3: a caller now picks a model from Models, where *Add a model* is the one way to make one.)</p>
 *
 * <p><b>Each assertion pins BOTH halves of its condition.</b> A structural test that only checks the
 * new call is present survives its own defect — leaving a second copy beside it would pass. So the
 * count is asserted, not the presence.</p>
 */

const PROVIDER = path.join(__dirname, '..', '..', 'src', 'panelProvider.ts');

const source = (): string => fs.readFileSync(PROVIDER, 'utf8');

function occurrences(text: string, needle: string): number {
  return text.split(needle).length - 1;
}

test('the name box and the URL box exist exactly once each, however many flows want them', () => {
  const host = source();

  assert.equal(occurrences(host, 'A short name — it identifies the vendor and names its key in the vault entry'), 1,
    'a second name box is a second idea of what a valid name is, and the id keys the vault');
  assert.equal(occurrences(host, 'Its OpenAI-compatible base URL'), 1,
    'a second URL box is a second idea of what a valid endpoint is');
  assert.equal(occurrences(host, 'private async askCustomEndpoint('), 1);
});
