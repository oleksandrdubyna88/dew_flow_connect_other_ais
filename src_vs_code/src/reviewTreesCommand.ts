import {
  canOpen, ignoredAsk, ListedTree, RemovalRead, removalSentence, treeLine, TreesRead,
} from './reviewTree';

/**
 * Seeing what review checkouts this machine holds, and giving one back.
 *
 * <p><b>Why a command and a picker rather than a block on the review page.</b> Three things decided
 * it, and the plan round left the placement to the implementer:</p>
 * <ul>
 *   <li>`bugzReviewPage.ts` is at 791 of the 800-line cap — nine lines, which is not room for a block
 *       and its click path, and extracting a fourth module out of that page is a refactor this story
 *       did not set out to do;</li>
 *   <li>the Bugz section repaints on a poll, and a list of trees costs a process to build — the same
 *       "a process per row at paint" trap story 3.1 measured and refused;</li>
 *   <li><b>one at a time becomes true by construction.</b> A picker returns one item. There is no
 *       markup in which a second selection could exist, no "remove all" that a later change could add
 *       by accident, and no assertion needed about the absence of a button — which the plan round
 *       rightly said is unobservable.</li>
 * </ul>
 *
 * <p>Everything decidable is here and takes its dependencies as parameters, so the whole flow is a
 * unit test: story 3.2a shipped its decisions with 85 of 142 lines unexecuted and only a coverage
 * gate noticed, which is a mistake worth making once.</p>
 */

/** One choice in a picker: what it says, and what it means. */
export interface Choice {
  readonly label: string;
  readonly description?: string;
  readonly detail?: string;
  /** What the caller does with it — never shown. */
  readonly id: string;
}

/** Everything this flow reaches the world through. */
export interface TreesDeps {
  /** What this machine holds. */
  readonly list: () => Promise<TreesRead>;

  /** Give one back. `withIgnored` is the person's second ask, never a default. */
  readonly remove: (name: string, withIgnored: boolean) => Promise<RemovalRead>;

  /** Open a checkout in a window of its own — story 3.2a's opener, not a second one. */
  readonly open: (path: string) => Promise<void>;

  /** Ask the person to choose one of these, or nothing. */
  readonly pick: (choices: readonly Choice[], title: string) => Promise<Choice | undefined>;

  /**
   * Tell them something.
   *
   * <p>`failed` and `subject` are what the caller needs to route it through the one notification
   * funnel: this extension counts every place it speaks to a person, and a raw
   * `showInformationMessage` here would be a call site outside it. The flow decides WHICH of the
   * two a sentence is, because only the flow knows.</p>
   */
  readonly say: (said: string, about: { readonly failed: boolean; readonly subject: string }) => void;
}

/** The whole flow: list, choose one, open it or give it back. */
export async function manageReviewTrees(deps: TreesDeps): Promise<void> {
  const read = await deps.list();
  if (!read.ok) {
    deps.say(read.why, { failed: true, subject: 'the list' });

    return;
  }

  if (read.answer.reason.length > 0) {
    deps.say(`the review checkouts could not be listed: ${read.answer.reason}`, { failed: true, subject: 'the list' });

    return;
  }

  if (read.answer.trees.length === 0) {
    deps.say(`this machine holds no review checkouts. They would be under ${read.answer.root}.`, { failed: false, subject: 'the list' });

    return;
  }

  await chosenAsync(deps, read.answer.trees);
}

/** One tree, then one thing to do with it. */
async function chosenAsync(deps: TreesDeps, trees: readonly ListedTree[]): Promise<void> {
  const picked = await deps.pick(
    trees.map((tree) => ({ label: treeLine(tree), detail: tree.path, id: tree.name })),
    `${trees.length} review ${trees.length === 1 ? 'checkout' : 'checkouts'} on this machine`);
  if (picked === undefined) {
    return;
  }

  const tree = trees.find((one) => one.name === picked.id);
  if (tree === undefined) {
    return;
  }

  await actedAsync(deps, tree);
}

async function actedAsync(deps: TreesDeps, tree: ListedTree): Promise<void> {
  const choices: Choice[] = [];
  if (canOpen(tree)) {
    choices.push({ label: 'Open it in a new window', id: 'open', detail: tree.path });
  }
  choices.push({ label: 'Remove it', id: 'remove', description: 'refused if it holds work of yours' });

  const what = await deps.pick(choices, treeLine(tree));
  if (what === undefined) {
    return;
  }

  if (what.id === 'open') {
    await deps.open(tree.path);

    return;
  }

  await removedAsync(deps, tree);
}

/**
 * Ask for it back — and, if the only thing in it is ignored, ask ONCE more.
 *
 * <p>That second ask is the whole confirmation this product has, and it is deliberately not a
 * blanket one: a person confirming "yes, including the ignored files" has been told how many there
 * are and seen some of their names. A confirmation that says only "are you sure?" teaches people to
 * press yes.</p>
 */
async function removedAsync(deps: TreesDeps, tree: ListedTree): Promise<void> {
  const first = await deps.remove(tree.name, false);
  if (!first.ok) {
    deps.say(first.why, { failed: true, subject: tree.name });

    return;
  }

  if (first.answer.reason !== 'has_ignored') {
    deps.say(`${treeLine(tree)} — ${removalSentence(first.answer)}`, { failed: first.answer.reason !== 'removed', subject: tree.name });

    return;
  }

  const asked = await deps.pick(
    [
      { label: ignoredAsk(first.answer), id: 'yes', detail: first.answer.ignoredSample.join(', ') },
      { label: 'Keep it', id: 'no' },
    ],
    removalSentence(first.answer));
  if (asked === undefined || asked.id !== 'yes') {
    return;
  }

  const second = await deps.remove(tree.name, true);
  deps.say(
    second.ok ? `${treeLine(tree)} — ${removalSentence(second.answer)}` : second.why,
    { failed: !second.ok || second.answer.reason !== 'removed', subject: tree.name });
}
