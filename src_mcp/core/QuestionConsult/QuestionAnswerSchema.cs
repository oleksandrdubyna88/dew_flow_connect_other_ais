using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Findings;

namespace CoaiMcp.Core.QuestionConsult;

/// <summary>
/// The shape an <c>api</c> question row answers in: the consultant's one-string answer, plus the
/// <c>sourceRequests</c> a feature reviewer may make (PLAN_question_consultant.md, A9).
/// </summary>
/// <remarks>
/// <para>DERIVED from <see cref="ConsultAnswerSchema.Json"/> by the same step that derives the feature
/// schema from the finding schema (<see cref="FindingSchema.WithSourceRequests"/>): the answer half has one
/// copy, and a reindented base derives the same schema. The hosted shim insists on a schema
/// (<c>--ask-api</c> refuses without one), and a schema that only said "answer" would leave the model
/// no way to ask for the code the outline names.</para>
/// <para><b>Its own FILE</b>, never the stuck consultant's: <see cref="Name"/> differs from
/// <see cref="ConsultAnswerSchema.Name"/>, so provisioning one cannot overwrite the other — the local
/// consultant reads its file on every turn.</para>
/// </remarks>
public static class QuestionAnswerSchema
{
    public const string Name = "question-answer-schema.json";

    public static readonly string Json = FindingSchema.WithSourceRequests(ConsultAnswerSchema.Json);
}
