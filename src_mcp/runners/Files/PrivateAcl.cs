namespace CoaiMcp.Runners.Files;

/// <summary>
/// A security identifier as its text (<c>S-1-5-18</c>) — a value of its own so a SID is never a bare string, and so the decisions
/// below need no <c>SecurityIdentifier</c>: that type is unusable off Windows (its constructor throws
/// <see cref="PlatformNotSupportedException"/> on .NET 10 Linux — measured), and the decisions must run in the Linux suite.
/// </summary>
internal readonly record struct Sid(string Value)
{
    /// <summary>No SID: a descriptor that names no owner.</summary>
    public static Sid None { get; } = new(string.Empty);

    /// <summary>The system account (<c>NT AUTHORITY\SYSTEM</c>) — a well-known SID, never a name.</summary>
    public static Sid System { get; } = new("S-1-5-18");

    /// <summary>The built-in Administrators group (<c>BUILTIN\Administrators</c>).</summary>
    public static Sid Administrators { get; } = new("S-1-5-32-544");
}

/// <summary>The inheritance bits of an access-control entry — the numeric values of <c>AceFlags</c>, minus its inherited bit.</summary>
[Flags]
internal enum AceInheritance
{
    None = 0,
    ObjectInherit = 1,
    ContainerInherit = 2,
    NoPropagateInherit = 4,
    InheritOnly = 8,
}

/// <summary>One Allow or Deny entry of a DACL, as the decisions read it — a plain value, so a verdict is a function of data.</summary>
/// <param name="Sid">Whom the entry names.</param>
/// <param name="Allow">An Allow entry (a Deny entry is <c>false</c>).</param>
/// <param name="Inherited">The entry came down from the parent rather than being set on the object.</param>
/// <param name="Mask">The access mask.</param>
/// <param name="Inheritance">What the entry passes on to children.</param>
internal sealed record AceFact(Sid Sid, bool Allow, bool Inherited, int Mask, AceInheritance Inheritance);

/// <summary>An object's owner and DACL, as the decisions read them.</summary>
/// <param name="Owner">The owner; <see cref="Sid.None"/> when the descriptor names none.</param>
/// <param name="Protected">Inheritance from the parent is cut.</param>
/// <param name="DaclPresent">The descriptor has a DACL; a NULL one grants everybody everything.</param>
/// <param name="Aces">The Allow and Deny entries, in order.</param>
internal sealed record AclFacts(Sid Owner, bool Protected, bool DaclPresent, IReadOnlyList<AceFact> Aces);

/// <summary>
/// The Windows half of <see cref="PrivateFolder"/> as pure functions of <see cref="AclFacts"/> (D7 of
/// <c>todo/PLAN_vibe_live_round_fixes.md</c>): what the DACL must be, whether a given one is that, who could use a folder that is
/// not private, and every sentence a refusal says. Nothing here touches a handle, a file or the platform — the native shell
/// (<c>PrivateFolderWindowsNative</c>) only turns the system's descriptors into facts and back — so each rule runs, and is tested,
/// on any OS.
/// </summary>
internal static class PrivateAcl
{
    /// <summary>Win32 ERROR_SHARING_VIOLATION.</summary>
    internal const int SharingViolation = 32;

    /// <summary>FILE_ALL_ACCESS — what <c>FileSystemRights.FullControl</c> is as a mask.</summary>
    internal const int FullControl = 0x1F01FF;

    /// <summary>What every entry of a private folder passes on: to its subfolders and to its files, with no limit.</summary>
    internal const AceInheritance Inheriting = AceInheritance.ContainerInherit | AceInheritance.ObjectInherit;

    /// <summary>FILE_WRITE_DATA, FILE_APPEND_DATA, DELETE and FILE_WRITE_ATTRIBUTES: the rights that make an entry "modify" rather than "read".</summary>
    private const int ModifyRights = 0x2 | 0x4 | 0x10000 | 0x100;

    /// <summary>The three SIDs a private folder names — the person, SYSTEM, Administrators — as sorted values, for comparing sets.</summary>
    private static string[] Three(Sid me) => [.. new[] { me, Sid.System, Sid.Administrators }.Select(sid => sid.Value).Order(StringComparer.Ordinal)];

    /// <summary>
    /// What a private folder is made with: a protected DACL of exactly the three Allow entries, full control, container- and
    /// object-inheriting. No owner is set (the creating token's default stands).
    /// </summary>
    public static AclFacts Wanted(Sid me) =>
        new(Sid.None, true, true, [.. new[] { me, Sid.System, Sid.Administrators }.Select(sid => new AceFact(sid, true, false, FullControl, Inheriting))]);

    /// <summary>The owner may be the person or Administrators — an elevated session creates folders owned by the group.</summary>
    public static bool OwnerIsOurs(AclFacts facts, Sid me) => facts.Owner != Sid.None && (facts.Owner == me || facts.Owner == Sid.Administrators);

    /// <summary>The DACL is exactly the three entries and nothing else: protected, set here (not inherited), full-control Allow, inheriting.</summary>
    public static bool IsExactlyOurs(AclFacts facts, Sid me) =>
        facts.Protected
        && facts.Aces.Count == 3
        && facts.Aces.All(IsFullControlAllow)
        && facts.Aces.Select(ace => ace.Sid.Value).Order(StringComparer.Ordinal).SequenceEqual(Three(me));

    private static bool IsFullControlAllow(AceFact ace) =>
        ace.Allow && !ace.Inherited && ace.Mask == FullControl && ace.Inheritance == Inheriting;

    /// <summary>A descendant after the reset: not protected, no explicit entry, exactly the three SIDs — all inherited.</summary>
    public static bool Inherited(AclFacts facts, Sid me) =>
        !facts.Protected
        && facts.Aces.All(ace => ace.Inherited)
        && facts.Aces.Select(ace => ace.Sid.Value).Distinct().Order(StringComparer.Ordinal).SequenceEqual(Three(me));

    /// <summary>A descendant that holds an explicit entry, or is protected, must be reset to inherit (propagation replaces only inherited entries).</summary>
    public static bool NeedsReset(AclFacts facts) => facts.Protected || facts.Aces.Any(ace => !ace.Inherited);

    /// <summary>
    /// Why a descendant's OWNER refuses the folder — or empty: an owner other than the person or Administrators holds an implicit
    /// WRITE_DAC, so whoever planted the child could widen its DACL again right after the walk reset it. The sentence names the child.
    /// </summary>
    public static string OwnerProblem(string entry, AclFacts facts, Sid me, Func<Sid, string> name) =>
        OwnerIsOurs(facts, me)
            ? string.Empty
            : $"{entry} inside the re-ACLed folder is owned by {name(facts.Owner)}, and an owner keeps the right to change its DACL (WRITE_DAC), "
              + $"so coai cannot make it private — delete it, or choose another Vibe homes folder in {PrivateFolder.Setting}";

    /// <summary>Who — beyond the three — could do what: <c>Everyone could read it</c>; a sentence of its own when no foreign Allow entry exists.</summary>
    public static string Exposure(AclFacts facts, Sid me, Func<Sid, string> name)
    {
        if (!facts.DaclPresent)
        {
            return "it has no access list, so every user could modify it";
        }

        var mine = Three(me);
        var others = facts.Aces.Where(ace => ace.Allow && !mine.Contains(ace.Sid.Value)).ToList();

        return others.Count == 0
            ? "nobody else is listed as able to use it, but its protection or entries were not exactly the person's"
            : string.Join("; ", others.GroupBy(ace => ace.Sid.Value).Select(group => $"{name(group.First().Sid)} could {Rights(group)} it"));
    }

    private static string Rights(IEnumerable<AceFact> aces) => aces.Any(ace => (ace.Mask & ModifyRights) != 0) ? "modify" : "read";

    // -------------------------------------------------------------------------------------------------------------
    // The sentences a refusal says
    // -------------------------------------------------------------------------------------------------------------

    /// <summary>The folder's final path is not the one asked for — a link, or a swap.</summary>
    public static string NotTheFolder(string path) =>
        $"{path} is not the folder it names — it is a link, or it was swapped — and coai does not use or make folders through one; "
        + $"delete it, or choose another Vibe homes folder in {PrivateFolder.Setting}";

    /// <summary>The folder is owned by someone else: <c>owner</c> is named, and so is who could use it.</summary>
    public static string ForeignOwner(string path, string owner, string exposure) =>
        $"{path} is not private to you (owner {owner}; {exposure}) — "
        + "coai keeps Vibe's homes, their transcripts and its copies of your tree only in a folder only you can open; "
        + $"delete it, or choose another Vibe homes folder in {PrivateFolder.Setting}";

    /// <summary>The folder is the person's, but re-ACLing it failed or did not take.</summary>
    public static string NotMadePrivate(string path, string exposure) =>
        $"{path} is not private to you and coai could not make it so — {exposure}; "
        + $"delete it, or choose another Vibe homes folder in {PrivateFolder.Setting}";

    /// <summary>The one line a successful re-ACL says.</summary>
    public static string MadePrivate(string path, string exposure) => $"made {path} private; {exposure} until now";

    /// <summary>A descendant that could not be made private.</summary>
    public static string ChildCouldNotBeMade(string entry, string why) =>
        $"{entry} inside the re-ACLed folder could not be made private ({why}) — delete it, or choose another Vibe homes folder in {PrivateFolder.Setting}";

    /// <summary>Why the folder's handle could not be opened: a link whose target would not open, a sharing violation, or anything else.</summary>
    public static string NotOpened(string path, int error, bool isLink)
    {
        if (isLink)
        {
            return $"{path} is a link, and its target could not be opened (error {error}) — Vibe's homes are never made through one; delete it, or choose another Vibe homes folder in {PrivateFolder.Setting}";
        }

        return error == SharingViolation
            ? $"{path} is open elsewhere in a way that lets it be renamed or deleted under coai, so it cannot be held — close what holds it, or choose another Vibe homes folder in {PrivateFolder.Setting}"
            : $"{path} could not be opened to check that it is private (error {error}) — choose another Vibe homes folder in {PrivateFolder.Setting}";
    }
}
