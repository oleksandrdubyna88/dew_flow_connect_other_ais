/** A pick that can be stored, or one refused before it reaches the settings. */
export type PromptPick =
  | { readonly ok: true; readonly value: Record<string, readonly string[]> }
  | { readonly ok: false };

/**
 * `coai.promptsPerRound` after a person picks one round's prompt for one role — a new value, the stored one untouched.
 *
 * <p>`round` is 1-based, as the page numbers its rounds, and must be one the role HAS (`rounds`, from
 * `coai.rounds`): a 0, a fraction, `NaN` or a stray huge number from a malformed message is refused rather than
 * written as `rounds[-1]` — which a JSON write silently drops, so the save "succeeds" and the pick vanishes — or
 * padded a billion times. The role is read only as an own key, so `constructor` names no role.</p>
 *
 * <p>Rounds before the picked one that hold nothing are padded with `''`, "not chosen", rather than with what they
 * resolve to today: padding them with today's default would freeze it into a stored choice the moment anyone touched
 * a later round, and a later change to that default would never reach them. coai-mcp reads `''` the same way
 * (`RoleCatalog.ForRound` matches no prompt id and answers the role's general prompt).</p>
 */
export function promptChosen(
  current: Readonly<Record<string, readonly string[]>>,
  role: string,
  round: number,
  id: string,
  rounds: number,
): PromptPick {
  if (!isRoundOf(round, rounds)) {
    return { ok: false };
  }
  const picked = [...ownRounds(current, role)];
  while (picked.length < round) {
    picked.push('');
  }
  picked[round - 1] = id;

  return { ok: true, value: { ...current, [role]: picked } };
}

/** A whole round the role has: 1 through `rounds`. */
function isRoundOf(round: number, rounds: number): boolean {
  return Number.isInteger(round) && round >= 1 && round <= rounds;
}

/** The role's stored rounds, read only as an own key — `constructor` names no role. */
function ownRounds(current: Readonly<Record<string, readonly string[]>>, role: string): readonly string[] {
  return Object.hasOwn(current, role) ? current[role] ?? [] : [];
}
