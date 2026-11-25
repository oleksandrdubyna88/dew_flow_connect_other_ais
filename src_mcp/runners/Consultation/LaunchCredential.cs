namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// The one credential a launch carries, as a type that cannot print itself (epoch 3 E2 s7 t5, KEY_POOL_SPEC.md section 6): the id of the key and its text, where the text leaves in ONE way only — <see cref="Into"/>, which puts it in an
/// environment dictionary under the variable the CLI reads. There is no member that returns the text.
/// </summary>
/// <remarks>
/// A record prints its members, so this is a class: <see cref="ToString"/> is <c>credential &lt;id&gt;</c>, and a <see cref="ConsultantLaunch"/> that is logged, asserted over or serialised by mistake shows the id and nothing else.
/// </remarks>
public sealed class LaunchCredential(string id, string text)
{
    /// <summary>The key's id: not a secret.</summary>
    public string Id { get; } = id;

    /// <summary>Puts the text into <paramref name="environment"/> under <paramref name="variable"/>. The only way the text leaves this object.</summary>
    public void Into(IDictionary<string, string?> environment, string variable) => environment[variable] = text;

    public override string ToString() => $"credential {Id}";
}
