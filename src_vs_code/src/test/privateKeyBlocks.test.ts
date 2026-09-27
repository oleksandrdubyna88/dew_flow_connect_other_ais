import assert from 'node:assert/strict';
import { test } from 'node:test';
import { DETAIL_LIMIT, safeText } from '../notifications';
import { BODY_LIMIT, redactPrivateKeyBlocks } from '../privateKeyBlocks';

/**
 * A PEM private key inside an ordinary string — a service-account JSON, a fixture, a quoted response
 * body — loses its body header to footer, across real line breaks and JSON-escaped ones, for every
 * variant, and keeps the line count. The mirror of `APrivateKeyBlockIsNeverServedTests` on the server;
 * `run-parity.mjs` holds the two halves to the same bytes over the same shapes.
 *
 * <p>The headers are assembled at run time rather than written down, for the reason `noticeShapes.ts`
 * gives for its Basic credentials: a scanner reading this file as a key would not be wrong about the
 * SHAPE, and the shape is the point.</p>
 */

const REDACTED = '[redacted]';
const BODY_MARKER = 'KEYBODYLINE';

function header(kind: string, suffix = ''): string {
  return `-----BEGIN ${kind}PRIVATE` + ` KEY${suffix}-----`;
}

function footer(kind: string, suffix = ''): string {
  return `-----END ${kind}PRIVATE` + ` KEY${suffix}-----`;
}

function bodyLine(n: number): string {
  return `MIIEvQ${BODY_MARKER}${String(n).padStart(2, '0')}${'AAAA'.repeat(20)}`.slice(0, 64);
}

function pem(kind: string, lines = 3, suffix = ''): string {
  const body = Array.from({ length: lines }, (_, i) => `${bodyLine(i + 1)}\n`).join('');

  return `${header(kind, suffix)}\n${body}${footer(kind, suffix)}\n`;
}

function escaped(kind: string, lines = 2): string {
  const body = Array.from({ length: lines }, (_, i) => `${bodyLine(i + 1)}\\n`).join('');

  return `${header(kind)}\\n${body}${footer(kind)}\\n`;
}

function serviceAccount(escapedKey: string): string {
  return '{\n'
    + '  "type": "service_account",\n'
    + '  "project_id": "example-project",\n'
    + `  "private_key": "${escapedKey}",\n`
    + '  "client_email": "svc@example-project.iam.example.invalid"\n'
    + '}\n';
}

function lineCount(text: string): number {
  return text.split('\n').length;
}

test('a service-account file loses its key body, keeps its other lines and its line count', () => {
  const file = serviceAccount(escaped(''));

  const safe = redactPrivateKeyBlocks(file, REDACTED);

  assert.ok(!safe.includes(BODY_MARKER), safe);
  assert.ok(safe.includes('"client_email": "svc@example-project.iam.example.invalid"'), 'every other line is kept');
  assert.ok(safe.includes(footer('')), 'the footer names what was taken out');
  assert.equal(lineCount(safe), lineCount(file), 'a served file\'s line numbers must survive its redaction');
});

test('a block with real line breaks loses every body line, one placeholder per line, indentation kept', () => {
  const block = `        ${header('RSA ')}\n        ${bodyLine(1)}\n        ${bodyLine(2)}\n        ${footer('RSA ')}\n`;

  const safe = redactPrivateKeyBlocks(`before\n${block}after\n`, REDACTED);

  assert.equal(safe, `before\n        ${header('RSA ')}\n        ${REDACTED}\n        ${REDACTED}\n        ${footer('RSA ')}\nafter\n`);
});

test('an escaped one-line JSON collapses the body to one placeholder between its markers', () => {
  const line = `{"private_key":"${escaped('EC ')}"}`;

  const safe = redactPrivateKeyBlocks(line, REDACTED);

  assert.ok(!safe.includes(BODY_MARKER), safe);
  assert.ok(safe.includes(`${REDACTED}${footer('EC ')}\\n"}`), safe);
  assert.ok(!safe.includes('\n'), 'one line in, one line out');
});

for (const kind of ['', 'RSA ', 'EC ', 'DSA ', 'OPENSSH ', 'ENCRYPTED ']) {
  test(`the ${kind || 'plain '}variant is taken out header to footer`, () => {
    const safe = redactPrivateKeyBlocks(`before\n${pem(kind)}after\n`, REDACTED);

    assert.equal(safe, `before\n${header(kind)}\n${REDACTED}\n${REDACTED}\n${REDACTED}\n${footer(kind)}\nafter\n`);
  });
}

test('a PGP private key block is a private key too', () => {
  assert.ok(!redactPrivateKeyBlocks(pem('PGP ', 3, ' BLOCK'), REDACTED).includes(BODY_MARKER));
});

test('an encrypted PEM\'s own header lines are inside the block, and its blank line stays blank', () => {
  const block = `${header('RSA ')}\nProc-Type: 4,ENCRYPTED\nDEK-Info: AES-128-CBC,0123456789ABCDEF\n\n${bodyLine(1)}\n${footer('RSA ')}\n`;

  const safe = redactPrivateKeyBlocks(block, REDACTED);

  assert.equal(safe, `${header('RSA ')}\n${REDACTED}\n${REDACTED}\n\n${REDACTED}\n${footer('RSA ')}\n`);
});

test('a block cut short of its footer still loses the body it has', () => {
  const cut = `the response was: {"private_key": "${header('')}\\n${bodyLine(1)}\\n${bodyLine(2).slice(0, 20)}`;

  assert.ok(!redactPrivateKeyBlocks(cut, REDACTED).includes(BODY_MARKER));
  assert.ok(!safeText(cut, DETAIL_LIMIT).includes(BODY_MARKER), 'and a notice quoting it is not a way past');
});

test('a header literal in code, with no body after it, leaves the code alone', () => {
  const code = `const header = "${header('')}";\nreturn header.length;\n`;

  assert.equal(redactPrivateKeyBlocks(code, REDACTED), code);
});

test('a header and a footer literal redact only what stands between them, and keep the line count', () => {
  const code = `const a = "${header('')}";\nconst b = "${footer('')}";\nreturn 1;\n`;

  const safe = redactPrivateKeyBlocks(code, REDACTED);

  assert.ok(safe.endsWith('";\nreturn 1;\n'), safe);
  assert.equal(lineCount(safe), lineCount(code));
});

test('safeText runs the block pass first, so a notice quoting a key on one line loses the body', () => {
  const detail = `the vendor answered 400: ${pem('RSA ')} — check the key`;

  const safe = safeText(detail, DETAIL_LIMIT);

  assert.ok(!safe.includes(BODY_MARKER), safe);
  assert.ok(safe.includes('check the key'), safe);
});

test('ten thousand headers with no footer are answered in linear time, and a run past the bound is text again', () => {
  // A `;` between the headers, because a header is made of `-`, capitals and spaces — all key-body
  // characters — so headers separated by a space ARE a footerless header's body, and are taken.
  const headers = `${header('')};`.repeat(10_000);
  const run = `${header('')}\n${'A'.repeat(BODY_LIMIT * 3)}\n${BODY_MARKER}`;
  const started = Date.now();

  const safeHeaders = redactPrivateKeyBlocks(headers, REDACTED);
  const safeRun = redactPrivateKeyBlocks(run, REDACTED);

  assert.ok(Date.now() - started < 2_000, `took ${Date.now() - started} ms`);
  assert.equal(safeHeaders, headers, 'a header followed by a semicolon has no body to take');
  assert.ok(safeRun.startsWith(`${header('')}\n${REDACTED}A`), 'up to the bound the run was a key body');
  assert.ok(safeRun.endsWith(BODY_MARKER), 'past the bound the run is the text it was');
  // The bound counts from the header's end, and the newline after it is the run's first character —
  // kept as layout — so `BODY_LIMIT - 1` characters of body became the one placeholder.
  assert.equal(run.length - safeRun.length, BODY_LIMIT - 1 - REDACTED.length);
});
