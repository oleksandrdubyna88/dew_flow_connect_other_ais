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
  }

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
