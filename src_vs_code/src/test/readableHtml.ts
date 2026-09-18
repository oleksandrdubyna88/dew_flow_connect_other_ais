/**
 * What a reader SEES in a fragment of markup: the tags gone, the entities put back.
 *
 * <p>Extracted from `codeHighlight.test.ts` when the review page started rendering reviewer prose
 * (story 2.1) and its test needed the same reading of the same escaping property — "the dangerous
 * sequence cannot appear as markup AND the text is still all there". A second copy of a scanner
 * that has already been corrected once (see below) is the thing most likely to be corrected once
 * and not twice.</p>
 */

/**
 * The text between the tags — SCANNED, not stripped with a regex.
 *
 * <p>The first version was `.replace(/<[^>]*>/g, '')` and CodeQL was right to refuse it: one pass
 * of a strip turns `&lt;scr&lt;script&gt;ipt&gt;` into `&lt;script&gt;`, which is the classic incomplete
 * sanitisation. In a TEST that matters more than it looks, not less — a helper that cleans
 * imperfectly can let an assertion pass for the wrong reason, and these are the assertions that
 * say a hostile value cannot become markup.</p>
 *
 * <p>It also has to be right about a `&gt;` that is NOT part of a tag. Shiki escapes `&lt;` and leaves
 * `&gt;` alone, so `Task&lt;List&lt;string&gt;&gt;` reaches here with real `&gt;` characters in the text, and a
 * scanner that simply dropped every `&gt;` would quietly eat the generic that suite exists to prove
 * survives. So: copy up to a `&lt;`, skip to its `&gt;`, repeat — anything else is text.</p>
 */
export function textOutsideTags(html: string): string {
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

const NAMED: Readonly<Record<string, string>> = {
  '&lt;': '<', '&gt;': '>', '&quot;': '"', '&apos;': "'", '&amp;': '&',
};

/**
 * The entities put back, in ONE pass over every form — and that is not tidiness.
 *
 * <p>The two escaping paths on the review page spell differently: Shiki writes `&#x26;` for an
 * ampersand and leaves the apostrophe alone, `escapeHtml` writes `&amp;` and `&#39;`. A chain that
 * decoded `&amp;` before the numeric forms would turn `&amp;#39;` into an apostrophe that was never
 * there, so a test about doubling would itself be doing the doubling.</p>
 */
export function unescaped(text: string): string {
  return text.replace(/&(?:#x([0-9a-f]+)|#(\d+)|(\w+));/giu, (whole, hex: string, dec: string) => {
    if (hex !== undefined) {
      return String.fromCodePoint(Number.parseInt(hex, 16));
    }
    if (dec !== undefined) {
      return String.fromCodePoint(Number(dec));
    }

    return NAMED[whole.toLowerCase()] ?? whole;
  });
}

/** The text a reader sees in a fragment: tags gone, entities decoded. */
export const readable = (html: string): string => unescaped(textOutsideTags(html));
