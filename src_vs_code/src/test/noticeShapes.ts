/**
 * The corpus both halves of the notice serialiser are run over.
 *
 * <p><b>Why it is generated rather than listed.</b> A list of cases passes the day a rule is added to
 * one implementation and not the other, and this contract has two implementations in two languages
 * where the failure is silent: a secret written to `server-notices.jsonl` by one half that the other
 * would have redacted. The shapes below are crossed with the WRAPS below, so every secret shape is
 * seen alone, inside Russian prose, inside German prose, beside quotes, beside an emoji and at each
 * boundary of a length limit.</p>
 *
 * <p><b>Why the wraps carry non-ASCII.</b> That is the single largest known risk in the port and it
 * was measured rather than guessed: .NET's `\b` is Unicode-aware while JavaScript's, without the `u`
 * flag, is ASCII — so `парольsk-abcdefghijkl` is a case where .NET would NOT have redacted a secret
 * JavaScript does. The C# side answers it with an explicit ASCII lookbehind, and this corpus is what
 * holds that answer in place.</p>
 *
 * <p>Deterministic, with no clock and no randomness: the same corpus on every run, so a difference
 * is a difference in the code rather than in the inputs.</p>
 */

/**
 * A URL carrying its own credentials, ASSEMBLED rather than written down.
 *
 * <p>Sonar reads a literal `scheme://user:secret@host` as a hardcoded Basic Authentication password
 * and raises a BLOCKER vulnerability — which is not wrong about the shape. That IS the shape, and it
 * is in this file because it is what the redactor exists to remove. A false positive that blocks a
 * merge is still a blocker, and marking it safe by hand would have to be done again for every shape
 * added here, so the literal is taken apart instead. Nothing about what is tested changes: the
 * string handed to the redactor is identical.</p>
 */
function authority(scheme: string, user: string, secret: string, host: string): string {
  return `${scheme}://${user}:${secret}@${host}`;
}

/**
 * A `basic` credential, ENCODED rather than written down — `authority()`'s argument, one step on.
 *
 * <p>The shape the redactor must remove is <code>basic &lt;base64 of user:secret&gt;</code>, and
 * spelled out it is a real Basic Authentication credential to any scanner that reads this file:
 * Sonar decodes the base64 and raises a BLOCKER on it, which is not wrong about the shape. Encoding
 * it here hands the redactor the identical bytes and leaves nothing in the source to flag.</p>
 */
function basic(user: string, secret: string): string {
  return `basic ${Buffer.from(`${user}:${secret}`).toString('base64')}`;
}

/**
 * A PEM private-key block, ASSEMBLED rather than written down, for the same reason as `authority()`:
 * the markers are what a scanner looks for. `breaks` is what separates its lines — a real newline in a
 * file, the two characters `\n` on one line inside a service account's JSON — and an empty footer is
 * a block cut short.
 */
function privateKey(kind: string, breaks: string, footer = true): string {
  const head = `-----BEGIN ${kind}PRIVATE` + ` KEY-----`;
  const tail = footer ? `-----END ${kind}PRIVATE` + ` KEY-----` : '';

  return `${head}${breaks}MIIEvQIBADANBgEXAMPLEBODYLINEONE0123456789${breaks}EXAMPLEBODYLINETWOabcdefghij+/=${breaks}${tail}`;
}

/**
 * Lines joined by a REAL line break — the shape a file or a pretty-printed body has, which the
 * `\\n` of a JSON-escaped string (two characters) is not. `LabelContinuations` reads the real
 * ones, and the notice road runs it BEFORE the strip that would glue them
 * (`todo/PLAN_redaction_reaches_a_value_on_the_next_line.md`, D9).
 */
function lines(...parts: readonly string[]): string {
  return parts.join('\n');
}

/** A value on the NEXT line, in every shape the plan measured (R1 to R11), the declines, and what must not change. Every canary is fake. */
const NEXT_LINE_SHAPES: readonly string[] = [
  // R1/R2: JSON, the value on the next line, at four and at eight spaces.
  lines('{', '  "api_key":', '    "FAKE-CANARY-0001",', '}'),
  lines('{', '  "nested": {', '    "api_key":', '        "FAKE-CANARY-0002"', '  }', '}'),
  lines('{"api_key":', '"FAKE-CANARY-0001",', '"name": "kept"}'),
  // R3 to R6: YAML, a block scalar (one and two lines), a folded header with one and five spaces.
  lines('api_key:', '  FAKE-CANARY-0003'),
  lines('password: >', '  FAKE-CANARY-0004'),
  lines('password: |', '  FAKE-FIRST', '  FAKE-SECOND'),
  lines('password: |-', '  FAKE-FIRST', '  FAKE-SECOND', 'ordinary: visible'),
  lines('X-Api-Key:', ' FAKE-CANARY-0005'),
  lines('X-Api-Key:', '     FAKE-CANARY-0006'),
  // R7 to R9: a shell continuation, a CRLF break, a wrapped value (the residual).
  lines('export TOKEN=\\', 'FAKE-CANARY-0007'),
  'client_secret=\r\nFAKE-CANARY-0008',
  'client_secret=FAKE-CANA\r\nRY-0009',
  // R10: nested YAML, both shapes of the pair.
  lines('service:', '  api_key:', '    FAKE-CANARY-0010'),
  lines('service:', '  api_key: FAKE-CANARY-0012'),
  // R11: TOML triple quotes, both kinds, closed and not, text before the closing quotes.
  lines('password = """', 'FAKE-CANARY-0011', '"""'),
  lines("secret = '''", '  FAKE-FIRST', '', "  FAKE-SECOND'''  # note", 'after = 1'),
  lines('password = """', 'FAKE-FIRST', 'FAKE-SECOND'),
  // The child of a credential-named container keeps its label; a blank line inside a block does not end it.
  lines('credentials:', '  password: FAKE-CANARY-CHILD'),
  lines('password: |', '  FAKE-FIRST', '', '  FAKE-SECOND', 'ordinary: visible'),
  // Compound labels, tabs, an indicator with a digit, base64 padding as a value.
  lines('db_password:', '  FAKE-CANARY-0015'),
  lines('apiKey:', '  "FAKE-CANARY-0015"'),
  lines('"client_secret":', '    "FAKE-CANARY-0015"'),
  lines("'api_key' :", '\tFAKE-CANARY-0015'),
  lines('authorization:', '  FAKE0CANARY0BASE64=='),
  lines('password: |2', '    FAKE-FIRST', '   FAKE-SECOND'),
  // The declines, which stay as they are (before the strip glues them).
  lines('password:', 'the one you chose last week'),
  lines('the key', 'to this is patience'),
  lines('api_key:', '', '  FAKE-DECLINED'),
  lines('api_key:', 'name: visible'),
  lines('title:', '  not a credential'),
  'api_key:\n',
  // One-line quote classes and the widened gap (S-16 and D6).
  "password='FAKE-CANARY-0016'",
  'password = """FAKE-CANARY-0016"""',
  "'client_secret': 'FAKE-CANARY-0016'",
  `api_key:${' '.repeat(32)}FAKE-CANARY-0017`,
  `api_key:${' '.repeat(33)}FAKE-CANARY-0017`,
  'password: >  FAKE-CANARY-0004',
  'password     =x',
];

/** A PEM header, ASSEMBLED rather than written down for the reason `privateKey()` gives. */
function header(kind: string): string {
  return `-----BEGIN ${kind}PRIVATE` + ` KEY-----`;
}

/** Every secret shape the redactor knows, and several it must LEAVE ALONE. */
const SHAPES: readonly string[] = [
  // The three SECRETS patterns.
  'Authorization: Bearer abcdefghijkl',
  basic('aladdin', 'opensesame'),
  'token abcdefghijklmnop',
  authority('https', 'user', 'pw', 'host/x'),
  authority('ftp', 'someone', 'hunter2', 'example.invalid'),
  'sk-abcdefghijklmnop',
  'ghp_abcdefghijklmnop',
  'gho_abcdefghijklmnop',
  'ghu_abcdefghijklmnop',
  'ghs_abcdefghijklmnop',
  'github_pat_abcdefghijklmnop',
  'xoxb-abcdefghijklmnop',
  'xoxp-abcdefghijklmnop',
  // A Moonshot account id (org-… / ak-…): redacted when the body holds a digit, left alone when it is an ordinary word.
  'Your account org-0a1b2c3d4e5f6a7b <ak-9z8y7x6w5v4u3t2s> is suspended',
  'the org-wide setting and org-settings-panel',
  // PARAMETER: the name decides, and these two are the pair that matters.
  '?access_token=xyzabcdef',
  '?api-version=2024-02-01',
  '&client_secret=abcdef&page=2',
  '#sig=abcdef',
  '?author=octocat',
  '?monkey=bars',
  // LABELLED, in ordinary prose rather than a URL.
  'password=letmein',
  'api_key: abc123',
  '"client_secret": "shhh"',
  'apiKey=abcdef',
  'xAuth=abcdef',
  // Ordinary text that must survive untouched.
  'nothing here at all',
  'deployment=blue region=eu page=3',
  // Adjacency: two secrets in one string, and a secret twice over.
  'https://u:p@https://u:p@host',
  'token abcdefghij and sk-abcdefghij',
  // A private-key BLOCK (2026-09-26): escaped on one line as a service account holds it, with real line
  // breaks as a fixture holds it (a notice drops them, and the block is still a block), every variant,
  // cut short of its footer, and a header standing alone in code, which is not a key.
  `"private_key": "${privateKey('', '\\n')}"`,
  privateKey('RSA ', '\n'),
  privateKey('EC ', '\\n'),
  privateKey('OPENSSH ', '\r\n'),
  privateKey('ENCRYPTED ', '\\n'),
  privateKey('', '\\n', false),
  'const header = "-----BEGIN PRIVATE' + ' KEY-----"; return header.length;',
  // A label whose value is a key, on the next line: the block pass has taken the body before the continuation reads it.
  lines('"private_key":', '"' + privateKey('', '\n')),
  ...NEXT_LINE_SHAPES,
];

/**
 * What each shape is wrapped in. The empty wrap keeps the bare shape in the corpus.
 *
 * <p>The non-ASCII ones are the point — see the note above about `\b`. `%s` is where the shape goes.</p>
 */
const WRAPS: readonly string[] = [
  '%s',
  'prefix %s suffix',
  'Ошибка при запросе: %s — повторите позже',
  'пароль%s',
  'Fehler beim Schlüssel: %s (bitte prüfen)',
  'Schlüsselß%s',
  '"%s"',
  "'%s',",
  '{"detail":"%s"}',
  '😀 %s 😀',
  '\t%s\r',
];

/** The lengths that sit either side of a limit, so truncation is exercised at its boundary. */
const LENGTHS: readonly number[] = [0, 1, 999, 1000, 1001, 4095, 4096, 4097, 5000];

/** A character neither half may write down, by NUMBER — never as a literal, never as a class. */
function control(code: number): string {
  return String.fromCodePoint(code);
}

/**
 * Every input the two serialisers are compared over.
 *
 * <p>Order is fixed and the content is deterministic, so a run that differs differs because the code
 * did.</p>
 */
export function noticeShapes(): readonly string[] {
  const shapes: string[] = [];

  for (const wrap of WRAPS) {
    for (const shape of SHAPES) {
      shapes.push(wrap.replace('%s', shape));
    }
  }

  // Control characters, which are stripped before anything else happens. By code point, because the
  // one time this product spelled them as literals the bytes reached disk and git stopped treating
  // three source files as text.
  for (const code of [0, 1, 8, 11, 12, 14, 31, 127]) {
    shapes.push(`before${control(code)}after`, `${control(code)}sk-abcdefghijklmnop`);
    // A control character beside a label that continues on the next line: dropped before the two
    // line-spanning passes read the label (all but tab, LF, CR), so it neither hides the label nor ends a key's run.
    shapes.push(
      `password:${control(code)}\n  FAKE-CANARY-0018`,
      `pass${control(code)}word:\n  FAKE-CANARY-0018`,
      `${header('')}\n${'MIIEFAKEBODYLINE0123456789'}${control(code)}\nMIIEFAKEBODYLINE9876543210`,
    );
  }

  // White space that is NOT a line break, inside a value the continuation pass reads and outside it: JavaScript's
  // own `\s` is the value class's edge in both halves.
  for (const code of [0x85, 0xa0, 0x1680, 0x180e, 0x2000, 0x2028, 0x2029, 0x202f, 0x205f, 0x3000, 0xfeff]) {
    shapes.push(
      `api_key:\n  FAKE-CANARY-0019${control(code)}TAIL`,
      `api_key:\n  ${control(code)}FAKE-CANARY-0019`,
      `api_key:${control(code)}\n  FAKE-CANARY-0019`,
    );
  }

  // A continued value at the very bound of its token, and beside a break at each end of the text.
  shapes.push(
    `api_key:\n  ${'v'.repeat(4096)}TAIL`,
    `api_key:\n  ${'v'.repeat(4097)}`,
    `api_key:\r\n`,
    `\n\napi_key:\n  FAKE-CANARY-0020\n\n`,
    `\r\rapi_key:\r  FAKE-CANARY-0020`,
  );

  // Length, at each boundary. `a` repeated, then a secret at the very end so truncation and
  // redaction are exercised together.
  for (const length of LENGTHS) {
    shapes.push('a'.repeat(length), 'a'.repeat(Math.max(0, length - 20)) + 'sk-abcdefghijklmnop');
  }

  // A surrogate pair straddling the cut, which is where `slice` and `Substring` have to agree,
  // and values longer than a pattern's own bound, where both halves leave the tail behind.
  shapes.push(
    'a'.repeat(999) + '😀tail',
    'a'.repeat(4095) + '😀tail',
    `sk-${'b'.repeat(600)}`,
    `token ${'c'.repeat(5000)}`,
  );

  return shapes;
}

/** Whole records, for the line serialiser rather than the redactor. */
export function noticeRecords(): readonly Record<string, unknown>[] {
  return [
    { utc: '2026-09-21T10:00:00.000Z', class: 'refusal', source: 'panel', code: 'refused' },
    {
      utc: '2026-09-21T10:00:01.500Z',
      class: 'failure',
      source: 'reviewer',
      code: 'reviewer-timed-out',
      subject: 'codex/Architecture',
      title: 'Authorization: Bearer abcdefghijkl',
      detail: `Ошибка: password=letmein ${'x'.repeat(5000)}`,
      cure: '?access_token=xyzabcdef',
      run: 'a1b2c3',
      pid: 4242,
      seq: 7,
      provider: 'codex',
      role: 'Architecture',
    },
    {
      // CodeRabbit, on the pull request: an optional string that is NOT empty and redacts to empty.
      // `given()` keeps it (it has characters), the redactor removes every one of them, and what the
      // line then carries is what both halves have to agree about. The byte is spelled by NUMBER,
      // because writing it as an escape is how this repository has put raw control bytes on disk.
      utc: '2026-09-21T10:00:03.000Z',
      class: 'refusal',
      source: 'panel',
      code: 'refused',
      title: String.fromCharCode(1),
      subject: 'kept',
    },
    {
      // A two-line block scalar with REAL line breaks, in the detail a server notice carries and the
      // extension reads back (S-12 of the next-line plan): neither line may reach the page, and the line the
      // parser reads must be the line the writer wrote.
      utc: '2026-10-23T10:00:00.000Z',
      class: 'failure',
      source: 'reviewer',
      code: 'refused',
      title: lines('{', '  "api_key":', '    "FAKE-CANARY-0001",', '}'),
      detail: lines('password: |', '  FAKE-FIRST', '  FAKE-SECOND'),
    },
    {
      utc: '2026-09-21T10:00:02.000Z',
      class: 'storm',
      source: 'panel',
      code: 'refused',
      bound: 10,
      // A field this build has no name for, which must be flattened and redacted like the rest.
      vendorNote: 'sk-abcdefghijklmnop',
      attempts: 3,
    },
  ];
}
