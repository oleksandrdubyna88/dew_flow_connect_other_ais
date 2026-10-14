namespace CoaiMcp.Runners.Processes;

/// <summary>
/// The reason inside a <see cref="ProcessTreeNotRecordedException"/> when no tracker write failed: the child the launcher
/// was ending exited BY ITSELF, and something it never listed was still holding the pipes it inherited.
/// </summary>
/// <remarks>
/// Its own type because the caller that maps the failure (the Team server's gauge capture) logs only the inner exception's
/// type name — a message could carry a path or an account, a type name cannot — and "the identity on disk is partial because
/// the disk refused" and "because a process nobody listed outlived the root" are two different sentences for a person.
/// </remarks>
public sealed class ProcessTreeUnlistedException(string message) : Exception(message);
