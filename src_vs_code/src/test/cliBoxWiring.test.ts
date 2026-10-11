import assert from 'node:assert/strict';
import { test } from 'node:test';
import * as fs from 'node:fs';
import * as path from 'node:path';

/**
 * That a CLI-path box's save is WIRED through the one vendor write, refusal first — the half the page tests and the
 * host scenario cannot reach (todo/PLAN_paths_per_side.md E4.1; E4's code round, finding 1).
 *
 * <p>`twoCliPathFieldsOnTheCard.test.ts` RUNS the page: the box posts `executablePath.<family>`, and the write that
 * message parses into, given to `vendorFieldWrite`, saves only that side's member. `twoPathFieldsScenario.ts` proves the
 * mirror then writes this side's value for the server. What neither can see is the panel provider's `vendor` case in
 * between — it imports `vscode`, and a host cannot type into a webview — so a provider that stopped refusing the
 * other side's box, or saved it around `vendorFieldWrite`, would pass both. The source is pinned, the house pattern for
 * host-only wiring (see `chatFreshWiring.test.ts`).</p>
 */

const source = (file: string): string =>
  fs.readFileSync(path.join(__dirname, '..', '..', 'src', file), 'utf8');

/** The provider's `vendor` case, up to the next case — so an assertion cannot match a neighbour. */
function vendorCase(): string {
  const text = source('panelProvider.ts');
  const from = text.indexOf("case 'vendor': {");
  const to = text.indexOf("case 'role': {", from);
  assert.ok(from >= 0 && to > from, 'the provider has no vendor case to pin');

  return text.slice(from, to);
}

test('a CLI-path box is refused BESIDE the box before anything is saved, and a refused write saves nothing', () => {
  const body = vendorCase();
  const refusal = body.indexOf('if (await this.refusedBesideTheBox(write, from, config)) {');
  const save = body.indexOf("await this.saveWrite(config, 'vendors', vendors, write, from);");

  assert.ok(refusal >= 0, 'the vendor case no longer asks whether the other side\'s box refuses the text');
  assert.ok(save > refusal, 'the vendor case saves before it asks whether the box refuses the text');
  assert.match(body.slice(refusal, refusal + 120), /\{\s*return;\s*\}/u, 'a refused box falls through to the save');
});

test('every vendor field — a CLI-path box of either side included — is saved through vendorFieldWrite, with this host\'s family', () => {
  const body = vendorCase();

  assert.match(body, /\.\.\.vendorFieldWrite\(v, write\.key, write\.value, hostFamily\(\)\)/u,
    'the vendor case saves a field around the one vendor write — the other side\'s box would reach the legacy value');
  assert.doesNotMatch(body, /executablePath:\s*write\.value/u, 'the vendor case writes a CLI path into the legacy field itself');
});
