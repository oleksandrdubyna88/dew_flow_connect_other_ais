import '../../scripts/inRunFolder.cjs';
import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { blanked } from './blankedSource';

/**
 * The host's half of the sidebar's first paint, as WIRED in `PanelProvider` (v2 E2).
 *
 * <p>Structural, for the reason `busyMarkWiring.test.ts` gives: `PanelProvider` imports `vscode` and no unit test here can construct it. What the
 * stages DO is run in `theSidebarPaintsFirst.test.ts` — the order, the page, the fill. What is read here is only that the provider hands the stages
 * what they need and nothing slow: the first stage's awaits are two, none of them a spawn, a network call or a probe; and the render calls the
 * stages before it reads anything else. A read added to the first stage would be a first paint that waits on it again.</p>
 */

const source = blanked(fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'panelProvider.ts'), 'utf8'));

/** One method's body, found by its signature — a search that finds nothing fails, never slices from -1. */
function body(signature: string): string {
  const at = source.indexOf(signature);
  assert.ok(at >= 0, `${signature} could not be found, so this proves nothing`);
  const rest = source.slice(at);
  const end = rest.search(/\r?\n {2}\}\r?\n/u);

  return rest.slice(0, end >= 0 ? end : undefined);
}

test('the first stage awaits a read of local session files and the host kind — and nothing else', () => {
  const awaited = [...body('private async sidebarKnown(').matchAll(/await ([^;,\n]+)/gu)].map((one) => one[1]!.trim());

  assert.deepEqual(awaited, ['this.readSessions()', 'this.hostKindHere'],
    'the first paint of the sidebar waits on something the list does not name');
  assert.doesNotMatch(body('private rankByRuntimeKnown('), /await|binaryFeatures/u, 'the cached ranking rule asks the binary');
  assert.match(body('private async sidebarKnown('), /rankByRuntime: this\.rankByRuntimeKnown\(\)/u, 'the first stage does not use the cached ranking rule');
});

test('the render paints the sidebar before it reads the models, the published version or the prices, and reuses what the fill read', () => {
  const render = body('private async renderNow(number: number): Promise<void> {');
  const staged = render.indexOf('await this.paintSidebarFirst(');

  assert.ok(staged >= 0, 'the render never paints the sidebar first');
  for (const slow of ['await this.readCodexModels()', 'await this.publishedVersion()', 'await this.cardPrices(', 'await this.vendorCliStatus(']) {
    const at = render.indexOf(slow);
    assert.ok(at > staged, `${slow} comes before the sidebar's first paint`);
  }
  assert.match(render, /staged\?\.bugz \?\? await this\.bugz\(\)/u, 'the corpus is read twice');
  assert.match(render, /staged\?\.localEngines \?\? await this\.probeLocalEngines\(vendors\)/u, 'the local engines are probed twice');
});

test('the first paint goes to the sidebar alone, and a new document is unpainted', () => {
  assert.match(body('private paintSidebarOnly('), /paintEach\(\[this\.sidebar\], /u, 'the Settings page would be painted from the stand-in state');
  assert.match(body('resolveWebviewView(view: vscode.WebviewView): void {'), /this\.sidebarPainted = false;/u,
    'a re-created view is taken for a painted one and is never given its first paint');
});
