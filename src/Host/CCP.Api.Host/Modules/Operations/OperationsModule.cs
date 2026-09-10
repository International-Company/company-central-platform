using CCP.Kernel.Api.Modules;
using CCP.Kernel.Api.Security;
using CCP.Kernel.Application.Jobs;
using CCP.Kernel.Application.Modules;
using CCP.Kernel.Infrastructure.Jobs;
using CCP.Kernel.Infrastructure.Persistence;
using CCP.Kernel.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Api.Host.Modules.Operations;

/// <summary>
/// What the Platform's own machinery has been doing.
/// <para>
/// <b>In the host rather than in a module, because none of it belongs to
/// one.</b> The job history and the outbox are in the <c>kernel</c> schema and
/// describe five modules' background work at once. A module owning this would
/// be a module reading another module's business, which is the one thing the
/// architecture forbids (ARCHITECTURE.md §6.2). The host already composes every
/// module and is the only place entitled to look across them.
/// </para>
/// <para>
/// <b>This is not business reporting.</b> ARCHITECTURE.md §5 puts business
/// dashboards outside the Platform entirely. Nothing here counts an invoice or
/// a request; it counts sweeps, failures and undelivered events — the Platform
/// as a machine, which is the Platform's own business.
/// </para>
/// </summary>
public sealed class OperationsModule : IPlatformModule, IModuleEndpoints
{
    /// <summary>
    /// Seeing the machinery is its own permission.
    /// <para>
    /// Separate from <c>platform.audit.view</c> on purpose. The audit trail is
    /// who did what — personal, and rightly restricted to a few people. This is
    /// whether the machine is working, which the person carrying the pager needs
    /// at three in the morning and which reveals nothing about anybody. Folding
    /// the two together would mean granting the trail to hand out the dashboard.
    /// </para>
    /// </summary>
    public const string ViewPermission = "platform.operations.view";

    /// <summary>
    /// How far back the summary looks when counting recent failures.
    /// <para>
    /// A day, because that is the window in which "it has started failing" is a
    /// true sentence. A week would smear yesterday's outage across six good days
    /// and read as healthy.
    /// </para>
    /// </summary>
    private static readonly TimeSpan RecentWindow = TimeSpan.FromDays(1);

    public string Name => "operations";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Nothing of its own. It reads the kernel context the host already has.
    }

    public void MapEndpoints(IEndpointRouteBuilder versionGroup)
    {
        ArgumentNullException.ThrowIfNull(versionGroup);

        RouteGroupBuilder group = versionGroup
            .MapGroup("/platform")
            .WithTags("Operations");

        group.MapGet("/jobs", async (
            [FromServices] KernelDbContext context,
            [FromServices] IClock clock,
            CancellationToken cancellationToken) =>
        {
            DateTimeOffset since = clock.UtcNow - RecentWindow;

            // Every run in the window, plus the newest run of every job whatever
            // its age. The second half is what makes the page honest: a job that
            // stopped a week ago has nothing in the window, and a summary built
            // from the window alone would drop its row entirely — a failing job
            // would disappear rather than turn red.
            List<JobRunRecord> recent = await context.JobRuns
                .AsNoTracking()
                .Where(run => run.StartedAt >= since)
                .OrderByDescending(run => run.StartedAt)
                .Take(2000)
                .ToListAsync(cancellationToken);

            List<JobRunRecord> latest = await context.JobRuns
                .AsNoTracking()
                .GroupBy(run => run.Job)
                .Select(group => group.OrderByDescending(run => run.StartedAt).First())
                .ToListAsync(cancellationToken);

            List<JobSummaryDto> summaries = [.. latest
                .Select(last =>
                {
                    List<JobRunRecord> window =
                        [.. recent.Where(run => string.Equals(run.Job, last.Job, StringComparison.Ordinal))];

                    return new JobSummaryDto(
                        Job: last.Job,
                        LastStartedAt: last.StartedAt,
                        LastOutcome: last.Outcome.ToString(),
                        LastDurationMs: last.DurationMs,
                        LastSummary: last.Summary,
                        LastError: last.Error,
                        LastInstance: last.Instance,
                        RecentRuns: window.Count,
                        RecentFailures: window.Count(run => run.Outcome == JobOutcome.Failed),

                        // The average over the window, not the last duration.
                        // One slow pass is weather; a job that has doubled in
                        // duration since Tuesday is the thing worth seeing.
                        AverageDurationMs: window.Count == 0
                            ? last.DurationMs
                            : window.Average(run => run.DurationMs));
                })
                .OrderBy(summary => summary.Job, StringComparer.Ordinal)];

            return TypedResults.Ok(summaries);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute(ViewPermission))
            .Produces<IReadOnlyList<JobSummaryDto>>(StatusCodes.Status200OK)
            .WithName("GetJobSummaries")
            .WithSummary("Every background job, when it last ran and how it went.");

        group.MapGet("/jobs/{job}/runs", async (
            string job,
            int? limit,
            [FromServices] KernelDbContext context,
            CancellationToken cancellationToken) =>
        {
            // Bounded here rather than trusted from the caller. An unbounded
            // history read is a way to make the Platform read its own largest
            // table on demand, from an endpoint whose whole audience is people
            // investigating a system already under strain.
            int take = Math.Clamp(limit ?? 50, 1, 200);

            List<JobRunDto> runs = await context.JobRuns
                .AsNoTracking()
                .Where(run => run.Job == job)
                .OrderByDescending(run => run.StartedAt)
                .Take(take)
                .Select(run => new JobRunDto(
                    run.Id,
                    run.Job,
                    run.StartedAt,
                    run.DurationMs,
                    run.Outcome.ToString(),
                    run.Summary,
                    run.Error,
                    run.Instance))
                .ToListAsync(cancellationToken);

            return TypedResults.Ok(runs);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute(ViewPermission))
            .Produces<IReadOnlyList<JobRunDto>>(StatusCodes.Status200OK)
            .WithName("GetJobRuns")
            .WithSummary("The recent runs of one background job, newest first.");

        group.MapGet("/outbox", async (
            [FromServices] KernelDbContext context,
            [FromServices] IClock clock,
            CancellationToken cancellationToken) =>
        {
            DateTimeOffset now = clock.UtcNow;

            int pending = await context.OutboxMessages
                .CountAsync(
                    message => message.ProcessedAt == null && message.DeadLetteredAt == null,
                    cancellationToken);

            int deadLettered = await context.OutboxMessages
                .CountAsync(message => message.DeadLetteredAt != null, cancellationToken);

            // The age of the oldest undelivered message, which is the figure
            // that matters. A depth of 400 is meaningless on its own — it is
            // either a busy minute or a relay that stopped on Sunday, and only
            // the age of the oldest one tells you which.
            DateTimeOffset? oldest = await context.OutboxMessages
                .Where(message => message.ProcessedAt == null && message.DeadLetteredAt == null)
                .OrderBy(message => message.OccurredAt)
                .Select(message => (DateTimeOffset?)message.OccurredAt)
                .FirstOrDefaultAsync(cancellationToken);

            return TypedResults.Ok(new OutboxDepthDto(
                Pending: pending,
                DeadLettered: deadLettered,
                OldestPendingAt: oldest,
                OldestPendingAgeSeconds: oldest is null ? null : (now - oldest.Value).TotalSeconds));
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute(ViewPermission))
            .Produces<OutboxDepthDto>(StatusCodes.Status200OK)
            .WithName("GetOutboxDepth")
            .WithSummary("How many integration events are undelivered, and how old the oldest is.");
    }
}

/// <summary>One background job, as the operations screen shows it.</summary>
public sealed record JobSummaryDto(
    string Job,
    DateTimeOffset LastStartedAt,
    string LastOutcome,
    double LastDurationMs,
    string? LastSummary,
    string? LastError,
    string LastInstance,
    int RecentRuns,
    int RecentFailures,
    double AverageDurationMs);

/// <summary>One execution of a background job.</summary>
public sealed record JobRunDto(
    Guid Id,
    string Job,
    DateTimeOffset StartedAt,
    double DurationMs,
    string Outcome,
    string? Summary,
    string? Error,
    string Instance);

/// <summary>How far behind event delivery is.</summary>
public sealed record OutboxDepthDto(
    int Pending,
    int DeadLettered,
    DateTimeOffset? OldestPendingAt,
    double? OldestPendingAgeSeconds);
