import assert from 'node:assert/strict';
import { test } from 'node:test';
import * as fs from 'node:fs';
import * as path from 'node:path';

/**
 * That the host REFUSES a write to the other side's box while this side keeps its own settings (research/PLAN_paths_per_side.md,
 * E4's own review, item 1): with `coai.perSideSettings` on, `vendors` and `qconsultRoots` are saved into THIS side's
 * own settings, which the other side never reads — so the other side's CLI box and typed folder box are not drawn, and a
 * page drawn before the switch moved cannot save one either.
 *
 * <p>The page tests RUN the page (`twoCliPathFieldsOnTheCard.test.ts`, `theFoldersOfTwoSides.test.ts`) and the pure
 * refusal (`cliPathRefusal`'s `ownSettings`, `keptPerSide`) is tested there. What neither reaches is the provider and
 * `QconsultHost` handing the decision in — both import `vscode` — so the source is pinned, the house pattern for host-only
 * wiring (see `cliBoxWiring.test.ts`).</p>
 */

const source = (file: string): string =>
  fs.readFileSync(path.join(__dirname, '..', '..', 'src', file), 'utf8');

/** A method of a class in `file`, from its signature to the end of its body (the first line that closes a member). */
function methodOf(file: string, signature: string): string {
  const text = source(file);
  const from = text.indexOf(signature);
  assert.ok(from >= 0, `${file} has no ${signature}`);
  const end = /\r?\n {2}\}\r?\n/u.exec(text.slice(from));
  assert.ok(end !== null, `${file}: ${signature} has no end`);

  return text.slice(from, from + end.index);
}

test('while this side keeps its own CLI paths the other side\'s box is refused — the refusal is asked whether `vendors` is saved into this side\'s own settings', () => {
  assert.match(source('panelProvider.ts'), /if \(await this\.refusedBesideTheBox\(write, from, config\)\) \{/u,
    'the vendor case does not hand the refusal the configuration — it cannot know this side keeps its own settings');
  assert.match(methodOf('panelProvider.ts', 'private async refusedBesideTheBox('),
    /cliPathRefusal\(write\.key, write\.value, hostFamily\(\), writesTheOverlay\(config, 'vendors'\)\)/u,
    'the other side\'s box is saved into this side\'s own settings, which that side never reads');
});

test('while this side keeps its own folders a folder typed for the other side is refused before the disk is asked or anything saved', () => {
  const typed = methodOf('qconsultHost.ts', 'private async typedRoot(');

  assert.ok(typed.includes("hooks.keptHere('qconsultRoots')"),
    'the typed root is not refused while this side keeps its own folders — it is saved into this side\'s own settings, which that side never reads');
  assert.match(typed, /const verdict = hooks\.keptHere\('qconsultRoots'\) \? keptHere\(root\) : await this\.typedVerdict\(root, settings\);/u,
    'the typed root is asked of the disk, or admitted, before it is asked whether this side keeps its own folders');
  assert.match(methodOf('panelProvider.ts', 'private qconsultHooks('), /keptHere: \(key\) => writesTheOverlay\(config, key\)/u,
    'the provider does not tell the question consultant which keys are saved into this side\'s own settings');
});
