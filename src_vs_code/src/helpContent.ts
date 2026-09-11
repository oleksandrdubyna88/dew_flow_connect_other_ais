import { DE } from './helpDe';
import { ES } from './helpEs';
import { RU } from './helpRu';
import { UK } from './helpUk';

/**
 * The help catalog: every article, in one fixed shape — what it is → why → how to set it up →
 * how to use it → what can go wrong.
 *
 * <p>The TYPE enforces the style: an article that skips *why* does not compile. The order is
 * explicit and deliberately not alphabetical — <b>the first four articles are the four things a
 * person does in their first ten minutes</b>, in the order they do them, and everything a panel
 * control does comes after that. Machinery nobody can see from the panel comes last, because it
 * is read when something has already gone wrong.</p>
 *
 * <p><b>Languages.</b> English is required on every article; the others are optional and fall
 * back VISIBLY — a missing translation must never hide an article.</p>
 */

export const HELP_LANGUAGES = ['en', 'ru', 'uk', 'de', 'es'] as const;
export type HelpLanguage = (typeof HELP_LANGUAGES)[number];

export const HELP_LANGUAGE_LABELS: Readonly<Record<HelpLanguage, string>> = {
  en: 'English',
  ru: 'Русский',
  uk: 'Українська',
  de: 'Deutsch',
  es: 'Español',
};

/** One article's text in one language. Every field required — the style IS the schema. */
export interface HelpBody {
  readonly title: string;
  readonly whatItIs: string;
  readonly why: string;
  readonly setup: string;
  readonly usage: string;
  readonly whatCanGoWrong: string;
}

export interface HelpArticle {
  readonly id: string;
  /** English is the floor; every other language lives in its own module. */
  readonly en: HelpBody;
}

/**
 * The translations, one module per language.
 *
 * <p>Separate files rather than more fields on the article, because a catalog with five languages
 * inline is one literal nobody can edit without breaking a quote — and because a translation pass
 * is then a file, reviewable on its own, instead of a diff threaded through eighteen articles.</p>
 *
 * <p>A language may be PARTIAL. What it does not carry falls back to English with a visible note,
 * which is the one behaviour a missing translation must have: never a blank page.</p>
 */
const TRANSLATIONS: Readonly<Record<Exclude<HelpLanguage, 'en'>, Readonly<Record<string, HelpBody>>>> = {
  ru: RU,
  uk: UK,
  de: DE,
  es: ES,
};

/** The article as shown: the asked language, or English with a visible note. */
export function bodyFor(
  article: HelpArticle,
  language: HelpLanguage,
): { body: HelpBody; fallback: boolean } {
  if (language === 'en') {
    return { body: article.en, fallback: false };
  }
  const body = TRANSLATIONS[language][article.id];
  return body === undefined ? { body: article.en, fallback: true } : { body, fallback: false };
}

export const HELP_ARTICLES: readonly HelpArticle[] = [
  // ---------- the first ten minutes, in the order they happen ----------
  {
    id: 'install-the-server',
    en: {
      title: 'Start here: install the MCP server',
      whatItIs:
        'The extension is a face. The reviewing is done by `coai-mcp`, a small program your AI assistant starts and talks to. Nothing reviews anything until it is installed.',
      why:
        'The review has to run where your code is, drive vendor CLIs, and keep going for minutes at a time. A VS Code extension cannot do any of that, and a server that VS Code owned would die with the window.',
      setup:
        'Open the **Server** section of the panel. It shows what is installed, what is published, and a button when those differ — press it. The download is verified against the release\'s own `.sha256`, and the binary lands in this extension\'s private storage, never on your `PATH`.\n\nThen the ⋯ menu → **Copy the MCP config block**, and paste it into your assistant\'s config. That is pasted ONCE. Restart the assistant so it starts the server.',
      usage:
        'After that the Server section is somewhere you visit rarely: it re-checks the published version at most every half hour, and **Check again** asks now.\n\nThe binary is per-platform. Windows, macOS and Linux builds are published for every release; a machine with no published build says so rather than downloading the wrong one.',
      whatCanGoWrong:
        'If the panel says the published version cannot be read, GitHub was unreachable — nothing is broken, and the installed server keeps working.\n\nIf your assistant does not list the tools after pasting the block, it has not restarted. The config block names the exact binary path; a copied block from another machine points at a path that does not exist here.',
    },
  },
  {
    id: 'choose-reviewers',
    en: {
      title: 'Then: choose who reviews',
      whatItIs:
        'The **Reviewers** section is the panel of other vendors\' models that will read your plan and your code. Each row is one vendor: a checkbox to include it, a model picker, a ▶ to open its CLI in a terminal, a ⤓ to install that CLI, a ⟳ that goes green when a newer version is published, its price per million tokens, and **remove**.',
      why:
        'The whole point is that the reviewer is not the author. A second opinion from the same model that wrote the code is worth less than a first opinion from a different one — and two vendors agreeing on a finding is the strongest signal this product produces.',
      setup:
        '**＋ Add a reviewer** offers the vendors this build knows: Codex, Antigravity (Gemini, Claude and GPT-OSS through one subscription), a second Claude, DeepSeek, OpenRouter, and a blank for any OpenAI-compatible endpoint.\n\nMost of them authenticate as themselves — if the CLI is signed in on this machine, no key is needed. Press ▶ on a row to open that vendor\'s CLI in a terminal: that is where you sign it in, and where its own usage command is typed and waiting for you to press Enter. Press ⤓ and the same terminal opens with the INSTALL command typed instead — the vendor\'s own published one, for the operating system that terminal actually runs in. VS Code attached to WSL gets the Linux command, not the PowerShell one.\n\n**A Team server is the other way to get a reviewer, and it needs nothing installed here.** One machine somewhere runs the vendor CLIs on ONE company subscription, and everybody signs in to it with their work account instead of buying their own. Add one under **Team servers**, press **Sign in**, and its vendors appear in ＋ Add a reviewer with the accounts they have free. Your plan and your diffs are sent to that machine, which the panel says on every row \u2014 being the company\u0027s own server makes it no less true that the code leaves this one.',
      usage:
        '**⟳ tells you whether there is anything to update, before you press it.** Green means the vendor publishes a version newer than the one on this machine; grey means you are on the newest, or that one of the two numbers could not be read — both are in the tooltip, so the colour is never the only signal. Pressing it opens a terminal with the same command ⤓ uses, because for every CLI here re-running the installer IS the update: OpenAI prints one line under both *Install Codex* and *Update Codex*, Anthropic\'s native install is the same script, and `agy` has no update subcommand at all.\n\nTwo reviewers is the useful minimum, because agreement is the signal. Three costs three times as much and rarely says three times as much.\n\nThe model picker lists what that CLI can actually reach on this machine — for Codex, its own cached model list; for the others, a curated list. **another model…** takes any exact id you type.',
      whatCanGoWrong:
        'Removing the last reviewer is refused: a panel with nobody in it reviews nothing.\n\nA vendor whose CLI is not installed reports that in **providers** before a round rather than failing halfway through one. Gemini in particular is marked RETIRED — Google closed Code Assist for individual accounts, and that CLI now refuses before it reaches a model. Use Antigravity instead.\n\nA Team server reviewer says what is wrong with it in its own row: *not signed in* (press Sign in), *sign in again* (the session expired), *outside the company domain* (signing in again cannot fix that one), or how many accounts that vendor has left \u2014 all signed out is the operator\u0027s job, all rate-limited comes back by itself.',
    },
  },
  {
    id: 'teach-your-ai',
    en: {
      title: 'And: tell your AI to use the gate',
      whatItIs:
        'The ⋯ menu → **Copy the CLAUDE.md snippet** gives you a paragraph to paste into the repository you want reviewed. It tells the AI working there that a review gate exists and in what order to use it.',
      why:
        'An assistant that has the tools but no instruction will not think to open a review session. The snippet is the instruction, and it lives in the repository rather than in this extension because it is a property of the project, not of your editor.',
      setup:
        'Copy it, paste it into that repository\'s `CLAUDE.md` (or whatever your assistant reads), commit it. It names no repository — the AI reading it is already in a checkout it can name for itself.',
      usage:
        'From then on the assistant opens a session, sends its plan, resolves the findings, implements, sends the diff, and resolves again. You watch it in **Active rounds** and answer anything it escalates.',
      whatCanGoWrong:
        'Nothing enforces the snippet. An assistant can ignore it, and the gate cannot make itself be called — what it CAN do is refuse to review code before a plan has passed, which it does.',
    },
  },
  {
    id: 'the-gate',
    en: {
      title: 'The gate: rounds, the threshold, and what happens when they run out',
      whatItIs:
        'Three settings that decide when a review is over. **Rounds per stage** is how many attempts a stage gets. **Passes at or under** is how many gating findings are acceptable. **When the rounds run out** is what happens if it never gets there.',
      why:
        'Without a limit a review loop never ends: there is always one more finding. The threshold says what "good enough" is, in a number, before the argument starts.',
      setup:
        'Each ROLE has its own rounds and its own threshold, set beside that role’s prompts — architecture may be worth two passes with different lenses while performance is worth one, and a shared budget forces the cheapest role to pay for the most expensive. Each code role also has a **tick box on its own heading**: unticked, that role takes no part in the round — no reviewer is launched for it, nothing it would have found is counted, and it lends the stage neither its rounds nor its threshold. Its numbers and prompt picks are kept and come back unchanged when you tick it again. The last one cannot be unticked, because a round with no reviewer in it never resolves; plan review has one role and no tick box. **Deal the lenses across vendors** is the other switch there: off, every vendor answers the same question and two vendors agreeing on a finding is a fact the gate can use; on, every lens gets asked once at half the launches and that agreement is gone. The defaults are three plan rounds at a threshold of two, two code rounds at three, dealing off, and *Ask a human*. Only **blocking** and **major** findings count towards the threshold — minor and nit never gate.\n\nA finding raised by two vendors counts ONCE. A finding you rejected with a reason is discounted unless a reviewer raises it again with something new.',
      usage:
        '*Ask\n\nThree switches below that box do not change what the gate DECIDES — they are orders it hands back to whichever AI called it, and they are off unless you turn them on. **Work autonomously**: the assistant is told what autonomous MEANS, not merely to be it — every bug gets a red-green-red test; the documentation, README, manifest and module docs are updated with every change; ALL the tests run before a release; a release or pull request is made where the repository has one, and the automatic comments of a pull request are read five minutes later and fixed; an automatic deploy is verified against dev, stage or test and its logs read; the code is re-read against the rules of the repository; and it says it is working autonomously and what it is writing. Questions that do not block are collected and asked at the end, all together; one that does block is asked at once, but only after every other blocking question has been gathered, so you are interrupted once. **Split the plan into epics and stories**: after a plan passes, the assistant is told to break it into 2-4 epics and each into 2-4 stories, and to close every story properly — review the diff here, fix, document, test, commit, then the next one. The gate measures the plan you sent and says whether it needs epics, stories or nothing, and says out loud that this is a heuristic. The order is given once per assistant: each epic comes back for its own plan review and is told it is a PIECE — build it as one unit and close it properly — rather than told to split again, which would have no end. **Split with Fable**: when a Fable vendor is configured, the split itself and the stories where being wrong is expensive — payments, security, architecture — go to Fable at its highest version, and the ordinary ones to Opus. a human* is the honest default: the gate stops and puts the decision in front of you. *Continue anyway* proceeds and says out loud that findings remain — it touches none of them, which is how a gate becomes decoration. *Good enough — take what’s true and move on* is the one between: the AI reads the open findings, applies the ones that are true and useful, rejects the rest with reasons, and proceeds. *Escalate* climbs a ladder — more reviewer effort, then a stronger reviewer model, then a stronger arbiter — and gives the stage a fresh set of rounds each time.',
      whatCanGoWrong:
        'A threshold of zero means every finding of any severity gates, which in practice means a review never passes.\n\nRounds are per STAGE: the plan gate and the code gate each get the full count.',
    },
  },

  {
    id: 'a-local-model',
    en: {
      title: 'A model on your own machine as a reviewer',
      whatItIs:
        'A reviewer called **local**, backed by an engine running on this machine — Ollama, or a vLLM, or anything speaking the same OpenAI-compatible shape. Its model dropdown is what YOU have installed, with each model\'s parameter size, quantisation and disk size, because what is installed is a fact about your machine rather than a list this extension could ship.',
      why:
        'It costs nothing per round and nothing leaves the building. That makes it the reviewer you can afford to run on every push, and the only one you can use on a change you are not allowed to send anywhere. It is a different kind of reviewer from the hosted ones rather than a cheaper one: the panel exists so you can mix.',
      setup:
        'Add it from **＋ Add a reviewer**, and the row fills itself in: the engine is found on 11434 (Ollama) or 8000 (vLLM), and the dropdown lists what it has. Nothing found says where it looked and why — refused, timed out, or answered something odd — rather than showing an empty list.\n\nThe endpoint box is for everything the probe cannot see: a vLLM on another port, a machine on your network, an engine behind a proxy. It wants the OpenAI-compatible base, ending in `/v1` — which is NOT the address you would open in a browser: Ollama serves its own API at the root and the compatible one under `/v1`, and a base without it fails at the first review with a 404 that reads like a model problem.\n\n⟳ asks the engine again. A successful probe is cached for a minute so the panel is not listing models on every repaint, which means a model you have just pulled is not there yet; that is the button for it.',
      usage:
        '**Give it room.** A review is the prompt, the plan or the diff, and the schema, all in one request. A model with a small context window refuses rather than answering badly — which is honest and still a wasted round — so prefer the larger-context build of a model where you have both.\n\nThere are no ▶, ⤓ or ⤤ buttons on this row, because those are a CLI\'s: there is nothing to sign in, install or update. Tokens ARE counted — the engine reports them and they appear in the spending chart — but money is a dash, because a model on your own hardware has no token bill. What it costs is electricity and the card being busy, and this panel can see neither.',
      whatCanGoWrong:
        '**An endpoint that is not on this machine is announced in the row, in red.** Every review sends the prompt — your plan, your diffs and the file contents around them — to whatever host is in that box. That is the whole point of a remote engine and it is also how source code leaves a building by accident, so the row says which host and what goes to it. Only use one you control.\n\n**In a window attached to WSL, an engine on the Windows side needs two fixes, not one.** It is bound to `127.0.0.1`, so start it with `OLLAMA_HOST=0.0.0.0` — and this side\'s `127.0.0.1` is WSL\'s own loopback rather than the Windows host, so the endpoint must point at the Windows host address.\n\n**A model the engine no longer lists** stays in the dropdown marked *NOT on this engine any more*, rather than vanishing — which would silently switch your reviewer — or looking normal, which would send a round for a model that answers 404.',
    },
  },
  // ---------- the rest of the panel, control by control ----------
  {
    id: 'prompts-per-round',
    en: {
      title: 'Prompts per round: which question each round asks',
      whatItIs:
        'Each reviewer role has a universal prompt and five narrow lenses. This section picks which prompt each ROUND of that role uses, and it sits with that role\'s rounds and threshold, because those three settings answer one question together.\n\nThe last twelve lenses were measured before they shipped: three wordings each, two runs each. The three wordings turned out to be three SHAPES, and they find the same AMOUNT while differing in how much survives a second run — a lens written as a task to perform (*“run it twice, a millisecond apart, and narrate both”*) repeated itself half again as often as the same question asked as a checklist.',
      why:
        'One prompt per role forever is the right default and the wrong ceiling. Asked to look at everything, a model spreads itself thin, and a second round of the same broad question tends to return the same broad answers.',
      setup:
        'One picker per round per role; each option carries its purpose as a tooltip. A round you have not touched shows what the server will actually run for it — the picker is never a guess, and never a prompt nobody would run.\n\n**Round one of every CODE role is the conventions pass unless you pick otherwise.** It judges the diff against the rules this project has written down — nothing else — and a finding there has to quote the sentence it breaks. A repository with no rule files falls back to that role\'s universal prompt, because a conventions pass with no conventions has nothing to say.\n\nEvery other unset round is that role\'s universal question. A lens is asked when you ask for it: there is no automatic rotation any more. There was one, it had no switch the panel could reach, and its only remaining effect was to make this picker name a prompt the server would not run.',
      usage:
        '**Deal the lenses across vendors** is the switch beside these pickers, and it is the one that changes how a round is spent: off, every vendor answers every question of that round; on, the round\'s prompts are dealt out one per vendor, so a round costs one launch per vendor instead of one per prompt per vendor. What it spends is agreement — two vendors filing the same finding is the strongest signal this product produces, and a dealt round cannot produce it. It is off by default for exactly that reason.\n\nWhen you do want two different lenses on one change, set them on two rounds and let both run. It was measured: over two code rounds, spending them on different lenses found FEWER distinct findings than asking the universal question twice (17 against 25) for less money — which is why nothing does it for you.\n\nRead the full text of every prompt in **The prompts, in full**.',
      whatCanGoWrong:
        '**What the measurement does not establish.** Across three plans the union of three lenses found roughly twice what any single one did — and that result does not survive its own control: the SAME prompt on the SAME text three times produced 6, 4 and 5 findings whose overlaps were 3, 1 and zero. Run-to-run variance alone explains the spread. The lenses are offered because they are useful to aim, not because they were shown to find more.',
    },
  },
  {
    id: 'limits',
    en: {
      title: 'Limits: how many at once, how long each may take',
      whatItIs:
        '**Reviewers at once** caps the whole fan-out. **Per vendor** caps one vendor. **Reviewer timeout** is how long a single reviewer may run. **Round limit** is how long they all get between them. **Wait for you** is how long an escalation waits before giving up on a person.',
      why:
        'A code round is six processes wanting to start in the same instant — three roles times two vendors. Unbounded, that is where local process limits, the CLIs\' own lock files and the vendors\' rate limits all arrive at once, each looking like a timeout unless it is handled by name.',
      setup:
        'Three at once and two per vendor are the defaults. The per-vendor cap exists because a rate limit is per vendor: a global cap alone would happily spend all of its slots on one of them.\n\nThe reviewer timeout is ten minutes. A code round on a large diff takes three to five.\n\nThe round limit bounds a whole round rather than one reviewer, and it is 0 by default — which means it is worked out rather than guessed. A round runs your vendors times its roles through the cap above, so it takes as many waves as that division needs, and each wave can honestly take a full reviewer timeout: three vendors, four code roles and a cap of three is four waves, forty minutes. Set a number and you override that. Set a small one and you are cutting into reviewers that have not finished — they are cancelled, their findings are lost, and the round is decided without them.',
      usage:
        'Raise the global cap on a machine with cores to spare; lower it on a laptop you are also working on. The per-vendor cap is the one to lower if a vendor starts rate-limiting you.',
      whatCanGoWrong:
        'A timeout kills the whole process tree, not just the CLI — a reviewer\'s own children do not outlive it.\n\n**Wait for you** running out is not a failure: the escalation stays open in the panel, and the AI is told to ask you in the conversation instead.',
    },
  },
  {
    id: 'language-and-translator',
    en: {
      title:
        "Language: the questions are English",
      whatItIs:
        "There is nothing to set here any more. A `call_human` question reaches you as one fixed English sentence and three buttons, and your answer goes back exactly as you gave it.",
      why:
        "There used to be a translator: the question was prose an AI had written, you had to read it, and you answered in your own words. Three buttons removed all of that. A subprocess per escalation that can time out, refuse, or answer in the wrong language is a moving part earning nothing.",
      setup:
        "Nothing. The **Ask and answer in** and **Translated by** controls are gone, along with the `COAI_LANGUAGE` and `COAI_TRANSLATOR_*` settings behind them.",
      usage:
        "The language of THIS help is separate and still yours: the selector at the top of these pages switches it, and it is stored as `coai.helpLanguage`. What changed is the reviewers’ side, not the reading side.",
      whatCanGoWrong:
        "Nothing here can fail any more, which was the point. If you type free text on a button that offers it, it reaches the AI unmediated — worth more than the same words rendered into another language by a third model.",
    },
  },
  {
    id: 'vendor-keys',
    en: {
      title: 'Vendor keys: only when a CLI cannot sign in for itself',
      whatItIs:
        'A single CredsForDevs `config` entry holding one key per vendor that needs one. The panel shows which of your reviewers actually need it.',
      why:
        'Most reviewer CLIs are already signed in on your machine and need no key at all. The ones that do — a custom endpoint, DeepSeek, OpenRouter — should not have their keys typed into a settings file.',
      setup:
        'Only needed if a reviewer row says so. Create a `config` entry in CredsForDevs, put the keys in it as `{"deepseek": "sk-…"}`, and paste its key id into **CredsForDevs config key**.',
      usage:
        'The server reads the entry at startup and passes each key to its vendor in the environment, never on a command line. Rotating a key takes effect when the server next starts.',
      whatCanGoWrong:
        'A vendor that needs a key and has none is reported as `unavailable` by **providers**, with the reason, and is left out of the fan-out — it is not silently skipped.',
    },
  },
  {
    id: 'chat-with-other-ai',
    en: {
      title:
        'Chat with other AI: ask a second model about a passage',
      whatItIs:
        'Select a paragraph in your assistant’s answer, press `Ctrl+Alt+A`, and it opens a tab where '
        + 'another vendor’s model explains it — in your language, in a conversation you can carry on. '
        + 'The same thing is in the right-click menu as **Chat with other AI**.',
      why:
        'A dense English answer is not always a clear one, and asking the model that wrote it to '
        + 'explain itself gets you the same words again. A different vendor reads it cold. Doing that by '
        + 'hand is five steps — select, copy, switch to a browser, new chat, paste — several times an '
        + 'hour; this is one keypress, and the answer stays in the editor.',
      setup:
        'Nothing, if a reviewer on the `antigravity` runtime is already enabled — that is which reviewer answers a chat, and the first one that can is used unless you name another. Four settings are yours: the prompt the passage travels with (one word, `Explain`, by default), the language the other AI answers in (English by default, and deliberately not the language of these help pages), which model a new tab OPENS with, and whether it sends at once or waits. The model is no longer only a setting — the tab has a picker of its own, and what you choose there belongs to that conversation rather than to every future one.\n\n**Edit chat presets** opens a tab with both lists in it, and everything there saves as you type. Since presets arrived, that single prompt is **your saved prompts, each with a name** — one of them ticked as the main one, which is the one used when a capture sends by itself. Beside them are **your saved models, each with a name** and optionally a prompt to open the composer with. Whatever you had typed into the old single prompt became your first named preset the first time this version read it, so nothing you wrote was lost.\n\n**In the sidebar, *What to ask about the selection* is a picker.** It names your saved prompts rather than holding a copy of one, and **Edit presets…** beside it opens the tab where the words themselves are edited — there is nowhere for the two to disagree now. Which of your saved prompts a chat opens with is what it remembers, and empty means the main one. *Which model answers* asks the same two questions the tab does — the provider first, then which of its models answers — and an empty second box means whatever that reviewer row is set to.',
      usage:
        'The keybinding copies the selection for you and asks straight away. The right-click item cannot — closing that menu takes the selection out of the panel — so it takes whatever you last copied and puts it in the box for you to send, which is also why the passage is shown at the top of the tab: you can see what is about to be asked. One tab per assistant session, so two conversations never mix.\n\n**The composer stays where it is** and the conversation scrolls above it. A new answer is scrolled to only when you were already at the bottom: scroll up to read something and nothing drags you away, and **Jump to newest ↓** takes you back when you want it. The box grows as you type, to about a third of the window, and only then scrolls. Enter sends, Shift+Enter is a new line, and there is a **Send** button for anyone who would rather press one.\n\n**Answers are rendered, not dumped** — headings, numbered and bulleted lists, tables, code. Links are blue, and what they open is bounded: a path INSIDE this workspace opens that file in the editor, an `http` or `https` address opens in your browser, and everything else stays text — a path that leaves the workspace, a `file://` URL, and an address a model merely mentioned in a sentence rather than wrote as a link. A file that cannot be found says so rather than doing nothing. Your own words sit on the right, an answer is captioned with the model that gave it in that model’s colour, and a rule closes it, so the end of a long answer is findable. **Copy** on any answer gives you its Markdown.\n\n**The picker asks for a provider, then for one of its models**, because a model list belongs to a provider. Switching mid-conversation is expected — the whole thread goes across — and beside the picker is what this conversation has cost so far. Above the box are two rows of presets: your models in their vendor’s colour, the same colour that vendor wears everywhere else here, and your prompts in a colour no vendor has, so a prompt can never be mistaken for a vendor.\n\n**An empty box with a different model chosen is a re-ask**: the same question goes to the other model, carrying the conversation minus the answer you did not want, and the button says whose turn it is before you press it. **Paste a screenshot** into the box and it goes with the next question. **Stop** appears while a turn is running and ends the turn it was drawn for, never the next one.',
      whatCanGoWrong:
        'Copying the selection needs a Windows session: this machine’s own, or — from a WSL window — the one that window is attached to. Anywhere else the keybinding says so and points you at the menu, which works everywhere. When the helper cannot be reached, or is started and does not finish, the message names that instead of blaming your selection. If the model’s process dies the answer after it says the conversation restarted, because it genuinely does not remember the earlier turns. A reviewer on another runtime is refused by name rather than quietly missing — the chat speaks one protocol so far. Reloading the window keeps what was SAID: every open chat tab comes back with its questions and answers, and a line saying the conversation was closed by the reload. The model behind it is gone — nothing is running until you ask again, and that first question carries the whole transcript across to a new session, which is what it costs.\n\n**A picture is refused BY NAME rather than sent into a void.** `codex` is refused as UNTESTED rather than as incapable — its account hit a usage limit mid-measurement, and that is a different sentence from a measured no — and a Team server reviewer has nowhere on the wire to put one yet. An SVG is refused although it is an image: it is markup with script in it, and what this does is hand a file to a process that will open it.\n\n**The running total wears a tilde for two vendors of three.** Only `claude` reports what it charged; for the others the figure is worked out from tokens, and one estimated turn makes the whole total an estimate. The exact record, turn by turn, is in **Show review rounds** — a conversation is a row in that log too. And it climbs: every question carries the whole conversation, so a long thread is slower and dearer than a short one. Capturing a fresh passage is the remedy.',
    },
  },
  {
    id: 'recent-rounds',
    en: {
      title: "Active rounds: what is running right now",
      whatItIs:
        "Every round in flight, newest first, shown whole: what is being reviewed, the stage, the branch, how long it has run — and under it every reviewer the round launched, with what each has done so far, how many findings it has filed, how long IT has taken and what it read.",
      why:
        "A review takes minutes, and what somebody is waiting on is not a verdict but the reviewers producing it. A finished round is a different question — \"what happened\" — and that is a log, which wants a table with filters, sorting and search rather than a sidebar. So the sidebar keeps only the present tense.",
      setup:
        "Nothing to set up. The server writes a round to disk the moment it starts and updates it as each reviewer moves, so the picture survives a reload, a restarted extension and a killed server.",
      usage:
        "There is nothing to click: a running round is already open, and when it finishes it leaves the sidebar. Everything that has ever run — finished, interrupted, every reviewer and every number — is in **Show review rounds** (the ⋯ menu on the panel's title).\n\nThat log holds your CONVERSATIONS as well as your rounds. Its **Kind** column says which a row is — *review* or *conversation* — and the filter beside it narrows to one or shows both, because “what did today cost me” is rarely a question about only one of them. A conversation leaves the repository, branch and stage columns empty: a chat is not held against a branch.",
      whatCanGoWrong:
        "A round abandoned by a crashed server is swept to *interrupted* on the next start and disappears from here; it is still in the log. If the section reads *Nothing is running* while your assistant says it is reviewing, the server it talks to is writing somewhere else — a `COAI_DATA_DIR` in its config that this window does not share.",
    },
  },
  {
    id: 'what-each-ai-has-used',
    en: {
      title: 'What each AI has used: tokens, money and time',
      whatItIs:
        'A bar per vendor over a day, a week, a month or a year: tokens in and out, **money**, how many runs, how many failed, total and average time — and one line at the bottom totalling every vendor. Chat turns are counted too, in a ledger of their own, and appear as *conversation* rows in the rounds log.',
      why:
        'A review panel spends real money on every round, and the question "what has this cost me this month" cannot be answered from a session file — sessions are rewritten as rounds advance and hold one branch each.',
      setup:
        'Nothing to set up for the tokens: the server appends one line per reviewer to `usage.jsonl` in its data directory, and this section reads it.\n\nFor MONEY there is one thing, and only you can supply it: **what this vendor charges per million tokens, in and out**, in the vendor\'s own row. This product ships no price table on purpose — a shipped one is wrong for anybody on a flat subscription, wrong the first time a vendor changes a price, and wrong silently in both cases. Fill in a rate and that vendor\'s money appears; leave it empty and it stays a dash.',
      usage:
        '**A failed reviewer is counted too.** A run that burned ninety seconds and answered nothing is exactly what a spending record must not hide, so every row carries its outcome and the failed count sits beside the tokens.\n\n**The tilde is load-bearing.** `$0.42` is what a vendor billed; `~$0.42` is what your own rate works out to. Claude reports its cost, Codex and Antigravity report tokens only — so those two are a tilde or a dash, never a bare figure. The total keeps the halves apart for the same reason: it reads `$0.18 + ~$0.31` rather than adding a fact to a calculation and presenting the sum as either.\n\nBelow a dollar, money is shown to four decimals. A round that cost eight hundredths of a cent is a real number, and rounding it to `$0.00` says the panel is not counting.',
      whatCanGoWrong:
        'Token counting is per vendor because one rule would be wrong for at least one of them: Codex folds cached tokens INTO its input count, Claude reports them BESIDE it, and Antigravity\'s thinking tokens sit inside its output count. Claude also reports the same run twice with different numbers; the aggregate one is used.',
    },
  },
  {
    id: 'questions-waiting',
    en: {
      title: 'A question waiting on you',
      whatItIs:
        'When the gate needs a person, a card appears at the top of the panel with the question and the findings still gating. A modal opens, the status bar shows it, and the title-bar icon turns green.',
      why:
        'A round is BLOCKED behind that question. A notification that can be missed is the wrong shape for it, which is why there are three surfaces and dismissing the modal loses nothing.',
      setup:
        'Nothing to set up. The server writes the question as a file in the directory this extension already watches — no port is opened by either half.',
      usage:
        'Answer it in the panel, in the modal, or from the status bar; they are the same action. Your answer goes back to the AI that asked, translated into the language it asked in, with your own words kept beside it.',
      whatCanGoWrong:
        'A `call_human` verdict raises one of these too, so a gate that ran out of rounds reaches you even if the AI says nothing. That was not always true: the verdict used to be an instruction to the AI alone, and a person could watch the panel all day and never learn the gate had asked for them.',
    },
  },

  // ---------- the prompts, in full ----------
  {
    id: 'prompts-in-full',
    en: {
      title: 'The prompts, in full',
      whatItIs:
        'The complete text of every prompt this product sends to a reviewer — the four universal ones, the twenty narrow lenses, and the conventions pass. Nothing is paraphrased here; this is what the model reads.',
      why:
        'A review you cannot audit is a review you have to take on faith. Knowing exactly what was asked is what lets you judge whether an answer was fair — and whether a finding you disagree with came from a bad model or a bad question.',
      setup:
        'Nothing to set up. What is printed below is compared against the server\'s own prompt files by a test, so it cannot drift from what actually runs.\n\nThe **conventions** prompt is grouped on its own rather than under a role, because it is not a lens on one role\'s question: it is a different question that all three code roles ask in round one. Its rules arrive with it — the project\'s own `CLAUDE.md`, `AGENTS.md`, `GEMINI.md` and `.claude/rules`, read from the worktree of the commit under review and quoted verbatim, up to 40 KB, with anything left out named rather than silently dropped.',
      usage:
        'Every prompt ends with the same discipline: a finding must name a concrete situation and the wrong outcome it produces, four kinds of non-finding are named and forbidden, and an empty findings list is stated to be a valid answer — a reviewer told to always find something will always find something.\n\nYou can override any of them: drop a file named after the prompt into `prompts/` inside the server\'s data directory, and it wins while it exists. Delete it to go back to the shipped text.',
      whatCanGoWrong:
        'An override is not validated. A prompt that does not ask for the finding schema will produce answers that cannot be parsed, and the round will report that reviewer as unparseable — with its raw answer kept on disk so you can see what you asked for.',
    },
  },

  {
    id: 'the-help-page',
    en: {
      title: 'This page: search, language, and text size',
      whatItIs:
        'The yellow ? in the panel title bar opens this help. The index lists every article with its first line; the box at the top searches all of them; the select changes language; the ± buttons change the text size, and the pair beside them the text tone — brighter, or dimmer and warmer.',
      why:
        'A settings panel with sixteen controls needs somewhere to say what each of them does, and a tooltip is not that place. Text size is here because eyesight is not a preference to be argued with — five steps either way is about ×1.6 up or down.',
      setup:
        'Nothing to set up. The language switch writes `coai.helpLanguage`, which scopes it to these pages only — it is not the language your reviewers are asked in, which lives in **Language**. The ± buttons write `coai.uiScale` and `coai.textTone`; zero on the tone is the colour your theme already uses, so an untouched control changes nothing. Both are real settings, so both sync to your other machines.',
      usage:
        'The search runs over titles AND the full text of every article in the language shown, so a word you remember from a paragraph finds the article it was in. Escape closes an article back to the index, and Back keeps whatever you had typed.\n\nAn article not yet translated shows English with a visible note rather than an empty page.',
      whatCanGoWrong:
        'A translation that lags behind the English is normal and is marked as such. What cannot happen is a missing article: the coverage test fails the build when a command or a setting has nothing written about it, which is why this page cannot quietly rot into a description of a product that no longer exists.',
    },
  },

  // ---------- machinery you cannot see from the panel ----------
  {
    id: 'the-protocol',
    en: {
      title: 'Under the hood: the protocol your AI follows',
      whatItIs:
        'Seven tools, in a fixed order. `open` starts a session for a repo and branch. `review_plan` sends the plan to every reviewer. `resolve` records an accept or reject for EVERY finding. `review_code` does the same for the diff, with three roles per vendor. `providers` reports health, `status` re-orients a resumed conversation, `ask_human` escalates to you.',
      why:
        'Ordering is enforced by refusal rather than by good behaviour: `review_code` refuses until a plan round has reached *proceed*, so a skipped stage is impossible rather than discouraged.',
      setup:
        'Nothing to set up — your AI drives this. The snippet you pasted into the repository is what tells it the order.',
      usage:
        'A rejection needs a reason, and the reason is kept: a finding you rejected is discounted in later rounds unless a reviewer raises it again with something new. That is what stops a loop from re-litigating a decision you already made.',
      whatCanGoWrong:
        'A round where NOBODY answered never passes. An empty result set is the absence of evidence, not evidence of absence — the gate calls for a person instead, which is the one case where "no findings" must not mean "approved".',
    },
  },
  {
    id: 'where-reviewers-run',
    en: {
      title: 'Under the hood: where a reviewer actually runs',
      whatItIs:
        'Every reviewer runs in an empty directory by default, the plan stage and the code stage alike. The code stage\'s switch — what a reviewer gets — has two positions: Fast hands it the composed prompt and nothing to explore; Full also gives it ONE git worktree, pinned to the branch\'s commit, read-only.',
      why:
        'Fast is the default because it was measured rather than preferred. On one commit, taking the checkout away made every hosted model find MORE useful defects — four to eight, six to ten, six to seven — at a half to a third of the input tokens, with no wrong finding from any of them, and three real defects surfaced that no run WITH a checkout had reached. A reviewer given a repository spends its attention deciding where to look; a reviewer given a diff reads the diff. On the plan stage the effect was cruder still: eight minutes and still running, for a fifteen-kilobyte document.',
      setup:
        'Nothing to set up. Worktrees live under the server\'s data directory and are pruned when a session opens, so a killed round leaves nothing behind. Full costs a checkout per round; Fast costs none.',
      usage:
        'What the prompt contains does not change between the two: the diff and this project\'s written rules are assembled by the server either way, so the only difference is whether there is a repository to wander into. Choose Full when the meaning of a change depends on callers the diff does not show — and know that on the commit measured, it found less. The plan stage has no switch, and its cost stated plainly: a plan reviewer cannot check that a `file.cs:line` reference in the plan is real.',
      whatCanGoWrong:
        'The one retry a reviewer gets also runs in an empty directory, for the same reason: a repair asks for the answer in the schema, not for more exploration. Handing it the checkout again was what made one code round in three lose a reviewer.',
    },
  },
  {
    id: 'when-a-reviewer-fails',
    en: {
      title: 'Under the hood: what happens when a reviewer fails',
      whatItIs:
        'Six named outcomes, never a silent zero: answered, timed out, exited non-zero, rate limited, could not start, or answered something that would not parse. A round that ran with four of six reviewers says so, by name.',
      why:
        'A panel that did not review is not a panel that approved. Every partial round is reported as partial, and the reviewer sentence you see in the panel names who failed and why.',
      setup:
        'Nothing to set up. A rate-limited reviewer is retried once after a backoff — unless the vendor said the limit is DAILY, which no retry can clear.',
      usage:
        'A failure carries the CLI\'s own words, chosen by content rather than position: the first line that announces an error, skipping stack frames and version banners. For five known failures — a retired CLI, an untrusted directory, a missing platform binary — it reports what to DO instead of what was printed.',
      whatCanGoWrong:
        'An answer that would not parse is kept on disk, under `unparseable/` in the data directory, and the failure names the file. That exists because the same answer, replayed by hand afterwards, succeeded — so the sentence named a symptom nobody could chase.',
    },
  },
  {
    id: 'settings-and-the-server',
    en: {
      title: 'Under the hood: how a setting reaches the server',
      whatItIs:
        'The panel writes your settings into a file in the server\'s own data directory, and the server re-reads that file whenever it changes.',
      why:
        'Settings used to reach the server only inside the pasted config block, which made every change to a threshold a chore: copy the block, find the client config, paste, restart. And then they applied only at startup — a gap invisible from both ends, because the panel saves instantly and says so.',
      setup:
        'Nothing to set up. A change in the panel is in effect for the NEXT round; there is no restart and nothing to re-paste.',
      usage:
        'A variable set in your assistant\'s own config still outranks the file — a variable there is more specific than a file any window may rewrite.\n\nThe panel writes only what DIFFERS from the defaults, so returning a setting to its default removes it from the file rather than pinning it.\n\nSeparate settings for each side: one machine can hold several working environments - a local window and one or more WSL distros - and VS Code hands the SAME settings file to all of them. Turn the switch on and each side keeps its own values, seeded from what it had at that moment, so nothing changes until you edit something. For one person working for two companies on one machine that is a different proxy, a different CLI path and a different vault key per side. Your text size and help language stay shared, because they belong to you rather than to the work.',
      whatCanGoWrong:
        'A half-written file leaves the last good configuration in place. A torn read that produced an empty vendor list would fail every reviewer and then report a panel that agreed with itself.',
    },
  },
  {
    id: 'the-audit-log',
    en: {
      title: 'Under the hood: the audit trail',
      whatItIs:
        'One log file per server run, beside the installed binary, under `logs/<day>/`. It holds the roster of every round, each reviewer\'s start and answer with its tokens and cost, every failure as a warning with its reason, and every finding with its origin.',
      why:
        'The round summary is deliberately one sentence, and one sentence cannot answer "why did this reviewer fail". That question was asked twice at a real gate and could not be answered either time.',
      setup:
        'Nothing to set up. Levels come from configuration rather than from call sites, and a new file is written per run — not per day, because the question is almost always "what did THAT run do".',
      usage:
        'At debug level each reviewer\'s exact command line is recorded, which is the difference between knowing a vendor was asked and being able to paste the command into a terminal and watch it fail the same way.',
      whatCanGoWrong:
        'The log is the only place some things are written down. It is not rotated or trimmed; a year of heavy use is a folder of small files, not a problem, but it is yours to clear.',
    },
  },
];

/** One article by id, for a lookup from a control. */
export function helpArticle(id: string): HelpArticle | undefined {
  return HELP_ARTICLES.find((a) => a.id === id);
}
