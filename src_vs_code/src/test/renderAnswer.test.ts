import assert from 'node:assert/strict';
import { test } from 'node:test';
import { fileTargetOf, renderAnswer } from '../renderAnswer';

/**
 * The renderer, and the file that is its safety.
 *
 * <p>Every other test here asks whether a document reads well. The HOSTILE section asks the only
 * question that matters more: this text arrives from another vendor's model, through a CLI, and is
 * exactly as trustworthy as anything else somebody else wrote. This repository has already shipped
 * one defect of that family — a URL inside a user's name that GitHub autolinked — so the rule is not
 * a theory here.</p>
 *
 * <p>The section is meant to GROW. A construct that turns out to be renderable into markup belongs
 * in it before it belongs in the renderer.</p>
 */

/**
 * Every tag name the output contains.
 *
 * <p>The first version of the hostile tests matched on words — `onerror`, `javascript:` — and that
 * is the wrong question twice over: escaped text that merely MENTIONS `onerror` is exactly what
 * should be shown, and a hostile tag this list has not thought of would pass. The question is which
 * ELEMENTS came out, and the answer must always be a subset of what the renderer is allowed to
 * emit.</p>
 */
const ALLOWED = new Set([
  'p', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'ul', 'ol', 'li', 'pre', 'code', 'blockquote', 'hr',
  'strong', 'em', 'del', 'br', 'a', 'table', 'thead', 'tbody', 'tr', 'th', 'td',
]);

function tagsIn(html: string): string[] {
  return [...html.matchAll(/<\/?([a-zA-Z][a-zA-Z0-9]*)/g)].map((match) => (match[1] ?? '').toLowerCase());
}

function assertOnlyAllowedTags(html: string, what: string): void {
  for (const tag of tagsIn(html)) {
    assert.ok(ALLOWED.has(tag), `<${tag}> reached the page from: ${what}`);
  }
  // Only inside REAL tags. Scanning the whole string flags escaped text that merely mentions
  // `onerror` — which is precisely the text that should be shown, and the first version of this
  // helper failed on exactly that. Escaped markup has no `<`, so this cannot reach it.
  for (const tag of html.match(/<[a-zA-Z][^>]*>/g) ?? []) {
    assert.doesNotMatch(tag, / on[a-z]+=/i, `an event attribute reached the page from: ${what}`);
    assert.doesNotMatch(tag, /\ssrc=|\shref=/i, `a loading or navigating attribute reached the page from: ${what}`);
  }
}

test('a heading, a paragraph and emphasis come out as themselves', () => {
  const html = renderAnswer('### Итог\n\nThe reviewers are **read-only** and *stay* so.\n');

  assert.match(html, /<h3>Итог<\/h3>/);
  assert.match(html, /<strong>read-only<\/strong>/);
  assert.match(html, /<em>stay<\/em>/);
});

test('a heading deeper than six is clamped, because h7 is not an element', () => {
  assert.match(renderAnswer('####### seven\n'), /<h6>|<p>/);
  assert.doesNotMatch(renderAnswer('####### seven\n'), /<h7/);
});

test('lists nest, and an ordered list keeps its own numbering', () => {
  // The one construct a hand-written renderer was going to be wrong about, which is why the
  // tokenizing is not hand-written.
  const html = renderAnswer('- one\n  - inner\n    - deeper\n- two\n');

  assert.match(html, /<ul><li>one<ul><li>inner<ul><li>deeper<\/li><\/ul><\/li><\/ul><\/li>/);

  const ordered = renderAnswer('3. three\n4. four\n');
  assert.match(ordered, /<ol start="3"><li>three<\/li><li>four<\/li><\/ol>/);
});

test('a fenced block keeps its text verbatim, entities and all', () => {
  // The case that decided the whole design: escaping the string BEFORE parsing turns `<` into
  // `&lt;` inside the fence, and the reader is shown an entity the model never wrote.
  const html = renderAnswer('```js\nif (a < b && c > d) { return "x"; }\n```\n');

  assert.match(html, /<pre><code class="language-js">/);
  assert.match(html, /if \(a &lt; b &amp;&amp; c &gt; d\)/);
  assert.doesNotMatch(html, /&amp;lt;/, 'the fence was escaped twice');
});

test('a language that is not a word never reaches the class attribute', () => {
  const html = renderAnswer('```" onmouseover="alert(1)\nx\n```\n');

  assert.match(html, /<pre><code>/, 'a hostile language string was used as a class');
  assert.doesNotMatch(html, /onmouseover/, 'the language string reached the markup');
});

test('a code span keeps its backticked text as text', () => {
  assert.match(renderAnswer('run `git merge-base --is-ancestor`\n'), /<code>git merge-base --is-ancestor<\/code>/);
});

test('a table, a rule and a quote each become themselves', () => {
  assert.match(renderAnswer('| a | b |\n|---|---|\n| 1 | 2 |\n'), /<table><thead><tr><th>a<\/th>/);
  assert.match(renderAnswer('---\n'), /<hr>/);
  assert.match(renderAnswer('> quoted\n'), /<blockquote><p>quoted<\/p><\/blockquote>/);
});

test('an http link becomes an anchor the HOST is asked to open, and carries no href', () => {
  const html = renderAnswer('see [the docs](https://example.com/a?b=1)\n');

  assert.match(html, /<a class="link" data-open="https:\/\/example\.com\/a\?b=1">the docs<\/a>/);
  assert.doesNotMatch(html, /href=/, 'the page carries a live href');
  assert.doesNotMatch(html, /src=/, 'the page carries something that loads');
});

test('a file reference becomes an anchor naming the file and the line', () => {
  assert.match(
    renderAnswer('[chatPage.ts:181](src_vs_code/src/chatPage.ts:181)\n'),
    /data-file="src_vs_code\/src\/chatPage\.ts" data-line="181"/,
  );
  assert.match(
    renderAnswer('[it](src/chatPage.ts#L42)\n'),
    /data-file="src\/chatPage\.ts" data-line="42"/,
  );
});

test('a link this page will not act on keeps its words and loses its link', () => {
  for (const href of ['javascript:alert(1)', 'data:text/html,<script>x</script>', 'vbscript:x',
    'file:///etc/passwd', '../../.ssh/id_rsa', '/etc/passwd', 'C:\\Windows\\win.ini',
    'mailto:someone@example.com']) {
    const html = renderAnswer(`[press here](${href})\n`);

    assert.match(html, /press here/, `${href} lost the words the model wrote`);
    assert.doesNotMatch(html, /<a /, `${href} was rendered as a link`);
  }
});

test('fileTargetOf refuses everything that is not a plain relative file', () => {
  assert.deepStrictEqual(fileTargetOf('src/a.ts#L3'), { path: 'src/a.ts', line: 3 });
  assert.deepStrictEqual(fileTargetOf('a.ts'), { path: 'a.ts', line: 0 });
  // A file without a dot is still a file, and the two ends of this must agree about that: the
  // renderer used to demand an extension where the host's check did not, so `Dockerfile` was never
  // offered as a link by a page whose host would happily have opened it.
  assert.deepStrictEqual(fileTargetOf('Dockerfile'), { path: 'Dockerfile', line: 0 });
  assert.deepStrictEqual(fileTargetOf('LICENSE#L2'), { path: 'LICENSE', line: 2 });
  // `no-extension` was in this list until the code round pointed out that the renderer and the host
  // disagreed about what a path is — Dockerfile and LICENSE are files, and the host would have
  // opened them. What is refused is what ESCAPES, not what lacks a dot. `.git/config` is a path this
  // will offer; it is inside the workspace, and refusing it here would be a policy invented in the
  // wrong place — the host decides what exists.
  for (const bad of ['../a.ts', 'a/../../b.ts', '/abs/a.ts', 'C:/x/a.ts', 'C:\\x\\a.ts',
    'https://e.com/a.ts']) {
    assert.strictEqual(fileTargetOf(bad), undefined, `${bad} was accepted as a workspace file`);
  }
});

/* ------------------------------------------------------------------------------------------------
 * HOSTILE. Everything below is text a model can write, and none of it may become markup.
 * ---------------------------------------------------------------------------------------------- */

test('raw HTML in an answer is shown, not run', () => {
  for (const attack of [
    '<script>alert(1)</script>',
    '<img src=x onerror=alert(1)>',
    '<iframe src="https://example.com"></iframe>',
    '<svg onload=alert(1)>',
    '<a href="javascript:alert(1)">x</a>',
    '<style>body{display:none}</style>',
    '<div onclick="alert(1)">click</div>',
  ]) {
    const html = renderAnswer(`${attack}\n`);

    assertOnlyAllowedTags(html, attack);
    assert.match(html, /&lt;/, `this was silently dropped instead of shown: ${attack}`);
  }
});

test('raw HTML inside a paragraph, a list and a quote is escaped too', () => {
  for (const markdown of [
    'text <img src=x onerror=alert(1)> more',
    '- <script>alert(1)</script>',
    '> <iframe src=x></iframe>',
    '| a |\n|---|\n| <script>alert(1)</script> |',
  ]) {
    assertOnlyAllowedTags(renderAnswer(markdown + '\n'), markdown);
  }
});

test('a closing script tag inside a fence cannot end the page script', () => {
  const html = renderAnswer('```\n</script><script>alert(1)</script>\n```\n');

  assert.doesNotMatch(html, /<\/script>/i, 'a fence closed the page script');
  assert.match(html, /&lt;\/script&gt;/);
});

test('an unclosed fence, an unclosed emphasis and a lone bracket render as text rather than nothing', () => {
  for (const markdown of ['```js\nnever closed', '**never closed', '[label](', 'a ] b', '`open']) {
    const html = renderAnswer(markdown);

    assert.notStrictEqual(html.length, 0, `nothing at all came out of: ${markdown}`);
    assert.doesNotMatch(html, /<script|onerror/i);
  }
});

test('a bare URL in prose is not turned into a link', () => {
  // The defect this family already shipped once, from the other direction: GitHub autolinked a URL
  // inside a user's name. A model writing an address in a sentence has not asked for a link.
  const html = renderAnswer('go to https://evil.example.com and sign in\n');

  assert.doesNotMatch(html, /<a /, 'a bare URL was autolinked');
  assert.match(html, /https:\/\/evil\.example\.com/);
});

test('an image renders as its words and never as an element', () => {
  const html = renderAnswer('![a screenshot](https://example.com/a.png)\n');

  assert.doesNotMatch(html, /<img/i, 'an image element reached a page whose CSP forbids loading one');
  assert.match(html, /a screenshot/);
});

test('a link whose LABEL is hostile is escaped like any other text', () => {
  const html = renderAnswer('[<script>alert(1)</script>](https://example.com)\n');

  assert.doesNotMatch(html, /<script/i);
  assert.match(html, /data-open="https:\/\/example\.com"/);
});

test('a link href with a quote cannot break out of the attribute', () => {
  const html = renderAnswer('[x](https://example.com/"onmouseover="alert(1))\n');

  assert.doesNotMatch(html, /onmouseover="alert/, 'the href escaped its attribute');
});

test('a list nested past any sane depth degrades to text instead of recursing', () => {
  const deep = Array.from({ length: 30 }, (_, i) => `${' '.repeat(i * 2)}- level ${i}`).join('\n');
  const html = renderAnswer(deep + '\n');

  assert.ok(html.length > 0, 'a deeply nested list rendered nothing');
  assert.doesNotMatch(html, /<script/i);
});

test('an empty answer renders nothing rather than an empty paragraph', () => {
  assert.strictEqual(renderAnswer(''), '');
});


test('nothing empty is wrapped in a paragraph of its own', () => {
  // An empty <p> is vertical space the model did not ask for, between things it did.
  for (const markdown of ['<span></span>\n', 'text\n\n\n\nmore\n']) {
    assert.doesNotMatch(renderAnswer(markdown), /<p>\s*<\/p>/, `an empty paragraph came out of: ${markdown}`);
  }
});

test('a form, an input and a comment that tries to swallow the page are all just text', () => {
  // Carried across from a parallel implementation of this plan (closed as a duplicate of the branch
  // that shipped): its hostile file had two cases mine did not. A form posting somewhere is the one
  // shape that could ask a person for something and send it away, and an HTML comment is the classic
  // way to try to swallow the markup that follows it.
  for (const attack of [
    '<form action="https://evil.example"><input name="p"></form>',
    '<!-- --><script>alert(1)</script>',
  ]) {
    const html = renderAnswer(`${attack}\n`);

    assertOnlyAllowedTags(html, attack);
    assert.match(html, /&lt;/, `this was silently dropped instead of shown: ${attack}`);
  }
});
