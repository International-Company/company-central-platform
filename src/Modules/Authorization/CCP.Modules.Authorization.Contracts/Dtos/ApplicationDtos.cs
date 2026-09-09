using System.Text.Json.Serialization;

namespace CCP.Modules.Authorization.Contracts.Dtos;

/// <summary>A registered application, as an administrator sees it.</summary>
public sealed record RegisteredApplicationDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsSystem,
    bool IsActive,
    int LiveCredentials,
    int DeclaredPermissions,
    DateTimeOffset CreatedAt);

/// <summary>
/// A credential, without its secret.
/// <para>
/// There is no shape in this Platform that carries a client secret back to
/// anybody after issuance, and that is deliberate: a secret that can be
/// retrieved is a secret that gets retrieved by whoever compromises the console.
/// </para>
/// </summary>
public sealed record ApplicationCredentialDto(
    Guid Id,
    string ClientId,
    string Label,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt,
    bool IsLive,
    DateTimeOffset CreatedAt);

/// <summary>
/// A newly issued credential. The one and only time the secret exists outside
/// the caller's own storage.
/// </summary>
/// <param name="Secret">
/// Shown once. Not stored in plaintext anywhere, not recoverable, and not
/// re-sendable — losing it means issuing another and revoking this one.
/// </param>
public sealed record IssuedCredentialDto(
    Guid Id,
    string ClientId,
    string Secret,
    string Label,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt);

/// <summary>A role an application holds.</summary>
public sealed record ApplicationRoleDto(
    Guid AssignmentId,
    Guid RoleId,
    string RoleCode,
    string RoleNameAr,
    string RoleNameEn,
    string Scope,
    Guid? ScopeUnitId,
    DateTimeOffset GrantedAt,
    DateTimeOffset? ExpiresAt,
    bool IsRevoked);

/// <summary>
/// The token response, in the shape RFC 6749 §5.1 specifies.
/// <para>
/// <b>Snake case on the wire, and deliberately.</b> Every OAuth client library
/// in every language already parses <c>access_token</c> and <c>expires_in</c>;
/// a Platform that answered <c>accessToken</c> because that is the house style
/// would make itself a special case in each of them, for nothing.
/// </para>
/// <para>
/// The names are set by attribute rather than by writing snake case in C#, so
/// the wire format is conventional and the code still reads like the rest of
/// the Platform.
/// </para>
/// </summary>
public sealed record MachineTokenDto(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresInSeconds,
    [property: JsonPropertyName("scope")] string? Scope = null);
