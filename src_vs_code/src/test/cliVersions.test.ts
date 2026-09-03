import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  needsShell,
  parseCliVersion,
  shimCommandLine,
  unquoted,
  updateAvailable,
  versionSourceFor,
} from '../cliVersions';

/**
 * Knowing whether a reviewer's CLI has an update, from what the CLI says and what its vendor
 * publishes.
 *
 * <p><b>Every source below was checked at the vendor's own site on 2026-09-01</b>, not recalled:
 * the three npm packages were queried live (`0.152.0`, `0.57.0`, `2.1.257`), and the antigravity
 * manifest is the endpoint Google's own `install.sh` reads — line 99 of the script builds
 * `$DOWNLOAD_BASE_URL/manifests/$platform.json` and takes `.version` from it. Six of those manifests
 * exist, one per os_arch, and all six answered.</p>
 *
 * <p>That checking is the point. The last thing assumed about a vendor here was that Antigravity had
 * no Linux CLI at all, and it was false, and a test written from the same assumption held it in
 * place for a day.</p>
 */

test('a version is read out of whatever the CLI prints', () => {
  // The real strings: `codex --version` and `agy --version` as run on this machine, and the format
  // Anthropic's own docs give for `claude --version` ("prints a version number such as
  // `2.1.211 (Claude Code)`").
  assert.equal(parseCliVersion('codex-cli 0.152.0'), '0.152.0');
  assert.equal(parseCliVersion('1.1.23'), '1.1.23');
  assert.equal(parseCliVersion('2.1.211 (Claude Code)'), '2.1.211');
  assert.equal(parseCliVersion('  0.55.1\n'), '0.55.1');
});

test('output with no version of its own is not half a version', () => {
  assert.equal(parseCliVersion(''), '');
  assert.equal(parseCliVersion('command not found'), '');
  // A node CLI that fails prints its own banner last, and that banner has a version in it. Taking
  // it would report the runtime's version as the vendor's — the same trap `ReviewerSummaryFactory`
  // already learned about, where `exit 1: Node.js v20.20.2` hid the real cause.
  assert.equal(parseCliVersion('Error: Missing optional dependency\n\nNode.js v20.20.2'), '');
});

test('versions compare as numbers, which is the whole reason this is a function', () => {
  // The case a string compare gets wrong, and the reason a `<` would have shipped a button that
  // lies: '0.9.0' > '0.10.0' as text.
  assert.equal(updateAvailable('0.9.0', '0.10.0'), true);
  assert.equal(updateAvailable('0.10.0', '0.9.0'), false);
  assert.equal(updateAvailable('1.1.23', '1.1.23'), false);
  assert.equal(updateAvailable('0.152.0', '0.152.1'), true);
  assert.equal(updateAvailable('2.0.14', '2.1.0'), true);
});

test('an unknown version on either side is never an update', () => {
  // "I could not tell" renders grey, never green: a button that lights up because a fetch failed is
  // worse than one that never lights up.
  assert.equal(updateAvailable('', '1.0.0'), false);
  assert.equal(updateAvailable('1.0.0', ''), false);
  assert.equal(updateAvailable('', ''), false);
});

test('the npm packages are the ones the vendors publish', () => {
  const npm = (runtime: string): string | undefined => {
    const source = versionSourceFor(runtime, 'linux', 'x64');
    return source?.kind === 'npm' ? source.package : undefined;
  };

  assert.equal(npm('codex'), '@openai/codex');
  assert.equal(npm('gemini'), '@google/gemini-cli');
  assert.equal(npm('claude'), '@anthropic-ai/claude-code');
});

test('antigravity is read from the manifest its own installer reads, per os and arch', () => {
  const url = (platform: 'linux' | 'win32' | 'darwin', arch: 'x64' | 'arm64'): string => {
    const source = versionSourceFor('antigravity', platform, arch);
    assert.equal(source?.kind, 'manifest');
    return source?.kind === 'manifest' ? source.url : '';
  };

  // All six were fetched and all six answered 1.1.23. The naming is the installer's own
  // `${os}_${arch}`, not a guess: amd64 rather than x64, windows rather than win32.
  assert.ok(url('linux', 'x64').endsWith('/manifests/linux_amd64.json'));
  assert.ok(url('linux', 'arm64').endsWith('/manifests/linux_arm64.json'));
  assert.ok(url('darwin', 'x64').endsWith('/manifests/darwin_amd64.json'));
  assert.ok(url('darwin', 'arm64').endsWith('/manifests/darwin_arm64.json'));
  assert.ok(url('win32', 'x64').endsWith('/manifests/windows_amd64.json'));
  assert.ok(url('win32', 'arm64').endsWith('/manifests/windows_arm64.json'));
});

test('a runtime this build does not know has no source at all', () => {
  // Never a guessed registry path. An unknown runtime rides the Codex CLI for REVIEWS, which is a
  // deliberate fallback — but "which version is installed" is a different question, and answering
  // it with codex's number for a vendor that is not codex would be a confident lie.
  assert.equal(versionSourceFor('deepseek', 'linux', 'x64'), undefined);
  assert.equal(versionSourceFor('', 'linux', 'x64'), undefined);
});

test('every source is a host the vendor itself publishes from', () => {
  const OFFICIAL = ['registry.npmjs.org', 'antigravity.google', 'run.app'];
  for (const runtime of ['codex', 'gemini', 'claude', 'antigravity']) {
    for (const platform of ['linux', 'win32', 'darwin'] as const) {
      const source = versionSourceFor(runtime, platform, 'x64');
      assert.ok(source !== undefined, `${runtime} has no version source`);
      const host = source.kind === 'npm' ? 'registry.npmjs.org' : new URL(source.url).host;
      assert.ok(
        OFFICIAL.some((o) => host.endsWith(o)),
        `${runtime} on ${platform} would read its version from ${host}, which the vendor does not publish`,
      );
    }
  }
});

/**
 * Asking a `.cmd` shim its version — the one case that needs a shell, and the boundary where a
 * typed path would become something `cmd.exe` interprets.
 *
 * <p>Measured 2026-09-03 on this machine: without a shell, `spawn('codex.cmd', …)` throws EINVAL
 * synchronously (node's 2024 argument-injection fix), which took the panel's repaint down with it
 * and left codex and gemini with no version at all. With it, `codex.cmd` answers 0.152.0 and
 * `gemini.cmd` 0.57.0.</p>
 */

test('only a Windows shim gets a shell', () => {
  assert.equal(needsShell('codex.cmd', 'win32'), true);
  assert.equal(needsShell('C:\\npm\\gemini.CMD', 'win32'), true, 'the extension is matched case-insensitively');
  assert.equal(needsShell('run.bat', 'win32'), true);
  assert.equal(needsShell('claude.exe', 'win32'), false, 'a binary that answers without a shell must not get one');
  assert.equal(needsShell('coai-mcp', 'win32'), false);
});

test('nothing on POSIX gets a shell, whatever it is called', () => {
  // Two reviewers caught this independently, one as Blocking: `/bin/sh` would be handed a path
  // whose metacharacters this file does not filter — `$(whoami)` and backticks mean nothing to
  // cmd.exe and are not in the refusal list, so a `.cmd` on Linux would have been an injection.
  for (const platform of ['linux', 'darwin'] as const) {
    assert.equal(needsShell('/opt/tools/report.cmd', platform), false, platform);
    assert.equal(needsShell('codex$(whoami).cmd', platform), false, platform);
    assert.equal(needsShell('run.bat', platform), false, platform);
  }
});

test('a path pasted with Explorer quotes is unwrapped, not refused', () => {
  assert.equal(unquoted('"C:\\Program Files\\npm\\codex.cmd"'), 'C:\\Program Files\\npm\\codex.cmd');
  assert.equal(unquoted('  C:\\npm\\codex.cmd  '), 'C:\\npm\\codex.cmd');
  assert.equal(unquoted('codex.cmd'), 'codex.cmd');
  assert.equal(unquoted('"'), '"', 'one character cannot be a balanced pair');
  assert.equal(
    unquoted('"C:\\npm\\codex.cmd'),
    '"C:\\npm\\codex.cmd',
    'an unbalanced quote is left alone, so the refusal below still sees it',
  );
});

test('a real Windows path survives the quoting', () => {
  assert.equal(
    shimCommandLine('C:\\Program Files (x86)\\npm\\codex.cmd'),
    '"C:\\Program Files (x86)\\npm\\codex.cmd" --version',
    'spaces and parens are what the quotes are FOR — this is where most npm globals live',
  );
});

test('a path a shell must not be given yields no command at all', () => {
  // The executable can be typed into the settings, so this is a real boundary and not a formality.
  assert.equal(shimCommandLine('codex.cmd" & echo PWNED & rem .cmd'), '', 'a quote would close the quoting');
  assert.equal(shimCommandLine('C:\\a%PATH%\\codex.cmd'), '', '% expands INSIDE double quotes');
  assert.equal(shimCommandLine('C:\\a!x!\\codex.cmd'), '', 'so does ! under delayed expansion');
  assert.equal(shimCommandLine('codex.cmd\nnotepad'), '', 'a newline ends the command line');
  assert.equal(
    shimCommandLine('C:\\a&b^(c)\\codex.cmd'),
    '"C:\\a&b^(c)\\codex.cmd" --version',
    'these are literal inside quotes, so a legitimate path with them is not refused',
  );
});
