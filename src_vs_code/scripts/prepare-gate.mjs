import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const LIMIT = 256 * 1024;
export const SOURCE = '.agents/conventions/common/coai-review-gate.md';

/**
 * The DOCUMENT gate, which is a second file rather than a section of the first.
 *
 * <p>`coai-review-gate.md` is one of the 24 rule bodies `tools/rules.test.mjs` hashes against
 * baseline `5d6984eb` as evidence that the migration lost nothing, so a section added to it
 * turns that suite red. The conventions repository has already settled what to do — a new file
 * that names the frozen rule it extends — and this is the third time it has been done.</p>
 */
export const DOCUMENT_SOURCE = '.agents/conventions/common/coai-document-gate.md';

/**
 * The CALLER rule — say which model you are when you open the gate.
 *
 * <p>A third file for the same reason as the second: `coai-review-gate.md` is frozen against the
 * conventions migration baseline, so the sentence asking an AI to declare its own model could not
 * be added to step 1 where it belongs. The rule file says so, and says where it goes when the
 * inventory retires.</p>
 */
export const CALLER_SOURCE = '.agents/conventions/common/coai-caller-model.md';
export const OUTPUT = 'src_vs_code/src/generated/gateRule.ts';

/**
 * The consultant half — a mounted rule like the other three since 2026-09-25.
 *
 * <p>It was this product's own file until then, on the operator's ruling of 2026-09-13 that a rule
 * about when to call one tool of one product is not shared. The ruling was reversed on 2026-09-25:
 * this server gates every repository in the family, so a rule about when to call its consultant is
 * as shared as the gate rule itself (research/PLAN_consult_on_a_cadence.md, epic 5). So it is read from
 * the pinned mount and held to its marker the way the three others are.</p>
 *
 * <p>It keeps an output of its own, `generated/consultantRule.ts`, rather than a fourth export in
 * `gateRule.ts`: that is the module `claudeSnippet.ts` already imports, and nothing is gained by moving
 * a constant whose readers do not care where it was generated.</p>
 */
export const CONSULTANT_SOURCE = '.agents/conventions/common/coai-consultant.md';
export const CONSULTANT_OUTPUT = 'src_vs_code/src/generated/consultantRule.ts';

/**
 * The FEATURE half — a shared rule since conventions #58 (S3.5 of todo/PLAN_feature_review.md, D24).
 *
 * <p>Until then it was this product's own file, `src_vs_code/src/featureRule.md` (D11): a rule about
 * when to call one tool of one product was ruled product material, as the consultant half's was until
 * 2026-09-25. The same reversal reached it — this server gates every repository in the family — and v3
 * moved into the conventions with the server's one-round verdicts. So it is read from the pinned mount
 * and held to its marker and heading the way the other halves are; there is no product copy.</p>
 *
 * <p>Generated rather than written as a TypeScript literal for the reason that cost this repository
 * three broken builds: a backtick inside a template literal ends it, and this text is full of code
 * spans. Prose stays prose; the generator turns it into a JSON string.</p>
 */
export const FEATURE_SOURCE = '.agents/conventions/common/coai-feature-gate.md';
export const FEATURE_OUTPUT = 'src_vs_code/src/generated/featureRule.ts';

/** The feature half's marker and heading, so a truncated or foreign file fails the build. */
const FEATURE_MARKER = /^<!-- coai-feature v\d+ -->\n## Reviewing the whole FEATURE before release/;

/** The feature rule: frontmatter and leading `owns:` lines stripped, held to its marker and heading. */
export function featureBody(source) {
  return ruleBody(source, FEATURE_SOURCE, FEATURE_MARKER);
}

/** The marker this file is recognised by, so a truncated or wrong file fails the build. */
const CONSULTANT_MARKER = /^<!-- coai-consultant v\d+ -->\n## When you are stuck, ask another vendor/;

/** Strip delivery metadata only; separators in the instruction body remain verbatim. */
export function gateBody(source) {
  return ruleBody(source, SOURCE, /^<!-- coai-snippet v\d+ -->\n## Multi-model review gate \(ConnectOtherAIs\)/);
}

/** The same treatment for the document rule, held to its own marker. */
export function documentBody(source) {
  return ruleBody(source, DOCUMENT_SOURCE, /^<!-- coai-document v\d+ -->\n## Reviewing a DOCUMENT/);
}

/** And for the caller rule. */
export function callerBody(source) {
  return ruleBody(source, CALLER_SOURCE, /^<!-- coai-caller v\d+ -->\n## Say which model you are/);
}

function ruleBody(source, name, marker) {
  const text = source.replaceAll('\r\n', '\n');
  if (!text.startsWith('---\n')) { throw new Error(`${name}: leading metadata is required`); }
  const end = text.indexOf('\n---\n', 4);
  if (end < 0) { throw new Error(`${name}: unterminated leading metadata`); }
  const body = withoutLeadingComments(text.slice(end + 5), marker);
  if (!marker.test(body)) { throw new Error(`${name}: missing canonical marker`); }
  return body;
}

/**
 * Delivery metadata written as HTML comments, dropped the way the frontmatter is.
 *
 * <p>The conventions release of 2026-09-15 armed an ownership check, and its `owns:` lines sit
 * between the frontmatter and the snippet marker. They are metadata by the same test the
 * frontmatter passes: they say which product names a shared rule is allowed to use, they are
 * addressed to a linter, and a person pasting this into their CLAUDE.md wants none of them. Until
 * this the body no longer STARTED with the marker, so the whole build stopped with
 * `missing canonical marker`.</p>
 *
 * <p>Only what comes BEFORE the marker is taken, and the marker itself is never eaten — it is an
 * HTML comment too. A comment inside the instruction body is the rule author's, means something to
 * the reader, and stays verbatim; this is the same line the file already draws for a `---` that
 * appears legitimately in the prose.</p>
 */
function withoutLeadingComments(body, marker) {
  let rest = body;
  while (rest.startsWith('<!--') && !marker.test(rest)) {
    const closed = rest.indexOf('-->');
    if (closed < 0) { break; }
    const next = rest.slice(closed + 3).replace(/^[ \t]*\n/, '');
    if (next.length === rest.length) { break; }
    rest = next;
  }

  return rest;
}

/** And for the consultant rule: frontmatter and `owns:` lines stripped, held to its marker and heading. */
export function consultantBody(source) {
  return ruleBody(source, CONSULTANT_SOURCE, CONSULTANT_MARKER);
}

function removeOutput(file) {
  try { fs.unlinkSync(file); } catch (error) { if (error.code !== 'ENOENT') { throw error; } }
}

function boundedSource(file) {
  // Named in the error, because there are two sources now and "exceeds 256 KiB" about an unnamed
  // one is a message somebody has to grep the script to act on.
  const descriptor = fs.openSync(file, 'r');
  try {
    const buffer = Buffer.alloc(LIMIT + 1);
    let length = 0;
    for (;;) {
      const read = fs.readSync(descriptor, buffer, length, buffer.length - length, null);
      length += read;
      if (length > LIMIT) { throw new Error(`${file}: exceeds 256 KiB`); }
      if (!read) { return buffer.subarray(0, length).toString('utf8'); }
    }
  } finally { fs.closeSync(descriptor); }
}

/** Every build invalidates its previous output before verifying the pinned canonical source. */
export function prepareGate(repo) {
  const output = path.join(repo, OUTPUT);
  const temporary = output + '.tmp';
  removeOutput(output);
  removeOutput(temporary);
  const resolver = path.join(repo, '.agents/conventions/tools/rules.mjs');
  if (!fs.existsSync(resolver)) {
    throw new Error('Missing conventions resolver. Run git submodule update --init .agents/conventions, then npm ci --ignore-scripts --prefix .agents/conventions.');
  }
  execFileSync(process.execPath, [resolver, 'check', '--repo', repo], {
    encoding: 'utf8', timeout: 30_000, maxBuffer: 1024 * 1024, windowsHide: true,
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  const body = gateBody(boundedSource(path.join(repo, SOURCE)));
  const documents = documentBody(boundedSource(path.join(repo, DOCUMENT_SOURCE)));
  const caller = callerBody(boundedSource(path.join(repo, CALLER_SOURCE)));
  fs.mkdirSync(path.dirname(output), { recursive: true });
  try {
    fs.writeFileSync(temporary, '// Generated from pinned conventions; do not edit.\n'
      + 'export const GATE_RULE = ' + JSON.stringify(body) + ';\n'
      + 'export const DOCUMENT_RULE = ' + JSON.stringify(documents) + ';\n'
      + 'export const CALLER_RULE = ' + JSON.stringify(caller) + ';\n', { flag: 'wx' });
    fs.renameSync(temporary, output);
  } finally { removeOutput(temporary); }

  // The consultant and feature halves, from the same pinned mount, each into an output of its own.
  prepareMountedHalf(repo, { source: CONSULTANT_SOURCE, output: CONSULTANT_OUTPUT, constant: 'CONSULTANT_RULE',
    half: 'the consultant half', since: '2026-09-25', body: consultantBody });
  prepareMountedHalf(repo, { source: FEATURE_SOURCE, output: FEATURE_OUTPUT, constant: 'FEATURE_RULE',
    half: 'the feature half', since: 'conventions #58 (2026-10-09)', body: featureBody });

  return body;
}

/**
 * One half from the pinned mount into its own generated module — invalidate, verify, then write, so a
 * build never compiles against a constant the previous one left behind.
 */
function prepareMountedHalf(repo, { source, output, constant, half, since, body }) {
  const file = path.join(repo, output);
  const temporary = file + '.tmp';
  removeOutput(file);
  removeOutput(temporary);
  const sourceFile = path.join(repo, source);
  if (!fs.existsSync(sourceFile)) {
    // Named, because the case is a pin from before the rule moved: nothing is dirty and nothing
    // mismatches, the file is simply not there — and a bare ENOENT would not say which half.
    throw new Error(`${source} is missing. It is ${half} of the pasted snippet, `
      + `a shared rule since ${since}: move the .agents/conventions pin to a release that carries it.`);
  }
  const text = body(boundedSource(sourceFile));
  try {
    fs.writeFileSync(temporary, '// Generated from pinned conventions; do not edit.\n'
      + `export const ${constant} = ` + JSON.stringify(text) + ';\n', { flag: 'wx' });
    fs.renameSync(temporary, file);
  } finally { removeOutput(temporary); }
}

const here = fileURLToPath(import.meta.url);
if (process.argv[1] && path.resolve(process.argv[1]) === here) {
  try {
    prepareGate(path.resolve(path.dirname(here), '../..'));
  } catch (error) {
    console.error(`prepare-gate: ${error.message}`);
    process.exitCode = 1;
  }
}
