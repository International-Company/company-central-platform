using CCP.Modules.Identity.Application;
using Microsoft.Extensions.Configuration;

namespace CCP.Modules.Identity.UnitTests;

/// <summary>
/// What a deployment's own settings do to the ones compiled in.
/// <para>
/// <b>This took the Platform down.</b> A deployment set
/// <c>CCP_Identity__WebAuthn__Origins__0</c> to its own address, expecting to
/// replace the development default. .NET's configuration binder does not
/// replace a collection that already has items in it — it adds to it. So the
/// list held the deployment's origin <i>and</i> <c>http://localhost:3000</c>,
/// the startup check refused a plain-HTTP origin in production, and the API
/// stopped serving. Everything about that is correct except the default.
/// </para>
/// <para>
/// The lesson is not about WebAuthn: <b>a non-empty default on a collection is
/// a value nobody can remove through configuration.</b> It can only be added
/// to, and the person adding has no way to tell from their settings file that
/// the old value is still in there.
/// </para>
/// </summary>
public sealed class WebAuthnConfigurationTests
{
    private static IdentityOptions Bind(params (string Key, string Value)[] settings)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s =>
                new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

        var options = new IdentityOptions();

        configuration.GetSection(IdentityOptions.SectionName).Bind(options);

        return options;
    }

    [Fact]
    public void ADeploymentsOriginReplacesTheDefaultRatherThanJoiningIt()
    {
        IdentityOptions options = Bind(
            ("Identity:WebAuthn:RelyingPartyId", "platform.example.com"),
            ("Identity:WebAuthn:Origins:0", "https://platform.example.com"));

        // One, not two. If this is ever two again, the second one is
        // http://localhost:3000 and production will not start.
        Assert.Equal(["https://platform.example.com"], options.WebAuthn.Origins);
    }

    [Fact]
    public void AndTheConfigurationItGetsIsOneTheStartupCheckAccepts()
    {
        IdentityOptions options = Bind(
            ("Identity:WebAuthn:RelyingPartyId", "platform.example.com"),
            ("Identity:WebAuthn:Origins:0", "https://platform.example.com"));

        Assert.Empty(options.WebAuthn.Misconfigurations());
    }

    /// <summary>
    /// A deployment that says nothing about passkeys gets a Platform that
    /// starts.
    /// <para>
    /// The first version of the fix made the default empty and left the check
    /// treating an empty list as a misconfiguration, which would have turned
    /// one outage into a different one: every deployment not using passkeys
    /// would have stopped starting. Passkeys are one way in among several, and
    /// an unconfigured one is a feature that is off, not a broken Platform.
    /// </para>
    /// </summary>
    [Fact]
    public void SayingNothingAboutPasskeysIsNotAMisconfiguration()
    {
        IdentityOptions options = Bind(("Identity:Issuer", "https://platform.example.com"));

        Assert.Empty(options.WebAuthn.Misconfigurations());
        Assert.False(options.WebAuthn.IsUsable);
    }

    /// <summary>
    /// Half of it, though, is somebody having tried. That is worth stopping
    /// for, because the symptom otherwise is a button that silently does
    /// nothing.
    /// </summary>
    [Fact]
    public void HalfConfiguringItIsStoppedAtStartupAndNamed()
    {
        IdentityOptions relyingPartyOnly = Bind(
            ("Identity:WebAuthn:RelyingPartyId", "platform.example.com"));

        Assert.NotEmpty(relyingPartyOnly.WebAuthn.Misconfigurations());

        IdentityOptions originOnly = Bind(
            ("Identity:WebAuthn:Origins:0", "https://platform.example.com"));

        Assert.NotEmpty(originOnly.WebAuthn.Misconfigurations());
    }

    [Fact]
    public void AnOriginThatDoesNotBelongToTheRelyingPartyIsStillRefused()
    {
        // The check that caught the outage. It was right; the default was not.
        IdentityOptions options = Bind(
            ("Identity:WebAuthn:RelyingPartyId", "platform.example.com"),
            ("Identity:WebAuthn:Origins:0", "https://platform.example.com"),
            ("Identity:WebAuthn:Origins:1", "http://localhost:3000"));

        Assert.NotEmpty(options.WebAuthn.Misconfigurations());
    }

    [Fact]
    public void ASubdomainOfTheRelyingPartyIsAllowed()
    {
        // How one passkey serves two applications: the relying party is the
        // parent domain, and each application's own origin sits under it.
        IdentityOptions options = Bind(
            ("Identity:WebAuthn:RelyingPartyId", "example.com"),
            ("Identity:WebAuthn:Origins:0", "https://platform.example.com"),
            ("Identity:WebAuthn:Origins:1", "https://assets.example.com"));

        Assert.Empty(options.WebAuthn.Misconfigurations());
        Assert.True(options.WebAuthn.IsUsable);
    }
}
