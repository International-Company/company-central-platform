namespace CCP.Modules.Authorization.Domain.Scopes;

/// <summary>What kind of caller holds a set of permissions.</summary>
public enum PermissionSubjectKind
{
    /// <summary>A person, signed in.</summary>
    User = 1,

    /// <summary>A registered application, authenticated by client credentials.</summary>
    Application = 2
}

/// <summary>
/// Whose permissions these are.
/// <para>
/// <b>The kind is part of the identity, not decoration.</b> A user id and an
/// application id are both GUIDs from different tables, and a cache keyed on the
/// value alone would be one collision — or one copy-paste — away from answering
/// a question about a machine with the answer for a person.
/// </para>
/// </summary>
/// <param name="Kind">Person or machine.</param>
/// <param name="Id">The identifier within that kind.</param>
public readonly record struct PermissionSubject(PermissionSubjectKind Kind, Guid Id)
{
    public static PermissionSubject ForUser(Guid userId) =>
        new(PermissionSubjectKind.User, userId);

    public static PermissionSubject ForApplication(Guid applicationId) =>
        new(PermissionSubjectKind.Application, applicationId);

    /// <summary>Whether holder-relative scopes can mean anything for this subject.</summary>
    public bool HasPlaceInOrganization => Kind == PermissionSubjectKind.User;

    public override string ToString() =>
        $"{(Kind == PermissionSubjectKind.User ? "user" : "app")}:{Id:N}";
}
