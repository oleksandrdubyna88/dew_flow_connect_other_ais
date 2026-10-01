namespace CoaiMcp.Server;

/// <summary>Which session a <c>resolve</c>, <c>status</c> or <c>ask_human</c> is about: the three key segments it resolves to.</summary>
/// <remarks>
/// Nested in <see cref="PanelService"/> until the <c>ask_human</c> block moved out (PLAN_question_consultant.md, S2):
/// two files resolve a caller's arguments to one of these now, and a nested record would have made the second
/// name the first in an alias — which the refusal census rightly refuses to see past.
/// </remarks>
internal sealed record SessionAddress(string Branch, string Document, string Feature);
