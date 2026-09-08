/**
 * One colour per reviewer, the same colour everywhere that reviewer is named — and never a colour
 * another reviewer already has.
 *
 * <p>In `codex/PlanCritique — running` the one word that says WHO was the same grey as the rest of
 * the row. A colour makes a list of nine reviewers scannable — you find your vendor by colour before
 * you have read anything.</p>
 *
 * <p><b>Why a hash of the name was not enough.</b> The first version of this file hashed the name
 * into six chart colours. Reported on 2026-09-08 from a panel with six reviewers configured: `local`
 * and `remsoftdev-codex` wore the same orange edge. That is not an unlucky hash — six names into six
 * buckets collide more often than they do not, and `claude` and `antigravity`, both shipped vendor
 * kinds, landed on the same purple too. A colour derived from ONE name cannot know what the other
 * five took, so no amount of re-hashing can make the promise. The promise is made over the LIST:
 * {@link vendorPalette} is handed every configured reviewer at once and answers for all of them.</p>
 *
 * <p><b>Sorted, never arrival order.</b> A palette handed out as rows appear would give a vendor one
 * colour in the rounds list and another in the spending chart, and a different one again after a
 * restart — worse than no colour, because it teaches a mapping that then lies. The list is
 * normalised, de-duplicated and sorted before anything is assigned, so the same set of names always
 * produces the same assignment, in every window and after every restart.</p>
 *
 * <p><b>One canonical list.</b> Every view asks for the palette of the CONFIGURED reviewers — the
 * vendor rows in the settings — never of "whoever happens to appear in the data I am drawing". Two
 * views that each inferred their own list would assign different colours to the same vendor the
 * moment their lists differed by one name, which is the defect this file exists to prevent. A
 * provider that is no longer configured (a removed vendor in an old round) still gets a stable
 * colour from its own name, and that one may coincide with a live reviewer's — the guarantee is over
 * the reviewers somebody can actually see in the panel.</p>
 *
 * <p>Nothing here is a bare hex. The twelve colours are contributed by this extension
 * (`contributes.colors` in package.json), so each carries a dark, a light and two high-contrast
 * variant and "readable in whichever theme the person actually uses" is the theme's arithmetic
 * rather than a hex somebody eyeballed once in dark mode. The hex inside each `var()` is the
 * fallback for the one case the variable is absent — an installed build older than the manifest that
 * declares these ids — where the alternative is an uncoloured edge. Same shape, and the same reason,
 * as the CredsForDevs dependency palette this is modelled on.</p>
 *
 * <p>`vscode`-free on purpose: the assignment below is the part worth testing, and a test for it
 * should not need a fake editor.</p>
 */

/** The twelve contributed ids, in palette order — what the manifest must declare. */
export const VENDOR_COLOUR_IDS: readonly string[] = [
  'coai.vendorColour1', 'coai.vendorColour2', 'coai.vendorColour3', 'coai.vendorColour4',
  'coai.vendorColour5', 'coai.vendorColour6', 'coai.vendorColour7', 'coai.vendorColour8',
  'coai.vendorColour9', 'coai.vendorColour10', 'coai.vendorColour11', 'coai.vendorColour12',
];

/**
 * The palette, as the CSS a webview can use.
 *
 * <p>Blue, Amber, Red, Cyan, Green, Pink, Purple, Brown, Turquoise, Lime, Orange, Slate — the twelve
 * hues already tuned for four theme flavours in CredsForDevs, reused rather than re-picked: they are
 * known to stay apart from each other in a light theme as well as a dark one, which is the whole
 * property being bought here.</p>
 */
export const VENDOR_PALETTE: readonly string[] = [
  'var(--vscode-coai-vendorColour1, #6E9BF0)',
  'var(--vscode-coai-vendorColour2, #E8B02A)',
  'var(--vscode-coai-vendorColour3, #FF8A76)',
  'var(--vscode-coai-vendorColour4, #5BC8DE)',
  'var(--vscode-coai-vendorColour5, #5CC46F)',
  'var(--vscode-coai-vendorColour6, #FF8FBC)',
  'var(--vscode-coai-vendorColour7, #B482F5)',
  'var(--vscode-coai-vendorColour8, #C0906A)',
  'var(--vscode-coai-vendorColour9, #4FCBB0)',
  'var(--vscode-coai-vendorColour10, #B9C742)',
  'var(--vscode-coai-vendorColour11, #FF9147)',
  'var(--vscode-coai-vendorColour12, #8FA8C8)',
];

/** A vendor with no name at all is the ordinary foreground — a colour says nothing about nothing. */
export const UNNAMED_VENDOR_COLOUR = 'var(--vscode-foreground)';

/**
 * The vendor kinds this product ships with, each pinned to a palette slot.
 *
 * <p>Not decoration. `codex`, `gemini` and `local` keep the blue, green and orange they have worn
 * since the feature shipped, because a mapping somebody has already learned is not worth
 * invalidating; `claude` and `antigravity` are pinned for the same reason before anybody learns
 * something else.</p>
 *
 * <p><b>Their slots are reserved whether or not they are configured.</b> Placing anchors "first" is
 * not enough: with only `claude` configured, an unanchored vendor would otherwise be free to take
 * blue, and `codex` would come back to a colour somebody else is wearing. So every anchor slot is
 * held out of the ordinary allocation, and only offered — last, in {@link candidates} — when the
 * unanchored reviewers would otherwise have to start repeating.</p>
 */
const ANCHORED: Readonly<Record<string, number>> = {
  codex: 0,        // Blue
  antigravity: 3,  // Cyan
  gemini: 4,       // Green
  claude: 6,       // Purple
  local: 10,       // Orange
};

const ANCHOR_SLOTS: ReadonlySet<number> = new Set(Object.values(ANCHORED));

/** The empty claim a stray provider is measured against. Read-only, like everything else here. */
const NOTHING_CLAIMED: ReadonlySet<number> = new Set<number>();

/** Case and surrounding space do not make a second vendor — here, and in the anchor lookup. */
function normalise(vendor: string): string {
  return vendor.trim().toLowerCase();
}

/**
 * Total order over names, by UTF-16 code unit — and deliberately NOT `localeCompare`.
 *
 * <p>The analyser asks for `localeCompare` whenever strings are sorted, and here that advice is
 * backwards. This sort decides which of two colliding vendors gets first pick of a colour, so what
 * it must be is the SAME order on every machine. `localeCompare` is collation: it depends on the
 * runtime's locale and on how much of ICU that runtime was built with, so two people looking at one
 * Team server could order the same two names differently and see the colours swapped. Code units do
 * not vary.</p>
 */
function byCodeUnit(left: string, right: string): number {
  if (left === right) {
    return 0;
  }

  return left < right ? -1 : 1;
}

/**
 * The slot this name would like, from the name alone.
 *
 * <p>FNV-1a, 32-bit, unsigned throughout: `>>> 0` after the multiply keeps it out of the sign bit,
 * where a negative modulus would index off the front of the palette.</p>
 */
function preferred(name: string): number {
  let hash = 0x811c9dc5;
  for (let at = 0; at < name.length; at += 1) {
    hash = (hash ^ name.charCodeAt(at)) >>> 0;
    hash = Math.imul(hash, 0x01000193) >>> 0;
  }

  return hash % VENDOR_PALETTE.length;
}

/**
 * Every slot this name will try, in the order it tries them.
 *
 * <p>Its preferred slot first and then the rest, wrapping — so a vendor moves only when the colour it
 * wanted is genuinely spoken for, rather than because the list was re-sorted around it. Reserved
 * anchor slots go to the BACK: with eight unanchored reviewers and no `codex` configured, blue is a
 * better answer than a repeat, but it is the answer of last resort.</p>
 */
function candidates(name: string): readonly number[] {
  const from = preferred(name);
  const wrapped = Array.from({ length: VENDOR_PALETTE.length }, (_, step) => (from + step) % VENDOR_PALETTE.length);

  return [...wrapped.filter((slot) => !ANCHOR_SLOTS.has(slot)), ...wrapped.filter((slot) => ANCHOR_SLOTS.has(slot))];
}

/**
 * The first slot nobody has taken — or, past twelve reviewers, the one this name wanted.
 *
 * <p>The thirteenth reviewer repeats a colour. That is deliberate and it is the honest end of a
 * twelve-colour palette: a thirteenth hue nobody can tell from the twelfth, or a grey that is
 * indistinguishable from the uncoloured foreground, would both cost more than the repeat does.</p>
 */
function slotFor(name: string, claimed: ReadonlySet<number>): number {
  const order = candidates(name);

  return order.find((slot) => !claimed.has(slot)) ?? order[0]!;
}

/** Answers with the colour for any vendor name, having already decided the whole list. */
export type VendorPalette = (vendor: string) => string;

/**
 * The colours for a whole list of reviewers — the only thing here that can promise no repeats.
 *
 * <p>Pass the CONFIGURED vendor ids, from the settings, at every call site. The returned function
 * also answers for a name that was not in the list, from that name alone, so a provider in an old
 * round is never uncoloured.</p>
 */
export function vendorPalette(configured: readonly string[]): VendorPalette {
  const names = [...new Set(configured.map(normalise))].filter((name) => name.length > 0).sort(byCodeUnit);
  const claimed = new Set<number>();
  const slots = new Map<string, number>();

  for (const name of names) {
    const anchor = ANCHORED[name];
    if (anchor !== undefined) {
      slots.set(name, anchor);
      claimed.add(anchor);
    }
  }

  for (const name of names.filter((candidate) => !slots.has(candidate))) {
    const slot = slotFor(name, claimed);
    claimed.add(slot);
    slots.set(name, slot);
  }

  return (vendor) => colourOf(normalise(vendor), slots);
}

/** A name the list knew, an anchor, or — for a stray — the slot its own hash asks for. */
function colourOf(name: string, slots: ReadonlyMap<string, number>): string {
  if (name.length === 0) {
    return UNNAMED_VENDOR_COLOUR;
  }

  const slot = slots.get(name) ?? ANCHORED[name] ?? slotFor(name, NOTHING_CLAIMED);

  return VENDOR_PALETTE[slot] ?? UNNAMED_VENDOR_COLOUR;
}
