import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';

/**
 * ONE flow asks for a custom endpoint's name and base URL — the reviewer's and the consultant's.
 *
 * <p>The boxes are `vscode.window.showInputBox`, so they live in `panelProvider.ts` and no test here
 * can drive them. What CAN be held is that there is exactly one of them. The id a name mints keys
 * the vault entry: two copies of the flow drifting on validation would be two spellings of one name,
 * which is two keys and a credential that is only there half the time. Story C6 extracted the boxes
 * out of `addVendor` for precisely that reason, and this is what stops them being pasted back.</p>
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

test('both flows go through it — the reviewer list and the consultant, by name', () => {
  const host = source();

  assert.equal(occurrences(host, 'this.askCustomEndpoint('), 2,
    'Add a reviewer and the consultant row are the two callers; a third is fine, none is the defect');
  assert.match(host, /private async customConsultant\(caller: string\)/u);
});

test('the consultant flow writes the CALLER it was given, and touches no reviewer row', () => {
  // The command carries the caller because four rows share one control. A handler that dropped it
  // would configure whichever caller the settings object happens to hold first, and every test over
  // the pure write would still pass.
  const host = source();
  const body = host.slice(host.indexOf('private async customConsultant('));
  const flow = body.slice(0, body.indexOf('\n  }'));

  assert.match(flow, /consultantEndpointWrite\(current, caller, own\.id, own\.baseUrl\)/u,
    'the caller from the command is what the write is keyed by');
  assert.doesNotMatch(flow, /saveVendor\(/u,
    'a consultant that added itself to the reviewer rows would be the coupling this plan removed');
});
