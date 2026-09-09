import assert from 'node:assert/strict';
import { test } from 'node:test';
import { renderAnswer, SUPPORTED } from '../renderAnswer';

/**
 * What a model's answer is allowed to become.
 *
 * <p>Two halves, and the second is the one that matters. The first walks {@link SUPPORTED} and
 * proves each construct renders. The second is the HOSTILE file the plan calls "the plan's safety":
 * text from another vendor's model, which may be quoting an attacker's README, asserted to come out
 * as words rather than as markup. Keep it growing — a case added here costs a line and buys a class
 * of defect that cannot ship.</p>
 *
 * <p><b>The invariant under test</b> is the order of operations: the raw markdown is tokenised
 * first, and escaping happens at emission knowing whether a token is going into text or into an
 * attribute. Escaping the whole string first and then running markup rules over the result was put
 * to the review gate and refused, because every rule would then be reading entity-laden text.</p>
 */

/** Every element this renderer is allowed to emit. Anything else is a defect, not a feature. */
const ALLOWED_TAGS = [
  'p', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
  'ul', 'ol', 'li', 'pre', 'code', 'hr', 'strong', 'em', 'a',
];

/** Every attribute it is allowed to put on one. */
const ALLOWED_ATTRIBUTES = ['class', 'href', 'data-file', 'data-line'];

/**
 * The contract, checked by PARSING the output rather than by searching it for scary words.
 *
 * <p>The first version of this helper looked for substrings — `javascript:`, ` onerror=`, `<script`
 * — and it was wrong in both directions. It failed on answers that were CORRECTLY escaped, because
 * escaped text legitimately contains the words `javascript:` and `onerror=` when the model was
 * writing about them; and a substring search would not have caught an attribute this renderer had
 * been taught to emit by mistake.</p>
 *
 * <p>After escaping, a literal `<` in the output can only be a tag this file emitted — every `<`
 * from the input has become `&lt;`. So the honest check is: find every tag, and assert its name and
 * every one of its attributes are on the two lists above, with `href` restricted to the two schemes
 * that may become one.</p>
 */
function assertOnlyAllowedMarkup(html: string, what: string): void {
  for (const [whole, closing, name, rest = ''] of html.matchAll(/<(\/?)([a-zA-Z][\w-]*)([^>]*)>/g)) {
    assert.ok(
      ALLOWED_TAGS.includes(name.toLowerCase()),
      `${what}: emitted a <${name}> element, which is not on the allow-list — from ${whole}`,
    );
    if (closing === '/') {
      continue;
    }
    for (const [, attribute, value = ''] of rest.matchAll(/([\w-]+)\s*=\s*"([^"]*)"/g)) {
      assert.ok(
        ALLOWED_ATTRIBUTES.includes(attribute.toLowerCase()),
        `${what}: emitted a ${attribute} attribute, which is not on the allow-list — from ${whole}`,
      );
      if (attribute.toLowerCase() === 'href') {
        assert.match(
          value,
          /^https?:\/\//i,
          `${what}: an href escaped the scheme allow-list — ${value}`,
        );
      }
    }
    // A bare attribute with no value cannot be emitted by this renderer at all; if one appears, the
    // renderer has started building attributes by concatenation and the allow-list above is moot.
    assert.doesNotMatch(
      rest,
      /\s[\w-]+\s*=\s*[^"\s>]/,
      `${what}: emitted an unquoted attribute value — from ${whole}`,
    );
  }
}

// ---------------------------------------------------------------------------
// The supported set. One case per entry in SUPPORTED, so the constant cannot
// drift from what the renderer actually does.
// ---------------------------------------------------------------------------

const CASES: Readonly<Record<(typeof SUPPORTED)[number], { markdown: string; expect: RegExp }>> = {
  heading: { markdown: '## Why it is pinned', expect: /<h2>Why it is pinned<\/h2>/ },
  paragraph: { markdown: 'A worktree is cheap.', expect: /<p>A worktree is cheap\.<\/p>/ },
  'unordered-list': { markdown: '- one\n- two', expect: /<ul><li>one<\/li><li>two<\/li><\/ul>/ },
  'ordered-list': { markdown: '1. one\n2. two', expect: /<ol><li>one<\/li><li>two<\/li><\/ol>/ },
  'nested-list': { markdown: '- one\n  - inner', expect: /<ul><li>one<ul><li>inner<\/li><\/ul><\/li><\/ul>/ },
  'fenced-code': { markdown: '```ts\nconst a = 1;\n```', expect: /<pre><code class="language-ts">const a = 1;<\/code><\/pre>/ },
  'thematic-break': { markdown: '---', expect: /<hr>/ },
  'code-span': { markdown: 'the `git mv` goes last', expect: /<code>git mv<\/code>/ },
  bold: { markdown: 'it is **pinned**', expect: /<strong>pinned<\/strong>/ },
  italic: { markdown: 'it is *pinned*', expect: /<em>pinned<\/em>/ },
  link: { markdown: '[the docs](https://example.com/x)', expect: /<a class="link" href="https:\/\/example\.com\/x">the docs<\/a>/ },
  'file-reference': { markdown: '[there](src/chatPage.ts#L42)', expect: /data-file="src\/chatPage\.ts" data-line="42"/ },
};

for (const construct of SUPPORTED) {
  test(`a ${construct} renders`, () => {
    const one = CASES[construct];
    assert.notStrictEqual(one, undefined, `SUPPORTED names ${construct} but no case covers it`);
    const html = renderAnswer(one.markdown);
    assert.match(html, one.expect, `${construct} did not render: ${html}`);
    assertOnlyAllowedMarkup(html, construct);
  });
}

test('SUPPORTED and the cases above are the same list', () => {
  // The constant exists so it can be walked. If it gains an entry with no case, the loop above
  // fails; this catches the other direction.
  assert.deepStrictEqual(Object.keys(CASES).sort(), [...SUPPORTED].sort());
});

// ---------------------------------------------------------------------------
// Anything OUTSIDE the supported set renders as its own literal text.
// ---------------------------------------------------------------------------

test('a table renders as the pipes the model wrote, not as half a table', () => {
  // Ugly and honest. A reader can still read the cells, which is a far smaller failure than a
  // mis-parsed table with orphaned tags. (the local reviewer, the plan round.)
  const html = renderAnswer('| a | b |\n| --- | --- |\n| 1 | 2 |');

  assert.doesNotMatch(html, /<table|<tr|<td|<th/i, 'a table must not be half-parsed');
  assert.match(html, /\| a \| b \|/, 'the cells must survive as text');
});

test('a blockquote renders as the marker the model wrote', () => {
  const html = renderAnswer('> quoted');

  assert.doesNotMatch(html, /<blockquote/i);
  assert.match(html, /&gt; quoted/, 'the marker survives, escaped');
});

test('a task list renders as text rather than as checkboxes', () => {
  const html = renderAnswer('- [ ] not done\n- [x] done');

  assert.doesNotMatch(html, /<input/i, 'no input element may ever be emitted');
  assert.match(html, /\[ \] not done/);
});

test('an image renders as text — no img element is ever emitted', () => {
  const html = renderAnswer('![alt](https://example.com/x.png)');

  assertOnlyAllowedMarkup(html, 'image');
  assert.match(html, /!/, 'the exclamation mark that made it an image survives');
});

// ---------------------------------------------------------------------------
// THE HOSTILE FILE. Every case here is text from a model that may be quoting
// somebody else's repository. None of it may become markup.
// ---------------------------------------------------------------------------

const HOSTILE: readonly { readonly name: string; readonly markdown: string }[] = [
  { name: 'a script tag', markdown: '<script>alert(1)</script>' },
  { name: 'a script tag inside a heading', markdown: '# <script>alert(1)</script>' },
  { name: 'a script tag inside a list item', markdown: '- <script>alert(1)</script>' },
  { name: 'an img with onerror', markdown: '<img src=x onerror=alert(1)>' },
  { name: 'an iframe', markdown: '<iframe src="https://evil.example"></iframe>' },
  { name: 'a javascript: link', markdown: '[click](javascript:alert(1))' },
  { name: 'a JavaScript: link in mixed case', markdown: '[click](JaVaScRiPt:alert(1))' },
  { name: 'a javascript: link with leading space', markdown: '[click]( javascript:alert(1))' },
  { name: 'a data: link', markdown: '[click](data:text/html;base64,PHNjcmlwdD4=)' },
  { name: 'a vbscript: link', markdown: '[click](vbscript:msgbox(1))' },
  { name: 'a link label carrying a tag', markdown: '[<script>alert(1)</script>](https://example.com)' },
  { name: 'a link target trying to close the attribute', markdown: '[x](https://a" onclick="alert(1))' },
  { name: 'a link target trying to open an attribute with a quote entity', markdown: '[x](https://a&quot;onclick=&quot;alert(1))' },
  { name: 'an unclosed fence', markdown: '```\n<script>alert(1)</script>' },
  { name: 'a tag inside a fence', markdown: '```html\n<script>alert(1)</script>\n```' },
  { name: 'a tag inside a code span', markdown: 'try `<script>alert(1)</script>` here' },
  { name: 'a bracket inside a code span', markdown: 'try `](https://evil.example)` here' },
  { name: 'a bare url', markdown: 'see https://example.com for more' },
  { name: 'a bare url inside a fence', markdown: '```\nhttps://example.com\n```' },
  { name: 'a bare url inside a code span', markdown: 'run `curl https://example.com`' },
  { name: 'an ampersand storm', markdown: '&amp;lt;script&amp;gt;alert(1)&amp;lt;/script&amp;gt;' },
  { name: 'an entity that would decode to a tag', markdown: '&lt;script&gt;alert(1)&lt;/script&gt;' },
  { name: 'a heading level nobody has', markdown: '####### too deep' },
  { name: 'an svg with a handler', markdown: '<svg onload=alert(1)></svg>' },
  { name: 'a style tag', markdown: '<style>body{display:none}</style>' },
  { name: 'a form and an input', markdown: '<form action="https://evil.example"><input name="p"></form>' },
  { name: 'a comment that tries to swallow the page', markdown: '<!-- --><script>alert(1)</script>' },
  { name: 'a null byte beside a scheme', markdown: '[x](java script:alert(1))' },
  { name: 'a newline inside a scheme', markdown: '[x](java\nscript:alert(1))' },
  { name: 'a protocol-relative url', markdown: '[x](//evil.example/x)' },
  { name: 'a file url', markdown: '[x](file:///etc/passwd)' },
  { name: 'a path traversal in a file reference', markdown: '[x](../../../../etc/passwd#L1)' },
  { name: 'an empty answer', markdown: '' },
  { name: 'only whitespace', markdown: '   \n\n   ' },
];

for (const one of HOSTILE) {
  test(`hostile: ${one.name} comes out as text, never as markup`, () => {
    assertOnlyAllowedMarkup(renderAnswer(one.markdown), one.name);
  });
}

test('hostile input is never silently dropped — the words survive', () => {
  // The other half of the safety. Refusing to render something must not mean losing it: a person
  // reading an answer about a script tag needs to see that the answer mentioned one.
  const html = renderAnswer('<script>alert(1)</script>');

  assert.match(html, /alert\(1\)/, 'the text of the refused markup must still be readable');
});

test('a bare url is not autolinked, in prose or anywhere else', () => {
  // The family has shipped this defect once. A model quoting a URL is not a model asking for it to
  // be clickable.
  for (const markdown of ['see https://example.com', '```\nhttps://example.com\n```', 'run `curl https://x.example`']) {
    const html = renderAnswer(markdown);
    assert.doesNotMatch(html, /<a\s/i, `autolinked: ${markdown}`);
  }
});

test('a code span protects everything inside it from every other rule', () => {
  // The rule the bare-url-in-a-fence case depends on: code is taken first and set aside, so nothing
  // inside a backtick pair is a link, emphasis, or anything but characters.
  const html = renderAnswer('use `[x](https://evil.example)` and `**not bold**`');

  assert.doesNotMatch(html, /<a\s/i, 'a link inside a code span must stay text');
  assert.doesNotMatch(html, /<strong>/i, 'emphasis inside a code span must stay text');
  assert.match(html, /<code>\[x\]\(https:\/\/evil\.example\)<\/code>/);
});

test('an unclosed fence keeps what it had said rather than losing it', () => {
  // The ordinary case while an answer is still arriving.
  const html = renderAnswer('```\nconst a = 1;\nconst b = 2;');

  assert.match(html, /const a = 1;/);
  assert.match(html, /const b = 2;/);
});

test('a file reference carries its path and line as DATA, never as an href', () => {
  // A pure function cannot know whether a path exists, and a link that looks live and is dead is
  // worse than text — so the page resolves it on a click. (gemini, the plan round, Blocking.)
  const html = renderAnswer('[there](src/chatPage.ts#L42)');

  assert.match(html, /data-file="src\/chatPage\.ts"/);
  assert.match(html, /data-line="42"/);
  assert.doesNotMatch(html, /<a[^>]*\shref=/i, 'a file reference must not carry an href at all');
});

test('the colon form of a file reference works too', () => {
  const html = renderAnswer('[there](src/chatPage.ts:42)');

  assert.match(html, /data-file="src\/chatPage\.ts"/);
  assert.match(html, /data-line="42"/);
});

test('a code span inside a link label survives, and no sentinel reaches the page', () => {
  // The code round found this and it was real: code spans are held aside FIRST, so a span inside a
  // link label was held, then the link was held with the placeholder inside it, and the single-pass
  // restore never looked inside a held construct. Four raw control characters reached the page and
  // the code span was lost. (gemini, the code round.)
  const html = renderAnswer('see [`renderAnswer`](https://example.com) here');

  assert.match(html, /<code>renderAnswer<\/code>/, 'the code span inside the label must survive');
  assert.doesNotMatch(html, /[\u0000-\u0008]/, 'no placeholder sentinel may reach the page');
  assertOnlyAllowedMarkup(html, 'code span in a link label');
});

test('no answer, however nested, leaks a placeholder sentinel', () => {
  for (const markdown of [
    'see [`a`](https://e.com) and [`b`](https://e.com)',
    '- [`a`](https://e.com)',
    '# [`a`](https://e.com)',
    '**[`a`](https://e.com)**',
    '[`a` and `b`](https://e.com)',
  ]) {
    assert.doesNotMatch(renderAnswer(markdown), /[\u0000-\u0008]/, `sentinel leaked from: ${markdown}`);
  }
});

test('a file reference that climbs out of the workspace is not offered as a link', () => {
  // The host refuses `..` when it resolves one, but a renderer that OFFERS the click is inviting it.
  // Refused here too, so the two halves agree. (gemini, the code round.)
  const html = renderAnswer('[passwd](../../../../etc/passwd#L1)');

  assert.doesNotMatch(html, /data-file/, 'a traversal path must not become a file link');
  assert.match(html, /etc\/passwd/, 'and the text still says what the model wrote');
});

test('an ordered list is not swallowed by an unordered one that follows it', () => {
  // Same indentation, different delimiter, no blank line between: two lists, not one. (gemini.)
  const html = renderAnswer('1. one\n- two');

  assert.match(html, /<ol><li>one<\/li><\/ol>/);
  assert.match(html, /<ul><li>two<\/li><\/ul>/);
});
