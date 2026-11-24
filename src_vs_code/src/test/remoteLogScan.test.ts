import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { bodyOf, sourceFiles, sourceOf } from './sourceReading';

/**
 * No stdout, no operand and no caught error in any log (E2.S2, S2-T10; parent § Limits).
 *
 * <p>The rule is written for every file named `src/remote*.ts`, so S3's `remoteRead.ts` and `remoteSessionHost.ts` are scanned
 * from the day they exist; the door functions in other files are S4's part (X10 of the spec, which adds the companion that the
 * scan FINDS the two terminal registrations and the Asked hook). A scan that matches nothing passes for ever, so the scanner
 * is proved on samples first, and the tree scan has companions that say it looked at something.</p>
 */

/** The words that must not be in the arguments of a console call or a `notify`: the child's stdout and what is cut from it, the operands, an error's message. */
const BANNED = /\b(stdout|output|lines|tail|operands|folders|folder|paneCwd|cwd|candidates|names|rows|script|reason)\b|\.message|asText\(/u;

/** The text of the argument list that opens at `open` (the index of the `(`), strings included; or the rest of the text when it never closes. */
function argumentsFrom(text: string, open: number): string {
  let depth = 0;
  let quote = '';
  for (let at = open; at < text.length; at += 1) {
    const char = text[at];
    if (quote !== '') {
      if (char === '\\') {
        at += 1;
      } else if (char === quote) {
        quote = '';
      }
    } else if (char === '\'' || char === '"' || char === '`') {
      quote = char;
    } else if (char === '(') {
      depth += 1;
    } else if (char === ')' && (depth -= 1) === 0) {
      return text.slice(open + 1, at);
    }
  }

  return text.slice(open + 1);
}

/** Every `console.<name>(`, `notify(`, `logged(` and `seams.log(` call (the last two are the remote reader's own log road) of a text whose arguments hold a banned word — each reported as the call's own text. Across lines. */
function logLeaks(text: string): readonly string[] {
  const calls = [...text.matchAll(/\b(?:console\.[A-Za-z]+|notify|logged|seams\.log)\(/gu)];

  return calls
    .map((call) => ({ call: call[0], args: argumentsFrom(text, (call.index ?? 0) + call[0].length - 1) }))
    .filter(({ args }) => BANNED.test(args))
    .map(({ call, args }) => `${call}${args.trim().slice(0, 80)}…`);
}

test('S2-T10: the scanner finds what it must, on samples', () => {
  assert.equal(logLeaks("console.error('x', run.stdout);").length, 1, 'stdout in a console call');
  assert.equal(logLeaks(`notify({
    kind: 'error',
    detail: asText(reason),
  });`).length, 1, 'a three-line notify with a caught error');
  assert.equal(logLeaks('console.error(`${operands}`);').length, 1, 'an operand in a template literal');
  assert.equal(logLeaks('console.warn(error.message);').length, 1, 'an error\'s message');
  assert.equal(logLeaks('logged(seams, got.output);').length, 1, 'stdout through the remote reader\'s log road');
  assert.equal(logLeaks('seams.log(`${operands}`);').length, 1, 'an operand through the log seam');
  assert.equal(logLeaks('console.error("a (b) c", folder);').length, 1, 'a banned word after a string that holds parentheses');

  assert.deepEqual(logLeaks('seams.log(logLine(host, session, end, stderrTail));'), [], 'the one log call of the remote reader');
  assert.deepEqual(logLeaks('logged(seams, BAD_OPERAND_LOG);'), [], 'a fixed line through the log road');
  assert.deepEqual(logLeaks("console.error('coai.takeFromTerminal failed');"), [], 'a fixed line');
  assert.deepEqual(logLeaks('console.error(line);'), [], 'the log seam\'s default');
  assert.deepEqual(logLeaks('const tail = 1; call(tail);'), [], 'a banned word outside a console call or a notify');
});

const remoteFiles = (): readonly string[] => sourceFiles().filter((file) => /^remote.*\.ts$/u.test(file));

test('S2-T10: no console call or notify of a src/remote*.ts file holds stdout, an operand or a caught error', () => {
  const files = remoteFiles();
  assert.ok(files.length >= 3, `the scan looked at ${files.length} files`);
  for (const file of files) {
    assert.deepEqual(logLeaks(sourceOf(file)), [], `${file} writes stdout, an operand or an error's message to a log`);
  }
});

test('S2-T10 companions: the scan finds the three S2 modules, and the one console call is the log seam\'s default', () => {
  const files = remoteFiles();
  for (const wanted of ['remoteScript.ts', 'remoteFrame.ts', 'remoteSsh.ts']) {
    assert.ok(files.includes(wanted), `the name pattern does not find ${wanted}`);
  }
  const three = ['remoteScript.ts', 'remoteFrame.ts', 'remoteSsh.ts'].map((file) => sourceOf(file)).join('\n');
  assert.equal(three.split('console.').length - 1, 1, 'console. must be in the three S2 modules exactly once (the default log seam)');
  assert.ok(sourceOf('remoteSsh.ts').includes('log: (line) => console.error(line)'), 'the one console call is not the default log seam');

  const ssh = sourceOf('remoteSsh.ts');
  assert.equal(ssh.split('logLine(').length - 1, 2, 'logLine( must be declared once and called once');
  const declared = /function logLine\(([^)]*)\)/u.exec(ssh);
  assert.deepEqual(declared?.[1]?.split(',').map((parameter) => parameter.split(':')[0]?.trim()), ['host', 'session', 'end', 'stderrTail'], 'logLine does not take exactly host, session, end, stderrTail');
});

// ───────────────── S4-T11: the doors' catches and the Asked hook, scanned with the same scanner (X10) ─────────────────

/** A command's registration in the chat module's `register.ts` (v2 E4): from its `registerCommand('<id>'` to the next registration. */
function registrationOf(command: string): string {
  const text = sourceOf('../shell/modules/chat/register.ts');
  const at = text.indexOf(`registerCommand('${command}'`);
  assert.ok(at >= 0, `the chat registration registers no ${command}, so the scan has nothing to look at`);
  const next = text.indexOf('registerCommand(', at + 20);

  return text.slice(at, next < 0 ? text.length : next);
}

/** The terminal branch of the Asked hook: the whole `onShowAsked` hook and the function that reads the remote prompts. */
const askedBranches = (): readonly string[] => {
  const text = sourceOf('chatHooks.ts');

  return [/onShowAsked:[\s\S]*?onStop:/u.exec(text)?.[0] ?? '', bodyOf(text, 'async function askedFromTerminal(')];
};

test('S4-T11: the catches of the two terminal doors and the Asked branch carry no error message and no stdout', () => {
  for (const command of ['coai.takeFromTerminal', 'coai.addFromTerminal']) {
    assert.deepEqual(logLeaks(registrationOf(command)), [], `${command} writes an error's message, stdout or an operand to a log or a notice`);
  }
  for (const branch of askedBranches()) {
    assert.deepEqual(logLeaks(branch), [], 'the Asked branch of a terminal writes an error\'s message, stdout or an operand to a log');
  }
});

test('S4-T11: the bodies of takeFromTerminal and addFromTerminal pass no stdout, operand or error to a notice', () => {
  // The refusal sentence the door shows is the road's own (it can name the remote pane folder, and a picked id), and notify keeps its title in the
  // notifications ledger: so what may reach that call is the sentence and nothing else. A scan of the bodies, with its companion.
  const text = sourceOf('chatCommand.ts');
  const bodies = ['export async function takeFromTerminal(', 'export async function addFromTerminal('].map((opening) => bodyOf(text, opening));

  for (const body of bodies) {
    assert.deepEqual(logLeaks(body), [], 'a terminal door passes stdout, lines, an operand or an error to a notice');
  }
  assert.ok(/\bnotify\(/u.test(bodies[0] ?? ''), 'takeFromTerminal has no notify call, so the scan of it matches nothing');
  assert.ok((bodies[0] ?? '').includes('question.refusal'), 'the sentence the notice carries is no longer the road\'s refusal');
});

test('S4-T11 companions: the scan finds the two registrations and the Asked branch, and a console call and a notice in each', () => {
  for (const command of ['coai.takeFromTerminal', 'coai.addFromTerminal']) {
    const handler = registrationOf(command);
    assert.ok(/console\.error\(/u.test(handler), `${command} has no console.error call, so the scan of it matches nothing`);
    assert.ok(/\bnotify\(/u.test(handler), `${command} has no notify call, so the scan of it matches nothing`);
    assert.ok(handler.includes('FromTerminal(chatPanels'), `${command} no longer reaches its door`);
  }
  const [hook, helper] = askedBranches();
  assert.ok(hook?.includes('askedFromTerminal('), 'the Asked hook no longer calls the terminal branch');
  assert.ok(helper?.includes('readRemoteSession('), 'the Asked terminal branch no longer reads the remote session');
  assert.ok(/console\.error\(/u.test(helper ?? ''), 'the Asked terminal branch has no console.error call, so the scan of it matches nothing');
  // The scanner itself reaches this shape: a terminal door's catch that put the error back.
  assert.equal(logLeaks(`registerCommand('coai.takeFromTerminal', () => { takeFromTerminal(a).catch((reason: unknown) => { void notify({ detail: asText(reason) }); }); })`).length, 1, 'the scanner misses a terminal door that logs an error');
});

// ───────────────── A2-T21: the four accounts modules (E1.S2) ─────────────────

const ACCOUNTS_MODULES = ['remoteFraming.ts', 'remoteAccountsScript.ts', 'remoteAccountsUsage.ts', 'remoteAccountsReply.ts'];

/** What a pure parser module must not hold, each with its name: a log or a notice, a message that carries data, the editor, a process, a file, code built at run time. */
const FORBIDDEN: ReadonlyArray<readonly [string, RegExp]> = [
  ['a console call', /\bconsole\./u],
  ['a notify call', /\bnotify\(/u],
  ['an error whose message is a template literal', /throw new Error\(`/u],
  ['an import or require of vscode, node:child_process or node:fs', /(?:from|require\()\s*'(?:vscode|node:child_process|child_process|node:fs|fs|node:fs\/promises)'/u],
  ['eval', /\beval\(/u],
  ['new Function', /\bnew Function\b/u],
];

const forbiddenIn = (text: string): readonly string[] => FORBIDDEN.filter(([, pattern]) => pattern.test(text)).map(([name]) => name);

test('A2-T21: the scan finds each forbidden thing in a sample, so a scan that matches nothing is seen', () => {
  const samples: Readonly<Record<string, string>> = {
    'a console call': 'console.error(x);',
    'a notify call': 'void notify({ title: t });',
    'an error whose message is a template literal': 'throw new Error(`bad ${x}`);',
    'an import or require of vscode, node:child_process or node:fs': "import * as vscode from 'vscode';",
    eval: 'eval(code);',
    'new Function': 'const f = new Function("a");',
  };
  assert.deepEqual(Object.keys(samples).sort(), FORBIDDEN.map(([name]) => name).sort(), 'a forbidden thing has no sample');
  for (const [name, sample] of Object.entries(samples)) {
    assert.deepEqual(forbiddenIn(sample), [name], `the scan does not find ${name} in its sample`);
  }
  assert.deepEqual(forbiddenIn("import { framed } from './remoteFraming';"), [], 'an ordinary import is not forbidden');
  assert.deepEqual(forbiddenIn("const fs = require('node:fs');").length, 1, 'a require of node:fs is forbidden');
});

test('A2-T21: the name pattern finds the four accounts modules, and they hold no log, notice, editor, process, file or run-time code', () => {
  const files = remoteFiles();
  for (const wanted of ACCOUNTS_MODULES) {
    assert.ok(files.includes(wanted), `the name pattern does not find ${wanted}`);
    assert.deepEqual(logLeaks(sourceOf(wanted)), [], `${wanted} writes stdout, an operand or an error's message to a log`);
    assert.deepEqual(forbiddenIn(sourceOf(wanted)), [], `${wanted} holds something a pure parser module must not`);
  }
});

// ───────────────── A3-T28: one rule takes control and format characters out (E1.S3; S2 X12) ─────────────────

/** The character class of the plain rule, written as source text: control and format characters. */
const PLAIN_CLASS = String.raw`[\p{Cc}\p{Cf}]`;

/** The files of `src/*.ts` (tests are in `src/test`, so none is here) that hold the class. */
const filesWithPlainClass = (names: readonly string[], read: (name: string) => string): readonly string[] => names.filter((name) => read(name).includes(PLAIN_CLASS));

test('A3-T28 companion: the scan finds the class in a sample, and finds nothing in text without it', () => {
  assert.deepEqual(filesWithPlainClass(['a.ts', 'b.ts'], (name) => (name === 'a.ts' ? `x.replace(/${PLAIN_CLASS}/gu, '')` : 'const none = 1;')), ['a.ts']);
  assert.deepEqual(filesWithPlainClass(['b.ts'], () => 'const none = 1;'), []);
});

test('A3-T28: the plain rule stands in ONE file, and the two older copies call it', () => {
  assert.deepEqual(filesWithPlainClass(sourceFiles(), sourceOf), ['remoteAccountsReply.ts'], 'the plain rule stands in two files');
  for (const file of ['remoteSsh.ts', 'remoteRead.ts']) {
    assert.match(sourceOf(file), /import\s*\{[^}]*\bwithoutControls\b[^}]*\}\s*from\s*'\.\/remoteAccountsReply'/u, `${file} does not import withoutControls from ./remoteAccountsReply`);
  }
  assert.ok(!/from\s*'\.\/remote(?:Ssh|Read)'/u.test(sourceOf('remoteAccountsReply.ts')), 'remoteAccountsReply.ts imports a file that imports it: an import circle');
});

// ───────────────── A4-T19: the scan reaches the files of the accounts list (E1.S4) ─────────────────

test('A4-T19: the log scan reaches the rows and the host of the accounts list, and both are clean', () => {
  const files = remoteFiles();
  for (const wanted of ['remoteAccountsRows.ts', 'remoteAccountsHost.ts']) {
    assert.ok(files.includes(wanted), `the name pattern does not find ${wanted}`);
    assert.deepEqual(logLeaks(sourceOf(wanted)), [], `${wanted} writes stdout, an operand or an error's message to a log or a notice`);
  }
  assert.ok(/\bnotify\(/u.test(sourceOf('remoteAccountsHost.ts')), 'the host has no notify( call, so the scan of its notices matches nothing');
  assert.ok(/\bdefaultRemoteSeams\(\)\.log\(/u.test(sourceOf('remoteAccountsHost.ts')), 'the host writes no log line, so the scan of its log matches nothing');
});
