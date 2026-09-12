using CCP.Kernel.Application.Events;
using CCP.Kernel.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CCP.Kernel.Infrastructure.Outbox;

/// <summary>
/// Resolves and invokes the handlers registered for an integration event.
/// <para>
/// Handlers are resolved from the container by the event's concrete type, so a
/// module subscribes simply by registering an
/// <see cref="IIntegrationEventHandler{TEvent}"/>. The publishing module never
/// learns who is listening, which is what keeps Audit dependency-free while
/// still recording everything (ARCHITECTURE.md §6.3).
/// </para>
/// </summary>
public sealed class IntegrationEventDispatcher(
    IServiceProvider serviceProvider,
    ILogger<IntegrationEventDispatcher> logger) : IIntegrationEventDispatcher
{
    public async Task DispatchAsync(
        IIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        Type eventType = integrationEvent.GetType();
        Type handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);

        object[] handlers = [.. serviceProvider.GetServices(handlerType).OfType<object>()];

        if (handlers.Length == 0 && logger.IsEnabled(LogLevel.Debug))
        {
            // Not an error. An event with no typed subscriber is normal, and it
            // may still have observers -- so this logs and carries on rather
            // than returning, which is what it used to do.
            logger.LogDebug(
                "No handler registered for {EventType}.", integrationEvent.EventType);
        }

        foreach (object handler in handlers)
        {
            // Any handler throwing propagates, which fails the whole message
            // and causes a retry of all its handlers. That is why handlers must
            // be idempotent.
            await InvokeAsync(handler, handlerType, integrationEvent, cancellationToken);
        }

        // Then whoever wanted all of them. Outbound webhooks are the reason this
        // exists: which events matter is chosen by a business application at
        // runtime, so no typed subscription could express it.
        foreach (IIntegrationEventObserver observer in
                 serviceProvider.GetServices<IIntegrationEventObserver>())
        {
            await observer.ObserveAsync(integrationEvent, cancellationToken);
        }
    }

    private static async Task InvokeAsync(
        object handler,
        Type handlerType,
        IIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        System.Reflection.MethodInfo method =
            handlerType.GetMethod(nameof(IIntegrationEventHandler<IIntegrationEvent>.HandleAsync))
            ?? throw new InvalidOperationException(
                $"Handler type {handlerType} does not expose HandleAsync.");

        object? result = method.Invoke(handler, [integrationEvent, cancellationToken]);

        if (result is Task task)
        {
            await task;
        }
    }
}
