namespace CoaiBugs;

/// <summary>What an administrator did — the closed set of verbs <c>admin_audit.action</c> may hold.</summary>
/// <remarks>Stored lowercase, by <see cref="AuditActions.Word"/>; a test pins that nothing else appears.</remarks>
public enum AuditAction
{
    /// <summary>A key was minted.</summary>
    Issue,

    /// <summary>A key was ended.</summary>
    Revoke,
}

/// <summary>The one spelling of each action, so the column holds words and never enum numbers.</summary>
public static class AuditActions
{
    /// <summary>The word written.</summary>
    public static string Word(this AuditAction action) => action switch
    {
        AuditAction.Issue => "issue",
        AuditAction.Revoke => "revoke",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "is not an audited action"),
    };

    /// <summary>The word read back. A word nothing here writes is corruption, and is said so.</summary>
    internal static AuditAction Parse(string word) => word switch
    {
        "issue" => AuditAction.Issue,
        "revoke" => AuditAction.Revoke,
        _ => throw new InvalidOperationException($"'{word}' is not an action this server audits"),
    };
}
