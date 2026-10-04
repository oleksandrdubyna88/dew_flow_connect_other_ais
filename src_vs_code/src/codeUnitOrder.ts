/**
 * Total order over strings, by UTF-16 code unit — and deliberately NOT `localeCompare`.
 *
 * <p>The analyser asks for `localeCompare` whenever strings are sorted, and for an order that must be the
 * SAME everywhere that advice is backwards. `localeCompare` is collation: it depends on the runtime's locale
 * and on how much of ICU that runtime was built with, and it calls some distinct strings equal — `é` as one
 * code point and `e` plus a combining accent — so a stable sort leaves those in whatever order they arrived.
 * Code units do not vary and never tie two different strings.</p>
 *
 * <p>Every caller needs exactly that. `vendorPalette` decides which of two colliding vendors gets first pick of
 * a colour, so two people looking at one Team server must order the names alike. `canonical` in
 * `configTransfer.ts` is an equality test, so one value must give one text whatever its key order — a
 * collating sort once canonicalised one object two ways and exported a setting equal to its default (qwen,
 * code round). The record watchers' signatures ({@link sortedJoin}) and the chat heartbeat's announced ids are
 * compared with their own previous value, so one set must give one text. Display orders a person reads are
 * `localeCompare`'s job, not this one's.</p>
 */
export function byCodeUnit(left: string, right: string): number {
  if (left === right) {
    return 0;
  }

  return left < right ? -1 : 1;
}

/**
 * A set's text: its parts in {@link byCodeUnit} order, joined — the same whatever order they arrived in.
 *
 * <p>What a directory watcher compares with its previous snapshot to decide whether anything a person can see
 * changed. The files come in the order the directory lists them, so the text must not depend on it — and a
 * `localeCompare` sort did, for two parts that collate as one: one unchanged snapshot, two signatures, a repaint.</p>
 */
export function sortedJoin(parts: readonly string[], separator: string): string {
  return [...parts].sort(byCodeUnit).join(separator);
}
