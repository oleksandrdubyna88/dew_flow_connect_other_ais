using CoaiMcp.Runners.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E4 s1 t2 (M4, M5, M6, M9, M10): the Node commands of a detected project, run INSIDE the check unit of E2 exactly as a coder job's steps are — the product's own <see cref="CheckBoundary"/> and <see cref="BoundaryJob"/>, no property changed, the project folder as the
/// working folder — on scratch projects (jest, vitest, node:test, typescript, a project whose lock file is out of step). The numbers and the consoles are in <c>v2/epoch-3/e4-adapters-coder-hardening/RESULTS_s1_node.md</c> and <c>src_mcp/tests/fixtures/toolchain</c>.
/// </summary>
/// <remarks>
/// Skipped unless <c>COAI_RECORD_NODE</c> names an ABSENT folder on a real disk with a short path. It needs the network (the registry) and about 10 minutes. The home of the unit is the stand-in of <see cref="MeasureBed"/> with a COPY of node; the real home is not used. No model is started.
/// </remarks>
[Collection("fakecli-env")]
public sealed class ANodeStepsAreMeasuredTests
{
    private const string RecordVariable = "COAI_RECORD_NODE";

    private const string TwoFailures = """
        import { test, expect } from 'vitest';

        test('passes always', () => { expect(1 + 1).toBe(2); });
        test('fails on the value', () => { expect(1 + 1).toBe(3); });
        test('fails by throwing', () => { throw new Error('boom from the scratch test'); });
        """;

    private static string Package(string name, string test, string dev, string type = "module") =>
        $$"""
        {
          "name": "{{name}}",
          "version": "1.0.0",
          "private": true,
          "type": "{{type}}",
          "scripts": {
            "test": "{{test}}",
            "build": "tsc -p .",
            "typecheck": "tsc --noEmit"
          }{{(dev.Length == 0 ? string.Empty : $",\n  \"devDependencies\": {{ {dev} }}")}}
        }
        """;

    private const string TsConfig = """{ "compilerOptions": { "strict": true, "target": "es2022", "module": "nodenext", "rootDir": "src", "outDir": "dist" }, "include": ["src"] }""";

    private static IReadOnlyDictionary<string, string> Scratch() => new Dictionary<string, string>
    {
        ["jest/.gitignore"] = "node_modules/\ndist/\n",
        ["jest/package.json"] = Package("scratch-jest", "jest", "\"jest\": \"*\"", "commonjs"),
        ["jest/sum.test.js"] = "test('passes always', () => { expect(1 + 1).toBe(2); });\ntest('fails on the value', () => { expect(1 + 1).toBe(3); });\ntest('fails by throwing', () => { throw new Error('boom from the scratch test'); });\n",
        ["jest/empty.test.js"] = "// a test file with no test in it\n",
        ["jest/helper.js"] = "module.exports = 1;\n",
        ["vitest/.gitignore"] = "node_modules/\ndist/\n",
        ["vitest/package.json"] = Package("scratch-vitest", "vitest run", "\"vitest\": \"*\""),
        ["vitest/sum.test.js"] = TwoFailures,
        ["vitest/empty.test.js"] = "// a test file with no test in it\n",
        ["vitest/helper.js"] = "export default 1;\n",
        ["nodetest/.gitignore"] = "node_modules/\ndist/\n",
        ["nodetest/package.json"] = Package("scratch-nodetest", "node --test", string.Empty),
        ["nodetest/sum.test.js"] = "import test from 'node:test';\nimport assert from 'node:assert/strict';\ntest('passes always', () => { assert.equal(1 + 1, 2); });\ntest('fails on the value', () => { assert.equal(1 + 1, 3); });\ntest('fails by throwing', () => { throw new Error('boom from the scratch test'); });\n",
        ["nodetest/empty.test.js"] = "// a test file with no test in it\n",
        ["nodetest/helper.js"] = "export default 1;\n",
        ["tserrors/.gitignore"] = "node_modules/\ndist/\n",
        ["tserrors/package.json"] = Package("scratch-tserrors", "echo no tests", "\"typescript\": \"*\""),
        ["tserrors/tsconfig.json"] = TsConfig,
        ["tserrors/src/index.ts"] = "const count: number = 'not a number';\nfunction shout(text: string): string { return text.toUpperCase(); }\nshout(42);\nexport { count };\n",
        ["tsok/.gitignore"] = "node_modules/\ndist/\n",
        ["tsok/package.json"] = Package("scratch-tsok", "echo no tests", "\"typescript\": \"*\""),
        ["tsok/tsconfig.json"] = TsConfig,
        ["tsok/src/index.ts"] = "export function add(a: number, b: number): number { return a + b; }\n",
        ["badci/.gitignore"] = "node_modules/\ndist/\n",
        ["uncached/.gitignore"] = "node_modules/\ndist/\n",
        ["uncached/package.json"] = Package("scratch-uncached", "echo no tests", "\"left-pad\": \"1.3.0\""),
        ["badci/package.json"] = Package("scratch-badci", "echo no tests", "\"is-odd\": \"3.0.1\""),
        ["badci/package-lock.json"] = """{ "name": "scratch-badci", "version": "1.0.0", "lockfileVersion": 3, "requires": true, "packages": { "": { "name": "scratch-badci", "version": "1.0.0" } } }""",
        ["noignore/package.json"] = Package("scratch-noignore", "echo no tests", "\"is-odd\": \"3.0.1\""),
    };

    private static readonly string[] WithLock = ["jest", "vitest", "nodetest", "tserrors", "tsok", "noignore", "uncached"];

    private static string Root => Environment.GetEnvironmentVariable(RecordVariable) ?? string.Empty;

    [Fact]
    public async Task ARecordedRunOfTheNodeCommandsInsideTheUnit()
    {
        Assert.SkipWhen(Root.Length == 0, $"set {RecordVariable} to a new folder on a real disk with a short path to measure the node commands inside the check unit (needs the network, about 10 minutes)");
        Assert.SkipUnless(SystemdUser.Works(out var why), why);
        Directory.Exists(Root).Should().BeFalse($"{Root} must not exist: the run makes it");

        await using var bed = await MeasureBed.CreateAsync(Root, main => MeasureBed.WriteAsync(main, Scratch()), withNode: true);
        // The lock files are made by npm inside the unit on the host network (the registry), then committed by the harness so that a later `git status` shows only what a step changed.
        await bed.StepAsync("n00-node-version", host: false, ["node", "--version"], node: true);
        await bed.StepAsync("n00-npm-version", host: false, ["npm", "--version"], node: true);
        foreach (var project in WithLock)
        {
            await bed.StepAsync($"n01-{project}-lock", host: true, ["npm", "install", "--package-lock-only", "--ignore-scripts", "--no-audit", "--no-fund"], project, node: true);
        }

        await bed.GitAsync(bed.Worktree, "add", "-A");
        await bed.GitAsync(bed.Worktree, "-c", "user.name=measure", "-c", "user.email=measure@example.invalid", "commit", "--quiet", "-m", "lock files");
        foreach (var project in WithLock.Concat(["badci"]))
        {
            var lockText = await File.ReadAllTextAsync(Path.Combine(bed.Worktree, project, "package-lock.json"));
            bed.Note($"lock of {project}: {lockText.Length} bytes, resolved: {string.Join(", ", ResolvedVersions(lockText, ["jest", "vitest", "typescript", "is-odd"]))}");
        }

        // M5: what does `npm ci` write? The top-level entries of the project folder before and after, the git status, and the job folder.
        var jobBefore = await Listing(bed, bed.Job.JobRoot);
        var before = await Listing(bed, Path.Combine(bed.Worktree, "jest"));
        var ci = await bed.StepAsync("n02-jest-npm-ci", host: true, ["npm", "ci", "--ignore-scripts"], "jest", node: true);
        bed.Note($"jest folder before npm ci: {before}");
        bed.Note($"jest folder after npm ci:  {await Listing(bed, Path.Combine(bed.Worktree, "jest"))}");
        bed.Note($"git status --ignored after npm ci (jest): {(await bed.StatusAsync()).Replace('\n', '|')}");
        bed.Note($"git status of the main checkout after npm ci: {(await bed.GitAsync(bed.Main, "status", "--porcelain", "--ignored")).StdOut.Replace('\n', '|')}");
        bed.Note($"job folder entries before: {jobBefore}");
        bed.Note($"job folder entries after:  {await Listing(bed, bed.Job.JobRoot)}");
        bed.Note($"sizes (KiB): node_modules of jest {await bed.SizeKiBAsync(Path.Combine(bed.Worktree, "jest", "node_modules"))}, npm cache of the job {await bed.SizeKiBAsync(bed.Job.NpmFolder)}, job folder {await bed.SizeKiBAsync(bed.Job.JobRoot)}");
        ci.ExitCode.Should().Be(0, ci.Text);

        // M5 without an ignore file: does git see node_modules?
        await bed.StepAsync("n03-noignore-npm-ci", host: true, ["npm", "ci", "--ignore-scripts"], "noignore", node: true);
        bed.Note($"git status of the project with no ignore file after npm ci: {(await bed.StatusAsync(ignored: false)).Replace('\n', '|')}");

        // M6: the three runners. Each: install, the failing run, a filter that finds no file, a file with no test, the passed names.
        foreach (var project in new[] { "vitest", "nodetest" })
        {
            await bed.StepAsync($"n04-{project}-npm-ci", host: true, ["npm", "ci", "--ignore-scripts"], project, node: true);
        }

        await RunnerAsync(bed, "jest", "jest", ["--verbose"]);
        await RunnerAsync(bed, "vitest", "vitest", ["--reporter=verbose"]);
        await RunnerAsync(bed, "nodetest", "node-test", []);

        // M4: typescript, build and typecheck.
        await bed.StepAsync("n06-tserrors-npm-ci", host: true, ["npm", "ci", "--ignore-scripts"], "tserrors", node: true);
        await bed.StepAsync("n06-tsok-npm-ci", host: true, ["npm", "ci", "--ignore-scripts"], "tsok", node: true);
        var tsc = await bed.StepAsync("n07-tserrors-tsc-no-emit", host: false, ["npx", "--no-install", "tsc", "--noEmit"], "tserrors", node: true);
        await bed.SaveFixtureAsync("node-tsc-two-errors.txt", tsc.Text);
        var typecheck = await bed.StepAsync("n07-tserrors-npm-run-typecheck", host: false, ["npm", "run", "typecheck"], "tserrors", node: true);
        await bed.SaveFixtureAsync("node-npm-run-typecheck-two-errors.txt", typecheck.Text);
        var buildBad = await bed.StepAsync("n07-tserrors-npm-run-build", host: false, ["npm", "run", "build"], "tserrors", node: true);
        await bed.SaveFixtureAsync("node-npm-run-build-two-errors.txt", buildBad.Text);
        await bed.StepAsync("n08-tsok-npm-run-build", host: false, ["npm", "run", "build"], "tsok", node: true);
        await bed.StepAsync("n08-tsok-tsc-no-emit", host: false, ["npx", "--no-install", "tsc", "--noEmit"], "tsok", node: true);
        await bed.StepAsync("n08-tsok-npm-run-typecheck", host: false, ["npm", "run", "typecheck"], "tsok", node: true);
        // A tool that is NOT installed: which spellings never reach for the registry? (private network: a download cannot happen, but the time and the text say whether it was tried; the host network is the control).
        await bed.StepAsync("n08-tsok-npx-no-install-missing-tool", host: false, ["npx", "--no-install", "prettier", "--version"], "tsok", node: true, timeout: TimeSpan.FromMinutes(4));
        await bed.StepAsync("n08-tsok-npx-no-missing-tool", host: false, ["npx", "--no", "prettier", "--version"], "tsok", node: true, timeout: TimeSpan.FromMinutes(4));
        await bed.StepAsync("n08-tsok-npm-exec-no-missing-tool", host: false, ["npm", "exec", "--no", "--", "prettier", "--version"], "tsok", node: true, timeout: TimeSpan.FromMinutes(4));
        await bed.StepAsync("n08-tsok-npm-exec-offline-no-missing-tool", host: false, ["npm", "exec", "--offline", "--no", "--", "prettier", "--version"], "tsok", node: true, timeout: TimeSpan.FromMinutes(4));
        await bed.StepAsync("n08-tsok-bin-missing-tool", host: false, ["./node_modules/.bin/prettier", "--version"], "tsok", node: true);
        await bed.StepAsync("n08-tsok-npx-no-install-missing-tool-host-control", host: true, ["npx", "--no-install", "prettier", "--version"], "tsok", node: true, timeout: TimeSpan.FromMinutes(4));
        bed.Note($"after the host-network control: node_modules of tsok holds prettier: {Directory.Exists(Path.Combine(bed.Worktree, "tsok", "node_modules", "prettier"))}; npm cache has it: {(await bed.Launcher.RunAsync(new CoaiMcp.Runners.Processes.ProcessRequest("grep", ["-rl", "--include=index-*", "registry.npmjs.org/prettier", bed.Job.NpmFolder], bed.Folder))).StdOut.Length > 0}");

        // The spellings that do not look for a tool outside the project, with the tool PRESENT.
        await bed.StepAsync("n08-tsok-npm-exec-offline-no-present-tool", host: false, ["npm", "exec", "--offline", "--no", "--", "tsc", "--noEmit"], "tsok", node: true);
        await bed.StepAsync("n08-tsok-npx-offline-no-install-present-tool", host: false, ["npx", "--offline", "--no-install", "tsc", "--noEmit"], "tsok", node: true);
        await bed.StepAsync("n08-tsok-bin-present-tool", host: false, ["./node_modules/.bin/tsc", "--noEmit"], "tsok", node: true);
        await bed.StepAsync("n08-tserrors-npm-exec-offline-no-present-tool", host: false, ["npm", "exec", "--offline", "--no", "--", "tsc", "--noEmit"], "tserrors", node: true);

        // npm ci with a lock file out of step (the error block), on the host network; and the same on a private network (the registry is not reachable).
        var bad = await bed.StepAsync("n09-badci-npm-ci", host: true, ["npm", "ci", "--ignore-scripts"], "badci", node: true);
        await bed.SaveFixtureAsync("node-npm-ci-error.txt", bad.Text);
        await bed.StepAsync("n09-tsok-npm-ci-private-network-warm-cache", host: false, ["npm", "ci", "--ignore-scripts"], "tsok", node: true);
        var offline = await bed.StepAsync("n09-uncached-npm-ci-private-network-cold-cache", host: false, ["npm", "ci", "--ignore-scripts"], "uncached", node: true, timeout: TimeSpan.FromMinutes(6));
        await bed.SaveFixtureAsync("node-npm-ci-no-network.txt", offline.Text);

        // M9: sizes of the logs of the round, and of the cache.
        var logs = Directory.GetFiles(bed.Logs, "*.log").Select(path => (Name: Path.GetFileName(path), Bytes: new FileInfo(path).Length)).ToList();
        bed.Note($"M9: {logs.Count} step logs, {logs.Sum(log => log.Bytes)} bytes in all, the largest {logs.MaxBy(log => log.Bytes)}");
        bed.Note($"M9: npm cache of the job {await bed.SizeKiBAsync(bed.Job.NpmFolder)} KiB; job folder {await bed.SizeKiBAsync(bed.Job.JobRoot)} KiB; worktree {await bed.SizeKiBAsync(bed.Worktree)} KiB");
        bed.Note($"git status of the worktree at the end: {(await bed.StatusAsync()).Replace('\n', '|')}");
        await bed.FinishAsync();
    }

    private static async Task RunnerAsync(MeasureBed bed, string project, string tag, IReadOnlyList<string> verbose)
    {
        var failed = await bed.StepAsync($"m6-{project}-npm-test", host: false, ["npm", "test"], project, node: true);
        await bed.SaveFixtureAsync($"node-{tag}-three-tests.txt", failed.Text);
        var names = await bed.StepAsync($"m6-{project}-npm-test-verbose", host: false, ["npm", "test", "--", .. verbose], project, node: true);
        await bed.SaveFixtureAsync($"node-{tag}-verbose.txt", names.Text);
        var onlyPassing = await bed.StepAsync($"m6-{project}-npm-test-by-name-pass", host: false, ["npm", "test", "--", .. verbose, ..NamePatternFor(project, "passes always")], project, node: true);
        await bed.SaveFixtureAsync($"node-{tag}-passing-only.txt", onlyPassing.Text);
        var noFile = await bed.StepAsync($"m6-{project}-given-a-file-no-runner-matches", host: false, ["npm", "test", "--", "helper.js"], project, node: true);
        await bed.SaveFixtureAsync($"node-{tag}-file-not-a-test.txt", noFile.Text);
        var empty = await bed.StepAsync($"m6-{project}-given-a-file-with-no-test", host: false, ["npm", "test", "--", "empty.test.js"], project, node: true);
        await bed.SaveFixtureAsync($"node-{tag}-file-with-no-test.txt", empty.Text);
        var missing = await bed.StepAsync($"m6-{project}-given-a-file-that-is-not-there", host: false, ["npm", "test", "--", "nothere.test.js"], project, node: true);
        await bed.SaveFixtureAsync($"node-{tag}-file-missing.txt", missing.Text);
    }

    private static string[] NamePatternFor(string project, string name) => project switch
    {
        "jest" => ["-t", name],
        "vitest" => ["-t", name],
        _ => ["--test-name-pattern", name],
    };

    private static async Task<string> Listing(MeasureBed bed, string folder)
    {
        var done = await bed.Launcher.RunAsync(new CoaiMcp.Runners.Processes.ProcessRequest("ls", ["-A", folder], bed.Folder));

        return done.StdOut.Replace('\n', ' ').Trim();
    }

    private static IEnumerable<string> ResolvedVersions(string lockText, string[] names)
    {
        foreach (var name in names)
        {
            var marker = $"\"node_modules/{name}\": {{";
            var at = lockText.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0)
            {
                continue;
            }

            var version = lockText.IndexOf("\"version\": \"", at, StringComparison.Ordinal);
            yield return version < 0 ? name : $"{name} {lockText[(version + 12)..lockText.IndexOf('"', version + 12)]}";
        }
    }
}
