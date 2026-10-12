using ModelContextProtocol;

namespace CoaiMcp.Server;

/// <summary>
/// The service source of a server that may not serve: every tool call is refused with <paramref name="reason"/>
/// (research/PLAN_names_and_a_missing_root.md, story 3.2 — the NAMED data folder is confirmed absent).
/// </summary>
/// <remarks>
/// <para>An <see cref="McpException"/> and not any other exception, because the SDK keeps ITS message in the error tool
/// result a client shows; an ordinary exception is reported as generic text, and the person would never learn which
/// folder to restore.</para>
/// <para>It builds nothing: no host, no sweep, no survey, nothing that would write under the folder a person has to
/// restore. <see cref="Ready"/> is complete, so nothing waiting on a first service is left waiting.</para>
/// </remarks>
public sealed class RefusingServiceSource(string reason) : IPanelServiceSource
{
    public ValueTask<PanelService> CurrentAsync(CancellationToken ct = default) => throw new McpException(reason);

    public Task Ready => Task.CompletedTask;
}
