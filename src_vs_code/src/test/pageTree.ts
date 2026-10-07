import { Node } from './pageScriptHarness';

/**
 * A page AS DRAWN, as the harness's nodes: every element of the rendered html, with its real parent, its `data-*` as a
 * dataset, its value, its tick, its text. So a test fires an event at the control the page actually drew — never at a
 * node it built with the attributes it expected, which stays green when the drawn attribute changes (the operator's
 * ruling against behaviour asserted over page source, and CodeRabbit on PR #688).
 *
 * <p>For the html this extension draws: double-quoted attributes, `<script>` and `<style>` bodies skipped whole, the
 * void elements closed by themselves. It is not a browser's parser and does not pretend to be one.</p>
 */

/** A drawn element: the harness's node, and what a test reads of it. */
export class PageNode extends Node {
  readonly id: string;
  readonly children: PageNode[] = [];
  /** The text directly inside it — what a `<textarea>` takes as its value; {@link PageNode.text} is the whole subtree's. */
  own = '';
  /** Its text and its child elements in the order the page wrote them, so {@link PageNode.text} reads as a DOM does. */
  readonly parts: (string | PageNode)[] = [];
  disabled: boolean;

  constructor(tagName: string, readonly attrs: Readonly<Record<string, string>>) {
    super(datasetOf(attrs), tagName.toUpperCase());
    this.id = attrs['id'] ?? '';
    this.type = attrs['type'] ?? (tagName === 'select' ? 'select-one' : 'text');
    this.value = attrs['value'] ?? '';
    this.checked = 'checked' in attrs;
    this.hidden = 'hidden' in attrs;
    this.disabled = 'disabled' in attrs;
    this.className = attrs['class'] ?? '';
  }

  /** Everything written inside it, tags left out. */
  text(): string {
    return this.parts.map((part) => (typeof part === 'string' ? part : part.text())).join('');
  }

  /** Every element under it, depth first. */
  all(): readonly PageNode[] {
    return this.children.flatMap((child) => [child, ...child.all()]);
  }

  /** The elements under it a test is after. */
  find(test: (node: PageNode) => boolean): readonly PageNode[] {
    return this.all().filter(test);
  }

  /** The one element under it a test is after — a test fails here, by name, when there is none. */
  one(test: (node: PageNode) => boolean, what: string): PageNode {
    const found = this.find(test)[0];
    if (found === undefined) {
      throw new Error(`the page draws no ${what}`);
    }

    return found;
  }
}

const VOID = new Set(['input', 'br', 'meta', 'img', 'hr', 'link', 'col', 'area', 'base', 'source', 'wbr']);
const SKIPPED = new Set(['script', 'style']);
const TOKEN = /<!--[\s\S]*?-->|<!DOCTYPE[^>]*>|<\/([a-zA-Z][\w-]*)\s*>|<([a-zA-Z][\w-]*)((?:\s+[^\s=>/]+(?:="[^"]*")?)*)\s*\/?>|([^<]+)/g;

function decoded(text: string): string {
  return text.replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&');
}

function attrsOf(raw: string): Record<string, string> {
  return Object.fromEntries([...raw.matchAll(/([^\s=]+)(?:="([^"]*)")?/g)].map((match) => [match[1]!, decoded(match[2] ?? '')]));
}

function datasetOf(attrs: Readonly<Record<string, string>>): Record<string, string> {
  return Object.fromEntries(Object.entries(attrs)
    .filter(([name]) => name.startsWith('data-'))
    .map(([name, value]) => [name.slice(5).replace(/-([a-z])/g, (_, letter: string) => letter.toUpperCase()), value]));
}

/**
 * What a page's `document.querySelectorAll` answers for the attribute selectors its scripts use at load —
 * `[data-x]` and `[data-x="y"]` — read off the drawn tree, so a script binds to exactly what the page drew.
 */
export function selectorsOf(tree: PageNode, selectors: readonly string[]): Record<string, readonly PageNode[]> {
  return Object.fromEntries(selectors.map((selector) => [selector, tree.find((node) => node.closest(selector) === node)]));
}

/** An event fired at `node` as a browser fires it: up through every ancestor's own listeners, then the document's. */
export function bubbled(page: { fire(kind: string, target: Node, extra?: Readonly<Record<string, unknown>>): unknown }, kind: string, node: Node): void {
  for (let at: Node | undefined = node; at !== undefined; at = at.parent) {
    at.runOwn(kind, node);
  }
  page.fire(kind, node);
}

/** The rendered html as a tree under one root; a `<textarea>` takes its text as its value, as a DOM does. */
export function pageTree(html: string): PageNode {
  const root = new PageNode('root', {});
  const open: PageNode[] = [root];
  let skipUntil = '';
  for (const match of html.matchAll(TOKEN)) {
    const [whole, closing, opening, attributes, text] = match;
    if (skipUntil.length > 0) {
      skipUntil = closing?.toLowerCase() === skipUntil ? '' : skipUntil;
      continue;
    }
    const at = open[open.length - 1]!;
    if (text !== undefined) {
      at.own += decoded(text);
      at.parts.push(decoded(text));
    } else if (opening !== undefined) {
      const tag = opening.toLowerCase();
      const node = new PageNode(tag, attrsOf(attributes ?? ''));
      node.under(at);
      at.children.push(node);
      at.parts.push(node);
      if (SKIPPED.has(tag)) {
        skipUntil = tag;
      } else if (!VOID.has(tag) && !whole.endsWith('/>')) {
        open.push(node);
      }
    } else if (closing !== undefined) {
      const tag = closing.toUpperCase();
      const index = open.map((one) => one.tagName).lastIndexOf(tag);
      if (index > 0) {
        if (tag === 'TEXTAREA') {
          open[index]!.value = open[index]!.own;
        }
        open.length = index;
      }
    }
  }

  return root;
}
