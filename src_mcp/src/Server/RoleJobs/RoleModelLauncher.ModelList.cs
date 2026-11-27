using CoaiMcp.Core.Roles;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>The read of the live list of a vendor's models (epoch 3 E3 s2 t5): its own file, so the road of a launch (<c>RoleModelLauncher.cs</c>) stays as vendor-neutral as it was.</summary>
internal sealed partial class RoleModelLauncher
{
    /// <summary>
    /// Reads the live list of models of the installed codex CLI (<c>codex debug models</c>: a network call, no model call; epoch 3 E3 s2 t5, SPEC_CHANGES Q1 item 2) with the SAME executable, home and environment as the launch that follows: the invocation is the one <see cref="LaunchAsync"/> would build for
    /// <paramref name="model"/>, with its arguments replaced. A coder's list is read in a unit with the property list of the launch and the job's own <c>CODEX_HOME</c> (<see cref="CoderOsLayer.ModelFreeAsync"/>), never with the real home. It has a deadline and a size limit
    /// (<see cref="CodexModelList"/>). The launch that comes back is read by the caller as any launch is, so a missing CLI, a lost sign-in and a spent quota keep their own words; no ledger line is made for it, because no model was called.
    /// </summary>
    public async Task<RoleLaunched> ListModelsAsync(ModelRef model, string repoPath, string outputDir, CancellationToken ct)
    {
        var (runtime, _) = RuntimeOf(model.Vendor);
        ICoderVendor? vendor = null;
        if (Coder is not null && Vendors.Resolve(model.Vendor) is CoderVendorResolution.Refused unshipped)
        {
            return new RoleLaunched(null, new ConsultFailure.LaunchRefused(unshipped.Sentence), runtime, null, string.Empty);
        }

        if (Coder is not null && Vendors.Resolve(model.Vendor) is CoderVendorResolution.Launchable launchable)
        {
            runtime = launchable.Vendor.Runtime;
            vendor = launchable.Vendor;
        }

        if (Coder is not null && Layer is null)
        {
            return new RoleLaunched(null, new ConsultFailure.LaunchRefused("a coder launch runs only inside the OS layer, and none was given; nothing was launched"), runtime, null, string.Empty);
        }

        var launcher = new ProcessLauncher(tracker);
        var launch = new ConsultantLaunch(repoPath, string.Empty, string.Empty, outputDir, new ReviewerSettings(model.Vendor)
        {
            ExecutablePath = settings(CliKey(model.Vendor)) ?? string.Empty,
            Model = model.Model,
            Timeout = CodexModelList.Deadline,
            McpServersToSwitchOff = NoMcpServers.CodexConfigured(settings),
        })
        {
            Confinement = Coder is null ? LaunchConfinement.AsShipped : Coder with { KeyId = string.Empty, Vendor = model.Vendor, WorkFolder = vendor is not null ? vendor.WorkingDirectory(Layer!.Job) : string.Empty, Resume = CoderHandle.None },
        };
        Directory.CreateDirectory(outputDir);
        var prepared = await runtime.PrepareAsync(launch, launcher, ct);
        if (prepared is ConsultantPreparation.Refused refused)
        {
            return new RoleLaunched(null, refused.Failure, runtime, null, string.Empty);
        }

        var ready = (ConsultantPreparation.Ready)prepared;

        var built = runtime.Build(ready.Launch);
        // A coder's list is read in a folder with no text of the repository (S4 item 13); a read-only role reads it where its launch runs.
        var listing = built with { Request = built.Request with { Arguments = CodexModelList.Arguments, StdIn = string.Empty, Timeout = CodexModelList.Deadline, MaxOutputChars = CodexModelList.MostChars + 1, WorkingDirectory = Coder is null ? built.Request.WorkingDirectory : CodexCoderVendor.ModelListFolder(Layer!.Job) }, OutputFile = string.Empty };
        if (Coder is null)
        {
            return new RoleLaunched(await new ReviewerExecutor(launcher).LaunchOnceAsync(listing, ct), null, runtime, listing, string.Empty) { CliVersion = ready.CliVersion };
        }

        return (await LaunchCoderAsync(runtime, listing, null, string.Empty, outputDir, vendor!, ct, modelFree: true)) with { CliVersion = ready.CliVersion };
    }
}
