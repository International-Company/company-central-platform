using System.Reflection;
using System.Runtime.CompilerServices;
using CCP.Kernel.Application.Events;
using CCP.Kernel.Domain;

namespace CCP.Kernel.Infrastructure.Outbox;

/// <summary>
/// Reads the event types from the event records themselves.
/// <para>
/// <b>Derived rather than listed</b>, for the reason the permission catalogue
/// is derived from the endpoints: a list maintained by hand is a list that
/// drifts. Every concrete <see cref="IIntegrationEvent"/> in the Platform's own
/// assemblies declares its type as a literal, and reading it needs no instance
/// worth constructing — an uninitialised one answers, because the property
/// returns a constant and touches no field.
/// </para>
/// <para>
/// <c>IntegrationEventPublicationTests</c> checks that what this finds is
/// exactly what the source declares, so a record whose type stopped being a
/// literal would fail the build rather than silently fall out of the catalogue.
/// </para>
/// </summary>
public sealed class ReflectedEventTypeCatalogue : IEventTypeCatalogue
{
    private readonly HashSet<string> _known;

    /// <param name="root">
    /// The composition root's assembly. Everything the Platform ships is
    /// reachable from it by reference, which is safer than asking what happens
    /// to be loaded: an assembly nobody has touched yet is not loaded, and its
    /// events would be missing from the catalogue until something touched it.
    /// </param>
    public ReflectedEventTypeCatalogue(Assembly root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var types = new SortedSet<string>(StringComparer.Ordinal);

        foreach (Assembly assembly in PlatformAssemblies(root))
        {
            foreach (Type type in LoadableTypes(assembly))
            {
                if (type is not { IsClass: true, IsAbstract: false }
                    || !typeof(IIntegrationEvent).IsAssignableFrom(type))
                {
                    continue;
                }

                var instance = (IIntegrationEvent)RuntimeHelpers.GetUninitializedObject(type);

                if (!string.IsNullOrWhiteSpace(instance.EventType))
                {
                    types.Add(instance.EventType);
                }
            }
        }

        EventTypes = [.. types];
        _known = new HashSet<string>(types, StringComparer.Ordinal);
    }

    public IReadOnlyList<string> EventTypes { get; }

    public bool IsKnown(string eventType) => _known.Contains(eventType);

    private static IEnumerable<Assembly> PlatformAssemblies(Assembly root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>([root]);

        while (pending.Count > 0)
        {
            Assembly assembly = pending.Dequeue();

            if (!seen.Add(assembly.GetName().Name ?? string.Empty))
            {
                continue;
            }

            yield return assembly;

            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                if (reference.Name is { } name
                    && name.StartsWith("CCP.", StringComparison.Ordinal)
                    && !seen.Contains(name))
                {
                    pending.Enqueue(Assembly.Load(reference));
                }
            }
        }
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }
}
