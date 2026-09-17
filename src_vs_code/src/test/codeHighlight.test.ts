import assert from 'node:assert/strict';
import { test } from 'node:test';

import { HIGHLIGHT_CSS, canHighlight, highlight } from '../codeHighlight';

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
 * The text a reader sees, with every tag stripped and the entities put back.
 *
 * <p>ONE pass over every entity form, rather than a chain of replaces — and that is not tidiness.
 * The two paths escape differently: Shiki writes `&#x26;` for an ampersand and leaves the
 * apostrophe alone, `escapeHtml` writes `&amp;` and `&#39;`. A chain that decoded `&amp;` before
 * the numeric forms would turn `&amp;#39;` into an apostrophe that was never there, so a test
 * about doubling would itself be doing the doubling.</p>
 */
function readable(html: string): string {
  return inside(html)
    .replace(/<[^>]*>/gu, '')
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

test('the palette follows the theme and the tone, rather than baking colours in', () => {
  // The measurement's whole reason for choosing the CSS-variables theme: a stock Shiki theme would
  // have written #1E1E1E into every block and left the tone control doing nothing to the code.
  assert.match(HIGHLIGHT_CSS, /--coai-hl-token-keyword: var\(--vscode-charts-blue/u);
  assert.match(HIGHLIGHT_CSS, /--coai-hl-foreground: var\(--coai-read/u);
  assert.doesNotMatch(highlight('public', 'CSharp'), /#[0-9a-fA-F]{6}/u,
    'a baked colour in the output is a block that ignores the theme');
});
