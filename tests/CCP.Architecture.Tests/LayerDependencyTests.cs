using System.Reflection;
using CCP.Kernel.Api.Modules;
using CCP.Kernel.Application.Modules;
using CCP.Kernel.Infrastructure.Persistence;
using CCP.Kernel.Results;

namespace CCP.Architecture.Tests;

/// <summary>
/// The dependency rules of ARCHITECTURE.md §6, expressed as executable
/// assertions.
/// <para>
/// Documented architecture decays; enforced architecture does not. These tests
/// fail the build when a rule is broken, which is the only reason the rules
/// will still hold in three years (ARCHITECTURE.md §26.1).
/// </para>
/// <para>
/// Assembly references are checked rather than namespace usage, because a
/// reference is what actually permits coupling — and the compiler records it
/// whether or not a developer intended it.
/// </para>
/// <para>
/// <b>Known limitation, verified during Phase 1:</b> the compiler prunes a
/// reference whose only use is a <c>const</c>, because const values are inlined
/// at the call site. A project that reads nothing but a const from another
/// assembly will therefore not be flagged here. That is acceptable — copying a
/// literal creates no runtime coupling — but it means these tests prove the
/// absence of a <i>binary</i> dependency, not the absence of every textual
/// reference. Project references themselves are reviewed in pull requests.
/// </para>
/// </summary>
public sealed class LayerDependencyTests
{
    private static readonly Assembly Kernel = typeof(Result).Assembly;
    private static readonly Assembly Application = typeof(IPlatformModule).Assembly;
    private static readonly Assembly Infrastructure = typeof(KernelDbContext).Assembly;
    private static readonly Assembly Api = typeof(IModuleEndpoints).Assembly;

    private static IReadOnlyList<string> ReferencedAssemblyNames(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty)];

    // -----------------------------------------------------------------------
    // Kernel: depends on nothing in the Platform.
    // -----------------------------------------------------------------------

    [Fact]
    public void Kernel_DependsOnNoOtherPlatformAssembly()
    {
        IReadOnlyList<string> references = ReferencedAssemblyNames(Kernel);

        string[] violations = [.. references.Where(r => r.StartsWith("CCP.", StringComparison.Ordinal))];

        Assert.True(
            violations.Length == 0,
            "CCP.Kernel must depend on nothing in the Platform (ARCHITECTURE.md §6.1). "
            + $"Found: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Kernel_DoesNotDependOnEfCoreOrAspNetCore()
    {
        IReadOnlyList<string> references = ReferencedAssemblyNames(Kernel);

        string[] violations =
        [
            .. references.Where(r =>
                r.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                || r.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
        ];

        Assert.True(
            violations.Length == 0,
            "The Kernel holds domain primitives and must stay free of infrastructure. "
            + $"Found: {string.Join(", ", violations)}");
    }

    // -----------------------------------------------------------------------
    // Application: no infrastructure, no web framework.
    // -----------------------------------------------------------------------

    [Fact]
    public void Application_DoesNotDependOnInfrastructure()
    {
        IReadOnlyList<string> references = ReferencedAssemblyNames(Application);

        Assert.DoesNotContain("CCP.Kernel.Infrastructure", references);
    }

    [Fact]
    public void Application_DoesNotDependOnApi()
    {
        IReadOnlyList<string> references = ReferencedAssemblyNames(Application);

        Assert.DoesNotContain("CCP.Kernel.Api", references);
    }

    [Fact]
    public void Application_DoesNotDependOnEfCore()
    {
        IReadOnlyList<string> references = ReferencedAssemblyNames(Application);

        string[] violations =
        [
            .. references.Where(r =>
                r.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                || r.StartsWith("Npgsql", StringComparison.Ordinal))
        ];

        Assert.True(
            violations.Length == 0,
            "The Application layer declares persistence needs as interfaces; EF Core belongs to "
            + $"Infrastructure (ARCHITECTURE.md §6.1). Found: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Application_DoesNotDependOnAspNetCore()
    {
        IReadOnlyList<string> references = ReferencedAssemblyNames(Application);

        string[] violations =
        [
            .. references.Where(r => r.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
        ];

        Assert.True(
            violations.Length == 0,
            "The Application layer must be usable without a web host. "
            + $"Found: {string.Join(", ", violations)}");
    }

    // -----------------------------------------------------------------------
    // Api: presentation concerns only; never reaches into Infrastructure.
    // -----------------------------------------------------------------------

    [Fact]
    public void Api_DoesNotDependOnInfrastructure()
    {
        IReadOnlyList<string> references = ReferencedAssemblyNames(Api);

        Assert.True(
            !references.Contains("CCP.Kernel.Infrastructure"),
            "The Api layer talks to Application, never to Infrastructure directly "
            + "(ARCHITECTURE.md §6.1). Only the host composition root wires the two together.");
    }

    [Fact]
    public void Api_DoesNotDependOnEfCore()
    {
        IReadOnlyList<string> references = ReferencedAssemblyNames(Api);

        string[] violations =
        [
            .. references.Where(r =>
                r.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                || r.StartsWith("Npgsql", StringComparison.Ordinal))
        ];

        Assert.True(
            violations.Length == 0,
            $"Endpoints must not query the database directly. Found: {string.Join(", ", violations)}");
    }

    // -----------------------------------------------------------------------
    // Infrastructure: implements Application's interfaces, never drives the Api.
    // -----------------------------------------------------------------------

    [Fact]
    public void Infrastructure_DoesNotDependOnApi()
    {
        IReadOnlyList<string> references = ReferencedAssemblyNames(Infrastructure);

        Assert.DoesNotContain("CCP.Kernel.Api", references);
    }

    // -----------------------------------------------------------------------
    // The guard on the guards: prove these assertions can actually fail.
    // A test that cannot fail protects nothing.
    // -----------------------------------------------------------------------

    [Fact]
    public void ReferenceInspection_ActuallySeesReferences()
    {
        IReadOnlyList<string> references = ReferencedAssemblyNames(Infrastructure);

        // Infrastructure genuinely does reference these. If this assertion ever
        // passes vacuously, the checks above are inspecting nothing and every
        // other test in this file is worthless.
        Assert.Contains("CCP.Kernel", references);
        Assert.Contains("CCP.Kernel.Application", references);
        Assert.Contains(references, r => r.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }
}
