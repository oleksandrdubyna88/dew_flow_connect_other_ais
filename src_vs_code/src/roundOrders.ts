/**
 * What a round ordered its caller to do, read off a `--findings` answer (issue #131) — moved out of
 * `roundsDb.ts` unchanged when that file reached the 800 lines the lint allows (todo/PLAN_question_consultant.md,
 * S4); `roundsDb.ts` re-exports it, so no importer changed.
 */

/**
 * What a round ORDERED its caller to do, and the size it measured (issue #131).
 *
 * <p>Absent from an answer when the round was recorded before this was written down, or by a server
 * or a database too old to hold it — which is NOT RECORDED, a different fact from an empty list.</p>
 */
export interface RoundOrders {
  readonly commands: readonly string[];
  readonly planShape: string;
}

/**
 * The `orders` of a `--findings` answer, or nothing when there are none or they are not that shape.
 *
 * <p>Nothing rather than a guess: orders are context beside the findings, never a reason to refuse
 * them, so a malformed member costs the orders block and nothing else.</p>
 */
export function parseOrders(text: string): RoundOrders | undefined {
  try {
    const orders: unknown = (JSON.parse(text) as { orders?: unknown }).orders;

    return isOrders(orders) ? { commands: orders.commands, planShape: orders.planShape } : undefined;
  } catch {
    return undefined;
  }
}

function isOrders(value: unknown): value is RoundOrders {
  const orders = value as Partial<Record<keyof RoundOrders, unknown>> | null | undefined;

  return typeof orders?.planShape === 'string' && isStrings(orders.commands);
}

function isStrings(value: unknown): value is readonly string[] {
  return Array.isArray(value) && value.every((one) => typeof one === 'string');
}
