namespace CCP.Kernel.Application.Events;

/// <summary>
/// Every integration event type the running Platform can send.
/// <para>
/// A webhook subscription names event types as strings, and until this existed
/// nothing checked them. A typo was accepted and then received nothing, silently
/// and for ever. So was a type that is declared and never raised: five of those
/// existed at the time. The catalogue is how a subscription is checked, and how
/// a business system finds out what it can subscribe to.
/// </para>
/// </summary>
public interface IEventTypeCatalogue
{
    /// <summary>Every event type, sorted ordinally.</summary>
    IReadOnlyList<string> EventTypes { get; }

    /// <summary>Whether the Platform can send an event of this type.</summary>
    bool IsKnown(string eventType);
}
