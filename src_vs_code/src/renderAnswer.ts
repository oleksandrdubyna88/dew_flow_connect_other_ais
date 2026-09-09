import { Tokens, lexer } from 'marked';
import { escapeHtml } from './webviewHtml';

/**
 * A model's answer, as a document.
 *
 * <p><b>The tokenizer is `marked`; the emitting is ours, and that split is the whole design.</b>
 * Getting markdown's shape right — a nested list, a fence that contains backticks, an emphasis that
 * spans a link — is a lexer's job and a hand-written one gets it wrong the first time a model writes
 * something ordinary. Deciding what HTML comes out is a SAFETY job, and no library's default belongs
 * anywhere near it: this text arrives from another vendor's model and is exactly as trustworthy as
 * anything else somebody else wrote.</p>
 *
 * <p>So nothing here renders a token it does not recognise, no tag is emitted that is not in the
 * list below, and every leaf of text goes through `escapeHtml` on its way out. A raw HTML token —
 * `&lt;script&gt;`, an `onerror` attribute, an `&lt;iframe&gt;` — is not passed through and is not
 * dropped either: it is escaped and shown, because a model that wrote a tag meant to show one.</p>
 *
 * <p><b>No `href` is ever emitted.</b> A link becomes an anchor carrying `data-open` or
 * `data-file`, and the page asks the host to act on it. There is nothing for a `javascript:` URL to
 * be, and a scheme this does not know renders as text rather than as a link nobody can trust. The
 * host validates again on the way in — the page is not a boundary, it is a surface.</p>
 *
 * <p><b>No image, ever.</b> The page's CSP is `default-src 'none'` with no `img-src`, so an
 * `&lt;img&gt;` could only ever be a broken one — and a `src` that reaches out is the whole reason
 * that CSP is written the way it is. An image renders as its alt text.</p>
 *
 * <p>The tags this can produce, and no others: `p`, `h1`-`h6`, `ul`, `ol`, `li`, `pre`, `code`,
 * `blockquote`, `hr`, `strong`, `em`, `del`, `br`, `a`, `table`, `thead`, `tbody`, `tr`, `th`,
 * `td`.</p>
 */

/** How deep a list may nest before the rest is shown as text. A model has never needed more. */
const MAX_DEPTH = 8;

/** A file reference a model writes: `src/foo.ts:12`, `src/foo.ts#L12`, or the path alone. */
const FILE_REFERENCE = /^(?!\/|[A-Za-z]:[\\/])([\w./-]+\.[A-Za-z][\w]*)(?:#L(\d+)|:(\d+))?$/;

interface FileTarget {
  readonly path: string;
  readonly line: number;
}

/**
 * A path this page is willing to offer as a link into the editor.
 *
 * <p>Relative, no traversal, no drive letter, no scheme, and it must look like a file rather than a
 * word with a dot in it. The HOST checks all of this again against the real workspace root: a model
 * asking for `.git/config` or `../../.ssh/id_rsa` is not a hypothetical, and neither end of this is
 * allowed to be the only one that says no.</p>
 */
export function fileTargetOf(href: string): FileTarget | undefined {
  const match = FILE_REFERENCE.exec(href);
  if (match === null || href.includes('..')) {
    return undefined;
  }
  const line = Number(match[2] ?? match[3] ?? 0);

  return { path: match[1] ?? '', line: Number.isFinite(line) ? line : 0 };
}

/** Only these two reach the outside world, and only through the host. */
function isWebLink(href: string): boolean {
  return /^https?:\/\/[^\s]+$/i.test(href);
}

function anchor(attribute: string, value: string, label: string, extra = ''): string {
  return `<a class="link" ${attribute}="${escapeHtml(value)}"${extra}>${label}</a>`;
}

/**
 * Did the model ASK for a link, or did the tokenizer make one out of a sentence?
 *
 * <p>GFM turns a bare address in prose into a link, and this repository has already shipped a defect
 * of exactly that family — a URL inside a person's name that GitHub autolinked. A model writing an
 * address in a sentence has not asked for anything clickable. The two are told apart by what the
 * tokenizer consumed: an explicit link's raw text starts with the bracket the model typed.</p>
 */
function wasWritten(token: Tokens.Link): boolean {
  return token.raw.startsWith('[') || token.raw.startsWith('!');
}

function link(token: Tokens.Link, depth: number): string {
  const label = inline(token.tokens ?? [], depth);
  if (!wasWritten(token)) {
    return escapeHtml(token.raw);
  }
  if (isWebLink(token.href)) {
    return anchor('data-open', token.href, label);
  }
  const file = fileTargetOf(token.href);
  if (file !== undefined) {
    return anchor('data-file', file.path, label, ` data-line="${file.line}"`);
  }

  // Not a scheme this page will act on. The LABEL survives as text — dropping it would lose what the
  // model said, and a blue link that does nothing is worse than no link at all.
  return label;
}

/** One inline token. Anything unrecognised falls back to its own raw text, escaped. */
function inlineToken(token: Tokens.Generic, depth: number): string {
  switch (token.type) {
    case 'text':
      return 'tokens' in token && Array.isArray(token.tokens) && token.tokens.length > 0
        ? inline(token.tokens as Tokens.Generic[], depth)
        : escapeHtml(String(token.text ?? ''));
    case 'escape':
      return escapeHtml(String(token.text ?? ''));
    case 'strong':
      return `<strong>${inline(token.tokens ?? [], depth)}</strong>`;
    case 'em':
      return `<em>${inline(token.tokens ?? [], depth)}</em>`;
    case 'del':
      return `<del>${inline(token.tokens ?? [], depth)}</del>`;
    case 'codespan':
      return `<code>${escapeHtml(String(token.text ?? ''))}</code>`;
    case 'br':
      return '<br>';
    case 'link':
      return link(token as Tokens.Link, depth);
    case 'image':
      return escapeHtml(String((token as Tokens.Image).text ?? (token as Tokens.Image).href ?? ''));
    default:
      return escapeHtml(String(token.raw ?? ''));
  }
}

function inline(tokens: readonly Tokens.Generic[], depth: number): string {
  return tokens.map((token) => inlineToken(token, depth)).join('');
}

function listItems(token: Tokens.List, depth: number): string {
  return token.items
    .map((item) => `<li>${blocks(item.tokens ?? [], depth + 1)}</li>`)
    .join('');
}

function table(token: Tokens.Table, depth: number): string {
  const head = token.header.map((cell) => `<th>${inline(cell.tokens ?? [], depth)}</th>`).join('');
  const body = token.rows
    .map((row) => `<tr>${row.map((cell) => `<td>${inline(cell.tokens ?? [], depth)}</td>`).join('')}</tr>`)
    .join('');

  return `<table><thead><tr>${head}</tr></thead><tbody>${body}</tbody></table>`;
}

function blockToken(token: Tokens.Generic, depth: number): string {
  switch (token.type) {
    case 'space':
      return '';
    case 'heading': {
      // Clamped rather than trusted: `####### seven` is not a heading level, and h7 is not an element.
      const level = Math.min(6, Math.max(1, Number((token as Tokens.Heading).depth) || 1));

      return `<h${level}>${inline(token.tokens ?? [], depth)}</h${level}>`;
    }
    case 'paragraph':
      return `<p>${inline(token.tokens ?? [], depth)}</p>`;
    case 'text':
      // Inside a list item, marked emits the item's prose as a bare text token.
      return 'tokens' in token && Array.isArray(token.tokens)
        ? inline(token.tokens as Tokens.Generic[], depth)
        : escapeHtml(String(token.text ?? ''));
    case 'code': {
      const code = token as Tokens.Code;
      const language = typeof code.lang === 'string' ? code.lang.split(/\s+/)[0] ?? '' : '';
      // The language is a CLASS, never a value that reaches an attribute unfiltered: a fence saying
      // ```" onmouseover=… is a fence a model can write.
      const cssClass = /^[\w+-]{1,24}$/.test(language) ? ` class="language-${language}"` : '';

      return `<pre><code${cssClass}>${escapeHtml(String(code.text ?? ''))}</code></pre>`;
    }
    case 'blockquote':
      return `<blockquote>${blocks(token.tokens ?? [], depth + 1)}</blockquote>`;
    case 'list': {
      const list = token as Tokens.List;
      const start = list.ordered && typeof list.start === 'number' && list.start !== 1
        ? ` start="${Math.trunc(list.start)}"`
        : '';

      return list.ordered
        ? `<ol${start}>${listItems(list, depth)}</ol>`
        : `<ul>${listItems(list, depth)}</ul>`;
    }
    case 'table':
      return table(token as Tokens.Table, depth);
    case 'hr':
      return '<hr>';
    case 'html':
      // Escaped and SHOWN. A model that wrote a tag meant to show one, and this is the one place
      // where a renderer's default — pass it through — would be a hole rather than a feature.
      return `<p>${escapeHtml(String(token.raw ?? ''))}</p>`;
    default:
      return `<p>${escapeHtml(String(token.raw ?? ''))}</p>`;
  }
}

function blocks(tokens: readonly Tokens.Generic[], depth: number): string {
  if (depth > MAX_DEPTH) {
    return escapeHtml(tokens.map((token) => String(token.raw ?? '')).join(''));
  }

  return tokens.map((token) => blockToken(token, depth)).join('');
}

/**
 * Markdown in, safe HTML out. Never throws: a tokenizer that cannot make sense of something answers
 * with the text itself, and so does this — an answer shown as plain text is a bad day, an answer
 * that renders nothing at all is a lost one.
 */
export function renderAnswer(markdown: string): string {
  if (markdown.length === 0) {
    return '';
  }
  try {
    return blocks(lexer(markdown), 0);
  } catch {
    return `<p>${escapeHtml(markdown)}</p>`;
  }
}
