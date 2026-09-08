using System.Reflection;
using CCP.Kernel.Application.Auditing;

namespace CCP.Architecture.Tests;

/// <summary>
/// That the handlers which change something can actually record it.
/// <para>
/// Phase 6 built the trail and left it empty: the module existed, the seam
/// existed, and no handler called it. The gap was invisible because nothing
/// structural noticed — every test passed and the audit table stayed empty.
/// This is what notices.
/// </para>
/// <para>
/// It checks that a handler <b>takes a dependency on</b> <see cref="IAuditTrail"/>,
/// not that it calls it on any particular path. Reflection cannot see call sites,
/// and pretending otherwise would be a test that reassures without checking.
/// What it does catch is the failure that actually happened: a state-changing
/// handler with no way to record anything at all.
/// </para>
/// </summary>
public sealed class AuditCoverageTests
{
    /// <summary>
    /// The handlers that change something a later investigation would ask about.
    /// <para>
    /// Named explicitly rather than inferred from a suffix. "Every class ending
    /// in Handler" would sweep in the read handlers, and a list that includes
    /// things nobody meant gets suppressed rather than fixed.
    /// </para>
    /// </summary>
    private static readonly string[] StateChangingHandlers =
    [
        // Authorization — how access is created and destroyed.
        "GrantRoleHandler",
        "RevokeRoleHandler",

        // Identity — accounts and credentials.
        "CreateUserHandler",
        "UpdateUserHandler",
        "ChangeUserStatusHandler",
        "PasswordSetter",

        // Organization — the hierarchy that organizational scope is computed
        // from, so a restructure changes who can see what.
        "CreateUnitHandler",
        "MoveUnitHandler",
        "RenameUnitHandler",
        "DeactivateUnitHandler",
        "CreateEmployeeHandler",
        "TransferEmployeeHandler",
        "LinkEmployeeUserHandler",

        // Security — the second factor.
        "ConfirmMfaEnrolmentHandler",
        "DisableMfaHandler"
    ];

    [Fact]
    public void EveryStateChangingHandler_CanRecordToTheAuditTrail()
    {
        IReadOnlyList<Type> types = LoadModuleTypes();

        var missing = new List<string>();
        var notFound = new List<string>();

        foreach (string name in StateChangingHandlers)
        {
            Type? handler = types.FirstOrDefault(t => t.Name == name);

            if (handler is null)
            {
                // A renamed or deleted handler must fail loudly rather than
                // silently stop being checked — which is how coverage lists rot.
                notFound.Add(name);
                continue;
            }

            bool takesTrail = handler
                .GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Any(p => p.ParameterType == typeof(IAuditTrail));

            if (!takesTrail)
            {
                missing.Add(handler.FullName ?? name);
            }
        }

        Assert.True(
            notFound.Count == 0,
            "These handlers are listed as state-changing but no longer exist. Rename or remove "
            + "them here so the list keeps meaning something:"
            + Environment.NewLine + string.Join(Environment.NewLine, notFound));

        Assert.True(
            missing.Count == 0,
            "These handlers change state but cannot record an audit event — they take no "
            + "IAuditTrail. An action nobody can reconstruct afterwards is an action that did not "
            + "happen, as far as an investigation is concerned:"
            + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void TheAuditSeam_LivesInTheKernel()
    {
        // Identity, Organization, Authorization and Security all record, and no
        // module may reference another (ARCHITECTURE.md §6.2). The contract
        // belonging to the kernel is what makes that possible; if it ever moved
        // into the Audit module, every other module would need a reference to
        // it and the boundary would be gone.
        Assert.Equal("CCP.Kernel.Application", typeof(IAuditTrail).Assembly.GetName().Name);
    }

    [Fact]
    public void NoModuleReferencesTheAuditModule()
    {
        IReadOnlyList<Type> types = LoadModuleTypes();

        List<string> offenders =
        [
            .. types
                .Select(t => t.Assembly)
                .Distinct()
                .Where(a => a.GetName().Name?.StartsWith("CCP.Modules.", StringComparison.Ordinal) == true
                         && a.GetName().Name?.Contains("Audit", StringComparison.Ordinal) != true)
                .Where(a => a.GetReferencedAssemblies()
                    .Any(r => r.Name?.StartsWith("CCP.Modules.Audit", StringComparison.Ordinal) == true))
                .Select(a => a.GetName().Name!)
        ];

        Assert.True(
            offenders.Count == 0,
            "These modules reference the Audit module directly. They must record through the "
            + "kernel's IAuditTrail instead:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Loads every module assembly through a type each one owns, so the
    /// assemblies are certain to be loaded rather than merely referenced.
    /// </summary>
    private static IReadOnlyList<Type> LoadModuleTypes()
    {
        Assembly[] assemblies =
        [
            typeof(CCP.Modules.Authorization.Application.Grants.GrantRoleHandler).Assembly,
            typeof(CCP.Modules.Identity.Application.Users.CreateUserHandler).Assembly,
            typeof(CCP.Modules.Organization.Application.Units.CreateUnitHandler).Assembly,
            typeof(CCP.Modules.Security.Application.Mfa.DisableMfaHandler).Assembly
        ];

        return [.. assemblies.SelectMany(a => a.GetTypes())];
    }
}
