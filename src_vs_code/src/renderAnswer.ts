import { Tokens, lexer } from 'marked';
import { escapeHtml } from './webviewHtml';
import { isConfinedRelativePath } from './chatMessages';

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
 * `td`, `button`.</p>
 *
 * <p><b>`button` is the one tag here that is OURS rather than the model's.</b> Every fenced block and
 * every blockquote this draws gets a copy control of its own underneath it, because the only control
 * an answer had copied the whole of it — a page and a half, to take the ten lines at the end. Model
 * text can never become one: a raw html token is escaped and shown, so it has no `&lt;` left by the
 * time it is output. The shape of every button emitted is pinned by its own test, which is what keeps
 * the allow-list sharp after widening it.</p>
 *
 * <p><b>One fence tag is reserved: ```` ```reply ````.</b> A block opened with it reads *Copy the
 * reply prompt* instead of *Copy block*, and nothing else about it changes. The product recognises
 * the tag and never asks for it — what makes a model write one is the person's own prompt. That split
 * is deliberate and it was measured: across 22 stored conversations the heading a model puts above
 * such a block had FOURTEEN spellings, a fifth of them used no fence at all, and ```` ```text ````
 * already marked both a reply prompt and an ordinary block. Anything guessed from that is wrong about
 * a fifth of the time while looking certain, so the product owns a marker instead of a heuristic.</p>
 */

/** How deep a list may nest before the rest is shown as text. A model has never needed more. */
const MAX_DEPTH = 8;

/** The fence tag this product reserves. A block opened with it is a reply meant to be sent onward. */
const REPLY_TAG = 'reply';

const COPY_BLOCK = 'Copy block';
const COPY_REPLY = 'Copy the reply prompt';

/**
 * One block of an answer that a person can take on its own.
 *
 * <p>`text` is what marked's tokenizer yields, which is already what somebody wants to paste: a
 * fence's body without its fence lines and without the info string, a quote's content with one level
 * of `&gt;` markers removed. A quote that CONTAINS a fence keeps the inner fence lines, and the inner
 * fence is a block in its own right — the two controls overlap deliberately.</p>
 */
export interface AnswerBlock {
  readonly kind: 'code' | 'quote';
  readonly reply: boolean;
  readonly text: string;
}

/**
 * A short token standing for the exact text a row was drawn from.
 *
 * <p><b>Why an answer needs one.</b> A control names a block by POSITION, and a position survives the
 * text under it changing. Messages are only appended to today, so it cannot happen — but a caveat is
 * not a guard, and two reviewers said so independently. The host recomputes this over its own stored
 * markdown and refuses when it differs, which closes the hard half: a rewrite that keeps the same
 * number of blocks, where no range check can see anything wrong.</p>
 *
 * <p>FNV-1a over the whole string, with the length beside it — not a security measure and not asked
 * to be one. The page can only ever echo this back, and a wrong value refuses a copy rather than
 * causing one, so there is nothing here for a lie to buy. The residual is an ordinary collision.</p>
 */
export function signatureOf(markdown: string): string {
  let hash = 0x811c9dc5;
  for (let at = 0; at < markdown.length; at += 1) {
    hash ^= markdown.charCodeAt(at);
    // The FNV prime, by shifts, because `hash * 16777619` leaves 32-bit range and loses its low bits.
    hash = (hash + ((hash << 1) + (hash << 4) + (hash << 7) + (hash << 8) + (hash << 24))) >>> 0;
  }

  return `${markdown.length.toString(36)}-${hash.toString(36)}`;
}

/**
 * What one walk is collecting while it draws.
 *
 * <p>`at` is the message the answer belongs to, and its ABSENCE is meaningful: a renderer that has
 * not been told which message it is drawing emits no controls at all. That is what keeps a caller
 * which cannot act on a press — and there was one for the whole of this module's life before now —
 * from showing buttons that do nothing.</p>
 */
interface Drawing {
  readonly at: number | undefined;
  readonly sig: string;
  readonly blocks: AnswerBlock[];
}

/**
 * Record a block and answer the ordinal it was given.
 *
 * <p>`blocks.length - 1` after the append, and never `push`'s own return value, which is the new
 * LENGTH: that would number the first block 1, make every button copy the block after its own, and
 * leave the last one resolving to nothing. Caught on the plan round before a line of this was
 * written. (gemini.)</p>
 */
function recordBlock(drawing: Drawing, block: AnswerBlock): number {
  drawing.blocks.push(block);

  return drawing.blocks.length - 1;
}

/** The control under a block — or nothing, when this walk is not drawing controls. */
function copyRow(drawing: Drawing, ordinal: number, reply: boolean): string {
  if (drawing.at === undefined) {
    return '';
  }

  return '<p class="blockRow">'
    + `<button type="button" class="copy blockCopy" data-block="${ordinal}"`
    + ` data-at="${drawing.at}" data-sig="${drawing.sig}">`
    + `${reply ? COPY_REPLY : COPY_BLOCK}</button></p>`;
}

/**
 * A file reference a model writes: `src/foo.ts:12`, `src/foo.ts#L12`, or the path alone.
 *
 * <p>The LINE is split off here; whether what is left is a path this product will open is
 * `isConfinedRelativePath`'s question, and asking it there is the point — two modules with their own
 * idea of what a path is had already disagreed once, and the disagreement was invisible: this one
 * demanded a dot, so `Dockerfile`, `LICENSE` and `Makefile` were never offered as links by a page
 * whose host would have opened them happily. (gemini, the code round.)</p>
 */
const FILE_REFERENCE = /^(.+?)(?:#L(\d+)|:(\d+))?$/;

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
  const path = match?.[1] ?? '';
  if (!isConfinedRelativePath(path)) {
    return undefined;
  }
  const line = Number(match?.[2] ?? match?.[3] ?? 0);

  return { path, line: Number.isFinite(line) ? line : 0 };
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

function listItems(token: Tokens.List, depth: number, drawing: Drawing): string {
  return token.items
    .map((item) => `<li>${blocks(item.tokens ?? [], depth + 1, drawing)}</li>`)
    .join('');
}

function table(token: Tokens.Table, depth: number): string {
  const head = token.header.map((cell) => `<th>${inline(cell.tokens ?? [], depth)}</th>`).join('');
  const body = token.rows
    .map((row) => `<tr>${row.map((cell) => `<td>${inline(cell.tokens ?? [], depth)}</td>`).join('')}</tr>`)
    .join('');

  return `<table><thead><tr>${head}</tr></thead><tbody>${body}</tbody></table>`;
}

/** A paragraph, or nothing at all: an empty one is vertical space the model did not ask for. */
function paragraph(body: string): string {
  return body.trim().length === 0 ? '' : `<p>${body}</p>`;
}

function blockToken(token: Tokens.Generic, depth: number, drawing: Drawing): string {
  switch (token.type) {
    case 'space':
      return '';
    case 'heading': {
      // Clamped rather than trusted: `####### seven` is not a heading level, and h7 is not an element.
      const level = Math.min(6, Math.max(1, Number((token as Tokens.Heading).depth) || 1));

      return `<h${level}>${inline(token.tokens ?? [], depth)}</h${level}>`;
    }
    case 'paragraph':
      return paragraph(inline(token.tokens ?? [], depth));
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
      const text = String(code.text ?? '');
      const ordinal = recordBlock(drawing, {
        kind: 'code',
        reply: language.toLowerCase() === REPLY_TAG,
        text,
      });

      return `<pre><code${cssClass}>${escapeHtml(text)}</code></pre>`
        + copyRow(drawing, ordinal, language.toLowerCase() === REPLY_TAG);
    }
    case 'blockquote': {
      // Numbered on the way OUT, after whatever it contains, so the ordinals run in the order the
      // rows appear rather than against it. A quote holding a fence therefore gives two controls:
      // the inner copies the code, the outer copies the quote whole — fence lines and all, which is
      // what marked puts in a blockquote's own `text`.
      const inner = blocks(token.tokens ?? [], depth + 1, drawing);
      const ordinal = recordBlock(drawing, {
        kind: 'quote',
        reply: false,
        text: String((token as Tokens.Blockquote).text ?? ''),
      });

      return `<blockquote>${inner}</blockquote>${copyRow(drawing, ordinal, false)}`;
    }
    case 'list': {
      const list = token as Tokens.List;
      const start = list.ordered && typeof list.start === 'number' && list.start !== 1
        ? ` start="${Math.trunc(list.start)}"`
        : '';

      return list.ordered
        ? `<ol${start}>${listItems(list, depth, drawing)}</ol>`
        : `<ul>${listItems(list, depth, drawing)}</ul>`;
    }
    case 'table':
      return table(token as Tokens.Table, depth);
    case 'hr':
      return '<hr>';
    case 'html':
      // Escaped and SHOWN. A model that wrote a tag meant to show one, and this is the one place
      // where a renderer's default — pass it through — would be a hole rather than a feature.
      return paragraph(escapeHtml(String(token.raw ?? '')));
    default:
      return paragraph(escapeHtml(String(token.raw ?? '')));
  }
}

function blocks(tokens: readonly Tokens.Generic[], depth: number, drawing: Drawing): string {
  if (depth > MAX_DEPTH) {
    // Drawn as TEXT, so nothing below here is a block: it earns no control and takes no ordinal.
    // That is why the walk that draws has to be the walk that numbers — a second enumerator counting
    // the markdown would count these and every ordinal after them would be one out.
    return escapeHtml(tokens.map((token) => String(token.raw ?? '')).join(''));
  }

  return tokens.map((token) => blockToken(token, depth, drawing)).join('');
}

/**
 * Markdown in, safe HTML out. Never throws: a tokenizer that cannot make sense of something answers
 * with the text itself, and so does this — an answer shown as plain text is a bad day, an answer
 * that renders nothing at all is a lost one.
 */
export function renderAnswer(markdown: string, at?: number): string {
  return walk(markdown, at).html;
}

/**
 * The blocks of an answer, as the renderer drew them.
 *
 * <p><b>The same walk, which is the whole design.</b> A control names a block by an ordinal, and the
 * host resolves that ordinal here — so if this counted blocks its own way, the two would disagree and
 * the button would copy the wrong text while both suites stayed green. They cannot: the ordinal
 * written into a row IS the index into the list this returns, assigned by one function in one pass.
 * It is not a hypothetical agreement either — a top-level scan of the markdown and what the renderer
 * actually emits already differ on 1 of 39 real stored answers, over a fence inside a list item.</p>
 */
export function answerBlocks(markdown: string): readonly AnswerBlock[] {
  return walk(markdown, undefined).blocks;
}

/**
 * Markdown in; the document, and what a person may take out of it.
 *
 * <p>Never throws: a tokenizer that cannot make sense of something answers with the text itself, and
 * so does this — an answer shown as plain text is a bad day, an answer that renders nothing at all is
 * a lost one. A walk that failed records NO blocks, so the host offers nothing rather than offering a
 * control for something it cannot resolve.</p>
 */
function walk(markdown: string, at: number | undefined): { html: string; blocks: readonly AnswerBlock[] } {
  const drawing: Drawing = { at, sig: signatureOf(markdown), blocks: [] };
  if (markdown.length === 0) {
    return { html: '', blocks: drawing.blocks };
  }
  try {
    return { html: blocks(lexer(markdown), 0, drawing), blocks: drawing.blocks };
  } catch {
    return { html: `<p>${escapeHtml(markdown)}</p>`, blocks: [] };
  }
}
