import { escapeHtml } from './webviewHtml';

/**
 * A model's answer, as a document.
 *
 * <p>An answer arrives as markdown and reached the tab as the raw characters under `pre-wrap` — a
 * numbered list as `1.` and `2.` down the left margin, a code block as backticks, a heading as
 * hashes. This turns it into the four or five elements it actually meant.</p>
 *
 * <h2>The safety, which is the reason this file exists at all</h2>
 *
 * <p><b>The input is UNTRUSTED.</b> `helpPage.ts`'s `bodyHtml` marks up text this repository wrote;
 * this marks up whatever another vendor's model emitted, which may be quoting an attacker's README.
 * So the rule is stronger than that file's, and it is a rule about ORDER OF OPERATIONS:</p>
 *
 * <p><b>Tokenise the RAW text, then escape at emission, per context.</b> Never the other way round.
 * The tempting shortcut — escape the whole string first, then run markup rules over the result — was
 * put to the review gate and refused for a specific reason worth keeping: every parsing rule would
 * then be running over entity-laden text, where a `>` blockquote marker has become `&gt;`, a code
 * span carrying `&` has become `&amp;`, and a backslash-escaped asterisk is no longer adjacent to
 * what it escaped. The rules get harder to state and each one is a new chance to be wrong about
 * somebody's code. Tokenising first means every rule sees the characters the model actually wrote,
 * and escaping happens once, at the point where a token becomes HTML, knowing whether it is going
 * into text or into an attribute.</p>
 *
 * <p>Two contexts, two escapes, and they are not interchangeable:</p>
 * <ul>
 *   <li><b>Text</b> — `escapeHtml`, the same one the rest of this extension uses.</li>
 *   <li><b>An `href`</b> — a SCHEME ALLOW-LIST first, then `escapeHtml`. A URL that is not
 *       `http:` or `https:` never becomes an attribute at all; it stays the text the model wrote.
 *       That is what keeps `javascript:`, `data:` and `vbscript:` out by construction rather than by
 *       a filter that has to enumerate them.</li>
 * </ul>
 *
 * <p>Nothing here emits `<img>`, and nothing autolinks a bare URL — this family has shipped that
 * defect once. A URL becomes a link only when the model wrote it as one, `[text](url)`.</p>
 *
 * <h2>What is supported, and what happens to everything else</h2>
 *
 * <p>{@link SUPPORTED} is the whole list, and it is a constant rather than a sentence in a comment
 * so that a test can walk it. Anything outside it is NOT parsed and NOT dropped: it renders as the
 * literal characters the model wrote. A markdown table therefore appears as its pipes, which is
 * ugly and honest, and is a far smaller failure than a half-parsed one — a reader can still read the
 * cells. That is the deliberate trade for not carrying a CommonMark parser.</p>
 */

/**
 * Every construct this renderer understands.
 *
 * <p>A constant, so the test file can assert one case per entry and the list cannot quietly drift
 * from what the code does. Anything absent from here renders as literal text, by design.</p>
 */
export const SUPPORTED = [
  'heading',
  'paragraph',
  'unordered-list',
  'ordered-list',
  'nested-list',
  'fenced-code',
  'thematic-break',
  'code-span',
  'bold',
  'italic',
  'link',
  'file-reference',
] as const;

/**
 * A file reference the PAGE may turn into a click — `path#L12` or `path:12`.
 *
 * <p>It requires an extension, so a bare word is not a file, and it refuses anything that is not
 * made of path characters. A traversal like `../../etc/passwd` fails it for want of an extension,
 * which is the answer this renderer wants anyway: the host refuses `..` when it resolves a path, and
 * a renderer that OFFERED the click would be inviting a refusal.</p>
 */
const FILE_REFERENCE = /^(?!https?:)([\w./\\-]+\.\w{1,12})(?:#L(\d{1,7})|:(\d{1,7}))$/;

/**
 * The placeholder marker for a construct held aside while the other rules run.
 *
 * <p>Composed from a control character rather than written as a word, and the difference matters:
 * the input is STRIPPED of this marker before anything else happens, so a marker a person could
 * type would be silently deleted from their own answer. It is built with {@link String.fromCharCode}
 * rather than embedded so that this file stays plain ASCII and git does not read it as binary.</p>
 */
const MARK = `${String.fromCharCode(1)}coai${String.fromCharCode(1)}`;

/** Only these become an `href`. Everything else stays the text the model wrote. */
function isOpenable(url: string): boolean {
  const trimmed = url.trim().toLowerCase();

  return trimmed.startsWith('http://') || trimmed.startsWith('https://');
}

/** One block of the answer, already told apart from its neighbours. */
type Block =
  | { readonly kind: 'heading'; readonly level: number; readonly text: string }
  | { readonly kind: 'paragraph'; readonly text: string }
  | { readonly kind: 'code'; readonly language: string; readonly lines: readonly string[] }
  | { readonly kind: 'rule' }
  | { readonly kind: 'list'; readonly ordered: boolean; readonly items: readonly Item[] };

interface Item {
  readonly text: string;
  readonly children: readonly Item[];
  readonly ordered: boolean;
}

/**
 * The answer, as HTML.
 *
 * <p>Pure: a string in, a string out, no host and no filesystem. That is what lets the hostile-input
 * file drive it directly, and it is also why a file REFERENCE is emitted as a marker rather than as
 * a link — this function cannot know whether a path exists, and a gate reviewer was right that
 * pretending otherwise would be a promise it has no way to keep. The page resolves it on a click.</p>
 */
export function renderAnswer(markdown: string): string {
  return blocksOf(markdown).map(html).join('\n');
}

/** Split into blocks on the raw text, before anything has been escaped. */
function blocksOf(markdown: string): Block[] {
  const lines = markdown.replace(/\r\n?/g, '\n').split('\n');
  const blocks: Block[] = [];
  let at = 0;

  while (at < lines.length) {
    const line = lines[at] ?? '';
    if (line.trim().length === 0) {
      at += 1;
      continue;
    }
    const fence = /^\s*(`{3,}|~{3,})\s*(\S*)\s*$/.exec(line);
    if (fence !== null) {
      const [, marker = '```', language = ''] = fence;
      const body: string[] = [];
      at += 1;
      // Compiled ONCE, outside the loop. It was being rebuilt for every line of the block, which is
      // a fresh regex compilation per line of a model's five-thousand-line log paste, on the
      // extension host's thread. (gemini, the code round.)
      const closing = new RegExp(`^\\s*${marker[0] === '`' ? '`' : '~'}{3,}\\s*$`);
      // An UNCLOSED fence runs to the end of the answer rather than being abandoned. A model that
      // stops mid-block is the ordinary case while an answer is still arriving, and dropping the
      // text would hide what it had already said.
      while (at < lines.length && !closing.test(lines[at] ?? '')) {
        body.push(lines[at] ?? '');
        at += 1;
      }
      at += 1;
      blocks.push({ kind: 'code', language, lines: body });
      continue;
    }
    if (/^\s*(?:-{3,}|\*{3,}|_{3,})\s*$/.test(line)) {
      blocks.push({ kind: 'rule' });
      at += 1;
      continue;
    }
    const heading = /^(#{1,6})\s+(.*)$/.exec(line);
    if (heading !== null) {
      blocks.push({ kind: 'heading', level: (heading[1] ?? '#').length, text: (heading[2] ?? '').trim() });
      at += 1;
      continue;
    }
    if (bulletOf(line) !== undefined) {
      const taken = listAt(lines, at);
      blocks.push(taken.block);
      at = taken.next;
      continue;
    }
    const paragraph: string[] = [];
    while (at < lines.length) {
      const next = lines[at] ?? '';
      if (next.trim().length === 0 || bulletOf(next) !== undefined || /^(#{1,6})\s+/.test(next)
        || /^\s*(`{3,}|~{3,})/.test(next) || /^\s*(?:-{3,}|\*{3,}|_{3,})\s*$/.test(next)) {
        break;
      }
      paragraph.push(next.trim());
      at += 1;
    }
    blocks.push({ kind: 'paragraph', text: paragraph.join(' ') });
  }

  return blocks;
}

/** A list line, told apart from a thematic break and from a paragraph that begins with a dash. */
function bulletOf(line: string): { indent: number; ordered: boolean; text: string } | undefined {
  const unordered = /^(\s*)[-*+]\s+(.*)$/.exec(line);
  if (unordered !== null && !/^\s*(?:-{3,}|\*{3,})\s*$/.test(line)) {
    return { indent: (unordered[1] ?? '').length, ordered: false, text: unordered[2] ?? '' };
  }
  const ordered = /^(\s*)\d{1,9}[.)]\s+(.*)$/.exec(line);

  return ordered === null
    ? undefined
    : { indent: (ordered[1] ?? '').length, ordered: true, text: ordered[2] ?? '' };
}

/**
 * One list, with ONE level of nesting.
 *
 * <p>The bound is deliberate and it is in {@link SUPPORTED}. Deeper indentation is folded into the
 * nested level rather than being parsed further, so a three-deep list reads as two-deep instead of
 * as broken markup — the words all survive, the shape is flattened.</p>
 */
function listAt(lines: readonly string[], from: number): { block: Block; next: number } {
  const first = bulletOf(lines[from] ?? '');
  const ordered = first?.ordered === true;
  const baseIndent = first?.indent ?? 0;
  // Built with a mutable child array per item rather than by cloning the parent's children and
  // popping-and-repushing it on every nested line, which copied the array once per child and cost
  // O(N²) on a long checklist. (gemini, the code round.)
  const items: { text: string; ordered: boolean; children: Item[] }[] = [];
  let at = from;

  while (at < lines.length) {
    const bullet = bulletOf(lines[at] ?? '');
    if (bullet === undefined) {
      break;
    }
    if (bullet.indent > baseIndent) {
      const parent = items[items.length - 1];
      if (parent === undefined) {
        break;
      }
      parent.children.push({ text: bullet.text, children: [], ordered: bullet.ordered });
      at += 1;
      continue;
    }
    if (bullet.indent < baseIndent) {
      break;
    }
    // A DIFFERENT delimiter at the same indent starts a different list. `1. one` followed by
    // `- two` with no blank line is two lists, and swallowing the second put an unordered item
    // inside an <ol>. (gemini, the code round.)
    if (bullet.ordered !== ordered) {
      break;
    }
    items.push({ text: bullet.text, ordered: bullet.ordered, children: [] });
    at += 1;
  }

  return { block: { kind: 'list', ordered, items }, next: at };
}

function html(block: Block): string {
  if (block.kind === 'rule') {
    return '<hr>';
  }
  if (block.kind === 'heading') {
    // Clamped, so a model writing `####### x` cannot ask for an element that does not exist.
    const level = Math.min(Math.max(block.level, 1), 6);

    return `<h${level}>${inline(block.text)}</h${level}>`;
  }
  if (block.kind === 'code') {
    // A code block is TEXT, whole. Nothing inside it is markup, nothing inside it is a link, and
    // the language is a class rather than anything that reaches an attribute unescaped.
    const language = /^[\w+-]{1,24}$/.test(block.language)
      ? ` class="language-${escapeHtml(block.language)}"`
      : '';

    return `<pre><code${language}>${escapeHtml(block.lines.join('\n'))}</code></pre>`;
  }
  if (block.kind === 'list') {
    return listHtml(block.items, block.ordered);
  }

  return `<p>${inline(block.text)}</p>`;
}

function listHtml(items: readonly Item[], ordered: boolean): string {
  const tag = ordered ? 'ol' : 'ul';
  const rendered = items
    .map((item) => {
      const nested = item.children.length > 0
        ? listHtml(item.children, item.children[0]?.ordered ?? false)
        : '';

      return `<li>${inline(item.text)}${nested}</li>`;
    })
    .join('');

  return `<${tag}>${rendered}</${tag}>`;
}

/**
 * The inline constructs, over RAW text, emitting escaped HTML.
 *
 * <p>Code spans are taken FIRST and their contents are escaped and set aside, so nothing inside a
 * backtick pair is ever treated as a link, as emphasis, or as anything but the characters it is.
 * That is the rule a bare URL inside a code fence depends on, and it is why the placeholder carries
 * an index rather than the text — a substitution that re-inserted text before the other rules ran
 * would put it back in their way.</p>
 */
function inline(text: string): string {
  const held: string[] = [];
  const hold = (rendered: string): string => {
    held.push(rendered);

    return `${MARK}${held.length - 1}${MARK}`;
  };

  // The sentinel is removed from the INPUT first, so a model cannot forge a placeholder and have
  // this function paste one of its own held constructs somewhere it chose.
  const clean = text.split(MARK).join('');

  // 1. Code spans first, so that nothing inside a backtick pair is seen by any rule below it. This
  //    is what a bare URL inside a fence, and a `](` inside a code span, both depend on.
  const withoutCode = clean.replace(/(`+)([^`]*?)\1/g, (whole, _ticks: string, body: string) =>
    (body.trim().length === 0 ? whole : hold(`<code>${escapeHtml(body)}</code>`)));

  // 2. Links, built from the RAW target and label and held aside fully formed.
  const withoutLinks = links(withoutCode, hold);

  // 3. Everything still in the string is plain TEXT, and this is the only place it is escaped.
  //
  //    <p><b>Getting this line wrong is not theoretical.</b> The first version of this function did
  //    not have it — it tokenised correctly and then emitted the surrounding text raw — and the
  //    hostile-input file caught `<script>alert(1)</script>` reaching the page on the very first
  //    run. Nothing above puts HTML into this string, because the two constructs that produce HTML
  //    are held aside, so this cannot double-escape a tag of ours.</p>
  const escaped = escapeHtml(withoutLinks);

  // 4. Emphasis, over the escaped text. Safe in this order because `escapeHtml` touches only
  //    `& < > " '`, and an asterisk is none of them — the markers are exactly where they were.
  const emphasised = escaped
    .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
    .replace(/(^|[^*\w])\*([^*\s][^*]*)\*(?=$|[^*\w])/g, '$1<em>$2</em>');

  // 5. Put the held constructs back, REPEATEDLY, until none is left.
  //
  //    A single pass is not enough, and the code round found the case: a code span inside a link
  //    LABEL is held first, then the link is held with that placeholder inside it, so one pass over
  //    the outer string restores the link and leaves the span's placeholder sitting inside it —
  //    four raw control characters on the page, and the code span lost. The loop is bounded by the
  //    number of held constructs, because each pass consumes at least one and no pass can add one.
  //    (gemini, the code round.)
  const marker = new RegExp(`${MARK}(\\d+)${MARK}`, 'g');
  let out = emphasised;
  for (let pass = 0; pass <= held.length; pass += 1) {
    const next = out.replace(marker, (_whole, index: string) => held[Number(index)] ?? '');
    if (next === out) {
      break;
    }
    out = next;
  }

  return out;
}

/**
 * `[text](target)` — and nothing else becomes a link.
 *
 * <p>No autolinking: a bare `https://…` in prose stays prose. The family shipped that defect once,
 * and a model quoting a URL is not a model asking for it to be clickable.</p>
 *
 * <p>Three outcomes, and the third is the one that matters. An `http(s)` target becomes an anchor.
 * A FILE reference becomes an anchor with no `href` at all — it carries the path and the line as
 * data, and the page decides on a click whether that file exists, because this function cannot know
 * and a link that looks live and is dead is worse than text. Anything else — `javascript:`, `data:`,
 * a protocol-relative `//host`, a scheme nobody has heard of — is left ALONE, brackets and all, to
 * be escaped as ordinary text by the caller. Leaving it rather than escaping it here is deliberate:
 * one escape, in one place, is the property this file is built on.</p>
 */
function links(text: string, hold: (rendered: string) => string): string {
  return text.replace(/\[([^\]\n]*)\]\(([^)\s]*)\)/g, (whole, label: string, target: string) => {
    if (isOpenable(target)) {
      return hold(`<a class="link" href="${escapeHtml(target)}">${escapeHtml(label)}</a>`);
    }
    const file = FILE_REFERENCE.exec(target);
    if (file !== null) {
      const path = file[1] ?? '';
      const line = file[2] ?? file[3] ?? '';

      return hold(
        `<a class="link file" data-file="${escapeHtml(path)}" data-line="${escapeHtml(line)}">`
        + `${escapeHtml(label)}</a>`,
      );
    }

    return whole;
  });
}
