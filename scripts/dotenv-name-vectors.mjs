// L-V6b of research/PLAN_kimi_and_vibe_on_linux.md: the names python-dotenv binds, per vector, from Vibe's OWN python-dotenv — and
// the verdict coai's names grammar (DL2, scripts/lib/dotenv-names.mjs) gives the same bytes. Writes shared/dotenv-name-vectors.json,
// which E2's C# grammar is tested against (S-V13). FREE: no Vibe launch, no network, no model.
//
//   node scripts/dotenv-name-vectors.mjs [--python <Vibe's python>] [--scratch <dir>] [--write | --check]
//
// The oracle is `dotenv_values(path)` — exactly how Vibe 2.26.1 reads `$VIBE_HOME/.env` (vibe/core/config/vibe_schema.py,
// load_dotenv_values: it sets every name whose value is non-empty, unless the environment already has it) — run by Vibe's own
// interpreter in isolated mode (`-I`: no user site, no PYTHON* variables, not the script's folder on sys.path). Each vector is
// written as a FILE, because python-dotenv opens a path in text mode and Python's universal newlines are part of what a line is.
//
// Every input is SYNTHETIC — `sk-test-…` or a plain word. Nobody's .env is read: the person's sign-in is never an input here.
// The default python is Vibe's own (lib/vibe.mjs vibePython). The scratch folder holds the vector files only; a folder this
// script made in the OS temp is removed afterwards, a `--scratch` folder is left for its owner (in WSL nothing is deleted).
//
// --write  replaces shared/dotenv-name-vectors.json.  --check  regenerates and compares with it (exit 1 on any difference).
// Without either it prints the document. It refuses (exit 70, nothing written) if any vector the reference ADMITS binds a name
// other than MISTRAL_API_KEY in python-dotenv: that would be the grammar's soundness argument failing, which is a finding.
import { spawnSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { ADMITTED_NAME, coaiVerdict } from './lib/dotenv-names.mjs';
import { vibePython } from './lib/vibe.mjs';

const argv = process.argv.slice(2);
const flag = (name, fallback) => {
  const i = argv.indexOf(name);
  return i >= 0 && i + 1 < argv.length ? argv[i + 1] : fallback;
};
const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const OUT = join(ROOT, 'shared', 'dotenv-name-vectors.json');
const python = flag('--python', '') || vibePython();
const given = flag('--scratch', '');

/**
 * The vectors: DL2's fixed lines, the whole-file multi-line shapes, the line-break and whitespace edges. `id` is stable — E2's
 * tests name them. Values are synthetic. Kept in this order in the output.
 */
const VECTORS = [
  // --- the forms DL2 and S-V13 say python-dotenv binds MISTRAL_API_KEY from
  ['plain', 'MISTRAL_API_KEY=sk-test-plain\n'],
  ['spaced-double-quoted', 'MISTRAL_API_KEY = "sk-test-quoted"\n'],
  ['export-single-quoted', "export MISTRAL_API_KEY='sk-test-export'\n"],
  ['indented-inline-comment', '  MISTRAL_API_KEY=sk-test-indent  # note\n'],
  ['quoted-name', "'MISTRAL_API_KEY'=sk-test-quotedname\n"],
  ['export-tab', 'export\tMISTRAL_API_KEY=sk-test-tab\n'],
  ['tabs-around-equals', '\tMISTRAL_API_KEY\t=\tsk-test-tabs\n'],
  ['no-final-newline', 'MISTRAL_API_KEY=sk-test-eof'],
  ['empty-value', 'MISTRAL_API_KEY=\n'],
  ['value-holds-equals', 'MISTRAL_API_KEY=sk-test-a=b\n'],
  ['value-interpolates', 'MISTRAL_API_KEY=${OTHER_NAME}\n'],
  ['comments-and-blank-lines', '# a comment\n\n   \nMISTRAL_API_KEY=sk-test-c\n# the end\n'],
  ['same-name-twice', 'MISTRAL_API_KEY=sk-test-one\nMISTRAL_API_KEY=sk-test-two\n'],
  ['crlf', 'MISTRAL_API_KEY=sk-test-crlf\r\n'],
  ['nbsp-before-equals', 'MISTRAL_API_KEY =sk-test-nbsp\n'],
  ['vt-leading', '\u000bMISTRAL_API_KEY=sk-test-vt\n'],
  // --- lines that bind no name: a pasted key, a bare word, a line starting with `=` (DL2's first refusal)
  ['bare-key-pasted', 'sk-test-pasted-alone\n'],
  ['bare-name', 'MISTRAL_API_KEY\n'],
  ['export-bare-name', 'export MISTRAL_API_KEY\n'],
  ['leading-equals', '=sk-test-noname\n'],
  ['export-then-equals', 'export =sk-test-x\n'],
  ['unterminated-quoted-name', "'MISTRAL_API_KEY=sk-test-q\n"],
  ['hash-after-name', 'MISTRAL_API_KEY#x=sk-test-h\n'],
  ['vt-inside-name', 'MISTRAL\u000bAPI_KEY=sk-test-split\n'],
  ['key-then-bare-word', 'MISTRAL_API_KEY=sk-test-k\nVIBE_X\n'],
  // --- another name (DL2's second refusal: by line number only)
  ['another-variable', 'VIBE_MCP_SERVERS=[]\n'],
  ['key-then-vibe-variable', 'MISTRAL_API_KEY=sk-test-k\nVIBE_ACTIVE_MODEL=canary-model\n'],
  ['export-glued-to-name', 'exportMISTRAL_API_KEY=sk-test-glued\n'],
  ['lowercase-name', 'mistral_api_key=sk-test-lower\n'],
  // --- the BYTES (DL2's two byte-level kinds, the A1 spec): a UTF-8 byte-order mark; a NUL; bytes that are not UTF-8
  ['byte-order-mark', '﻿MISTRAL_API_KEY=sk-test-bom\n'],
  ['byte-order-mark-then-another-variable', '﻿MISTRAL_API_KEY=sk-test-bom\nVIBE_X=1\n'],
  ['nul-in-value', 'MISTRAL_API_KEY=sk-test-a\u0000b\n'],
  ['invalid-utf8-second-line', Buffer.concat([Buffer.from('MISTRAL_API_KEY=sk-test-ok\nVIBE_X=', 'utf8'), Buffer.from([0xff]), Buffer.from('\n', 'utf8')])],
  ['nel-leading-second-line', 'MISTRAL_API_KEY=sk-test-k\n\u0085VIBE_X=1\n'],
  ['file-separator-leading-second-line', 'MISTRAL_API_KEY=sk-test-k\n\u001cVIBE_X=1\n'],
  // --- a value python-dotenv cannot parse: coai never parses a value, so it admits; python-dotenv binds nothing
  ['junk-after-quoted-value', 'MISTRAL_API_KEY="sk-test-v" junk\n'],
  ['unterminated-double-quote', 'MISTRAL_API_KEY="sk-test-open\n'],
  // --- the three multi-line shapes of DL2, and two more
  ['multiline-binding-shaped-continuation', 'MISTRAL_API_KEY="dummy\nLEAK_CANARY=ending"\n'],
  ['multiline-comment-shaped-continuation', 'MISTRAL_API_KEY="dummy\n#ending"\n'],
  ['multiline-closed-then-binding', 'MISTRAL_API_KEY="a\n"\nVIBE_X=1\n'],
  ['multiline-word-continuation', 'MISTRAL_API_KEY="dummy\nending"\n'],
  ['multiline-single-quoted', "MISTRAL_API_KEY='dummy\nVIBE_X=1'\n"],
  // --- what a line break is: a bare CR is one; VT, FF, NEL and LINE SEPARATOR are not
  ['bare-cr', 'MISTRAL_API_KEY=x\rVIBE_X=1\n'],
  ['bare-cr-no-lf-at-all', 'MISTRAL_API_KEY=x\rVIBE_X=1'],
  ['vt-in-value', 'MISTRAL_API_KEY=x\u000bVIBE_X=1\n'],
  ['ff-in-value', 'MISTRAL_API_KEY=x\u000cVIBE_X=1\n'],
  ['nel-in-value', 'MISTRAL_API_KEY=x\u0085VIBE_X=1\n'],
  ['line-separator-in-value', 'MISTRAL_API_KEY=x VIBE_X=1\n'],
];

/** Fixed Python: the vector files arrive as argv, so no input is ever Python source. Prints one JSON document. */
const ORACLE = String.raw`
import sys, json, re, importlib.metadata as m
from dotenv import dotenv_values
import dotenv.parser as parser
out = {"python": sys.version.split()[0], "pythonDotenv": m.version("python-dotenv"), "vibe": m.version("mistral-vibe"),
       "whitespace": [c for c in range(0x110000) if re.match(r"\s", chr(c))], "files": {}}
for path in sys.argv[1:]:
    try:
        values = dotenv_values(path)
        with open(path, encoding="utf-8") as stream:
            errors = [b.original.line for b in parser.parse_stream(stream) if b.error]
    except UnicodeDecodeError as e:
        out["files"][path] = {"binds": [], "noValue": [], "vibeSets": [], "errorLines": [], "decodeError": type(e).__name__, "nulInValue": False}
        continue
    out["files"][path] = {
        "binds": [k for k, v in values.items() if v is not None],
        "noValue": [k for k, v in values.items() if v is None],
        "vibeSets": [k for k, v in values.items() if v],
        "errorLines": errors,
        "decodeError": "",
        "nulInValue": any("\x00" in v for v in values.values() if v),
    }
print(json.dumps(out))
`;

/** The words a refusal sentence about this file must never hold: every word of three or more characters but the admitted name. */
const mustNotEcho = (text) => [...new Set(text.split(/[^A-Za-z0-9_-]+/u).filter((w) => w.length >= 3 && w !== ADMITTED_NAME && w !== 'export'))];

/** A vector's exact bytes (a string is written as UTF-8), and its text when those bytes are UTF-8 — null when they are not. */
const bytesOf = (input) => (typeof input === 'string' ? Buffer.from(input, 'utf8') : input);
const textOf = (input) => (typeof input === 'string' ? input : null);

const scratch = given || mkdtempSync(join(tmpdir(), `testsToDelete-dotenv-vectors-${randomBytes(3).toString('hex')}-`));
mkdirSync(scratch, { recursive: true });
const paths = VECTORS.map(([id, input], n) => {
  const path = join(scratch, `${String(n + 1).padStart(2, '0')}-${id}.env`);
  writeFileSync(path, bytesOf(input));
  return path;
});

console.error(`[dotenv-vectors] asking python-dotenv about ${paths.length} vectors …`);
const r = spawnSync(python, ['-I', '-c', ORACLE, ...paths], { encoding: 'utf8', timeout: 120_000, maxBuffer: 16 * 1024 * 1024 });
if (!given) rmSync(scratch, { recursive: true, force: true });
if (r.status !== 0) {
  console.error(`the oracle failed (exit ${r.status}, ${r.error ?? ''}): ${String(r.stderr).slice(-2000)}`);
  process.exit(69);
}
const oracle = JSON.parse(r.stdout);

const vectors = VECTORS.map(([id, input], n) => {
  const python = oracle.files[paths[n]];
  const bytes = bytesOf(input);
  const { names, ...coai } = coaiVerdict(bytes);
  return {
    id, input: textOf(input), inputBase64: bytes.toString('base64'),
    binds: python.binds, noValue: python.noValue, vibeSets: python.vibeSets, errorLines: python.errorLines,
    decodeError: python.decodeError, nulInValue: python.nulInValue,
    coaiReads: names, coai, mustNotEcho: mustNotEcho(bytes.toString('utf8')),
  };
});

const unsound = vectors.filter((v) => v.coai.verdict === 'admitted' && v.binds.some((name) => name !== ADMITTED_NAME));
if (unsound.length > 0) {
  console.error(`UNSOUND: the grammar admits ${unsound.map((v) => v.id).join(', ')}, and python-dotenv binds another name there — nothing written`);
  process.exit(70);
}

const document = {
  why: [
    'L-V6b of research/PLAN_kimi_and_vibe_on_linux.md (DL2): per vector, the names python-dotenv binds from these exact bytes, read the',
    "way Vibe 2.26.1 reads $VIBE_HOME/.env (dotenv_values(path), Vibe's own interpreter), and the verdict coai's names grammar gives.",
    'Generated by `node scripts/dotenv-name-vectors.mjs --write`, never edited by hand. inputBase64: the exact bytes (input: the',
    'same as text, null when the bytes are not UTF-8). binds: names python-dotenv gives a value (empty included); noValue: a bare',
    'name it keeps with NO value; vibeSets: what Vibe sets (a non-empty value); errorLines: lines python-dotenv could not parse (it',
    'binds nothing there); decodeError: the file is not UTF-8 (python-dotenv raises, Vibe does not start); nulInValue: a value',
    "holds a NUL (Vibe cannot set it). coai: admitted, or refused at the FIRST refused line by DL2's four kinds - ByteOrderMark,",
    'NotText, NoBinding, OtherName; every refused line is in refusals. The invariant E2 is tested on (S-V13): coai admits a file',
    '=> every name python-dotenv binds is MISTRAL_API_KEY. A refusal sentence names line numbers only; mustNotEcho lists the words',
    'of the file it must not hold. Every value here is synthetic.',
  ],
  generator: 'scripts/dotenv-name-vectors.mjs',
  reference: 'scripts/lib/dotenv-names.mjs',
  oracle: { vibe: oracle.vibe, pythonDotenv: oracle.pythonDotenv, python: oracle.python, platform: process.platform, read: 'dotenv_values(path)', whitespace: oracle.whitespace },
  admittedName: ADMITTED_NAME,
  lineBreaks: ['\r\n', '\n', '\r'],
  vectors,
};

/** JSON with every character outside printable ASCII escaped, so a NEL or a LINE SEPARATOR survives editors and git's text handling. */
const asciiJson = (value) => `${JSON.stringify(value, null, 2).replace(/[^\x20-\x7e\n]/gu, (ch) => `\\u${ch.charCodeAt(0).toString(16).padStart(4, '0')}`)}\n`;

if (argv.includes('--check')) {
  const committed = JSON.parse(readFileSync(OUT, 'utf8'));
  const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);
  const drift = [
    ...(same(committed.oracle.whitespace, document.oracle.whitespace) ? [] : ['oracle.whitespace']),
    ...(committed.oracle.pythonDotenv === document.oracle.pythonDotenv ? [] : [`python-dotenv ${committed.oracle.pythonDotenv} → ${document.oracle.pythonDotenv}`]),
    ...vectors.filter((v) => !same(v, committed.vectors.find((c) => c.id === v.id))).map((v) => v.id),
    ...committed.vectors.filter((c) => !vectors.some((v) => v.id === c.id)).map((c) => `${c.id} (gone)`),
  ];
  console.log(JSON.stringify({ platform: process.platform, python: oracle.python, pythonDotenv: oracle.pythonDotenv, vibe: oracle.vibe, vectors: vectors.length, drift }, null, 2));
  process.exit(drift.length === 0 ? 0 : 1);
}
if (argv.includes('--write')) {
  writeFileSync(OUT, asciiJson(document));
  console.error(`wrote ${OUT}: ${vectors.length} vectors, python-dotenv ${oracle.pythonDotenv} (Vibe ${oracle.vibe}, ${process.platform})`);
} else {
  process.stdout.write(asciiJson(document));
}
if (!existsSync(OUT) && !argv.includes('--write')) console.error('shared/dotenv-name-vectors.json does not exist yet — run with --write');
