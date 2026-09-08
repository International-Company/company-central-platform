using CCP.Modules.Security.Infrastructure.Protection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Security.UnitTests.Infrastructure;

/// <summary>
/// Builds a protector around a key file.
/// <para>
/// The environment is reported as Production deliberately: that path refuses to
/// invent a key, so a test that forgot to supply one fails loudly instead of
/// quietly passing against an ephemeral development key.
/// </para>
/// </summary>
internal static class TestProtectorFactory
{
    public static MfaSecretProtector Create(string keyPath)
        => new(
            Options.Create(new MfaProtectionOptions { KeyPath = keyPath }),
            new StubHostEnvironment(),
            NullLogger<MfaSecretProtector>.Instance);

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "CCP.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
