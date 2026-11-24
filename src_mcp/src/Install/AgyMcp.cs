namespace CoaiMcp.Install;

/// <summary>
/// agy's MCP file. It answers <c>unmeasured</c> and never writes: agy's own <c>agy mcp add</c> was measured on Linux only (agy 1.5.14, <c>shared/install-clients.json</c>) and the
/// spec's measurement is not complete until Windows is measured too — a path nobody observed is not one to write from.
/// </summary>
internal sealed class AgyMcp : McpTarget
{
    internal const string Unmeasured = "unmeasured: agy's MCP file is measured on Linux only (agy 1.5.14); Windows is not measured, so nothing is written";

    public override Client Cli => Client.Agy;

    public override string FileOf(InstallHost host) => host.AgyJson;

    protected override bool RemoveBeforeAdd => false;

    public override ConfigRead Read(string? text) => ConfigRead.None;

    protected override Task<VendorAnswer> AddAsync(McpEntry entry, ApplyContext context, CancellationToken ct) =>
        Task.FromResult(new VendorAnswer(false, Unmeasured));

    protected override Task<VendorAnswer> RemoveAsync(ApplyContext context, CancellationToken ct) =>
        Task.FromResult(new VendorAnswer(false, Unmeasured));
}
