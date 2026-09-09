using CCP.Kernel.Results;
using CCP.Modules.Authorization.Domain.Applications;
using CCP.Modules.Authorization.Domain.Scopes;
using CCP.Modules.Authorization.Infrastructure.Security;

namespace CCP.Modules.Authorization.UnitTests.Domain;

/// <summary>
/// Client credentials, and the rules that make rotating one possible.
/// </summary>
public sealed class ApplicationCredentialTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ApplicationId = Guid.CreateVersion7();

    private static ApplicationCredential ACredential(
        DateTimeOffset? expiresAt = null, string label = "production")
        => ApplicationCredential.Issue(
            ApplicationId, "ccp_abc", "hash", label, Now, expiresAt).Value;

    [Fact]
    public void ALiveCredentialIsOneThatIsNeitherRevokedNorExpired()
    {
        ApplicationCredential credential = ACredential();

        Assert.True(credential.IsLive(Now));
        Assert.True(credential.IsLive(Now.AddYears(5)));
    }

    [Fact]
    public void AnExpiredCredentialStopsOnItsOwn()
    {
        ApplicationCredential credential = ACredential(expiresAt: Now.AddDays(30));

        Assert.True(credential.IsLive(Now.AddDays(29)));
        Assert.False(credential.IsLive(Now.AddDays(31)));
    }

    [Fact]
    public void RevocationTakesEffectImmediately()
    {
        ApplicationCredential credential = ACredential();

        Result revoked = credential.Revoke(Guid.CreateVersion7(), Now);

        Assert.True(revoked.IsSuccess);

        // No grace period, no next-refresh, no cache. "We think this key leaked"
        // must not be followed by an interval in which it still works.
        Assert.False(credential.IsLive(Now));
    }

    [Fact]
    public void RevokingTwiceIsRefused()
    {
        ApplicationCredential credential = ACredential();
        credential.Revoke(Guid.CreateVersion7(), Now);

        Assert.Equal(
            "AUTHZ.CREDENTIAL_ALREADY_REVOKED",
            credential.Revoke(Guid.CreateVersion7(), Now.AddMinutes(1)).Error.Code);
    }

    [Fact]
    public void ACredentialNeedsALabel()
    {
        Assert.True(ApplicationCredential.Issue(
            ApplicationId, "ccp_abc", "hash", "   ", Now, null).IsFailure);
    }

    [Fact]
    public void AnExpiryInThePastIsRefused()
    {
        Assert.True(ApplicationCredential.Issue(
            ApplicationId, "ccp_abc", "hash", "production", Now, Now.AddMinutes(-1)).IsFailure);
    }

    [Fact]
    public void UseIsRecorded()
    {
        ApplicationCredential credential = ACredential();

        Assert.Null(credential.LastUsedAt);

        credential.RecordUse(Now);

        // The field that makes finishing a rotation possible. Without it,
        // "can we revoke the old key?" is a guess and nobody is willing to be
        // the one who breaks production.
        Assert.Equal(Now, credential.LastUsedAt);
    }
}

/// <summary>
/// Generating and checking client secrets.
/// </summary>
public sealed class ApplicationSecretHasherTests
{
    private readonly ApplicationSecretHasher _hasher = new();

    [Fact]
    public void EveryGeneratedPairIsDifferent()
    {
        (string firstId, string firstSecret, _) = _hasher.Generate();
        (string secondId, string secondSecret, _) = _hasher.Generate();

        Assert.NotEqual(firstId, secondId);
        Assert.NotEqual(firstSecret, secondSecret);
    }

    [Fact]
    public void SecretsCarryARecognisablePrefix()
    {
        (string clientId, string secret, _) = _hasher.Generate();

        // Secret scanners look for recognisable prefixes. A secret that looks
        // like anonymous base64 is a secret nobody notices in a commit.
        Assert.StartsWith("ccp_", clientId, StringComparison.Ordinal);
        Assert.StartsWith("ccps_", secret, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStoredHashIsNotTheSecret()
    {
        (_, string secret, string hash) = _hasher.Generate();

        Assert.DoesNotContain(secret, hash, StringComparison.Ordinal);
        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public void TheRightSecretMatchesAndTheWrongOneDoesNot()
    {
        (_, string secret, string hash) = _hasher.Generate();

        Assert.True(_hasher.Matches(secret, hash));
        Assert.False(_hasher.Matches(secret + "x", hash));
        Assert.False(_hasher.Matches("ccps_something-else", hash));
    }

    [Fact]
    public void ComparingAgainstNothingFailsRatherThanThrows()
    {
        // The token handler compares even when no credential was found, so that
        // a real client id is not measurably slower to reject than an invented
        // one. That path passes an empty hash through here.
        Assert.False(_hasher.Matches("ccps_anything", string.Empty));
        Assert.False(_hasher.Matches(string.Empty, "abc"));
    }
}

/// <summary>
/// Granting a role to a machine.
/// </summary>
public sealed class ApplicationRoleAssignmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ApplicationId = Guid.CreateVersion7();
    private static readonly Guid RoleId = Guid.CreateVersion7();
    private static readonly Guid Granter = Guid.CreateVersion7();

    [Fact]
    public void ACompanyWideGrantIsAllowed()
    {
        Result<ApplicationRoleAssignment> granted = ApplicationRoleAssignment.Grant(
            ApplicationId, RoleId, new GrantedScope(ScopeType.All, null), Granter, Now);

        Assert.True(granted.IsSuccess);
        Assert.True(granted.Value.IsEffective(Now));
    }

    [Fact]
    public void ASelfScopedGrantIsRefused()
    {
        // Self means "the holder's own records", and an application has no place
        // in the organization for that to follow. A grant that silently resolved
        // to nothing would look like a working configuration.
        Result<ApplicationRoleAssignment> granted = ApplicationRoleAssignment.Grant(
            ApplicationId, RoleId, new GrantedScope(ScopeType.Self, null), Granter, Now);

        Assert.True(granted.IsFailure);
        Assert.Equal("AUTHZ.SELF_SCOPE_FOR_APPLICATION", granted.Error.Code);
    }

    [Fact]
    public void AUnitScopedGrantNeedsAUnit()
    {
        Result<ApplicationRoleAssignment> granted = ApplicationRoleAssignment.Grant(
            ApplicationId, RoleId, new GrantedScope(ScopeType.UnitAndBelow, null), Granter, Now);

        Assert.True(granted.IsFailure);
        Assert.Equal("AUTHZ.SCOPE_UNIT_REQUIRED", granted.Error.Code);
    }

    [Fact]
    public void AnExpiredGrantSimplyStopsCounting()
    {
        ApplicationRoleAssignment granted = ApplicationRoleAssignment.Grant(
            ApplicationId, RoleId, new GrantedScope(ScopeType.All, null),
            Granter, Now, Now.AddDays(7)).Value;

        Assert.True(granted.IsEffective(Now.AddDays(6)));
        Assert.False(granted.IsEffective(Now.AddDays(8)));
    }

    [Fact]
    public void RevokingStopsIt()
    {
        ApplicationRoleAssignment granted = ApplicationRoleAssignment.Grant(
            ApplicationId, RoleId, new GrantedScope(ScopeType.All, null), Granter, Now).Value;

        granted.Revoke(Granter, Now);

        Assert.False(granted.IsEffective(Now));
        Assert.True(granted.IsRevoked);
    }
}
