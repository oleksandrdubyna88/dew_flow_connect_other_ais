import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { sourceFiles, sourceOf } from './sourceReading';

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
