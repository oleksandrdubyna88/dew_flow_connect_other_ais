namespace CoaiMcp.Runners.Files;

/// <summary>
/// The folder a link pointed at lost something while the link was being removed — so the deletion of the tree that held
/// the link STOPS, and the message names what is missing (E4.1 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>).
/// </summary>
/// <remarks>An <see cref="IOException"/> on purpose: every caller of the shared deleter already catches one, so the sentence
/// reaches its log without a new call — and the callers that used to swallow it say it now.</remarks>
public sealed class LinkTargetLostException(string message) : IOException(message);
