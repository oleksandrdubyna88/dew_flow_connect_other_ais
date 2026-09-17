import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as vscode from 'vscode';

/**
 * The scenarios that run INSIDE a real extension host, against the extension as it ships.
 *
 * <p><b>The gap this begins to close.</b> `research/module_tests.md` mentions an extension host
 * fourteen times, and twelve of those are table rows ending <i>"NOT covered … which is the row
 * below"</i>. That row says there is no harness here, calls it the largest single gap in the
 * repository, and names `@vscode/test-electron` as what would close it. This is that.</p>
 *
 * <p><b>What a scenario is FOR.</b> Not for anything a value can answer — this repository's
 * architecture is decisions-as-values with a thin host layer, and `sonarExclusions.test.ts` asserts
 * that half stays a small minority. A scenario earns its seconds only where a value cannot reach:
 * a control actually drawn, an event actually raised, an extension actually activated. Anything a
 * pure function can answer stays a pure function, or this suite gets slower for nothing.</p>
 *
 * <p><b>Why `node:test` is not used here.</b> The runner inside an extension host has to hand a
 * pass/fail back to the launcher through a thrown error, and it runs inside Electron's main-world
 * module system rather than a plain Node process. The fifteen lines below are a way of REPORTING,
 * not a second test framework: there is still exactly one runner in this repository, and the
 * Definition of Done in the tail plan pins that `@vscode/test-cli` — which would have brought mocha
 * — was not added.</p>
 */

type Scenario = { readonly name: string; readonly run: () => Promise<void> };

/** Every scenario, named explicitly. A glob that matches nothing is a pass, which is the whole trap. */
const SCENARIOS: readonly Scenario[] = [
  {
    name: 'the extension activates, and every command its manifest declares is really registered',
    run: async (): Promise<void> => {
      const extension = vscode.extensions.getExtension('remsoftdev.connect-other-ais');

      assert.ok(extension !== undefined,
        'the extension under test is not installed in this host, so nothing below is about it');
      await extension.activate();
      assert.equal(extension.isActive, true, 'the extension did not activate');

      // WHAT THE MANIFEST PROMISES, against what the host actually has. A command declared and never
      // registered is a menu item that does nothing, and no test outside a host can see the
      // difference: the manifest parses, the source contains a `registerCommand`, and the two can
      // still be about different names. `module_tests.md` has carried "the command through a real
      // extension host — NOT covered" on row after row for exactly this.
      const manifest = JSON.parse(
        fs.readFileSync(path.join(extension.extensionPath, 'package.json'), 'utf8'),
      ) as { contributes?: { commands?: readonly { readonly command: string }[] } };
      const declared = (manifest.contributes?.commands ?? []).map((one) => one.command);
      const registered = new Set(await vscode.commands.getCommands(true));

      assert.ok(declared.length > 0, 'the manifest declares no commands at all, so this proves nothing');
      const missing = declared.filter((name) => !registered.has(name));

      assert.deepEqual(missing, [],
        `declared in the manifest and never registered, so the menu item does nothing: ${missing.join(', ')}`);
    },
  },
];

/**
 * Run them, and FAIL LOUDLY when there is nothing to run.
 *
 * <p>The launcher decides pass or fail by whether this throws. A suite that quietly finds no
 * scenarios would therefore report success — a green tick over nothing, which is the exact shape of
 * the rate-limited review check this repository has already been caught by. So an empty list is an
 * error, not a pass.</p>
 */
export async function run(): Promise<void> {
  assert.ok(SCENARIOS.length > 0, 'no scenario was discovered, and an empty run is not a pass');

  const failures: string[] = [];
  for (const scenario of SCENARIOS) {
    try {
      await scenario.run();
      console.log(`  ok  ${scenario.name}`);
    } catch (reason) {
      failures.push(`${scenario.name}: ${reason instanceof Error ? reason.message : String(reason)}`);
      console.log(`  FAIL ${scenario.name}`);
    }
  }
  console.log(`  ${SCENARIOS.length - failures.length}/${SCENARIOS.length} scenarios passed`);
  if (failures.length > 0) {
    throw new Error(`extension-host scenarios failed:\n  - ${failures.join('\n  - ')}`);
  }
}
