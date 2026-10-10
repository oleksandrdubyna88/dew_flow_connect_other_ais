import { pageTree, PageNode } from './pageTree';

/**
 * A small DOM the Review rounds page can actually RUN on — for the tests that must see what the page does to its own
 * document: a section that stays hidden, a card a push must not close, cards a search hides and a sort reorders
 * (todo/PLAN_team_usage_by_person.md, story 1.4).
 *
 * <p>The page's older stub (`roundsLogPageHarness.ts` `runningPage`) invents an element for ANY id and answers every
 * `querySelectorAll` with nothing, so none of that could be observed — a hidden section, an absent one and a present
 * one all looked alike. This one is built from the html the page really rendered (parsed by `pageTree.ts`'s reader):
 * an id that is not in the page is `null`, and a selector finds what is there.</p>
 *
 * <p><b>Stricter than a browser, never more permissive</b> (`generated-code-tests.md`): a selector this DOM does not
 * understand THROWS rather than matching nothing, so a page that starts using one fails its test instead of passing
 * it on an empty list. What it understands: a tag, `#id`, `.class`, `[attr]` and `[attr="value"]`, compounded, with no
 * combinators. `pageDom.test.ts` holds it to that, and to the null for an absent id.</p>
 */

type Listener = (event: DomEvent) => void;

/** An event as the page's handlers see it. */
export interface DomEvent {
  readonly type: string;
  readonly target: DomElement;
  defaultPrevented: boolean;
  preventDefault(): void;
}

/** What a selector asks: a tag, and attribute tests. */
interface Compound {
  readonly tag: string;
  readonly tests: readonly { readonly name: string; readonly value?: string; readonly word?: boolean }[];
}

const PART = /^(?:#([\w-]+)|\.([\w-]+)|\[([\w-]+)(?:="([^"]*)")?\])/;

/** One compound selector, parsed — or a refusal naming what this DOM will not pretend to understand. */
export function compound(selector: string): Compound {
  const tagMatch = /^[a-z][\w-]*/.exec(selector);
  const tag = tagMatch?.[0] ?? '';
  let rest = selector.slice(tag.length);
  const tests: { name: string; value?: string; word?: boolean }[] = [];
  while (rest.length > 0) {
    const part = PART.exec(rest);
    if (part === null) {
      throw new Error(`pageDom does not understand the selector "${selector}" — it takes a tag, #id, .class and [attr], compounded`);
    }
    tests.push(part[1] !== undefined ? { name: 'id', value: part[1] }
      : part[2] !== undefined ? { name: 'class', value: part[2], word: true }
        : { name: part[3]!, ...(part[4] === undefined ? {} : { value: part[4] }) });
    rest = rest.slice(part[0].length);
  }
  if (tag.length === 0 && tests.length === 0) {
    throw new Error(`pageDom was asked for an empty selector "${selector}"`);
  }

  return { tag, tests };
}

const VOID = new Set(['input', 'br', 'meta', 'img', 'hr', 'link', 'col', 'area', 'base', 'source', 'wbr']);

function escapeText(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function escapeAttribute(text: string): string {
  return escapeText(text).replace(/"/g, '&quot;');
}

/** A text node: only what an element's textContent and innerHTML need of it. */
export class DomText {
  parent: DomElement | undefined;

  constructor(readonly data: string) {}
}

export class DomElement {
  readonly attributes = new Map<string, string>();
  readonly childNodes: (DomElement | DomText)[] = [];
  parent: DomElement | undefined;
  private readonly listeners = new Map<string, Listener[]>();
  /** An input's or a select's live value, once anything set it; until then, the markup's. */
  private typed: string | undefined;
  readonly style: Record<string, string> = {};
  indeterminate = false;

  constructor(readonly document: DomDocument, readonly tag: string) {}

  get tagName(): string { return this.tag.toUpperCase(); }

  get id(): string { return this.getAttribute('id') ?? ''; }

  getAttribute(name: string): string | null { return this.attributes.get(name) ?? null; }

  setAttribute(name: string, value: string): void { this.attributes.set(name, String(value)); }

  removeAttribute(name: string): void { this.attributes.delete(name); }

  hasAttribute(name: string): boolean { return this.attributes.has(name); }

  private flag(name: string, on: boolean): void {
    if (on) { this.setAttribute(name, ''); } else { this.removeAttribute(name); }
  }

  get hidden(): boolean { return this.hasAttribute('hidden'); }

  set hidden(on: boolean) { this.flag('hidden', on); }

  get open(): boolean { return this.hasAttribute('open'); }

  set open(on: boolean) { this.flag('open', on); }

  get disabled(): boolean { return this.hasAttribute('disabled'); }

  set disabled(on: boolean) { this.flag('disabled', on); }

  get checked(): boolean { return this.hasAttribute('checked'); }

  set checked(on: boolean) { this.flag('checked', on); }

  get className(): string { return this.getAttribute('class') ?? ''; }

  set className(value: string) { this.setAttribute('class', value); }

  get children(): DomElement[] { return this.childNodes.filter((node): node is DomElement => node instanceof DomElement); }

  get value(): string {
    if (this.tag === 'select') {
      return this.selectedOption()?.optionValue() ?? '';
    }
    if (this.tag === 'option') {
      return this.optionValue();
    }

    return this.typed ?? this.getAttribute('value') ?? '';
  }

  set value(next: string) {
    if (this.tag !== 'select') {
      this.typed = String(next);
      return;
    }
    // As a browser does: the option with that value is selected, and a value no option has selects none.
    for (const option of this.querySelectorAll('option')) {
      option.flag('selected', option.optionValue() === next);
    }
  }

  private optionValue(): string { return this.getAttribute('value') ?? this.textContent; }

  private selectedOption(): DomElement | undefined {
    const options = this.querySelectorAll('option');

    return options.find((option) => option.hasAttribute('selected')) ?? options.find((option) => !option.disabled);
  }

  get textContent(): string {
    return this.childNodes.map((node) => (node instanceof DomText ? node.data : node.textContent)).join('');
  }

  set textContent(text: string) {
    this.replaceChildren([new DomText(String(text))]);
  }

  get innerHTML(): string {
    return this.childNodes.map((node) => (node instanceof DomText ? escapeText(node.data) : node.outerHTML)).join('');
  }

  set innerHTML(html: string) {
    this.replaceChildren(this.document.parse(String(html)));
  }

  get outerHTML(): string {
    const attributes = [...this.attributes].map(([name, value]) => (value === '' ? ` ${name}` : ` ${name}="${escapeAttribute(value)}"`)).join('');

    return VOID.has(this.tag) ? `<${this.tag}${attributes}>` : `<${this.tag}${attributes}>${this.innerHTML}</${this.tag}>`;
  }

  private replaceChildren(nodes: readonly (DomElement | DomText)[]): void {
    for (const node of this.childNodes) { node.parent = undefined; }
    this.childNodes.length = 0;
    for (const node of nodes) { this.adopt(node); }
  }

  private adopt(node: DomElement | DomText): void {
    node.parent = this;
    this.childNodes.push(node);
  }

  /** Moves a node here, from wherever it was — which is how a page reorders a list. */
  appendChild(node: DomElement): DomElement {
    node.parent?.childNodes.splice(node.parent.childNodes.indexOf(node), 1);
    this.adopt(node);

    return node;
  }

  /** Every element under this one, in document order. */
  descendants(): DomElement[] {
    return this.children.flatMap((child) => [child, ...child.descendants()]);
  }

  matches(selector: string): boolean {
    const wanted = compound(selector);

    return (wanted.tag === '' || wanted.tag === this.tag) && wanted.tests.every((one) => this.passes(one));
  }

  private passes(test: { readonly name: string; readonly value?: string; readonly word?: boolean }): boolean {
    const held = this.getAttribute(test.name);
    if (held === null || test.value === undefined) {
      return held !== null;
    }

    return test.word === true ? held.split(/\s+/).includes(test.value) : held === test.value;
  }

  querySelectorAll(selector: string): DomElement[] {
    compound(selector);

    return this.descendants().filter((node) => node.matches(selector));
  }

  querySelector(selector: string): DomElement | null {
    return this.querySelectorAll(selector)[0] ?? null;
  }

  closest(selector: string): DomElement | null {
    return this.matches(selector) ? this : (this.parent?.closest(selector) ?? null);
  }

  addEventListener(type: string, listener: Listener): void {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  }

  /** This element's own listeners for an event — the document runs them on the way up. */
  hear(event: DomEvent): void {
    for (const listener of this.listeners.get(event.type) ?? []) { listener(event); }
  }

  focus(): void { this.document.activeElement = this; }

  /** A press, as a browser delivers it: here, every ancestor, then the document. */
  click(): void { this.document.dispatch('click', this); }
}

/** The document: the rendered page's tree, its listeners, and which element has focus. */
export class DomDocument {
  readonly root: DomElement;
  activeElement: DomElement | null = null;
  private readonly listeners = new Map<string, Listener[]>();

  constructor(html: string) {
    this.root = new DomElement(this, '#document');
    for (const node of this.parse(html)) { this.root.appendChild(node as DomElement); }
  }

  /** Html into nodes, through `pageTree.ts`'s reader — script and style bodies skipped, as that reader does. */
  parse(html: string): (DomElement | DomText)[] {
    return pageTree(html).parts.map((part) => this.convert(part));
  }

  private convert(part: string | PageNode): DomElement | DomText {
    if (typeof part === 'string') {
      return new DomText(part);
    }
    const element = new DomElement(this, part.tagName.toLowerCase());
    for (const [name, value] of Object.entries(part.attrs)) { element.setAttribute(name, value); }
    for (const child of part.parts) {
      const node = this.convert(child);
      node.parent = element;
      element.childNodes.push(node);
    }

    return element;
  }

  /** An id that is not in the page is NULL — never an invented stand-in. */
  getElementById(id: string): DomElement | null {
    return this.root.descendants().find((node) => node.id === id) ?? null;
  }

  querySelectorAll(selector: string): DomElement[] { return this.root.querySelectorAll(selector); }

  querySelector(selector: string): DomElement | null { return this.root.querySelector(selector); }

  addEventListener(type: string, listener: Listener): void {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  }

  /** An event at `target`: its own listeners and every ancestor's, then the document's. */
  dispatch(type: string, target: DomElement): DomEvent {
    const event: DomEvent = { type, target, defaultPrevented: false, preventDefault() { event.defaultPrevented = true; } };
    for (let at: DomElement | undefined = target; at !== undefined; at = at.parent) { at.hear(event); }
    for (const listener of this.listeners.get(type) ?? []) { listener(event); }

    return event;
  }
}
