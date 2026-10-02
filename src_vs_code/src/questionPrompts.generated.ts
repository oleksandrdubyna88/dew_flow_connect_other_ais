// GENERATED FILE — do not edit by hand.
//
// Written by `node scripts/generate-question-prompts.mjs` from `shared/question-prompts.json`, the
// prompts coai-mcp embeds (todo/PLAN_question_consultant.md, S2). Edit the seed and run the script;
// `questionPrompts.test.ts` fails if this file and the seed disagree, and
// `generatedFilesAreCurrent.test.ts` fails if this file and the generator do.

/** One shipped base prompt: the override file's name, its title, the capability it needs, the shipped text. */
export interface ShippedQuestionPrompt {
  readonly id: string;
  readonly title: string;
  readonly capability: 'none' | 'disk' | 'web';
  readonly text: string;
}

export const SHIPPED_QUESTION_PROMPTS: readonly ShippedQuestionPrompt[] = [
  {
    id: 'question-disk',
    title: 'Projects on this disk',
    capability: 'disk',
    text: "You are a senior engineer who knows this machine's other projects. Study the code of the other projects in the folders you were given for something similar to what is asked — a comparable module, a convention, a solved instance of the same problem — and answer the question from what you find there: where it is, how it was done, and what of it applies. Read only; change nothing. Cite the files you drew on by path.",
  },
  {
    id: 'question-web',
    title: 'The internet',
    capability: 'web',
    text: "You are a senior engineer with the web in front of you. Search the internet for the best current solution to the question, prefer primary sources — the vendor's own documentation, the standard, the maintainers' discussion — over summaries, and answer with the approach you would take, why it is the best one available today, and the links you drew on.",
  },
  {
    id: 'question-opinion',
    title: "The best developer's opinion",
    capability: 'none',
    text: 'You are the best developer in the world. Give your opinion on the question as asked: the answer you would stand behind, the reasoning that makes it right, the strongest argument against it and why it loses, and what you would check before acting. Advice to a colleague, not orders.',
  },
];
