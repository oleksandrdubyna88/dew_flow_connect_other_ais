import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as vscode from 'vscode';
import { ChatPanels } from '../../chatPanels';
import { conversationHooks } from '../../chatHooks';
import { signatureOf } from '../../renderAnswer';
import { threads, type Thread } from '../../chatThread';

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

const FENCE = '```';

/**
 * An answer with two blocks in it, so "the whole answer" and "one block of it" are different strings.
 *
 * <p>If they were the same, a control wired to the wrong entry point would copy the right text by
 * accident and this whole file would prove nothing.</p>
 */
const ANSWER = [
  'Before the blocks.',
  '',
  `${FENCE}ts`,
  'const first = 1;',
  FENCE,
  '',
  `${FENCE}sh`,
  'echo second',
  FENCE,
].join('\n');

/** A sentinel that is on the clipboard BEFORE every press, so "nothing happened" is visible. */
const UNTOUCHED = 'coai-host-scenario: nothing was copied';

/**
 * A conversation the hooks can find, holding one question and one answer.
 *
 * <p>Cast rather than built whole: `Thread` has thirty-nine members and the copy path reads exactly
 * one of them, `messages`. `chatHost.test.ts` already does this (`{ title } as unknown as Thread`)
 * and this follows it rather than inventing a second shape. The cast is a promise to maintain by
 * hand, per the TypeScript doctrine — the promise here is small and stated: if the copy path ever
 * reads a second field, this scenario throws rather than lying.</p>
 */
function conversationHolding(messages: readonly { role: 'you' | 'model'; text: string }[]): object {
  const id = {};
  threads.set(id, { messages } as unknown as Thread);

  return id;
}

/**
 * Wait for the clipboard to stop saying the sentinel, or give up and say so.
 *
 * <p>The hooks return `void` — the copy is a promise the host consumes internally, and there is no
 * handle to await from out here. That is not a gap in the test: it is what a real press is, and
 * polling the clipboard is what a person watching the screen does. The deadline is what turns a
 * silent hang into a named failure.</p>
 */
async function copiedWithin(ms: number): Promise<string> {
  const until = Date.now() + ms;
  for (;;) {
    const held = await vscode.env.clipboard.readText();
    if (held !== UNTOUCHED) {
      return held;
    }
    if (Date.now() > until) {
      return UNTOUCHED;
    }
    await new Promise((settle) => { setTimeout(settle, 25); });
  }
}

/** Every press starts from a known clipboard, so what lands on it is this press and not the last one. */
async function pressing(act: () => void): Promise<string> {
  await vscode.env.clipboard.writeText(UNTOUCHED);
  act();

  return copiedWithin(5000);
}

/**
 * The same, for a press that must reach the clipboard NOT AT ALL.
 *
 * <p>Waiting for a change cannot prove an absence, so this waits a fixed moment and reads once. The
 * window is generous next to a copy that lands in tens of milliseconds, and short enough that a
 * refusal does not cost five seconds of a run.</p>
 *
 * <p><b>And the scenario that uses this runs FIRST, which is part of the test rather than tidiness.</b>
 * `copyText.ts` re-asserts the newest text after a write it was overtaken during — a corrective write,
 * added because one could otherwise settle last and restore text already copied past. That means a
 * copier from an EARLIER scenario can still write after this one has laid down its sentinel, and it
 * did: an early run of this file had this scenario fail on a break that cannot reach it. A press that
 * is refused writes nothing at all, so with nothing copied before it there is no corrective write in
 * flight to race.</p>
 */
async function pressingCopiesNothing(act: () => void): Promise<string> {
  await vscode.env.clipboard.writeText(UNTOUCHED);
  act();
  await new Promise((settle) => { setTimeout(settle, 1500); });

  return vscode.env.clipboard.readText();
}

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
  {
    // The guard, wired. `answerCopy.test.ts` proves it refuses; this proves the refusal is what the
    // real press reaches. A hook that called the decision and then copied anyway would pass every
    // test in that file.
    name: 'a press on an answer the conversation has moved past copies nothing at all',
    run: async (): Promise<void> => {
      // The message at index 1 is the PERSON's turn now, which is the race the guard exists for: the
      // conversation moved on while the press was queued.
      const id = conversationHolding([
        { role: 'you', text: 'ask' },
        { role: 'you', text: 'and one more thing' },
      ]);
      const hooks = conversationHooks(new ChatPanels());

      const held = await pressingCopiesNothing(() => {
        hooks.onCopyAnswer?.(id, 1, signatureOf(ANSWER));
      });

      assert.equal(held, UNTOUCHED,
        'a press whose answer had gone still reached the clipboard, so the guard is not in the path');
    },
  },
  {
    // THE WIRING, which is the one thing no test outside a host can reach. `answerCopy.test.ts`
    // proves both entry points behave, and `chatHooks.ts` cannot be imported by that suite at all —
    // it takes `vscode` on line 3 — so which hook calls which entry point was covered by NOTHING.
    // Swapping them is a compile error since the entries stopped taking the same arguments, but a
    // type is not a test: a hook that resolved the wrong INDEX, forgot to call the copier, or
    // dropped the promise would compile perfectly and copy the wrong thing or nothing at all.
    //
    // In here `vscode` is real, so `conversationHooks` can simply be called, and the clipboard it
    // writes to is the actual system clipboard rather than a fake.
    name: 'a press on the ANSWER control puts that whole answer on the real clipboard',
    run: async (): Promise<void> => {
      const id = conversationHolding([{ role: 'you', text: 'ask' }, { role: 'model', text: ANSWER }]);
      const hooks = conversationHooks(new ChatPanels());

      const held = await pressing(() => {
        hooks.onCopyAnswer?.(id, 1, signatureOf(ANSWER));
      });

      assert.equal(held, ANSWER,
        'the answer control did not put the whole answer on the clipboard — it is wired to the wrong '
        + 'decision, or it resolved the wrong message');
    },
  },
  {
    name: 'a press on a BLOCK control puts only that block on the real clipboard',
    run: async (): Promise<void> => {
      const id = conversationHolding([{ role: 'you', text: 'ask' }, { role: 'model', text: ANSWER }]);
      const hooks = conversationHooks(new ChatPanels());

      // Block 1, the SECOND fence, deliberately — block 0 is what a handler that ignored the ordinal
      // would copy, so asking for the second is the only version of this that can fail.
      const held = await pressing(() => {
        hooks.onCopyBlock?.(id, 1, 1, signatureOf(ANSWER));
      });

      assert.equal(held, 'echo second',
        'the block control copied something other than the block it was pressed on');
      assert.notEqual(held, ANSWER,
        'the block control copied the WHOLE answer, which is what being wired to the other control '
        + 'looks like from the clipboard');
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
