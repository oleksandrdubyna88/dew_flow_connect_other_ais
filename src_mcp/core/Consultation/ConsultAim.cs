namespace CoaiMcp.Core.Consultation;

/// <summary>What a consultation is FOR. v2 has one kind — <c>stuck</c>: the caller recognised it was stuck and asked.</summary>
/// <remarks>
/// The cadence and risk kinds the gate ordered are gone with the cadence. A record written by an older build may still carry
/// them in its <c>kind</c> member; it is read as it was written and prompted as stuck.
/// </remarks>
public static class ConsultKinds
{
    public const string Stuck = "stuck";

    /// <summary>The consultant's prompt for a kind — every kind, known or not, is prompted as stuck.</summary>
    public static string PromptId(string kind) => "consult";
}
