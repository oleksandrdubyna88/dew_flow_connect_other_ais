import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';

import { HIGHLIGHT_CSS, canHighlight, highlight, plainBlock, timesTokenised } from '../codeHighlight';

/**
 * The highlighter, with the security property first and the colours second.
 *
 * <p>The corpus is source code out of somebody's repository, and this page decides what a person
 * believes about which row is which. A skeleton containing `</script>` or a `<style>` block that
 * reached the document as MARKUP could end the page's own script, or hide and re-label rows —
 * and the person would then keep or drop the wrong method. So the escaping tests come before the
 * token tests, and they are written as properties of the OUTPUT rather than as the presence of a
 * particular escape.</p>
 *
 * <p><b>Why that distinction is not pedantry.</b> Shiki escapes `<` as `&#x3C;`, a numeric
 * reference — not `&lt;`. A test asserting `&lt;` would have gone red on entirely correct
 * behaviour, and a test asserting "no raw `<`" would go red on Shiki's own markup. What actually
 * matters is that the dangerous SEQUENCES cannot appear, whatever spelling the escape uses.</p>
 */

/** Everything a skeleton could carry that must never become markup. */
const HOSTILE = [
  '</script>',
  '<style>*{display:none}</style>',
  '<img src=x onerror=alert(1)>',
  '<!-- ${bad} -->',
].join('\n');

/** The part of the output that is the CODE, with Shiki's own wrapper taken off. */
function inside(html: string): string {
  const opens = html.indexOf('<code>');
  const shuts = html.lastIndexOf('</code>');
  assert.ok(opens >= 0 && shuts > opens, 'the highlighter emitted no code element at all');

  return html.slice(opens + '<code>'.length, shuts);
}

const NAMED: Readonly<Record<string, string>> = {
  '&lt;': '<', '&gt;': '>', '&quot;': '"', '&apos;': "'", '&amp;': '&',
};

/**
 * The text between the tags — SCANNED, not stripped with a regex.
 *
 * <p>The first version was `.replace(/<[^>]*>/g, '')` and CodeQL was right to refuse it: one pass
 * of a strip turns `&lt;scr&lt;script&gt;ipt&gt;` into `&lt;script&gt;`, which is the classic incomplete
 * sanitisation. In a TEST that matters more than it looks, not less — a helper that cleans
 * imperfectly can let an assertion pass for the wrong reason, and these are the assertions that say
 * a hostile skeleton cannot become markup.</p>
 *
 * <p>It also has to be right about a `&gt;` that is NOT part of a tag. Shiki escapes `&lt;` and leaves
 * `&gt;` alone, so `Task&lt;List&lt;string&gt;&gt;` reaches here with real `&gt;` characters in the text, and a
 * scanner that simply dropped every `&gt;` would quietly eat the generic this suite exists to prove
 * survives. So: copy up to a `&lt;`, skip to its `&gt;`, repeat — anything else is text.</p>
 */
function textOutsideTags(html: string): string {
  let text = '';
  let at = 0;
  for (;;) {
    const opens = html.indexOf('<', at);
    if (opens < 0) {
      return text + html.slice(at);
    }
    text += html.slice(at, opens);
    const shuts = html.indexOf('>', opens);
    if (shuts < 0) {
      // An unterminated tag: everything after it is markup, not text.
      return text;
    }
    at = shuts + 1;
  }
}

/**
 * The text a reader sees, with every tag stripped and the entities put back.
 *
 * <p>ONE pass over every entity form, rather than a chain of replaces — and that is not tidiness.
 * The two paths escape differently: Shiki writes `&#x26;` for an ampersand and leaves the
 * apostrophe alone, `escapeHtml` writes `&amp;` and `&#39;`. A chain that decoded `&amp;` before
 * the numeric forms would turn `&amp;#39;` into an apostrophe that was never there, so a test
 * about doubling would itself be doing the doubling.</p>
 */
function readable(html: string): string {
  return textOutsideTags(inside(html))
    .replace(/&(?:#x([0-9a-f]+)|#(\d+)|(\w+));/giu, (whole, hex: string, dec: string) => {
      if (hex !== undefined) {
        return String.fromCodePoint(Number.parseInt(hex, 16));
      }
      if (dec !== undefined) {
        return String.fromCodePoint(Number(dec));
      }

      return NAMED[whole.toLowerCase()] ?? whole;
    });
}

for (const language of ['CSharp', 'TypeScript', 'JavaScript', 'Unsupported', '']) {
  test(`hostile markup in a ${language || 'blank'} skeleton renders as text, never as markup`, () => {
    const html = highlight(HOSTILE, language);
    const code = inside(html);

    // The script element the page ships inside. One of these in the output ends it early and
    // everything after becomes markup.
    assert.ok(!code.includes('</script>'), 'a skeleton could close the page\'s own script');
    // A style block can hide rows and re-label them, which this CSP permits — `style-src
    // 'unsafe-inline'` is there for the page's own stylesheet and does not tell one from the other.
    assert.ok(!/<style[\s>]/iu.test(code), 'a skeleton could restyle the page');
    assert.ok(!/<img[\s>]/iu.test(code), 'a skeleton could add an element');
    assert.ok(!/<!--/u.test(code), 'a skeleton could comment out the rest of the row');

    // And the text is all still THERE — an escaper that dropped it would pass every line above.
    assert.ok(readable(html).includes('</script>'), 'the code itself must survive being escaped');
    assert.ok(readable(html).includes('onerror=alert(1)'), 'the code itself must survive being escaped');
  });
}

test('an apostrophe and an ampersand survive a round trip without doubling', () => {
  const code = "var s = 'a & b';";

  assert.equal(readable(highlight(code, 'CSharp')), code);
  assert.equal(readable(highlight(code, 'Unsupported')), code);
});

// --------------------------------------------------------------------------------------------
// It colours the three languages the corpus actually has.
// --------------------------------------------------------------------------------------------

/** Which palette variable a given word came out wearing. */
function tokenOf(html: string, word: string): string {
  const at = html.indexOf(`>${word}<`) >= 0 ? html.indexOf(`>${word}<`) : html.indexOf(word);
  assert.ok(at >= 0, `the highlighter did not emit ${word} at all`);
  const opened = html.lastIndexOf('var(--coai-hl-', at);
  assert.ok(opened >= 0, `${word} carries no colour`);

  return html.slice(opened + 'var(--coai-hl-'.length, html.indexOf(')', opened));
}

test('C# is tokenised by a real grammar, generics and all', () => {
  const html = highlight('public async Task<List<string>> method_1() { return null; } // a note', 'CSharp');

  assert.equal(tokenOf(html, 'public'), 'token-keyword');
  assert.equal(tokenOf(html, '// a note'), 'token-comment');
  // The one a regex highlighter gets wrong: `<` here opens a type argument, not a comparison.
  assert.ok(readable(html).includes('Task<List<string>>'), 'the generic survived tokenising');
});

test('TypeScript and JavaScript are tokenised too, and differently from plain text', () => {
  for (const language of ['TypeScript', 'JavaScript']) {
    const html = highlight('const var_1 = "x"; // note', language);

    assert.equal(tokenOf(html, '// note'), 'token-comment', language);
    assert.match(tokenOf(html, 'const'), /token-/u, language);
  }
});

test('a language the corpus has not seen is rendered plainly rather than refused', () => {
  const html = highlight('SELECT 1', 'Fortran');

  assert.equal(readable(html), 'SELECT 1');
  assert.ok(!html.includes('--coai-hl-token-'), 'nothing is coloured by guesswork');
  // The same wrapper as a highlighted block, so one stylesheet dresses both.
  assert.ok(html.startsWith('<pre class="shiki"'), 'a fallback must not be a differently-shaped row');
  assert.match(html, /data-highlight="plain"/u);
});

/**
 * "We do not read this language" and "this language broke" look identical on screen.
 *
 * <p>Both render uncoloured, so without something in the markup a person cannot tell a deliberate
 * fallback from broken highlighting, and nobody chasing a corpus-extraction defect can tell which
 * rows failed. Two reviewers asked for the distinction independently.</p>
 *
 * <p>Asserted on {@link plainBlock} rather than by forcing a grammar to throw, because there is no
 * input that makes a loaded Shiki grammar throw on demand — see that function's own note. What this
 * covers is that the two reasons are distinguishable and that BOTH escape; what it does not cover
 * is `highlight` routing a real failure to the second one.</p>
 */
test('an unreadable language and a broken grammar are told apart in the markup', () => {
  const nothing = plainBlock('<script>x</script>', 'plain');
  const broken = plainBlock('<script>x</script>', 'failed');

  assert.notEqual(nothing, broken, 'the two silences must not look the same');
  assert.match(nothing, /data-highlight="plain"/u);
  assert.match(broken, /data-highlight="failed"/u);
  // And the difference is one a PERSON can see, not only a parser: uncoloured is uncoloured, so an
  // attribute alone leaves somebody assuming "we do not read this language" when a grammar broke.
  assert.match(broken, /syntax highlighting failed/u);
  assert.doesNotMatch(nothing, /syntax highlighting failed/u,
    'an unsupported language is not a failure and must not claim to be one');
  for (const html of [nothing, broken]) {
    assert.ok(!html.includes('</script>'), 'both paths escape, not just the one in the happy case');
    assert.equal(readable(html), '<script>x</script>');
  }
});

/**
 * The same skeleton is rendered once, however many times the page is drawn.
 *
 * <p>Three reviewers raised the cost and they were right to ask for a number: 200 pairs is 400
 * `codeToHtml` calls, 316–485 ms expanded and 209–222 ms collapsed, and a draw happens after every
 * decision. The corpus does not change between collections, so a redraw was re-rendering text it had
 * already rendered.</p>
 *
 * <p><b>Counted, not compared — and the first version of this test was wrong.</b> It asserted
 * `Object.is(first, second)`, the same string back, and stayed green with the cache deleted: strings
 * in JavaScript are primitives, so `Object.is` compares their value and there is no reference
 * identity to see. The map's SIZE does not tell them apart either, because re-setting an existing
 * key leaves it unchanged. How often Shiki was ASKED is the only externally visible difference
 * between a cache that works and one that does not, which is why `timesTokenised` exists.</p>
 */
test('a skeleton already rendered is not tokenised a second time', () => {
  const code = `public void method_1() { var var_1 = ${Math.random()}; }`;

  const before = timesTokenised();
  const first = highlight(code, 'CSharp');
  const once = timesTokenised();
  highlight(code, 'CSharp');

  assert.equal(once, before + 1, 'the first call must actually tokenise');
  assert.equal(timesTokenised(), once, 'the page re-tokenised a skeleton that had not changed');

  // And the key is the TEXT, so a skeleton that differs by one character is not served the old one…
  assert.notEqual(highlight(`${code} `, 'CSharp'), first);
  // …nor is the same text in a different language.
  assert.notEqual(highlight(code, 'JavaScript'), first);
  assert.equal(timesTokenised(), once + 2, 'both of those are genuinely new work');
});

/**
 * A malformed `language` renders that pair plainly — it does not take the page down.
 *
 * <p>`keyFor` called `.trim()` unconditionally, and the throw happened BEFORE the catch that exists
 * for exactly this, so one row with a null language would have failed `reviewPageHtml()` entirely
 * and shown a person nothing at all. The type says `string`; a page module's safety cannot rest on
 * what its caller currently happens to do. (Code round, codex.)</p>
 */
test('a language that is not a string renders that pair plainly rather than crashing', () => {
  for (const bad of [null, undefined, 42, {}, []]) {
    const html = highlight('var a = 1;', bad as unknown as string);

    assert.match(html, /data-highlight="plain"/u, String(bad));
    assert.equal(readable(html), 'var a = 1;', String(bad));
  }
});

/**
 * Reading a block keeps it, rather than leaving it first in line to be evicted.
 *
 * <p>`Map` iterates in insertion order and `set` on an existing key does not move it, so a cache
 * that only ever inserted would evict the blocks at the TOP of a long review however often they are
 * read — backwards, and three reviewers arrived at it independently. A hit now re-inserts.</p>
 *
 * <p>What this covers is that a block survives other blocks being rendered after it. What it does
 * NOT cover is the eviction order once the byte ceiling is actually reached, which would take four
 * megabytes of markup to reach and is recorded in `research/module_tests.md` rather than asserted.</p>
 */
test('a block stays cached while other blocks are rendered around it', () => {
  const salt = Math.random();
  const first = `void method_1() { var var_1 = ${salt}; }`;

  highlight(first, 'CSharp');
  const after = timesTokenised();
  for (let n = 0; n < 5; n += 1) {
    highlight(`void method_2() { var var_2 = ${salt}_${n}; }`, 'CSharp');
  }
  assert.equal(timesTokenised(), after + 5, 'the five new blocks are new work');

  highlight(first, 'CSharp');
  assert.equal(timesTokenised(), after + 5, 'the first block was evicted or forgotten');
});

test('which languages this page claims to colour is a decision, not a lowercase', () => {
  assert.equal(canHighlight('CSharp'), true);
  assert.equal(canHighlight('TypeScript'), true);
  assert.equal(canHighlight('JavaScript'), true);
  assert.equal(canHighlight('Unsupported'), false);
  assert.equal(canHighlight(''), false);
  // `codeToHtml` THROWS on an id it has not loaded, and a blank one is such an id in disguise —
  // the table is what keeps that off the page rather than in a catch.
  assert.equal(canHighlight('python'), false);
});

/**
 * The table is checked against the PRODUCER, not against itself.
 *
 * <p>Three reviewers made the same point from different directions: a table that only agrees with
 * its own unit tests drifts silently the day the server learns a fourth language, and the page then
 * renders it plain while every test here stays green. `SourceLanguage` is where that decision is
 * actually made, so this reads it — the enum is two projects away and a source read is the only
 * thing that can cross that gap in a suite with no server running.</p>
 *
 * <p>It is a positive pin on a declaration rather than a match on prose, per `testing.md`: it
 * enumerates the enum's members and asserts a verdict for each, so adding `FSharp` to the enum
 * turns this red with the name in the message rather than passing quietly.</p>
 */
test('every language the collector can record has a verdict on this page', () => {
  const enumFile = join(__dirname, '..', '..', '..', 'src_mcp', 'core', 'Normalising', 'IAstNormalizer.cs');
  const declared = /public enum SourceLanguage\s*\{([^}]*)\}/u.exec(readFileSync(enumFile, 'utf8'));
  assert.ok(declared !== null, 'SourceLanguage has moved — this test is reading nothing');

  const members = (declared[1] ?? '')
    .split(',').map((m) => m.trim()).filter((m) => m.length > 0 && !m.startsWith('//'));
  // No hand-typed copy of the list. A reviewer was right that repeating it here would be the very
  // duplication this test exists to remove — and it is not needed, because the loop DERIVES the
  // expectation: a fourth member added to the enum arrives with `canHighlight` false where the loop
  // demands true, and the assertion names it. What IS asserted directly is only that the read found
  // something, since a regex matching nothing would otherwise pass an empty loop in silence.
  assert.ok(members.length >= 2, 'the enum was read but no members came out of it');
  assert.ok(members.includes('Unsupported'), 'the honest "we do not read this one" value is gone');

  for (const member of members) {
    assert.equal(canHighlight(member), member !== 'Unsupported', member);
  }
});

/**
 * Every colour Shiki asks for is a colour this page defines.
 *
 * <p>The failure this catches is invisible: a variable Shiki emits that `HIGHLIGHT_CSS` does not
 * define resolves to nothing, so that token renders in the inherited colour and the block looks
 * *almost* right — one token kind quietly uncoloured, which no screenshot and no existing assertion
 * would notice. One typo in either list does it. A reviewer asked for the tone promise to be checked
 * as behaviour rather than as the presence of a variable; this is the checkable half of that.</p>
 */
test('no token asks for a colour the page never defines', () => {
  const samples: Readonly<Record<string, string>> = {
    CSharp: 'public async Task<List<string>> m(int a = 1) { /* c */ var s = "x"; return null; } // n',
    TypeScript: 'export const a: number = 1; // c\nfunction f<T>(x: T): T { return x; }',
    JavaScript: 'const a = 1; /* c */ class K { m() { return `t${a}`; } }',
  };
  const asked = new Set<string>();
  for (const [language, code] of Object.entries(samples)) {
    for (const found of highlight(code, language).matchAll(/var\(--coai-hl-([a-z0-9-]+)\)/gu)) {
      asked.add(found[1] ?? '');
    }
  }
  const defined = new Set([...HIGHLIGHT_CSS.matchAll(/--coai-hl-([a-z0-9-]+):/gu)].map((m) => m[1]));

  assert.ok(asked.size > 3, 'the samples coloured almost nothing — they are no longer representative');
  assert.deepEqual([...asked].filter((name) => !defined.has(name)), [],
    'a token asks for a variable this page does not define, so it renders uncoloured');
});

test('the palette follows the theme and the tone, rather than baking colours in', () => {
  // The measurement's whole reason for choosing the CSS-variables theme: a stock Shiki theme would
  // have written #1E1E1E into every block and left the tone control doing nothing to the code.
  assert.match(HIGHLIGHT_CSS, /--coai-hl-token-keyword: var\(--vscode-charts-blue/u);
  assert.match(HIGHLIGHT_CSS, /--coai-hl-foreground: var\(--coai-read/u);
  assert.doesNotMatch(highlight('public', 'CSharp'), /#[0-9a-fA-F]{6}/u,
    'a baked colour in the output is a block that ignores the theme');
});
