using System.Globalization;
using CCP.Kernel.Application.Events;
using CCP.Modules.Notifications.Application.Sending;
using CCP.Modules.Notifications.Domain.Notifications;
using CCP.Modules.Workflow.Contracts.Events;
using Microsoft.Extensions.Logging;

namespace CCP.Modules.Notifications.Infrastructure.Listeners;

/// <summary>
/// Tells somebody they have an approval waiting.
/// <para>
/// <b>Workflow does not know that notifications exist.</b> It raises an event
/// saying a task was assigned; this listens. That is why the escalation sweep
/// could be written without deciding what "escalate" means to a person, and why
/// adding a channel later changes nothing in the workflow engine.
/// </para>
/// <para>
/// Idempotent by construction: a redelivery creates a second notification with
/// the same text, which is a duplicate in an inbox rather than a corruption.
/// Deduplicating on the event id would be better and is recorded as debt; a
/// second copy of "you have something to approve" is a nuisance, and dropping
/// the first copy because of a bug in deduplication is not.
/// </para>
/// </summary>
public sealed class TaskAssignedListener(
    NotificationSender sender,
    ILogger<TaskAssignedListener> logger)
    : IIntegrationEventHandler<WorkflowTaskAssignedEvent>
{
    /// <summary>What this message is, and therefore what a preference can silence.</summary>
    public const string Category = "workflow";

    public async Task HandleAsync(
        WorkflowTaskAssignedEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var result = await sender.SendAsync(
            new SendRequest(
                integrationEvent.AssignedToUserId,
                "workflow.task.assigned",
                Category,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["resourceType"] = integrationEvent.ResourceType,
                    ["resourceId"] = integrationEvent.ResourceId,
                    ["step"] = integrationEvent.StepKey,
                    ["dueAt"] = integrationEvent.DueAt?.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)
                                ?? string.Empty
                },
                    Channels: null,
                    CausedBy: integrationEvent.EventId),
            cancellationToken);

        if (result.IsFailure)
        {
            // Logged rather than thrown. The outbox retries a throwing handler,
            // and retrying a missing template forever would fill the relay with
            // work that cannot succeed until somebody writes the template. The
            // log line names the template so they can.
            logger.LogWarning(
                "Could not notify {User} about task {Task}: {Error}",
                integrationEvent.AssignedToUserId,
                integrationEvent.TaskId,
                result.Errors[0].Message);
        }
    }
}

/// <summary>
/// Tells somebody an approval they are holding is late.
/// <para>
/// The escalation sweep marks the task and raises an event; it does not
/// reassign, because moving somebody's work to their manager automatically is a
/// company policy rather than an engine behaviour. This is where that event
/// becomes a message somebody reads.
/// </para>
/// </summary>
public sealed class TaskEscalatedListener(
    NotificationSender sender,
    ILogger<TaskEscalatedListener> logger)
    : IIntegrationEventHandler<WorkflowTaskEscalatedEvent>
{
    public async Task HandleAsync(
        WorkflowTaskEscalatedEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var result = await sender.SendAsync(
            new SendRequest(
                integrationEvent.AssignedToUserId,
                "workflow.task.escalated",
                TaskAssignedListener.Category,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["step"] = integrationEvent.StepKey,
                    ["dueAt"] = integrationEvent.DueAt.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)
                },
                    Channels: null,
                    CausedBy: integrationEvent.EventId),
            cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning(
                "Could not notify {User} that task {Task} is overdue: {Error}",
                integrationEvent.AssignedToUserId,
                integrationEvent.TaskId,
                result.Errors[0].Message);
        }
    }
}

/// <summary>
/// Tells the requester what was decided.
/// <para>
/// The one message the person who started the request is actually waiting for.
/// It says approved, rejected or cancelled, because those are three different
/// facts and "your request has been processed" tells nobody anything.
/// </para>
/// </summary>
public sealed class InstanceCompletedListener(
    NotificationSender sender,
    ILogger<InstanceCompletedListener> logger)
    : IIntegrationEventHandler<WorkflowInstanceCompletedEvent>
{
    public async Task HandleAsync(
        WorkflowInstanceCompletedEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        // The requester, not the decider. The person who has been waiting is the
        // one this message is for; the approver already knows what they just
        // approved.
        var result = await sender.SendAsync(
            new SendRequest(
                integrationEvent.RequestedBy,
                $"workflow.instance.{integrationEvent.Outcome.ToLowerInvariant()}",
                TaskAssignedListener.Category,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["resourceType"] = integrationEvent.ResourceType,
                    ["resourceId"] = integrationEvent.ResourceId,
                    ["outcome"] = integrationEvent.Outcome
                },
                    Channels: null,
                    CausedBy: integrationEvent.EventId),
            cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning(
                "Could not notify about completed instance {Instance}: {Error}",
                integrationEvent.InstanceId,
                result.Errors[0].Message);
        }
    }
}
