/**
 * `coai.promptsPerRound` after a person picks one round's prompt for one role — a new value, the stored one untouched.
 *
 * <p>Rounds before the picked one that hold nothing are padded with `''`, "not chosen", rather than with what they
 * resolve to today: padding them with today's default would freeze it into a stored choice the moment anyone touched
 * a later round, and a later change to that default would never reach them. Both halves read `''` as "not chosen".</p>
 */
export function promptChosen(
  current: Readonly<Record<string, readonly string[]>>,
  role: string,
  round: number,
  id: string,
): Record<string, readonly string[]> {
  const rounds = [...(current[role] ?? [])];
  while (rounds.length < round) {
    rounds.push('');
  }
  rounds[round - 1] = id;

  return { ...current, [role]: rounds };
}
