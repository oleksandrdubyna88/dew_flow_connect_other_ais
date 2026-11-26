using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// The check of a codex coder turn's OWN stream (epoch 3 E3 s5 t4; ids CX-S1, CX-T4, CX-T5). Built in s5 t4; until then the stream says nothing wrong.
/// </summary>
internal static class CodexTurnCheck
{
    /// <summary>What the stream of a finished turn says about the boundary.</summary>
    public static CoderTurnVerdict Check(ProcessResult process, string expectedSession) => new CoderTurnVerdict.Fine();
}
