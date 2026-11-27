using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Two real runs of the agy coder's boundary with NO model (0 paid calls), run only when asked: <c>COAI_REAL_AGY=1</c> and <c>COAI_REAL_AGY_RESULTS</c> naming the file to write. A skip is not a pass.
/// (a) One real <c>agy models</c> on a stale copy that coai's own <see cref="SignInRefresh"/> made, in the refresh unit, on a temp job folder. (b) The product's own road before a launch — the sign-in copy, the guard folder, the preflight — in the real unit, with the real
/// <c>agy</c> as the program, and no launch of it. The real sign-in file is only read (a byte copy); its sha256 before and after is compared and only "equal" or "changed" is written. No token, environment or file content is ever written.
/// </summary>
[Collection("fakecli-env")]
public sealed partial class TheRealAgyRunsTests
{
    [GeneratedRegex("[A-Za-z0-9_\\-\\.]{40,}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LongToken();

    private static string Redact(string text) => LongToken().Replace(text, "<redacted>");

    private static string HashOf(string file) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));

    /// <summary>Records every process the road starts: the program, its argv (secrets redacted), the exit code and the time.</summary>
    private sealed class Recording(IProcessLauncher inner, List<string> log) : IProcessLauncher
    {
        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            var watch = Stopwatch.StartNew();
            var result = await inner.RunAsync(request, ct);
            var argv = Redact(string.Join(' ', request.Arguments.Select(argument => argument.Length > 300 ? argument[..300] + "…" : argument)));
            log.Add($"- `{request.Executable}` ({watch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s, exit {result.ExitCode.ToString(CultureInfo.InvariantCulture)}{(result.TimedOut ? ", timed out" : string.Empty)}): {argv}");

            return result;
        }
    }

    [Fact]
    public async Task TheRealAgy_RefreshesAStaleCopyCoaiMade_AndTheProductsPreflightHoldsInTheRealUnit()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("COAI_REAL_AGY") == "1", "the real agy runs only with COAI_REAL_AGY=1 — this is a skip, not a pass");
        var resultsFile = Environment.GetEnvironmentVariable("COAI_REAL_AGY_RESULTS");
        Assert.SkipWhen(string.IsNullOrEmpty(resultsFile), "COAI_REAL_AGY_RESULTS names the file the results are written to");
        var realHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var agy = Environment.GetEnvironmentVariable("COAI_REAL_AGY_CLI") is { Length: > 0 } named ? named : Path.Combine(realHome, ".local", "bin", "agy");
        Assert.SkipUnless(File.Exists(agy), $"needs the real agy at '{agy}'");
        var realFile = Path.Combine(realHome, AgyCoderUnit.SignInRelative);
        Assert.SkipUnless(File.Exists(realFile), "needs a real agy sign-in file");

        var before = HashOf(realFile);
        var report = new StringBuilder();
        var log = new List<string>();
        await using (var bed = await CoderBed.CreateAsync())
        {
            var host = bed.Host with { RealHome = realHome };
            var launcher = new Recording(bed.Launcher, log);
            var vendor = AgyCoderBed.Vendor();
            var boundary = new CoderBoundary(launcher, host) { Vendor = vendor };
            var job = bed.Job;
            var recipe = (CoderSignInRecipe.CopyForJob)vendor.SignIn;
            var relative = recipe.RealRelativePath.Length > 0 ? recipe.RealRelativePath : recipe.RelativePath;
            var program = vendor.Program(agy, host.Path, job);
            report.AppendLine($"program problem: {(program.Problem.Length == 0 ? "none" : Redact(program.Problem))}");
            var realExpiry = SignInCopy.ExpiryOfFile(recipe, realFile);
            report.AppendLine($"real file: expiry readable = {(realExpiry is not null).ToString().ToLowerInvariant()}; minutes left = {(realExpiry is null ? "n/a" : Math.Round((realExpiry.Value - DateTime.UtcNow).TotalMinutes).ToString(CultureInfo.InvariantCulture))}");

            // (a) One refresh through the product's own SignInRefresh, forced: the copy is made stale by coai, `agy models` runs in the refresh unit on the scratch home.
            var scratch = job.RefreshFolder;
            var refresh = await new SignInRefresh(launcher, host).RunAsync(job, realFile, scratch, relative, program, recipe.RefreshArgv,
                [new("PATH", BoundaryHost.SystemPath), new("HOME", scratch), new("LANG", "C.UTF-8"), new("TERM", "dumb"), new("NO_COLOR", "1")],
                BoundaryUnit.UnitName(job, "refresh-real"), degrade: null, TestContext.Current.CancellationToken, recipe.ExpiryIsJwt ? string.Empty : recipe.ExpiryField);
            switch (refresh)
            {
                case SignInRefreshResult.Refreshed refreshed:
                    var after = SignInCopy.ExpiryOfFile(recipe, refreshed.File);
                    report.AppendLine($"(a) refresh unit: finished; the scratch copy's expiry is {(after is null ? "unreadable" : after.Value > DateTime.UtcNow ? $"in the FUTURE, {Math.Round((after.Value - DateTime.UtcNow).TotalMinutes).ToString(CultureInfo.InvariantCulture)} minutes (agy refreshed the stale copy)" : "still in the PAST (agy did not refresh it)")}");
                    SignInRefresh.Delete(scratch);
                    break;
                case SignInRefreshResult.Failed failed:
                    report.AppendLine($"(a) refresh unit: FAILED — {Redact(failed.Sentence)}");
                    break;
            }

            report.AppendLine($"(a) the refresh folder is gone afterwards: {(!Directory.Exists(scratch)).ToString().ToLowerInvariant()}");
            report.AppendLine($"real sign-in file after (a): {(HashOf(realFile) == before ? "equal" : "changed")}");

            // (b) The product's own road before a launch: the sign-in copy (a refresh only when the margin is missing), the guard folder, the preflight in the real unit, with the real agy as the program. agy itself is not launched.
            var states = new SignInJobs();
            var runtimeMax = TimeSpan.FromMinutes(45) + UnitControl.Backstop;
            var unsigned = await boundary.PrepareSignInAsync(job, agy, runtimeMax, states, DateTime.UtcNow, TestContext.Current.CancellationToken);
            report.AppendLine($"(b) sign-in copy for the job: {(unsigned.Length == 0 ? "made and gated" : "REFUSED — " + Redact(unsigned))}");
            report.AppendLine($"(b) refreshes counted by the road: {states.Of(vendor.Id, () => throw new InvalidOperationException("no state")).Refreshes.ToString(CultureInfo.InvariantCulture)}");
            var request = new ProcessRequest(agy, vendor.FreshArgv("gemini-4-argon-medium", job.Tree.Root), vendor.WorkingDirectory(job)) { Timeout = TimeSpan.FromMinutes(45), InheritsEnvironment = false };
            var prepared = boundary.PrepareLaunch(job, request);
            report.AppendLine($"(b) guard folder: {(prepared.Problem.Length == 0 ? "written and read back" : "REFUSED — " + Redact(prepared.Problem))}");
            var admission = await boundary.AdmitAsync(job, agy, TestContext.Current.CancellationToken, withKey: false, runtimeMax);
            var rows = admission switch
            {
                BoundaryAdmission.Admitted admitted => admitted.Records.Values.SelectMany(record => record.Preflight).ToList(),
                BoundaryAdmission.Refused refused => [.. refused.Record.Preflight],
                _ => [],
            };
            report.AppendLine($"(b) admission: {(admission is BoundaryAdmission.Admitted ? "ADMITTED" : "REFUSED — " + Redact(((BoundaryAdmission.Refused)admission).Sentence))}");
            report.AppendLine($"(b) rows: {rows.Count(row => row.Held).ToString(CultureInfo.InvariantCulture)} held, {rows.Count(row => !row.Held).ToString(CultureInfo.InvariantCulture)} not held, of {rows.Count.ToString(CultureInfo.InvariantCulture)}");
            foreach (var row in rows)
            {
                report.AppendLine($"  - {(row.Held ? "ok " : "BAD")} {row.Item}: {Redact(row.Detail)}");
            }

            report.AppendLine($"real sign-in file after (b): {(HashOf(realFile) == before ? "equal" : "changed")}");
            report.AppendLine($"main checkout stand-in untouched by the preflight marker: {(!File.Exists(Path.Combine(job.Tree.MainCheckout, ".coai-preflight-cover"))).ToString().ToLowerInvariant()}");
        }

        var final = $"{report}\nprocesses the road started:\n{string.Join('\n', log)}\n";
        await File.WriteAllTextAsync(resultsFile!, final, TestContext.Current.CancellationToken);
        HashOf(realFile).Should().Be(before, "the real sign-in file was only read");
    }
}
