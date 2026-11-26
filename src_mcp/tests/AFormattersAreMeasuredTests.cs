using CoaiMcp.Runners.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E4 s1 t3 (M7, M8): the formatters of the two adapters, run INSIDE the check unit of E2 exactly as a coder job's steps are — the product's own <see cref="CheckBoundary"/> and <see cref="BoundaryJob"/>, no property changed. <c>dotnet format</c> on three named files of a
/// scratch clone of this repository, with only a loopback device and <c>--no-restore</c>; prettier through <c>npx --no-install</c> on three named files of a scratch project. The numbers are in <c>v2/epoch-3/e4-adapters-coder-hardening/RESULTS_s1_format.md</c>.
/// </summary>
/// <remarks>
/// Skipped unless <c>COAI_RECORD_FORMAT</c> names an ABSENT folder on a real disk with a short path. It needs the network (a cold restore of this repository, the registry) and about 10 minutes. The real home is not used (the stand-in of <see cref="MeasureBed"/>). No model is started.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AFormattersAreMeasuredTests
{
    private const string RecordVariable = "COAI_RECORD_FORMAT";

    private static readonly string[] Included =
    [
        "src_mcp/core/Toolchain/CheckResult.cs",
        "src_mcp/core/Toolchain/MsBuildLines.cs",
        "src_mcp/core/Toolchain/ToolchainTables.cs",
    ];

    private const string NotIncluded = "src_mcp/core/Toolchain/FailureDigest.cs";

    private static IReadOnlyDictionary<string, string> Prettier() => new Dictionary<string, string>
    {
        [".gitignore"] = "node_modules/\n",
        ["app/package.json"] = """{ "name": "scratch-prettier", "version": "1.0.0", "private": true, "devDependencies": { "prettier": "*" } }""",
        ["app/.prettierrc"] = """{ "singleQuote": true }""",
        ["app/a.js"] = "const  a = {x:1,\n   y:2}\nfunction  f( p ){return \"text\"+p}\n",
        ["app/b.js"] = "export default   function( ){ return   [1,2,\n3] }\n",
        ["app/c.json"] = "{\"k\":   [1,2,3],\n \"n\":{\"m\":true}}\n",
        ["app/broken.js"] = "const = ;\nfunction (\n",
        ["app/--help"] = "const  help = {x:1}\n",
        ["app/notes.xyz"] = "this   is   not   a   language   prettier   knows\n",
        ["app/--help.js"] = "const  helpjs = {x:1}\n",
        ["app/untouched.js"] = "const  untouched = {x:1}\n",
    };

    private static string Root => Environment.GetEnvironmentVariable(RecordVariable) ?? string.Empty;

    [Fact]
    public async Task ARecordedRunOfTheFormattersInsideTheUnit()
    {
        Assert.SkipWhen(Root.Length == 0, $"set {RecordVariable} to a new folder on a real disk with a short path to measure the formatters inside the check unit (needs the network, about 10 minutes)");
        Assert.SkipUnless(SystemdUser.Works(out var why), why);
        Directory.Exists(Root).Should().BeFalse($"{Root} must not exist: the run makes it");

        await DotnetFormatAsync(Path.Combine(Root, "repo"));
        await PrettierAsync(Path.Combine(Root, "scratch"));
    }

    private static async Task DotnetFormatAsync(string folder)
    {
        await using var bed = await MeasureBed.CreateAsync(folder, _ => Task.CompletedTask, withNode: false, cloneOf: RepoPaths.Root());
        var sln = "dew_flow_connect_other_ais.slnx";
        await bed.StepAsync("f00-sdk", host: false, ["dotnet", "--version"]);
        await bed.StepAsync("f00-format-version", host: false, ["dotnet", "format", "--version"]);
        await bed.StepAsync("f00-format-help", host: false, ["dotnet", "format", "--help"]);
        await bed.StepAsync("f01-restore-host", host: true, ["dotnet", "restore", sln]);

        // Four files are made badly formatted and committed by the harness (outside the unit); three of them are the ones given to the formatter.
        foreach (var file in (string[])[.. Included, NotIncluded])
        {
            var path = Path.Combine(bed.Worktree, file);
            var lines = (await File.ReadAllLinesAsync(path)).ToList();
            lines[2] += "   ";
            lines[4] = "\t" + lines[4].TrimStart();
            await File.WriteAllLinesAsync(path, lines);
        }

        await bed.GitAsync(bed.Worktree, "-c", "user.name=measure", "-c", "user.email=measure@example.invalid", "commit", "--quiet", "-am", "badly formatted");
        string[] include = ["--include", .. Included];

        await FormatFormAsync(bed, "f10-whitespace-folder", ["dotnet", "format", "whitespace", ".", "--folder", .. include]);
        await ResetAsync(bed);
        await FormatFormAsync(bed, "f11-whitespace-solution-no-restore", ["dotnet", "format", "whitespace", sln, "--no-restore", .. include]);
        await ResetAsync(bed);
        await FormatFormAsync(bed, "f12-style-solution-no-restore", ["dotnet", "format", "style", sln, "--no-restore", .. include]);
        await ResetAsync(bed);
        await FormatFormAsync(bed, "f13-style-info-solution-no-restore", ["dotnet", "format", "style", sln, "--no-restore", "--severity", "info", .. include]);
        await ResetAsync(bed);
        // The same form with each name written as ./<path>, the way the product would hand a file to a formatter (SPEC D2).
        string[] dotted = ["--include", .. Included.Select(file => "./" + file)];
        await FormatFormAsync(bed, "f15-whitespace-folder-dot-slash-names", ["dotnet", "format", "whitespace", ".", "--folder", .. dotted]);
        await ResetAsync(bed);
        await FormatFormAsync(bed, "f14-whitespace-folder-private-three-files-verify", ["dotnet", "format", "whitespace", ".", "--folder", "--verify-no-changes", .. include], second: false);
        await bed.FinishAsync();
    }

    private static async Task FormatFormAsync(MeasureBed bed, string id, IReadOnlyList<string> argv, bool second = true)
    {
        var first = await bed.StepAsync(id, host: false, argv, timeout: TimeSpan.FromMinutes(5));
        var changed = (await bed.GitAsync(bed.Worktree, "diff", "--name-only")).StdOut.Replace('\n', ' ').Trim();
        bed.Note($"{id}: files changed by the first run: [{changed}]; the included three: [{string.Join(' ', Included)}]; the fourth, not included: {NotIncluded}");
        bed.Note($"{id}: git status: {(await bed.StatusAsync(ignored: false)).Replace('\n', '|')}");
        if (second)
        {
            var after = await DiffHashAsync(bed);
            await bed.StepAsync(id + "-second", host: false, argv, timeout: TimeSpan.FromMinutes(5));
            bed.Note($"{id}: the diff against the commit after the first run {after}, after the second run {await DiffHashAsync(bed)} (equal: the second run changed nothing more)");
        }

        await bed.SaveFixtureAsync($"dotnet-format-{id}.txt", first.Text);
    }

    private static async Task<string> DiffHashAsync(MeasureBed bed) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes((await bed.GitAsync(bed.Worktree, "diff")).StdOut)))[..16];

    private static Task ResetAsync(MeasureBed bed) => bed.GitAsync(bed.Worktree, "checkout", "--quiet", "--", ".");

    private static async Task PrettierAsync(string folder)
    {
        await using var bed = await MeasureBed.CreateAsync(folder, main => MeasureBed.WriteAsync(main, Prettier()), withNode: true);
        await bed.StepAsync("p00-node", host: false, ["node", "--version"], node: true);
        await bed.StepAsync("p01-lock", host: true, ["npm", "install", "--package-lock-only", "--ignore-scripts", "--no-audit", "--no-fund"], "app", node: true);
        await bed.GitAsync(bed.Worktree, "add", "-A");
        await bed.GitAsync(bed.Worktree, "-c", "user.name=measure", "-c", "user.email=measure@example.invalid", "commit", "--quiet", "-m", "lock");
        await bed.StepAsync("p02-npm-ci", host: true, ["npm", "ci", "--ignore-scripts"], "app", node: true);
        await bed.StepAsync("p03-prettier-version", host: false, ["npx", "--no-install", "prettier", "--version"], "app", node: true);
        string[] three = ["./a.js", "./b.js", "./c.json"];
        string[] write = ["npx", "--no-install", "prettier", "--write", "--ignore-unknown"];

        var first = await bed.StepAsync("p10-write-three-files", host: false, [.. write, .. three], "app", node: true);
        await bed.SaveFixtureAsync("prettier-write-three.txt", first.Text);
        bed.Note($"p10: files changed by the first run: [{(await bed.GitAsync(bed.Worktree, "diff", "--name-only")).StdOut.Replace('\n', ' ').Trim()}] (untouched.js and --help were not given)");
        var afterFirst = await DiffHashAsync(bed);
        await bed.StepAsync("p11-write-three-files-second-run", host: false, [.. write, .. three], "app", node: true);
        bed.Note($"p11: the diff against the commit after the first run {afterFirst}, after the second run {await DiffHashAsync(bed)} (equal: the second run changed nothing more)");

        var broken = await bed.StepAsync("p20-write-a-file-with-a-syntax-error", host: false, [.. write, "./broken.js"], "app", node: true);
        await bed.SaveFixtureAsync("prettier-syntax-error.txt", broken.Text);
        var mixed = await bed.StepAsync("p21-write-three-and-a-broken-one", host: false, [.. write, .. three, "./broken.js"], "app", node: true);
        await bed.SaveFixtureAsync("prettier-write-with-a-broken-file.txt", mixed.Text);
        await bed.StepAsync("p22-write-a-file-of-unknown-language", host: false, [.. write, "./notes.xyz"], "app", node: true);
        await bed.StepAsync("p23-write-a-file-of-unknown-language-without-ignore-unknown", host: false, ["npx", "--no-install", "prettier", "--write", "./notes.xyz"], "app", node: true);

        // A file named --help: written as ./--help it is a file; written bare it is the option.
        var asFile = await bed.StepAsync("p30-write-dot-slash-dash-dash-help", host: false, [.. write, "./--help"], "app", node: true);
        await bed.SaveFixtureAsync("prettier-write-file-named-help.txt", asFile.Text);
        bed.Note($"p30: the file named --help after: {(await File.ReadAllTextAsync(Path.Combine(bed.Worktree, "app", "--help"))).Replace('\n', '|')}");
        await bed.StepAsync("p33-write-dot-slash-dash-dash-help-dot-js", host: false, [.. write, "./--help.js"], "app", node: true);
        bed.Note($"p33: the file named --help.js after: {(await File.ReadAllTextAsync(Path.Combine(bed.Worktree, "app", "--help.js"))).Replace('\n', '|')}");
        await bed.StepAsync("p34-write-bare-dash-dash-help-dot-js-for-contrast", host: false, [.. write, "--help.js"], "app", node: true);
        var asOption = await bed.StepAsync("p31-write-bare-dash-dash-help-for-contrast", host: false, [.. write, "--help"], "app", node: true);
        await bed.SaveFixtureAsync("prettier-bare-help-is-the-option.txt", asOption.Text.Length > 600 ? asOption.Text[..600] : asOption.Text);
        await bed.StepAsync("p32-write-after-double-dash-for-contrast", host: false, [.. write, "--", "--help"], "app", node: true);

        bed.Note($"git status at the end: {(await bed.StatusAsync()).Replace('\n', '|')}");
        await bed.FinishAsync();
    }
}
