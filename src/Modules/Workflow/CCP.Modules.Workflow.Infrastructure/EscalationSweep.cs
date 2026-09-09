using CCP.Kernel.Primitives;
using CCP.Modules.Workflow.Application.Abstractions;
using CCP.Modules.Workflow.Domain.Events;
using CCP.Modules.Workflow.Domain.Instances;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Workflow.Infrastructure;

/// <summary>How often to look for late tasks, and how many to take at a time.</summary>
public sealed class EscalationOptions
{
    public const string SectionName = "Workflow:Escalation";

    /// <summary>
    /// How often the sweep runs.
    /// <para>
    /// Five minutes, because a service level is measured in hours or days and
    /// nobody is served by learning about a breach thirty seconds sooner. A
    /// tighter interval buys nothing and costs a query against every instance
    /// of the application.
    /// </para>
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The most tasks to escalate in one pass.
    /// <para>
    /// Bounded so a backlog — a service level shortened by mistake, a long
    /// outage — produces a steady stream of notifications rather than ten
    /// thousand at once, which is how a mail provider decides the Platform is
    /// sending spam.
    /// </para>
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Whether to run at all. On by default.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Escalates tasks that have missed their service level.
/// <para>
/// <b>The engine's only clock.</b> Everything else in the module happens because
/// somebody acted; this happens because nobody did, which is the case a workflow
/// exists to catch.
/// </para>
/// <para>
/// Escalating raises an event and marks the task. It does not reassign, and that
/// is deliberate: moving somebody's work to their manager automatically is a
/// company policy, not an engine behaviour, and a Platform that decided it would
/// be making an organizational decision on the company's behalf. The event says
/// what happened; Notifications tells whoever should know.
/// </para>
/// <para>
/// Marked once. A timer that fires on every sweep sends a reminder every five
/// minutes until somebody acts, and people learn to filter it — at which point
/// the escalation has made the problem harder to see rather than easier.
/// </para>
/// </summary>
public sealed class EscalationSweep(
    IServiceScopeFactory scopeFactory,
    IOptions<EscalationOptions> options,
    IClock clock,
    ILogger<EscalationSweep> logger) : BackgroundService
{
    private readonly EscalationOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Workflow escalation is disabled by configuration.");

            return;
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Workflow escalation sweep started. Interval={Interval} BatchSize={BatchSize}",
                _options.Interval.ToString(),
                _options.BatchSize);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int escalated = await SweepAsync(stoppingToken);

                if (escalated > 0)
                {
                    logger.LogWarning(
                        "Escalated {Count} workflow tasks past their service level.",
                        escalated);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // A sweep must not die of one bad pass.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                // Logged and swallowed. The next pass will find the same tasks:
                // a background worker that stops on a transient database error
                // silently ends escalation for the life of the process, and
                // nobody notices until an approval has been sitting for a month.
                logger.LogError(exception, "The workflow escalation sweep failed. Retrying.");
            }

            await Task.Delay(_options.Interval, stoppingToken);
        }
    }

    private async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IWorkflowRepository>();
        var outbox = scope.ServiceProvider.GetRequiredService<IWorkflowOutbox>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IWorkflowUnitOfWork>();

        DateTimeOffset now = clock.UtcNow;

        IReadOnlyList<WorkflowTask> overdue = await repository.GetOverdueTasksAsync(
            now, _options.BatchSize, cancellationToken);

        if (overdue.Count == 0)
        {
            return 0;
        }

        int escalated = 0;

        foreach (WorkflowTask task in overdue)
        {
            if (task.Escalate(now).IsFailure)
            {
                continue;
            }

            WorkflowInstance? instance = await repository.FindInstanceAsync(
                task.InstanceId, cancellationToken);

            await outbox.EnqueueAsync(
                new WorkflowTaskEscalatedEvent(
                    task.Id,
                    task.InstanceId,
                    instance?.ApplicationCode ?? string.Empty,
                    task.StepKey,
                    task.AssignedToUserId,
                    task.DueAt ?? now,
                    now),
                cancellationToken);

            escalated++;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return escalated;
    }
}
