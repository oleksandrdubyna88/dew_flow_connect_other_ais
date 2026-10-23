import assert from 'node:assert/strict';
import { test } from 'node:test';
import { redactLabelContinuations } from '../labelContinuations';
import { DETAIL_LIMIT, notificationLine, safeText } from '../notifications';

/**
 * A credential NAMED on one line is redacted when its value stands on the NEXT line — the extension's
 * half of `ALabelledValueOnTheNextLineIsRedactedTests` (todo/PLAN_redaction_reaches_a_value_on_the_next_line.md).
 *
 * <p>`safeText` drops every control character, line breaks included, so a notice used to glue the label
 * and the value together with the next line's indentation between them — and a value of several lines
 * kept every line but the first readable. The pass runs BEFORE that strip, on the real line breaks, and
 * `run-parity.mjs` holds the two halves to the same bytes over the same shapes. Every canary is fake.</p>
 */

const LF = '\n';

function lines(...parts: readonly string[]): string {
  return parts.join(LF);
}

function source(text: string): string {
  return redactLabelContinuations(text, '[redacted]');
}

function notice(text: string): string {
  return safeText(text, DETAIL_LIMIT);
}

test('R2: a nested JSON value eight spaces deep is redacted on the notice road', () => {
  const given = lines('{', '  "nested": {', '    "api_key":', '        "FAKE-CANARY-0002"', '  }', '}');

  const safe = notice(given);

  assert.ok(!safe.includes('FAKE-CANARY-0002'), safe);
  assert.equal(safe, '{  "nested": {    "api_key":        "[redacted]"  }}');
});

test('R6: a folded header with five spaces of indentation is redacted', () => {
  const safe = notice(lines('X-Api-Key:', '     FAKE-CANARY-0006'));

  assert.ok(!safe.includes('FAKE-CANARY-0006'), safe);
});

test('R4: a block scalar value is redacted, and a glued indicator is not taken for the value', () => {
  assert.ok(!notice(lines('password: >', '  FAKE-CANARY-0004', '')).includes('FAKE-CANARY-0004'));
  assert.ok(!notice('password: >  FAKE-CANARY-0004').includes('FAKE-CANARY-0004'));
});

test('R4b: a two-line block scalar has neither line readable', () => {
  const safe = notice(lines('password: |', '  FAKE-FIRST', '  FAKE-SECOND'));

  assert.ok(!safe.includes('FAKE-FIRST'), safe);
  assert.ok(!safe.includes('FAKE-SECOND'), safe);
});

test('S-12: the extension\'s own notice line of a two-line scalar carries neither line', () => {
  const line = notificationLine({
    utc: '2026-10-23T10:00:00.000Z',
    class: 'failure',
    source: 'reviewer',
    code: 'refused',
    detail: lines('password: |', '  FAKE-FIRST', '  FAKE-SECOND'),
  });

  assert.ok(!line.includes('FAKE-FIRST'), line);
  assert.ok(!line.includes('FAKE-SECOND'), line);
});

test('S-14: blank lines inside a block scalar do not end it', () => {
  const safe = notice(lines('password: |', '  FAKE-FIRST', '', '  FAKE-SECOND', 'ordinary: visible'));

  assert.ok(!safe.includes('FAKE-FIRST'), safe);
  assert.ok(!safe.includes('FAKE-SECOND'), safe);
  assert.ok(safe.includes('ordinary: visible'), safe);
});

test('S-16: a single- or triple-quoted value on one line is redacted', () => {
  assert.equal(notice("password='FAKE-CANARY-0016'"), "password='[redacted]'");
  assert.equal(notice('password = """FAKE-CANARY-0016"""'), 'password = """[redacted]"""');
  assert.equal(notice("'client_secret': 'FAKE-CANARY-0016'"), "'client_secret': '[redacted]'");
});

test('R11: a TOML triple-quoted value is redacted on the notice road', () => {
  const safe = notice(lines('password = """', 'FAKE-CANARY-0011', '"""', 'name = "visible"'));

  assert.ok(!safe.includes('FAKE-CANARY-0011'), safe);
  assert.ok(safe.includes('name = "visible"'), safe);
});

test('a pinned negative: five spaces before the joiner still stay', () => {
  assert.equal(notice('password     =x'), 'password     =x');
});

// ---------- the pass alone: the layout is kept, which is what a served file would need ----------

test('S-1: R1 keeps its layout and its line count', () => {
  const given = lines('{', '  "api_key":', '    "FAKE-CANARY-0001",', '}');

  assert.equal(source(given), lines('{', '  "api_key":', '    "[redacted]",', '}'));
});

test('S-3: each shape with evidence has its value redacted and its indentation kept', () => {
  const rows: ReadonlyArray<readonly [string, string]> = [
    ['api_key:\n  FAKE-CANARY-0003', 'api_key:\n  [redacted]'],
    ['X-Api-Key:\n FAKE-CANARY-0005', 'X-Api-Key:\n [redacted]'],
    ['X-Api-Key:\n     FAKE-CANARY-0006', 'X-Api-Key:\n     [redacted]'],
    ['X-Api-Key:\n\tFAKE-CANARY-0005', 'X-Api-Key:\n\t[redacted]'],
    ['service:\n  api_key:\n    FAKE-CANARY-0010', 'service:\n  api_key:\n    [redacted]'],
    [`export TOKEN=\\${LF}FAKE-CANARY-0007`, `export TOKEN=\\${LF}[redacted]`],
    ['api_key:\r\n  FAKE-CANARY-0003\r\nname: visible\r\n', 'api_key:\r\n  [redacted]\r\nname: visible\r\n'],
    ['api_key:\r  FAKE-CANARY-0003\rname: visible', 'api_key:\r  [redacted]\rname: visible'],
  ];

  for (const [given, expected] of rows) {
    assert.equal(source(given), expected, given);
  }
});

test('S-5: the declines leave the text untouched, and nested YAML is not swallowed by its parent', () => {
  for (const given of [
    lines('password:', 'the one you chose last week'),
    lines('the key', 'to this is patience'),
    lines('api_key:', '', '  x'),
    'api_key:',
    'api_key:\n',
    lines('title:', '  FAKE-CANARY-9999'),
    lines('api_key:', 'name: visible'),
  ]) {
    assert.equal(source(given), given, JSON.stringify(given));
  }
  // The pass leaves `api_key: FAKE` to the labelled rule: it only ever reaches a label that ENDS a line.
  const nested = lines('service:', '  api_key: FAKE-CANARY-0012');
  assert.equal(source(nested), nested);
});

test('S-13: a credential-named container leaves its child to the labelled rule', () => {
  const given = lines('credentials:', '  password: FAKE-CANARY-CHILD');

  assert.equal(source(given), given);
  assert.equal(source(lines('secrets:', '  "password": "x"')), lines('secrets:', '  "password": "x"'));
});

test('base64 padding on its own line is a value, not a child label', () => {
  assert.equal(source(lines('authorization:', '  FAKE0CANARY0BASE64==')), lines('authorization:', '  [redacted]'));
});

test('S-14: blank lines inside a block, and the end of a block by its structure', () => {
  assert.equal(
    source(lines('password: |', '  FAKE-FIRST', '', '  FAKE-SECOND', 'ordinary: visible')),
    lines('password: |', '  [redacted]', '', '  [redacted]', 'ordinary: visible'),
  );
  assert.equal(
    source(lines('config:', '  password: |', '    FAKE-FIRST', '    FAKE-SECOND', '  name: visible', 'other: visible')),
    lines('config:', '  password: |', '    [redacted]', '    [redacted]', '  name: visible', 'other: visible'),
  );
  assert.equal(source(lines('password: |', '  user=FAKE-X', '  pass=FAKE-Y')), lines('password: |', '  [redacted]', '  [redacted]'));
});

test('S-15: compound labels reach their next-line value', () => {
  for (const given of [
    'db_password:\n  FAKE-CANARY-0015',
    'apiKey:\n  "FAKE-CANARY-0015"',
    'X-Api-Key:\n FAKE-CANARY-0015',
    '"client_secret":\n    "FAKE-CANARY-0015"',
    'refresh_token =\n  FAKE-CANARY-0015',
    "'api_key' :\n  FAKE-CANARY-0015",
    `${'x'.repeat(100)}_password:\n  FAKE-CANARY-0015`,
  ]) {
    assert.ok(!source(given).includes('FAKE-CANARY-0015'), given);
  }
});

test('R11: a TOML triple-quoted value of one line is redacted, its closing quotes kept', () => {
  assert.equal(
    source(lines('password = """', 'FAKE-CANARY-0011', '"""', 'name = "visible"')),
    lines('password = """', '[redacted]', '"""', 'name = "visible"'),
  );
});

test('a colon label keeps its triple-quote block, to the closing quotes or the end of the text', () => {
  assert.equal(
    source(lines("secret: '''", '  FAKE-FIRST', '', "  FAKE-SECOND'''  # note", 'after: 1')),
    lines("secret: '''", '  [redacted]', '', "  [redacted]'''  # note", 'after: 1'),
  );
  assert.ok(!source(lines('password: """', 'FAKE-FIRST', 'FAKE-SECOND')).includes('FAKE-'));
});

test('S2b: a YAML list under a credential key has every item redacted and its dashes kept', () => {
  const given = lines('api_key:', '  - FAKE-CANARY-L1', '  - FAKE-CANARY-L2', 'name: visible');

  assert.equal(source(given), lines('api_key:', '  - [redacted]', '  - [redacted]', 'name: visible'));
  assert.ok(!notice(given).includes('FAKE-CANARY-L'), notice(given));
  assert.equal(
    source(lines('tokens:', '  - "FAKE-CANARY-L1",', "  - 'FAKE-CANARY-L2'")),
    lines('tokens:', '  - "[redacted]",', "  - '[redacted]'"),
  );
  assert.equal(
    source(lines('credentials:', '  - name: ci', '    password: FAKE-CANARY-L3')),
    lines('credentials:', '  - name: ci', '    password: FAKE-CANARY-L3'),
    'an item that is itself an assignment is left to the labelled rule',
  );
  const broken = source(lines('api_key:', '  - FAKE-CANARY-L1', '    nested continuation', '  - FAKE-CANARY-L2', 'other: x'));
  assert.ok(broken.includes('nested continuation') && !broken.includes('FAKE-CANARY-L1') && broken.includes('other: x'));
});

test('S2b: a code assignment is served byte for byte', () => {
  for (const given of [
    'private static readonly IReadOnlyDictionary<string, string?> WithoutTheKey =\n    new Dictionary<string, string?> { [KeyVariable] = null };',
    "type KeyRead =\n  | { readonly kind: 'value'; readonly value: unknown }\n  | { readonly kind: 'absent' };",
    'internal const string TheRoundsKey = """\n    ALTER TABLE rounds ADD COLUMN round_key TEXT NOT NULL DEFAULT \'\';\n    """;',
    'var token =\n    (await Load(key)).Value;',
    'const secret =\n  [first, second];',
  ]) {
    assert.equal(source(given), given, given);
  }
});

test('S2b: an assignment with a value-shaped next line is still redacted, and takes only that one line', () => {
  assert.equal(source('client_secret =\n  FAKE-CANARY-EQ1'), 'client_secret =\n  [redacted]');
  assert.equal(source('password =\n\t"FAKE-CANARY-EQ1",'), 'password =\n\t"[redacted]",');
  assert.equal(source('password = """\nFAKE-CANARY-EQ1\n"""'), 'password = """\n[redacted]\n"""');
  assert.equal(source(lines("secret = '''", '  FAKE-FIRST', '  FAKE-SECOND', "'''")), lines("secret = '''", '  [redacted]', '  FAKE-SECOND', "'''"));
});

test('S-11: a block scalar of 262 144 characters has no canary anywhere, and the line after it survives', () => {
  const rows = ['password: |'];
  let length = rows[0].length;
  for (let line = 0; length < 262_000; line += 1) {
    const row = `  ${line % 10 === 9 ? `FAKE-CANARY-DEEP-${(line + 1) / 10} ` : ''}${'x'.repeat(100)}`;
    rows.push(row);
    length += row.length + 1;
  }
  rows.push('ordinary: visible');

  const safe = source(rows.join(LF));

  assert.ok(!safe.includes('FAKE-CANARY-DEEP'));
  assert.ok(safe.endsWith('ordinary: visible'));
  assert.equal(safe.split(LF).length, rows.length, 'the line count survives');
});

test('the pass is linear on the adversarial shapes: nothing takes a second', () => {
  const shapes: ReadonlyArray<readonly [string, string]> = [
    ['labels that decline', 'api_key:\n'.repeat(29_000)],
    ['one endless block', `api_key: |\n${`  ${'x'.repeat(98)}\n`.repeat(2_600)}`],
    ['a blank-heavy block', `api_key: |\n${'  x\n\n'.repeat(60_000)}`],
    ['a line of 65 536 spaces then a label', `${' '.repeat(65_536)}api_key:\n${' '.repeat(65_537)}v\n`.repeat(2)],
    ['fifty thousand pairs', 'token:\n  v\n'.repeat(50_000)],
    ['no line break', 'x'.repeat(262_144)],
    ['an unclosed triple quote', `password: """\n${'FAKE-LINE\n'.repeat(26_000)}`],
  ];

  for (const [what, text] of shapes) {
    const started = performance.now();
    source(text);
    safeText(text, DETAIL_LIMIT);
    assert.ok(performance.now() - started < 5_000, `${what}: ${(performance.now() - started).toFixed(0)} ms`);
  }
});
