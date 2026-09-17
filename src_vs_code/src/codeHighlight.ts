import csharp from '@shikijs/langs/csharp';
import javascript from '@shikijs/langs/javascript';
import typescript from '@shikijs/langs/typescript';
import { createCssVariablesTheme, createHighlighterCoreSync, type HighlighterCore } from 'shiki/core';
import { createJavaScriptRegexEngine } from 'shiki/engine/javascript';

import { escapeHtml } from './webviewHtml';

/**
 * Syntax highlighting for the review page, with VS Code's own grammars.
 *
 * <p>The choice between this and porting `creds_for_devs`'s regex highlighter was made by a
 * measurement, recorded in `todo/PLAN_the_review_page_can_be_read.md` — and the number that decided
 * it was not the size. <b>Neither option can use VS Code's own colours</b>: a webview is handed the
 * workbench theme variables and no token colours, and the only token-ish variable in either
 * repository is `--vscode-debugTokenExpression-name`. So "as close to VS Code's own as possible"
 * reduces to TOKENISATION, which real TextMate grammars buy and a regex cannot — and the corpus is
 * real code, because `Normalise` renames identifiers and leaves literals, comments, generics and
 * interpolation exactly as they were.</p>
 *
 * <p><b>Three grammars, one theme, and the JavaScript engine rather than the WASM one.</b> The
 * corpus has exactly three languages (`TreeSitterNormalizer`), so the bundle carries three and not
 * a hundred; `createJavaScriptRegexEngine` means no `.wasm` to ship, load or find at run time. Both
 * measured: +96 KB deflated, and no engine warning on any of the three.</p>
 *
 * <p><b>Pure, and it must stay that way.</b> No `node:` and no `vscode` import — this is reachable
 * from `bugzReviewPage.ts`, and a page module that reaches for either fails the bundle test. Shiki's
 * core is browser-safe by design; it is the `shiki` entry point (bundled languages, WASM engine)
 * that is not, which is why every import here is a deep one.</p>
 */

/**
 * The theme, as CSS VARIABLES rather than colours.
 *
 * <p>A stock Shiki theme bakes `#1E1E1E` and `#D4D4D4` into every block, which would put a dark
 * slab inside a light editor and — worse — would ignore the tone control story 1.1 shipped, on
 * exactly the text somebody dimming their screen at night is reading. Emitting
 * `var(--coai-hl-token-keyword)` instead leaves the palette to {@link HIGHLIGHT_CSS}, which maps it
 * to the theme's own chart colours the way `creds_for_devs` maps its `tok-*` classes.</p>
 */
const THEME = 'coai-vars';

/**
 * What the corpus calls a language, and what Shiki does.
 *
 * <p>Not a `toLowerCase()`: `codeToHtml` THROWS on an id it has not loaded, and `CSharp` is exactly
 * such an id — verified, not assumed. An explicit table is also the thing that decides, once, which
 * languages this page claims to highlight at all.</p>
 */
const GRAMMARS: Readonly<Record<string, string>> = {
  csharp: 'csharp',
  cs: 'csharp',
  typescript: 'typescript',
  ts: 'typescript',
  javascript: 'javascript',
  js: 'javascript',
};

/**
 * Built once, on first use — never at import.
 *
 * <p>Module-level construction would run inside `require`, and this module is reachable from the
 * extension's entry point: every window would pay for three grammars at activation whether or not
 * anybody opened the review page.</p>
 */
let core: HighlighterCore | undefined;

function highlighter(): HighlighterCore {
  core ??= createHighlighterCoreSync({
    themes: [createCssVariablesTheme({ name: THEME, variablePrefix: '--coai-hl-' })],
    langs: [csharp, typescript, javascript],
    engine: createJavaScriptRegexEngine(),
  });

  return core;
}

/** Whether this page will colour a pair written in this language. */
export function canHighlight(language: string): boolean {
  return GRAMMARS[language.toLowerCase()] !== undefined;
}

/**
 * One skeleton, as markup — coloured where the language is known, escaped plain text where it is not.
 *
 * <p><b>The security property comes first and does not depend on Shiki being right.</b> The corpus
 * is source code from somebody's repository: a skeleton can contain `&lt;/script&gt;`, a `&lt;style&gt;` block or
 * an `onerror` attribute, and this page decides what a person believes about which row is which.
 * Shiki escapes `&lt;` (as `&#x3C;`, a numeric reference rather than `&amp;lt;` — which is why the tests
 * assert that the dangerous SEQUENCE cannot appear rather than that a particular entity does), and
 * the fallback path escapes through `escapeHtml`. Both are covered, because the unknown-language
 * path is the one a new corpus language reaches first.</p>
 *
 * <p>A highlighter that throws must not take the page down with it either: an unexpected grammar
 * failure falls back to the same escaped text rather than to an empty row, because a row rendering
 * nothing is indistinguishable from a pair that was never collected.</p>
 */
export function highlight(code: string, language: string): string {
  const grammar = GRAMMARS[language.toLowerCase()];
  if (grammar === undefined) {
    return plain(code);
  }

  try {
    return highlighter().codeToHtml(code, { lang: grammar, theme: THEME });
  } catch {
    return plain(code);
  }
}

/** The same shape Shiki emits, so one stylesheet dresses both and a fallback is not a different page. */
function plain(code: string): string {
  return `<pre class="shiki"><code><span class="line">${escapeHtml(code)}</span></code></pre>`;
}

/**
 * The palette, mapped to the theme's own colours.
 *
 * <p>`var(--vscode-charts-*)` with VS Code's default dark values as fallbacks — the arrangement
 * `creds_for_devs` already uses for its `tok-*` classes, and for the same reason: these are the only
 * colour variables a webview is given, so they are what "follows the theme" can mean here.</p>
 *
 * <p>`--coai-hl-foreground` is `--coai-read`, NOT `--vscode-editor-foreground`, so the tone control
 * moves the uncoloured majority of the code with the rest of the page. The token colours are left
 * alone by the tone deliberately: dimming a keyword towards the background is how a highlighted
 * block stops being readable.</p>
 */
export const HIGHLIGHT_CSS = `
  :root {
    --coai-hl-background: transparent;
    --coai-hl-foreground: var(--coai-read, var(--vscode-editor-foreground));
    --coai-hl-token-keyword: var(--vscode-charts-blue, #569cd6);
    --coai-hl-token-string: var(--vscode-charts-orange, #ce9178);
    --coai-hl-token-string-expression: var(--vscode-charts-orange, #ce9178);
    --coai-hl-token-comment: var(--vscode-descriptionForeground, #6a9955);
    --coai-hl-token-constant: var(--vscode-charts-green, #b5cea8);
    --coai-hl-token-number: var(--vscode-charts-green, #b5cea8);
    --coai-hl-token-function: var(--vscode-charts-yellow, #dcdcaa);
    --coai-hl-token-punctuation: var(--coai-read, var(--vscode-editor-foreground));
    --coai-hl-token-parameter: var(--vscode-debugTokenExpression-name, #9cdcfe);
    --coai-hl-token-link: var(--vscode-textLink-foreground);
  }
  /* Shiki brings its own pre; it must not bring its own BOX. The page decides the spacing, the
     wrapping and the size, exactly as it did when these were plain pre elements. */
  pre.shiki {
    margin: 0; padding: 0; background: none;
    font-family: var(--vscode-editor-font-family); font-size: .85em;
    white-space: pre-wrap; word-break: break-word;
  }
  pre.shiki code { font-family: inherit; }
  pre.shiki .line { display: block; }`;
