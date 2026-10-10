namespace CoaiServer;

/// <summary>
/// A refusal in the shape every other answer here has: <c>{ error }</c>, as JSON, with the status the
/// handler chose.
/// </summary>
/// <remarks>
/// <c>Results.BadRequest(string)</c> and <c>Results.Forbid()</c> write text or nothing, and a client
/// deserialising this API's <see cref="ErrorDto"/> meets a parse error where the sentence should be
/// (gemini, the sessions' code round). The shape was built in-line at each endpoint until
/// <c>/api/people</c> would have been the third copy; it is one function now so the next endpoint
/// cannot get it slightly different.
/// </remarks>
public static class Refusal
{
    public static IResult Json(string because, int status) =>
        Results.Json(new ErrorDto(because), ServerJsonContext.Default.ErrorDto, statusCode: status);
}
