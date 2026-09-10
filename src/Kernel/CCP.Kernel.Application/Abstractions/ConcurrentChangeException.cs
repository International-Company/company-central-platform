namespace CCP.Kernel.Application.Abstractions;

/// <summary>
/// Somebody else changed the row between it being read and being saved.
/// <para>
/// <b>In the kernel because the application layers cannot see the database.</b>
/// EF reports this as <c>DbUpdateConcurrencyException</c>, which lives in a
/// package no module's Application project references and should not — that
/// boundary is the point. A unit of work translates it into this on the way out,
/// so a handler can answer "reload and try again" without knowing what a
/// DbContext is.
/// </para>
/// <para>
/// An exception rather than a result, because a save that fails this way is not
/// a decision the handler made: it is the outcome of a race, discovered inside a
/// call that had no other way to report it.
/// </para>
/// </summary>
public sealed class ConcurrentChangeException : Exception
{
    public ConcurrentChangeException()
        : base("The record was changed by somebody else after it was read.")
    {
    }

    public ConcurrentChangeException(string message) : base(message)
    {
    }

    public ConcurrentChangeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
