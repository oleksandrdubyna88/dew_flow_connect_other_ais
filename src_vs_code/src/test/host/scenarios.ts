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
 * <p>Waiting for a change cannot prove an absence, so each attempt waits a fixed moment and reads
 * once. The window is generous next to a copy that lands in tens of milliseconds, and short enough
 * that a refusal does not cost five seconds of a run.</p>
 *
 * <p>It answers with the SENTENCE to fail with, or the empty string when nothing leaked — rather than
 * with the clipboard's contents — because the three outcomes are not one value: nothing happened, the
 * guard leaked, and somebody else wrote. A caller holding a string of text cannot tell them apart.</p>
 *
 * <p><b>And the scenario that uses this runs FIRST, which is part of the test rather than tidiness.</b>
 * `copyText.ts` re-asserts the newest text after a write it was overtaken during — a corrective write,
 * added because one could otherwise settle last and restore text already copied past. That means a
 * copier from an EARLIER scenario can still write after this one has laid down its sentinel, and it
 * did: an early run of this file had this scenario fail on a break that cannot reach it. A press that
 * is refused writes nothing at all, so with nothing copied before it there is no corrective write in
 * flight to race.</p>
 */
async function pressingCopiesNothing(act: () => void, mustNotBe: string): Promise<string> {
  // THREE ATTEMPTS, because the clipboard is one per MACHINE and this is the only scenario asserting
  // that nothing reached it. Any write by anyone inside the window breaks that — a clipboard manager,
  // a person pressing Ctrl+C, or a second `npm run test:host` from another checkout, which is what
  // actually happened. Measured 2026-09-18: one run passes and two pass; THREE concurrent runs
  // reproduce it, and this is the only one of the three clipboard scenarios that breaks, because the
  // other two wait for a change they can RECOGNISE and this one waited for a change that must not
  // come from anywhere.
  //
  // So interference is RETRIED and a leak is not. A refused press writes nothing, so a re-press costs
  // nothing and the next attempt usually lands in a quiet window. What must never be retried away is
  // the real defect, which is why the answer's own text fails on sight.
  let interference = '';
  for (let attempt = 0; attempt < 3; attempt += 1) {
    await vscode.env.clipboard.writeText(UNTOUCHED);
    act();
    await new Promise((settle) => { setTimeout(settle, 1500); });
    const held = await vscode.env.clipboard.readText();
    if (held === UNTOUCHED) {
      return '';
    }
    if (held === mustNotBe) {
      return 'a press whose answer had gone still reached the clipboard, so the guard is not in the path';
    }
    interference = held.slice(0, 50);
  }

  // Three windows in a row spoiled. NOT passed — a scenario that cannot observe anything must not
  // report success, which is what a green tick over nothing looks like. Named so the reader re-runs it
  // alone instead of going to look at the guard.
  return 'could not be measured here: something outside this run wrote to the clipboard in all three '
    + `attempts (${interference}). Another test:host, or a clipboard manager. Run it alone.`;
}

/**
 * Wait for something to become true, or say what did not happen.
 *
 * <p>Bounded, and the sentence is the caller's: a scenario that hangs until the launcher's own
 * ten-minute budget expires reports "the extension host did not finish", which names the harness
 * rather than the defect.</p>
 */
async function until(ready: () => boolean, whatDidNotHappen: string, ms = 15_000): Promise<void> {
  const deadline = Date.now() + ms;
  while (Date.now() < deadline) {
    if (ready()) {
      return;
    }
    await new Promise((wake) => setTimeout(wake, 100));
  }
  assert.fail(`${whatDidNotHappen} (waited ${Math.round(ms / 1000)}s)`);
}

/**
 * The settings file's text, or `undefined` in the instant it is being replaced.
 *
 * <p><b>Why a scenario needs this.</b> The thing under test is a writer whose whole job is to
 * REPLACE this file, and it publishes by renaming over it. `vscode.workspace.fs.rename(…,
 * { overwrite: true })` is not documented as atomic — the note in `extension.ts` beside the lock
 * says the disk provider checks for existence and then renames — so a reader can land in the
 * instant between. A bare `readFileSync` then throws `ENOENT` and the whole scenario fails with a
 * raw errno instead of an assertion, which is what it did on 2026-09-18 and what has been resetting
 * the twenty-run promotion streak this job is counting towards.</p>
 *
 * <p>An absent file is NEVER "ready", in either direction. That is the property that keeps this from
 * weakening anything: were it read as empty text, the final wait — that the marker is GONE — would
 * be satisfied by the file simply not being there for a moment, which is the opposite of what it
 * asserts.</p>
 */
function settingsTextNow(file: string): string | undefined {
  try {
    return fs.readFileSync(file, 'utf8');
  } catch (reason) {
    if ((reason as NodeJS.ErrnoException).code === 'ENOENT') {
      return undefined;
    }

    throw reason;
  }
}

/** Waits for the settings file to EXIST and to say something, and hands back what it said. */
async function settingsUntil(
  file: string,
  ready: (text: string) => boolean,
  whatDidNotHappen: string,
): Promise<string> {
  let seen = '';
  await until(() => {
    const text = settingsTextNow(file);
    if (text === undefined) {
      return false;
    }

    seen = text;

    return ready(text);
  }, whatDidNotHappen);

  return seen;
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

      // The second argument is what a LEAKING guard would have put there — the person's own turn.
      // Anything else on the clipboard came from outside this run; see `pressingCopiesNothing`.
      const wrong = await pressingCopiesNothing(() => {
        hooks.onCopyAnswer?.(id, 1, signatureOf(ANSWER));
      }, 'and one more thing');

      assert.equal(wrong, '', wrong);
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
  {
    name: 'a changed setting reaches the file the server reads, written by the extension itself',
    run: async (): Promise<void> => {
      // THE FLOW THIS STORY OWNS, end to end in a real editor. Everything else about the mirror is
      // tested as a value - the schedule against an injected clock, the payload against a pure
      // builder - and the one thing no value can answer is whether a person changing a setting in
      // THIS editor makes this extension write the file. That is the whole chain: the configuration
      // listener, the schedule, the lock, the rename. The 2026-09-16 incident lived inside it.
      const home = process.env['COAI_DATA_DIR'] ?? '';

      assert.ok(home.length > 0, 'the launcher did not give this host a data directory of its own');
      const extension = vscode.extensions.getExtension('remsoftdev.connect-other-ais');

      assert.ok(extension !== undefined, 'the extension under test is not installed in this host');
      await extension.activate();

      const file = path.join(home, 'settings.json');
      const config = (): vscode.WorkspaceConfiguration => vscode.workspace.getConfiguration('coai');

      // Activation mirrors once, so the file is the proof that the mirror ran at all.
      const before = await settingsUntil(
        file, () => true, 'the extension never wrote a settings file at all');

      assert.ok(!before.includes('COAI_DEAL_PLAN'), 'the setting this scenario changes is already on');
      try {
        await config().update('dealPlanLenses', true, vscode.ConfigurationTarget.Global);
        await settingsUntil(
          file,
          (text) => text.includes('COAI_DEAL_PLAN'),
          'the setting was changed and the file the server reads never followed, which is '
          + 'the defect of this story, in the one place no unit test can look',
        );
      } finally {
        await config().update('dealPlanLenses', undefined, vscode.ConfigurationTarget.Global);
      }
      await settingsUntil(
        file,
        (text) => !text.includes('COAI_DEAL_PLAN'),
        'returning the setting to its default left the old value in the file',
      );
    },
  },
  {
    name: 'a deletion the mirror carries takes the prompt text with it, in a real host',
    run: async (): Promise<void> => {
      // The flow this story owns, driven where it actually lives. Everything decidable about a
      // deletion is a value and is RUN against a map and an injected clock; what no value can
      // answer is whether the REAL mirror, in a REAL editor, tells the REAL coordinator — the
      // configuration listener, the schedule, the terminal callback, the claim on a real
      // filesystem, and the unlink.
      //
      // What it does NOT drive is the webview press, and the FAILURE path. A stood-down mirror
      // inside a real host needs a second build's stamp in the settings file, and that is a
      // scenario of its own.
      const home = process.env['COAI_DATA_DIR'] ?? '';

      assert.ok(home.length > 0, 'the launcher did not give this host a data directory of its own');
      const extension = vscode.extensions.getExtension('remsoftdev.connect-other-ais');

      assert.ok(extension !== undefined, 'the extension under test is not installed in this host');
      await extension.activate();

      const role = 'HostRole';
      const prompt = path.join(home, 'prompts', 'hostrole-general.md');
      const tombstone = path.join(home, 'deletions', `${role}.json`);
      const settings = path.join(home, 'settings.json');
      const config = (): vscode.WorkspaceConfiguration => vscode.workspace.getConfiguration('coai');

      fs.mkdirSync(path.dirname(prompt), { recursive: true });
      fs.mkdirSync(path.dirname(tombstone), { recursive: true });
      fs.writeFileSync(prompt, 'Prose a person wrote.', 'utf8');
      fs.writeFileSync(tombstone, JSON.stringify({
        roleId: role,
        name: 'A host role',
        promptIds: ['hostrole-general'],
        askedAt: new Date().toISOString(),
        nonce: 'scenario',
        reason: '',
        failedAt: '',
      }), 'utf8');

      try {
        // The row goes in first, so the payload the mirror carries mentions it and the deletion is
        // refused — which is the guard, exercised rather than assumed.
        await config().update('roles', [{
          id: role,
          name: 'A host role',
          stage: 'code',
          programmingTask: true,
          active: false,
          prompts: [{ id: 'hostrole-general', label: 'General', purpose: '' }],
        }], vscode.ConfigurationTarget.Global);
        await until(
          () => fs.existsSync(settings) && fs.readFileSync(settings, 'utf8').includes(role),
          'the role never reached the file the server reads, so nothing below is about a deletion',
        );

        assert.ok(fs.existsSync(prompt),
          'the text was deleted while the server still had the role, which is the incident itself');

        // And now the removal: the row leaves, the mirror carries it, and the coordinator is told.
        await config().update('roles', [], vscode.ConfigurationTarget.Global);
        await until(
          () => !fs.existsSync(prompt),
          'the mirror carried the removal and the prompt text was never deleted',
        );
        await until(() => !fs.existsSync(tombstone), 'the deletion finished and left its tombstone behind');
      } finally {
        await config().update('roles', undefined, vscode.ConfigurationTarget.Global);
        fs.rmSync(prompt, { force: true });
        fs.rmSync(tombstone, { force: true });
      }
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
